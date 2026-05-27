namespace Nop.IntegrationWorker.Data;

public class OutboxRecord
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? LastError { get; set; }
}
