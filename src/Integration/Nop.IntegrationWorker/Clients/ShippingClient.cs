using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nop.IntegrationWorker.Clients;

public class ShippingClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ShippingClient> _logger;

    public ShippingClient(HttpClient http, ILogger<ShippingClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns the tracking number on success, or throws on non-2xx.
    /// </summary>
    public async Task<string> CreateLabelAsync(string payloadJson, CancellationToken ct = default)
    {
        using var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/labels", content, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Shipping /labels → {Status}: {Body}", (int)response.StatusCode, body);

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("trackingNumber", out var tnProp);
        return tnProp.GetString() ?? string.Empty;
    }
}
