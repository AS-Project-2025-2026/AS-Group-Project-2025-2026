# ADR 007: Dead-Letter Queue and Operator Intervention Model

## Status

Accepted.

## Context

After retries are exhausted, a message has failed in the automated path.
Discarding it is unacceptable because fulfillment and shipping messages represent
paid customer orders. Retrying forever hides permanent failure and can create
unbounded queues.

## Decision

Exhausted integration messages are stored as `DeadLetterRecord` rows with the
original payload, idempotency key, correlation ID, adapter name, failure reason,
first/last attempt timestamps, and escalation state. The Operations View shows
these records and provides a requeue action. Operators can requeue after the
dependency recovers or resolve the issue manually outside the automated path.

## Options Considered

| Option | What it tries to preserve | Why rejected or accepted |
|---|---|---|
| Drop message after max retry | Simplicity and bounded queues | Rejected. It silently abandons paid-order work. |
| Retry forever | Avoid manual operator action | Rejected. It hides permanent failure and creates unbounded backlog. |
| Broker-only DLQ | Native RabbitMQ dead-letter behavior | Rejected as the operator-facing model. It requires broker access and separates evidence from commerce operations. |
| Database dead-letter with Operations View | Durable audit trail and operator recovery | Accepted. It keeps failed work visible beside outbox and circuit state. |

## Forces Matrix

| Option | Data Safety | Supportability | Recoverability | Auditability | Operational Complexity |
|---|---|---|---|---|---|
| Drop message | Poor | Poor | Poor | Poor | Low |
| Retry forever | Medium: message remains | Poor: permanent failures hidden | Poor/medium | Medium | High over time |
| Broker-only DLQ | Strong | Medium/poor for business operators | Medium | Medium | Medium |
| DB dead-letter + UI | Strong | Strong | Strong via requeue/manual resolution | Strong | Medium |

## Consequences

Permanent failures become visible operational work instead of invisible technical
events. The same admin surface shows outbox, dead letters, circuit breakers, and
inventory projection state.

## Accepted Damage

Some failures require human review. The dead-letter table is integration-critical
and must be monitored, backed up, and eventually archived.

## Evidence

- `src/Libraries/Nop.Core/Domain/Integration/DeadLetterRecord.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Views/Operations/List.cshtml`
- `docs/evidence/screenshots/09-deadletter-created.png`
- `docs/evidence/screenshots/10-deadletter-requeued1.png`
- `docs/evidence/screenshots/10-deadletter-requeued2.png`

## Revisit When

Revisit if dead-letter volume makes manual review unsustainable, or if operations
needs automated triage, severity routing, or escalation policies.
