namespace Nop.IntegrationWorker.Models;

public class CustomerSupportPayload
{
    public int OrderId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
