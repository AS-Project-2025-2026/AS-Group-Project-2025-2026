using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nop.IntegrationWorker.Clients;

public class CustomerSupportClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CustomerSupportClient> _logger;

    public CustomerSupportClient(HttpClient http, ILogger<CustomerSupportClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> CreateTicketAsync(string payloadJson, CancellationToken ct = default)
    {
        using var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/tickets", content, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("CustomerSupport /tickets → {Status}: {Body}", (int)response.StatusCode, body);

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("ticketId", out var ticketProp);
        return ticketProp.GetString() ?? string.Empty;
    }
}
