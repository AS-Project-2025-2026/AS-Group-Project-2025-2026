using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nop.IntegrationWorker.Clients;

public record InventoryStockItem(int ProductId, int WmsQuantity, int? PosQuantity);

public class InventoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

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

            // Support both old format (quantity) and new format (wmsQuantity + optional posQuantity)
            int wmsQty;
            if (item.TryGetProperty("wmsQuantity", out var wmsEl))
                wmsQty = wmsEl.GetInt32();
            else if (item.TryGetProperty("quantity", out var qtyEl))
                wmsQty = qtyEl.GetInt32();
            else
                continue;

            int? posQty = item.TryGetProperty("posQuantity", out var posEl)
                ? posEl.GetInt32()
                : null;

            result.Add(new InventoryStockItem(pid.GetInt32(), wmsQty, posQty));
        }
        return result;
    }

    public async Task ReportPosStockAsync(int productId, int quantity, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { productId, quantity });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/stock/pos-report", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Inventory /stock/pos-report → {Status}: {Body}", (int)response.StatusCode, body);
    }
}
