# ADR Forces Matrix

This matrix summarizes the main alternatives considered across the ADRs and
scores them against the dominant architectural forces. It follows the evaluation
forces style from the 04.02 data movement and resilience patterns slides: compare
solutions before selecting the pattern.

Legend: `++` strong fit, `+` acceptable fit, `0` neutral/mixed, `-` weak fit,
`--` poor fit.

## Forces

| Force | Meaning in VerdeMart |
|---|---|
| Availability / fault isolation | Checkout and core commerce must remain available when warehouse, shipping, or inventory systems fail. |
| Consistency / data safety | Paid orders, fulfillment work, inventory freshness, and duplicate handling must not be silently wrong. |
| Modifiability / ownership | New systems should be integrated through bounded adapters without hidden database coupling. |
| Operability / supportability | Operators must see degraded states, retries, dead letters, and reconciliation work without database spelunking. |
| Performance / latency | Customer-facing paths must stay fast; background work can be eventually consistent if bounded and observable. |
| Complexity / delivery risk | The design must be buildable inside the assignment scope and maintainable by the team. |

## Global Matrix

| Decision area | Option | Availability | Consistency | Modifiability | Operability | Performance | Complexity | Verdict |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Fulfillment propagation | Synchronous warehouse call in checkout | -- | + | - | - | -- | + | Rejected: violates QAS 1. |
| Fulfillment propagation | In-process nopCommerce event | 0 | - | 0 | - | + | ++ | Rejected for external integration: no durable retry. |
| Fulfillment propagation | Async integration task | ++ | + | + | ++ | ++ | 0 | Accepted in ADR 001. |
| Durable publication | Publish after DB commit | - | -- | 0 | - | + | ++ | Rejected: dual-write message loss. |
| Durable publication | Distributed transaction / 2PC | 0 | ++ | - | 0 | - | -- | Rejected: too heavy for RabbitMQ/nopCommerce spike. |
| Durable publication | Transactional outbox | ++ | ++ | + | ++ | + | 0 | Accepted in ADR 002. |
| Data boundaries | Shared nopCommerce database access | 0 | - | -- | - | ++ | + | Rejected: hidden schema and ownership coupling. |
| Data boundaries | Owner API only | 0 | + | ++ | 0 | - | 0 | Rejected as default for high-volume operational state. |
| Data boundaries | Replicated projection plus adapters | ++ | + | ++ | + | ++ | 0 | Accepted in ADR 003 for inventory and external state. |
| Commerce core | Extract order service | - | - | 0 | 0 | 0 | -- | Rejected: distributed checkout risk. |
| Commerce core | Full microservices rewrite | 0 | - | + | 0 | 0 | -- | Rejected: high delivery risk. |
| Commerce core | Retain nopCommerce plus integration layer | + | ++ | + | + | + | + | Accepted in ADR 004. |
| Duplicate handling | Trust broker delivery | 0 | -- | 0 | - | + | ++ | Rejected: at-least-once delivery permits duplicates. |
| Duplicate handling | In-memory dedupe | 0 | -- | 0 | - | + | + | Rejected: fails on restart/scale-out. |
| Duplicate handling | Stable idempotency key | + | ++ | + | ++ | + | 0 | Accepted in ADR 005. |
| Dependency failure | Fixed retry only | - | 0 | 0 | 0 | - | + | Rejected: retry storm during recovery. |
| Dependency failure | Exponential backoff only | + | + | 0 | 0 | 0 | 0 | Insufficient alone: no open/degraded state. |
| Dependency failure | Backoff plus circuit breaker | ++ | + | 0 | ++ | + | 0 | Accepted in ADR 006. |
| Permanent failures | Drop after max retry | + | -- | 0 | -- | + | ++ | Rejected: silently abandons paid-order work. |
| Permanent failures | Retry forever | - | 0 | 0 | -- | -- | - | Rejected: hides permanent failure and grows backlog. |
| Permanent failures | Broker-only DLQ | + | + | 0 | 0 | + | 0 | Rejected as operator-facing recovery path. |
| Permanent failures | DB dead-letter plus Operations View | + | ++ | + | ++ | + | 0 | Accepted in ADR 007. |

## Main Trade-Offs

The selected architecture intentionally accepts eventual consistency in
fulfillment and inventory projection state. In exchange, checkout remains
available and paid-order work becomes durable, retryable, observable, and
recoverable.

The design also accepts extra integration infrastructure: outbox, worker,
idempotency records, circuit breakers, dead letters, and projections. This is
the cost of making failures explicit instead of letting operational dependencies
leak into checkout or the nopCommerce schema.
