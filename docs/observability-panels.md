# Observability Panels

This document explains each observability panel used in the VerdeMart architectural demo and why it was chosen. The observability design has two surfaces:

- **Operations View**: operator-facing state and actions inside nopCommerce at `http://localhost:8080/Admin/Operations/List`.
- **Grafana dashboard**: trend and metric view at `http://localhost:3000`, backed by Prometheus and Loki.

The panels were selected to make the assignment's architectural pressure visible: async delivery, degradation, retries, stale or conflicting inventory, dead letters, and recovery.

## Operations View Panels

### Architectural Driver Summary

**What it shows:** Four high-level driver cards: Availability, Resilience, Consistency, and Operability.

**Why it was chosen:** The assignment evaluates architectural judgment through quality attributes, not just features. This panel maps runtime state directly to the quality attributes we claimed in the report.

**Signals used:**

- Availability: outbox backlog, failed outbox records, published records, and publish latency.
- Resilience: open or half-open circuit breakers, retrying records, and adapter failures.
- Consistency: stale inventory records, conflicts, and pending reconciliation.
- Operability: new dead letters, requeued dead letters, and age of unresolved dead letters.

### Outbox

**What it shows:** Integration work created by nopCommerce, including order id, message type, status, retry count, next attempt, last error, correlation id, and a requeue action.

**Why it was chosen:** The outbox is the core reliability decision in ADR-2. This panel proves that order-side work is persisted durably before external systems are contacted. It also shows whether work is pending, retrying, failed, or already published.

**Demo value:** During warehouse or shipping degradation, checkout can still succeed while the outbox shows pending or retrying integration work.

### Dead Letters

**What it shows:** Messages that exhausted retries, with adapter, correlation id, failure reason, timestamps, escalation state, and operator requeue action.

**Why it was chosen:** Dead-letter handling is the recovery path for permanent or long-running failures. This panel proves that failed paid-order work is not silently lost.

**Demo value:** It supports ADR-7 by showing the point where automatic recovery stops and operator-controlled recovery begins.

### Circuit Breaker State

**What it shows:** Adapter circuit state, failure count, threshold, opened time, next probe time, last error, and last update time.

**Why it was chosen:** Retries alone can overload a failing dependency. Circuit breakers make degradation explicit and protect external systems from continuous retry pressure.

**Demo value:** During a warehouse or shipping outage, the circuit opens after repeated failures. During recovery, it moves toward probing and closes again when the adapter is healthy.

### Inventory Projection - Stale and Conflicted

**What it shows:** Product inventory projections by source, reported quantity, last confirmed time, stale flag, conflict flag, pending reconciliation flag, and resolution timestamps.

**Why it was chosen:** Scenario C requires cross-channel stock visibility when systems are stale or contradictory. This panel makes eventual consistency explicit instead of hiding it behind a single stock number.

**Demo value:** It supports QAS 2 and QAS 6 by showing stale inventory, POS vs WMS conflicts, checkout capping, and reconciliation state.

## Grafana Panels

### Live Counters - Current Integration Health at a Glance

**What it shows:** A compact top row for current health, including failures, retries, dead letters, stale inventory, products synced, and worker throughput.

**Why it was chosen:** The demo needs a quick "is the architecture healthy right now?" view before drilling into details.

### Adapter Failures (5m)

**What it shows:** Adapter calls that ended in failure, error, or timeout over the last five minutes.

**Why it was chosen:** Adapter failures are the clearest first symptom of a degraded external system.

**Architectural link:** Validates the system's ability to observe dependency failure instead of treating it as an invisible background problem.

### Retry Attempts (5m)

**What it shows:** Retry attempts scheduled by the exponential backoff policy.

**Why it was chosen:** Retries show that degradation is being handled automatically and asynchronously.

**Architectural link:** Supports ADR-6, where retry and circuit breaker behavior are the selected recoverability tactics.

### Dead Letters Created (5m)

**What it shows:** Dead-letter records created recently.

**Why it was chosen:** Any non-zero value means automatic recovery has failed and operator action is required.

**Architectural link:** Supports ADR-7 and the requirement to show a recovery or resolution path.

### Inventory Stale (Last Cycle)

**What it shows:** Number of inventory projections marked stale in the latest staleness check.

**Why it was chosen:** Stock freshness is one of the central consistency risks in Scenario C.

**Architectural link:** Supports QAS 2 by proving stale state is detected and surfaced.

### Products Synced (Last Cycle)

**What it shows:** Number of products returned by the inventory stub in the latest sync.

**Why it was chosen:** It distinguishes "inventory is healthy and returning data" from "inventory sync returned nothing."

**Architectural link:** Shows whether the inventory projection model is still being refreshed.

### Worker Throughput (msg/s)

**What it shows:** Message processing rate across the Integration Worker.

**Why it was chosen:** A resilient async architecture still needs a running worker. This panel shows whether queue consumers are processing messages.

**Architectural link:** Supports operability and availability of the integration layer.

### Adapter Call Rate by Outcome (2m Window)

**What it shows:** Per-adapter call rate split into successful and failed outcomes, including timeouts and circuit-open skips.

**Why it was chosen:** It shows whether failure is isolated to one adapter or affecting the whole integration layer.

**Architectural link:** Supports the adapter boundary decision in ADR-3.

### Retries and Dead Letters per Adapter (2m Buckets)

**What it shows:** Retry attempts and dead-letter creation grouped by adapter.

**Why it was chosen:** Operators need to know which external capability is failing: warehouse, shipping, inventory, store operations, or support.

**Architectural link:** Shows fault isolation by adapter and separates recoverable degradation from permanent failure.

### Adapter Latency Percentiles - p50 / p95 / p99 (5m)

**What it shows:** Median and tail latency of adapter calls.

**Why it was chosen:** Slow external systems are a pressure point even when they do not fail completely.

**Architectural link:** Supports QAS 3 by proving slow adapters are measured while checkout remains decoupled through async integration.

### Inventory Sync Cycle Duration - p50 / p95 / p99 / avg (5m)

**What it shows:** Duration of full inventory sync cycles.

**Why it was chosen:** Inventory staleness can come from slow syncs as well as outright failures.

**Architectural link:** Supports consistency and performance monitoring for the inventory projection path.

### Inventory Sync - Products Returned vs Written per Cycle

**What it shows:** Products returned by the inventory stub versus products written to nopCommerce stock.

**Why it was chosen:** It checks that external stock data is actually applied, not just received.

**Architectural link:** Validates cross-channel stock visibility and makes projection gaps visible.

### Inventory Staleness Events and Conflict Auto-Resolution per Cycle

**What it shows:** Stale records marked in the last cycle and conflicts automatically resolved.

**Why it was chosen:** Scenario C requires both detection and recovery from stale or contradictory inventory.

**Architectural link:** Supports QAS 2 and QAS 6: stale state is flagged, conflicts are detected, and reconciliation can clear them.

### Worker Message Processing Rate by Adapter (2m)

**What it shows:** Message processing rate per adapter consumer.

**Why it was chosen:** It shows whether each adapter-specific queue is active.

**Architectural link:** Validates that the Integration Worker is not just running, but actually consuming integration work.

### All Adapter Calls by Outcome (2m)

**What it shows:** All adapter calls grouped by adapter and outcome.

**Why it was chosen:** It provides the broadest view of integration behavior in one panel.

**Architectural link:** Useful during the live demo to compare healthy and degraded adapters side by side.

### Worker - Warnings and Errors

**What it shows:** Warning and error logs from the Integration Worker.

**Why it was chosen:** Metrics show what is happening; logs explain why.

**Architectural link:** Supports operability by exposing retry causes, circuit breaker transitions, dead-letter creation, staleness, and inventory conflicts.

### Worker - All Logs Containing an Order Correlation ID

**What it shows:** Logs tied to order-style correlation ids.

**Why it was chosen:** A single order must be traceable across outbox, adapter calls, retries, and recovery.

**Architectural link:** Supports traceability and the evidence pack requirement.

### Log Volume by Container and Level

**What it shows:** Log volume over time per container and severity level.

**Why it was chosen:** A spike in warnings or errors often indicates degradation before individual tables are inspected.

**Architectural link:** Helps correlate system-wide symptoms with adapter-specific metrics.

## Why These Panels Were Chosen Together

The panels are intentionally not generic infrastructure monitoring. They were chosen to map directly to the architectural claims:

- **Availability:** orders are accepted and integration work is retained even when external systems fail.
- **Recoverability:** retries, circuit breakers, and dead letters show the recovery path.
- **Consistency:** stale and conflicting inventory are made visible and reconciliable.
- **Operability:** operators can see what failed, why it failed, and what action is available.
- **Traceability:** correlation ids and logs connect a business event to its technical path.

Together, the Operations View and Grafana dashboard show normal operation, degradation, and recovery without relying only on code inspection or verbal explanation.
