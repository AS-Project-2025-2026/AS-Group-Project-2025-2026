# ADR 005: Idempotency Key Strategy for Integration Messages

## Status

Accepted.

## Context

The outbox gives at-least-once delivery. A message may be delivered more than
once if the worker retries after a timeout, loses an acknowledgement, or restarts
after a side effect but before marking the record complete. Without duplicate
handling, the warehouse could receive duplicate fulfillment requests or the
shipping provider could create duplicate labels.

## Decision

Every integration message carries a stable `IdempotencyKey` generated at message
creation time and stored with the outbox/dead-letter record. Consumers and
adapters use that key to detect duplicate work and return the previous outcome
without repeating the side effect. Outcomes are retained for a bounded window
of 48 hours in the spike design.

## Options Considered

| Option | Where duplicate handling lives | Why rejected or accepted |
|---|---|---|
| Trust the broker | Broker delivery semantics | Rejected. At-least-once delivery still permits duplicates. |
| Deduplicate in memory | Worker process memory | Rejected. It fails on restart, scale-out, or message rebalancing. |
| Use order ID as key | Existing business identifier | Rejected as a general rule. One order can create multiple side-effecting integration messages. |
| Check destination state | Adapter queries the external system before acting | Partially useful but not universal. Some external systems do not expose reliable lookup by request. |
| Stable idempotency key | Message contract and idempotency record | Accepted. It makes duplicate detection explicit and portable across adapters. |

## Forces Matrix

| Option | Consistency | Restart Safety | Adapter Independence | Supportability | Complexity |
|---|---|---|---|---|---|
| Trust broker | Poor | Poor | Medium | Poor | Low |
| In-memory dedupe | Poor after restart | Poor | Medium | Poor | Low |
| Order ID key | Medium for one message per order | Strong | Poor for split workflows | Medium | Low |
| Destination state check | Medium/strong where possible | Strong | Poor: depends on destination API | Medium | Medium/high |
| Stable idempotency key | Strong | Strong | Strong | Strong: queryable key/outcome | Medium |

## Consequences

Duplicate delivery does not create duplicate business effects when the adapter
honors the idempotency contract. Idempotency becomes part of the integration
message contract, not a hidden implementation detail.

## Accepted Damage

Every adapter must implement or emulate duplicate detection. The key/outcome
store requires retention and cleanup. Duplicates arriving after the retention
window may be treated as new work.

## Evidence

- `src/Libraries/Nop.Core/Domain/Integration/IdempotencyRecord.cs`
- `src/Integration/Nop.IntegrationWorker/Data/WorkerDataService.cs`
- Worker logs include `IdempotencyKey=...` for adapter calls.

## Revisit When

Revisit if retry windows exceed the retention window, or if external systems
provide stronger native idempotency contracts that should be used directly.
