using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Clients;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Metrics;
using Nop.IntegrationWorker.Options;

namespace Nop.IntegrationWorker.Services;

public class InventorySyncService : BackgroundService
{
    private readonly InventoryClient _inventory;
    private readonly WorkerDataService _data;
    private readonly InventoryOptions _opts;
    private readonly ILogger<InventorySyncService> _logger;

    public InventorySyncService(
        InventoryClient inventory,
        WorkerDataService data,
        IOptions<InventoryOptions> opts,
        ILogger<InventorySyncService> logger)
    {
        _inventory = inventory;
        _data = data;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InventorySyncService started — polling every {Interval}s, staleness threshold {Threshold}s, conflict tolerance {Tolerance} units",
            _opts.SyncIntervalSeconds, _opts.StalenessThresholdSeconds, _opts.ConflictToleranceUnits);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncStockAsync(stoppingToken);
                await MarkStaleAsync();
                await ResolveConflictsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InventorySyncService error — will retry next cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opts.SyncIntervalSeconds), stoppingToken);
        }
    }

    private async Task SyncStockAsync(CancellationToken ct)
    {
        using var timer = WorkerMetrics.TimeInventorySync();
        var items = await _inventory.GetStockAsync(ct);

        var updated = 0;
        if (items.Count == 0)
        {
            WorkerMetrics.ObserveInventorySync(totalProducts: 0, updatedProducts: 0);
            _logger.LogDebug("Inventory sync returned 0 products — skipping");
            return;
        }

        foreach (var item in items)
        {
            bool conflictFlag = false;
            bool pendingReconciliation = false;
            int checkoutQty = item.WmsQuantity;

            // Backward-compatible path for old demo payloads that embedded POS stock
            // inside the inventory response. The current QAS 6 path uses POS events.
            if (item.PosQuantity.HasValue)
            {
                var diff = Math.Abs(item.WmsQuantity - item.PosQuantity.Value);
                if (diff > _opts.ConflictToleranceUnits)
                {
                    conflictFlag = true;
                    pendingReconciliation = true;
                    checkoutQty = Math.Min(item.WmsQuantity, item.PosQuantity.Value);
                    _logger.LogWarning(
                        "Inventory conflict on ProductId={ProductId}: wms={WmsQty} vs pos={PosQty} (diff={Diff}, tolerance={Tolerance}) — checkout capped at {CheckoutQty}",
                        item.ProductId, item.WmsQuantity, item.PosQuantity.Value,
                        diff, _opts.ConflictToleranceUnits, checkoutQty);

                    // Also upsert the POS projection so it appears in the Operations View
                    await _data.UpsertInventoryProjectionAsync(
                        item.ProductId, "pos", item.PosQuantity.Value,
                        isStale: false, conflictFlag: false, pendingReconciliation: false);
                }
            }
            else
            {
                // Fallback: check DB for a manually-injected POS row (e.g. test via SQL)
                var others = await _data.GetProjectionsByProductAsync(item.ProductId);
                var dbPos = others.FirstOrDefault(p => p.SourceSystem == "pos");
                if (dbPos != null)
                {
                    var diff = Math.Abs(item.WmsQuantity - dbPos.ReportedQuantity);
                    if (diff > _opts.ConflictToleranceUnits)
                    {
                        conflictFlag = true;
                        pendingReconciliation = true;
                        checkoutQty = Math.Min(item.WmsQuantity, dbPos.ReportedQuantity);
                        _logger.LogWarning(
                            "Inventory conflict on ProductId={ProductId}: wms={WmsQty} vs pos={PosQty} (diff={Diff}, tolerance={Tolerance}) — checkout capped at {CheckoutQty}",
                            item.ProductId, item.WmsQuantity, dbPos.ReportedQuantity,
                            diff, _opts.ConflictToleranceUnits, checkoutQty);
                    }
                }
            }

            await _data.UpsertInventoryProjectionAsync(
                item.ProductId, _opts.SourceSystem, item.WmsQuantity,
                isStale: false, conflictFlag, pendingReconciliation);

            var rows = await _data.UpdateProductStockAsync(item.ProductId, checkoutQty);
            if (rows > 0) updated++;
        }

        WorkerMetrics.ObserveInventorySync(items.Count, updated);
        _logger.LogInformation("Inventory sync complete — {Updated}/{Total} products updated", updated, items.Count);
    }

    private async Task MarkStaleAsync()
    {
        var marked = await _data.MarkStaleProjectionsAsync(_opts.StalenessThresholdSeconds);
        WorkerMetrics.ObserveInventoryStaleMarked(marked);
        if (marked > 0)
            _logger.LogWarning("Inventory staleness: {Count} projection(s) marked IsStale=true (threshold={Threshold}s)",
                marked, _opts.StalenessThresholdSeconds);
    }

    private async Task ResolveConflictsAsync()
    {
        var resolved = await _data.ResolveConflictsAsync(_opts.ConflictToleranceUnits);
        WorkerMetrics.ObserveInventoryConflictsResolved(resolved);
        if (resolved > 0)
            _logger.LogInformation("Inventory conflict auto-resolved: {Count} record(s) cleared", resolved);
    }
}
