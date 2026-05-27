namespace Nop.IntegrationWorker.Options;

public class ResilienceOptions
{
    public int InitialDelaySeconds { get; set; } = 2;
    public int MaxDelaySeconds { get; set; } = 300;
    public int MaxRetryAttempts { get; set; } = 10;
    public int CircuitFailureThreshold { get; set; } = 5;
    public int CircuitCooldownSeconds { get; set; } = 300;
}
