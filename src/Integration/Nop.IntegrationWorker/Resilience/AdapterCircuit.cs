namespace Nop.IntegrationWorker.Resilience;

public sealed class AdapterCircuit
{
    public string Adapter { get; }
    public CircuitState State { get; private set; } = CircuitState.Closed;
    public int ConsecutiveFailures { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? NextProbeAtUtc { get; private set; }
    public string? LastError { get; private set; }

    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldown;
    private readonly object _lock = new();

    public AdapterCircuit(string adapter, int failureThreshold, int cooldownSeconds)
    {
        Adapter = adapter;
        _failureThreshold = failureThreshold;
        _cooldown = TimeSpan.FromSeconds(cooldownSeconds);
    }

    public bool IsCallAllowed()
    {
        lock (_lock)
        {
            return State switch
            {
                CircuitState.Closed => true,
                CircuitState.HalfOpen => true,
                CircuitState.Open => DateTime.UtcNow >= NextProbeAtUtc,
                _ => false
            };
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            ConsecutiveFailures = 0;
            State = CircuitState.Closed;
            OpenedAtUtc = null;
            NextProbeAtUtc = null;
            LastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_lock)
        {
            ConsecutiveFailures++;
            LastError = error;

            if (State == CircuitState.HalfOpen || ConsecutiveFailures >= _failureThreshold)
            {
                State = CircuitState.Open;
                OpenedAtUtc = DateTime.UtcNow;
                NextProbeAtUtc = DateTime.UtcNow.Add(_cooldown);
            }
        }
    }

    public void TransitionToHalfOpen()
    {
        lock (_lock)
        {
            if (State == CircuitState.Open && DateTime.UtcNow >= NextProbeAtUtc)
                State = CircuitState.HalfOpen;
        }
    }
}
