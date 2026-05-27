using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nop.IntegrationWorker.Clients;

public class WarehouseClient
{
    private readonly HttpClient _http;
    private readonly ILogger<WarehouseClient> _logger;

    public WarehouseClient(HttpClient http, ILogger<WarehouseClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns the warehouseReference on success (e.g. "WH-123"), or throws on non-2xx.
    /// </summary>
    public async Task<string> RequestFulfillmentAsync(string payloadJson, CancellationToken ct = default)
    {
        using var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/fulfillment", content, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Warehouse /fulfillment → {Status}: {Body}", (int)response.StatusCode, body);

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("warehouseReference", out var refProp);
        return refProp.GetString() ?? string.Empty;
    }
}
