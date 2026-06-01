namespace Nop.Services.Integration;

public partial record IntegrationMetricsSnapshot
{
    public DateTime GeneratedAtUtc { get; init; }
    public string OverallStatus { get; init; }

    public int OutboxBacklog { get; init; }
    public int OutboxRetrying { get; init; }
    public int OutboxFailed { get; init; }
    public int PublishedLast24Hours { get; init; }
    public decimal? AveragePublishLatencySecondsLast24Hours { get; init; }
    public decimal? P95PublishLatencySecondsLast24Hours { get; init; }

    public int NewDeadLetters { get; init; }
    public int RequeuedDeadLettersLast24Hours { get; init; }
    public decimal? OldestNewDeadLetterAgeMinutes { get; init; }

    public int OpenCircuitBreakers { get; init; }
    public int HalfOpenCircuitBreakers { get; init; }
    public int CircuitBreakerFailures { get; init; }

    public int StaleInventoryProjections { get; init; }
    public int ConflictedInventoryProjections { get; init; }
    public int PendingInventoryReconciliations { get; init; }

    public string AvailabilityStatus { get; init; }
    public string ResilienceStatus { get; init; }
    public string ConsistencyStatus { get; init; }
    public string OperabilityStatus { get; init; }
}
