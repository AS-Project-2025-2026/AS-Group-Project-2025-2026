namespace Nop.Core.Domain.Integration;

public partial class DeadLetterRecord : BaseEntity
{
    public int? OriginalOutboxRecordId { get; set; }
    public string Payload { get; set; }
    public string IdempotencyKey { get; set; }
    public string CorrelationId { get; set; }
    public string Adapter { get; set; }
    public string FailureReason { get; set; }
    public DateTime? FirstAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public string EscalationState { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
