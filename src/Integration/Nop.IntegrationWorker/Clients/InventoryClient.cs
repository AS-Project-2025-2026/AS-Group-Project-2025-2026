using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nop.IntegrationWorker.Clients;

public record InventoryStockItem(int ProductId, int Quantity, string SourceSystem);

public class InventoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns current stock levels. Returns empty list on error so sync can be skipped gracefully.
    /// </summary>
    public async Task<List<InventoryStockItem>> GetStockAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/stock", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Inventory /stock → {Status}: {Body}", (int)response.StatusCode, body);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Inventory stub returned {Status} — skipping sync", (int)response.StatusCode);
            return [];
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("products", out var products))
            return [];

        var result = new List<InventoryStockItem>();
        foreach (var item in products.EnumerateArray())
        {
            if (!item.TryGetProperty("productId", out var pid))
                continue;

            var productId = pid.GetInt32();

            if (item.TryGetProperty("quantity", out var qty))
                result.Add(new InventoryStockItem(productId, qty.GetInt32(), "wms"));

            if (item.TryGetProperty("wmsQuantity", out var wmsQty))
                result.Add(new InventoryStockItem(productId, wmsQty.GetInt32(), "wms"));

            if (item.TryGetProperty("posQuantity", out var posQty))
                result.Add(new InventoryStockItem(productId, posQty.GetInt32(), "pos"));
        }
        return result;
    }
}
