# ADR 002: Transactional Outbox for Durable Integration Publishing

## Status

Accepted.

## Context

ADR 001 makes fulfillment asynchronous, but asynchronous publication creates a
dual-write problem: the order is stored in the database and a message must also
reach RabbitMQ. If the process crashes or the broker is unavailable between
those two operations, the order can be confirmed while the warehouse never gets
the work.

## Decision

nopCommerce writes an `OutboxRecord` in the same database transaction as the
order. The Integration Worker polls pending outbox rows, publishes the message
to RabbitMQ, and marks the row as published. This gives an atomic local write:
the order and its integration task either both exist or neither exists.

## Options Considered

| Option | What it tries to protect | Why rejected or accepted |
|---|---|---|
| Publish after commit | Simple implementation and immediate broker use | Rejected. A crash or broker outage after DB commit loses the integration message. |
| Publish before commit | Broker visibility before the DB write finishes | Rejected. It can publish a false fact for an order that later rolls back. |
| Distributed transaction / 2PC | Atomic commit across database and broker | Rejected. Too much infrastructure and coupling for this spike; not aligned with RabbitMQ/nopCommerce simplicity. |
| Transactional outbox | Atomic local commit plus reliable later publication | Accepted. It moves broker failure out of checkout and makes missing work queryable. |
| Event sourcing | Events as the system of record | Rejected. It would be a major modeling rewrite rather than an incremental architectural evolution. |

## Forces Matrix

| Option | Consistency | Fault Isolation | Observability | Delivery Effort | Operational Burden |
|---|---|---|---|---|---|
| Publish after commit | Poor: lost message gap | Poor: broker affects order completion path | Poor: missing work is invisible | Low | Low initially, high during incidents |
| Publish before commit | Poor: false event gap | Poor | Medium: broker has a message but state may disagree | Low | High reconciliation burden |
| Distributed transaction | Strong | Medium: broker still in transaction path | Medium | High | High |
| Transactional outbox | Strong for local atomicity | Strong: broker can be down during checkout | Strong: pending rows are inspectable | Medium | Medium: relay, cleanup, monitoring |
| Event sourcing | Strong if fully adopted | Strong | Strong | Very high | High |

## Consequences

No confirmed order silently loses its integration task because of a transient
broker or process failure. At-least-once delivery is expected, so downstream
consumers and adapters must be idempotent.

## Accepted Damage

The outbox table becomes integration-critical infrastructure. Polling introduces
a bounded propagation delay and requires monitoring for backlog, retries, and
old unpublished rows.

## Evidence

- `src/Libraries/Nop.Core/Domain/Integration/OutboxRecord.cs`
- `src/Libraries/Nop.Services/Integration/IntegrationRecordService.cs`
- `src/Integration/Nop.IntegrationWorker/Services/OutboxPollingService.cs`
- `docs/evidence/screenshots/05-normal-flow.png`

## Revisit When

Revisit if outbox polling delay exceeds the QAS threshold, or if the platform
adopts a native transactional messaging primitive that removes the relay.
