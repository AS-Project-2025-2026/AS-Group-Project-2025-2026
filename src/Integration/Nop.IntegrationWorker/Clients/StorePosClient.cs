using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nop.IntegrationWorker.Clients;

public class StorePosClient
{
    private readonly HttpClient _http;
    private readonly ILogger<StorePosClient> _logger;

    public StorePosClient(HttpClient http, ILogger<StorePosClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Notifies the store POS to prepare a pickup order. Returns pickupReference on success, throws on non-2xx.
    /// </summary>
    public async Task<string> ConfirmPickupAsync(string payloadJson, CancellationToken ct = default)
    {
        using var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/pickup", content, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("StorePOS /pickup → {Status}: {Body}", (int)response.StatusCode, body);

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("pickupReference", out var refProp);
        return refProp.GetString() ?? string.Empty;
    }
}
