namespace Nop.IntegrationWorker.Resilience;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}
