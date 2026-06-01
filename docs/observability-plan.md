# VerdeMart Observability Plan

Grafana is the primary observability surface for architectural-driver metrics. The admin Operations View remains useful for operator actions such as requeueing dead letters, but metrics and trends should be inspected in Grafana.

## Architectural Drivers

- Availability: outbox backlog, failed outbox records, published records in the last 24 hours, and publish latency.
- Resilience: open or half-open circuit breakers, retrying outbox records, adapter failures, adapter retries, and adapter call latency.
- Consistency: stale inventory projections, conflicted projections, pending reconciliations, sync duration, and products updated by the latest sync.
- Operability: new dead letters, requeued dead letters, oldest unresolved dead-letter age, worker message outcomes, and dead letters created by adapter.

## Implementation

- `nopcommerce_web` exposes `/metrics` using `prometheus-net.AspNetCore`.
- `integration_worker` exposes `/metrics` on port `9100`.
- Prometheus scrapes web, worker, and itself every 10 seconds.
- Grafana is provisioned with a Prometheus datasource and a VerdeMart architectural-drivers dashboard.
- Docker Compose starts Prometheus on `http://localhost:9090` and Grafana on `http://localhost:3000`.

## Demo Workflow

1. Run `make up`.
2. Open Grafana at `http://localhost:3000` with `admin/admin`.
3. Open the VerdeMart dashboard.
4. Generate signals with `make order`, `make warehouse-fail`, `make shipping-fail`, `make stale-start`, or `make conflict-inject`.
5. Use `make observability` or `make metrics` to check scrape target health from the terminal.
