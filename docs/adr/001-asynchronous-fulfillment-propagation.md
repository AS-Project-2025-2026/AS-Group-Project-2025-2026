# ADR 001: Asynchronous Fulfillment Propagation

## Status

Accepted.

## Context

After nopCommerce accepts and records a paid web order, VerdeMart must notify
warehouse and shipping systems. Those systems may be slow, unavailable, or
temporarily inconsistent. QAS 1 requires warehouse failure not to block checkout.
QAS 4 requires failed fulfillment and shipping work to be retried and surfaced
instead of silently dropped.

The baseline nopCommerce extension mechanisms, synchronous plugin calls and
in-process events, are useful for local extension but unsafe as the main
integration path because they keep the customer HTTP request coupled to remote
system availability.

## Decision

Fulfillment and shipping propagation are asynchronous. Checkout records the
order and the integration work, then returns to the customer without waiting for
warehouse or shipping calls. An Integration Worker processes the work later,
using retry, idempotency, and dead-letter handling.

## Options Considered

| Option | What it tries to preserve | Why rejected or accepted |
|---|---|---|
| Synchronous API call during checkout | Immediate warehouse acknowledgement and simpler control flow | Rejected. Checkout latency and success rate would depend on warehouse availability, directly violating QAS 1. |
| In-process nopCommerce event bus | Existing extension mechanism and low implementation cost | Rejected as the external integration backbone. It has no durable retry path if the process restarts after the order commit. |
| Asynchronous fulfillment task | Checkout availability and durable retryable work | Accepted. It isolates checkout from operational systems and gives the worker a durable recovery path. |

## Forces Matrix

| Option | Availability | Consistency | Latency | Supportability | Complexity |
|---|---|---|---|---|---|
| Synchronous API call | Poor: dependency outage blocks checkout | Medium: immediate result if dependency works | Poor under slow warehouse | Poor: failures appear as checkout errors | Low initially |
| In-process event bus | Medium: still tied to web process | Poor for external effects after crash | Good when process is healthy | Poor: no durable operational state | Low |
| Asynchronous task | Strong: checkout remains available | Strong with outbox/idempotency | Strong for customer path; eventual for fulfillment | Strong: pending/retrying state is visible | Medium |

## Consequences

Checkout is independent of warehouse and shipping availability. Fulfillment
state becomes eventually consistent, so operators and support agents need clear
visibility into pending, retrying, failed, and dead-lettered work.

## Accepted Damage

Order fulfillment is not confirmed immediately at checkout. The architecture
must provide intermediate states and idempotency protection to make delayed
delivery understandable and safe.

## Evidence

- `make order` creates a real order and outbox record.
- `docs/evidence/logs/normal-flow.txt` shows asynchronous worker processing.
- `docs/evidence/logs/checkout-not-blocked.txt` demonstrates checkout does not
  wait for warehouse recovery.

## Revisit When

Revisit if normal propagation delay exceeds the QAS 1 bounds, or if a future
commerce requirement needs synchronous reservation confirmation before accepting
payment.
