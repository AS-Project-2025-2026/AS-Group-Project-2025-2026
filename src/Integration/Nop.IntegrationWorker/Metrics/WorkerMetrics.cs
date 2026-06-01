using Prometheus;

namespace Nop.IntegrationWorker.Metrics;

public static class WorkerMetrics
{
    private static readonly Counter AdapterCalls = Prometheus.Metrics.CreateCounter(
        "verdemart_adapter_calls_total",
        "Adapter calls attempted by the integration worker.",
        new CounterConfiguration { LabelNames = ["adapter", "outcome"] });

    private static readonly Counter AdapterRetries = Prometheus.Metrics.CreateCounter(
        "verdemart_adapter_retries_total",
        "Retry attempts scheduled by the integration worker.",
        new CounterConfiguration { LabelNames = ["adapter"] });

    private static readonly Histogram AdapterCallDuration = Prometheus.Metrics.CreateHistogram(
        "verdemart_adapter_call_duration_seconds",
        "Adapter call duration in seconds.",
        new HistogramConfiguration
        {
            LabelNames = ["adapter", "outcome"],
            Buckets = Histogram.ExponentialBuckets(0.05, 2, 10)
        });

    private static readonly Counter DeadLettersCreated = Prometheus.Metrics.CreateCounter(
        "verdemart_dead_letters_created_total",
        "Dead-letter records created by the integration worker.",
        new CounterConfiguration { LabelNames = ["adapter"] });

    private static readonly Counter WorkerMessages = Prometheus.Metrics.CreateCounter(
        "verdemart_worker_messages_processed_total",
        "Worker messages processed by adapter and outcome.",
        new CounterConfiguration { LabelNames = ["adapter", "outcome"] });

    private static readonly Histogram InventorySyncDuration = Prometheus.Metrics.CreateHistogram(
        "verdemart_inventory_sync_duration_seconds",
        "Inventory sync cycle duration in seconds.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.05, 2, 10) });

    private static readonly Gauge InventorySyncProducts = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_sync_products_last",
        "Number of products returned by the last inventory sync.");

    private static readonly Gauge InventoryProductsUpdated = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_products_updated_last",
        "Number of products updated by the last inventory sync.");

    private static readonly Gauge InventoryStaleMarked = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_stale_marked_last",
        "Number of inventory projections marked stale by the last staleness check.");

    private static readonly Gauge InventoryConflictsResolved = Prometheus.Metrics.CreateGauge(
        "verdemart_inventory_conflicts_resolved_last",
        "Number of inventory conflicts resolved by the last reconciliation check.");

    public static void ObserveAdapterCall(string adapter, string outcome, double durationSeconds)
    {
        AdapterCalls.WithLabels(adapter, outcome).Inc();
        AdapterCallDuration.WithLabels(adapter, outcome).Observe(durationSeconds);
    }

    public static void ObserveCircuitOpenSkip(string adapter)
    {
        AdapterCalls.WithLabels(adapter, "circuit_open").Inc();
    }

    public static void ObserveRetry(string adapter)
    {
        AdapterRetries.WithLabels(adapter).Inc();
    }

    public static void ObserveDeadLetter(string adapter)
    {
        DeadLettersCreated.WithLabels(adapter).Inc();
    }

    public static void ObserveWorkerMessage(string adapter, string outcome)
    {
        WorkerMessages.WithLabels(adapter, outcome).Inc();
    }

    public static IDisposable TimeInventorySync()
    {
        return InventorySyncDuration.NewTimer();
    }

    public static void ObserveInventorySync(int totalProducts, int updatedProducts)
    {
        InventorySyncProducts.Set(totalProducts);
        InventoryProductsUpdated.Set(updatedProducts);
    }

    public static void ObserveInventoryStaleMarked(int count)
    {
        InventoryStaleMarked.Set(count);
    }

    public static void ObserveInventoryConflictsResolved(int count)
    {
        InventoryConflictsResolved.Set(count);
    }
}
