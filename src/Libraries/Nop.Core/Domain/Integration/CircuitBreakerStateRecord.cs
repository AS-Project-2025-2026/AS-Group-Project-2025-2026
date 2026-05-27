namespace Nop.Core.Domain.Integration;

public partial class CircuitBreakerStateRecord : BaseEntity
{
    public string Adapter { get; set; }
    public string State { get; set; }
    public int FailureCount { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? NextProbeAtUtc { get; set; }
    public string LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
