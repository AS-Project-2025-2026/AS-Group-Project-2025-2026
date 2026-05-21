namespace Nop.Core.Domain.Integration;

public partial class OutboxRecord : BaseEntity
{
    public int OrderId { get; set; }
    public string MessageType { get; set; }
    public string Payload { get; set; }
    public string CorrelationId { get; set; }
    public string IdempotencyKey { get; set; }
    public string Status { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string LastError { get; set; }
}
