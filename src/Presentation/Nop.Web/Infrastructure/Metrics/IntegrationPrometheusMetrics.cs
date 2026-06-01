using Nop.Services.Integration;
using Prometheus;

namespace Nop.Web.Infrastructure.Metrics;

public static class IntegrationPrometheusMetrics
{
    private static readonly Gauge ArchitecturalDriverHealth = Prometheus.Metrics.CreateGauge(
        "verdemart_architectural_driver_health",
        "Architectural driver status encoded as 0=healthy, 1=watch, 2=degraded.",
        new GaugeConfiguration { LabelNames = ["driver"] });

    private static readonly Gauge OutboxBacklog = Prometheus.Metrics.CreateGauge(
        "verdemart_outbox_backlog",
        "Outbox records waiting to be published or retried.");

    private static readonly Gauge OutboxRetrying = Prometheus.Metrics.CreateGauge(
        "verdemart_outbox_retrying_records",
        "Outbox records currently in retrying state.");

    private static readonly Gauge OutboxFailed = Prometheus.Metrics.CreateGauge(
        "verdemart_outbox_failed_records",
        "Outbox records currently in failed state.");

    private static readonly Gauge OutboxPublishedLast24Hours = Prometheus.Metrics.CreateGauge(
        "verdemart_outbox_published_last_24h",
        "Outbox records published in the last 24 hours.");

    private static readonly Gauge OutboxAverageLatency = Prometheus.Metrics.CreateGauge(
        "verdemart_outbox_publish_latency_seconds_avg_24h",
        "Average outbox publish latency in seconds for records created in the last 24 hours.");

    private static readonly Gauge OutboxP95Latency = Prometheus.Metrics.CreateGauge(
        "verdemart_outbox_publish_latency_seconds_p95_24h",
        "P95 outbox publish latency in seconds for records created in the last 24 hours.");

    private static readonly Gauge NewDeadLetters = Prometheus.Metrics.CreateGauge(
        "verdemart_dead_letters_new",
        "Dead-letter records requiring operator action.");

    private static readonly Gauge RequeuedDeadLettersLast24Hours = Prometheus.Metrics.CreateGauge(
        "verdemart_dead_letters_requeued_last_24h",
        "Dead-letter records requeued in the last 24 hours.");

    private static readonly Gauge OldestNewDeadLetterAge = Prometheus.Metrics.CreateGauge(
        "verdemart_dead_letter_oldest_new_age_minutes",
        "Age in minutes of the oldest new dead-letter record.");

    private static readonly Gauge OpenCircuitBreakers = Prometheus.Metrics.CreateGauge(
        "verdemart_circuit_breakers_open",
        "Circuit breakers currently open.");

    private static readonly Gauge HalfOpenCircuitBreakers = Prometheus.Metrics.CreateGauge(
        "verdemart_circuit_breakers_half_open",
        "Circuit breakers currently half-open.");

    private static readonly Gauge CircuitBreakerFailures = Prometheus.Metrics.CreateGauge(
        "verdemart_circuit_breaker_failures",
        "Current sum of persisted circuit breaker failure counts.");

    private static readonly Gauge StaleInventory = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_stale_projections",
        "Inventory projections currently marked stale.");

    private static readonly Gauge ConflictedInventory = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_conflicted_projections",
        "Inventory projections currently marked conflicted.");

    private static readonly Gauge PendingInventoryReconciliation = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_pending_reconciliations",
        "Inventory projections currently pending reconciliation.");

    public static void Observe(IntegrationMetricsSnapshot snapshot)
    {
        ArchitecturalDriverHealth.WithLabels("availability").Set(ToStatusValue(snapshot.AvailabilityStatus));
        ArchitecturalDriverHealth.WithLabels("resilience").Set(ToStatusValue(snapshot.ResilienceStatus));
        ArchitecturalDriverHealth.WithLabels("consistency").Set(ToStatusValue(snapshot.ConsistencyStatus));
        ArchitecturalDriverHealth.WithLabels("operability").Set(ToStatusValue(snapshot.OperabilityStatus));

        OutboxBacklog.Set(snapshot.OutboxBacklog);
        OutboxRetrying.Set(snapshot.OutboxRetrying);
        OutboxFailed.Set(snapshot.OutboxFailed);
        OutboxPublishedLast24Hours.Set(snapshot.PublishedLast24Hours);
        OutboxAverageLatency.Set((double)(snapshot.AveragePublishLatencySecondsLast24Hours ?? decimal.Zero));
        OutboxP95Latency.Set((double)(snapshot.P95PublishLatencySecondsLast24Hours ?? decimal.Zero));

        NewDeadLetters.Set(snapshot.NewDeadLetters);
        RequeuedDeadLettersLast24Hours.Set(snapshot.RequeuedDeadLettersLast24Hours);
        OldestNewDeadLetterAge.Set((double)(snapshot.OldestNewDeadLetterAgeMinutes ?? decimal.Zero));

        OpenCircuitBreakers.Set(snapshot.OpenCircuitBreakers);
        HalfOpenCircuitBreakers.Set(snapshot.HalfOpenCircuitBreakers);
        CircuitBreakerFailures.Set(snapshot.CircuitBreakerFailures);

        StaleInventory.Set(snapshot.StaleInventoryProjections);
        ConflictedInventory.Set(snapshot.ConflictedInventoryProjections);
        PendingInventoryReconciliation.Set(snapshot.PendingInventoryReconciliations);
    }

    private static int ToStatusValue(string status)
    {
        return string.Equals(status, "Degraded", StringComparison.OrdinalIgnoreCase) ? 2 :
            string.Equals(status, "Watch", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}
