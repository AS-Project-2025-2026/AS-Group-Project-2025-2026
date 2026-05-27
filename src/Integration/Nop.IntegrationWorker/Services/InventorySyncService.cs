using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Clients;
using Nop.IntegrationWorker.Data;
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
        _logger.LogInformation("InventorySyncService started — polling every {Interval}s", _opts.SyncIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncStockAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InventorySyncService encountered an error — will retry next cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opts.SyncIntervalSeconds), stoppingToken);
        }
    }

    private async Task SyncStockAsync(CancellationToken ct)
    {
        var items = await _inventory.GetStockAsync(ct);
        if (items.Count == 0)
        {
            _logger.LogDebug("Inventory sync returned 0 products — skipping");
            return;
        }

        var updated = 0;
        foreach (var item in items)
        {
            var rows = await _data.UpdateProductStockAsync(item.ProductId, item.Quantity);
            if (rows > 0)
                updated++;
        }

        _logger.LogInformation("Inventory sync complete — {Updated}/{Total} products updated", updated, items.Count);
    }
}
