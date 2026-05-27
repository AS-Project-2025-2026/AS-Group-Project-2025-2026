namespace Nop.IntegrationWorker.Data;

public class CircuitBreakerStateDto
{
    public string Adapter { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? NextProbeAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
