# ADR 006: Retry Policy and Circuit Breaker for Adapter Calls

## Status

Accepted.

## Context

Remote adapters fail in different ways: transient timeouts, short outages,
sustained outages, and slow recovery. Retrying too aggressively can overload a
recovering dependency. Retrying forever hides permanent failures. QAS 1 and QAS
4 require recovery without blocking checkout and without losing failed work.

## Decision

Adapter calls use bounded retry with exponential backoff and a per-adapter
circuit breaker. The default policy is:

- Initial delay: 2 seconds.
- Backoff multiplier: 2.
- Maximum delay: 5 minutes in the architecture, accelerated to 30 seconds for demo.
- Maximum attempts: 10 in the architecture, accelerated to 5 for demo.
- Circuit opens after 5 consecutive failures in the architecture, accelerated to 3 for demo.
- Circuit cooldown: 5 minutes in the architecture, accelerated to 30 seconds for demo.

When the cooldown elapses, one half-open probe is attempted. Success closes the
circuit; failure reopens it. Exhausted messages move to ADR 007 dead-letter
handling.

## Options Considered

| Option | What it tries to protect | Why rejected or accepted |
|---|---|---|
| No retry | Fast failure and simple implementation | Rejected. Transient outages would require manual recovery for normal failures. |
| Fixed retry interval | Simplicity and predictable retry timing | Rejected. It can overload recovering systems and creates needless traffic during long outages. |
| Linear backoff | Reduced retry pressure compared with fixed interval | Rejected. It still converges to high pressure faster than exponential backoff. |
| Exponential backoff only | Dependency protection during transient failure | Partially useful but incomplete. It still keeps trying during sustained outages. |
| Exponential backoff plus circuit breaker | Bounded retry, dependency protection, visible degraded state | Accepted. It balances recoverability with fault isolation and operator visibility. |

## Forces Matrix

| Option | Fault Isolation | Recoverability | Operator Visibility | Latency to Recovery | Operational Complexity |
|---|---|---|---|---|---|
| No retry | Strong for dependency, poor for business | Poor | Medium: failures are immediate | Poor/manual | Low |
| Fixed retry | Poor under outage | Medium | Medium | Medium | Low |
| Linear backoff | Medium | Medium | Medium | Medium | Medium |
| Exponential only | Medium/strong | Strong for transient faults | Medium | Medium | Medium |
| Exponential + breaker | Strong | Strong with half-open probe | Strong: circuit state is visible | Medium, bounded by cooldown | Medium/high |

## Consequences

Temporary failures are retried automatically. Sustained failures stop hammering
the dependency and are visible as open circuit-breaker state in the Operations
View and observability dashboard.

## Accepted Damage

Under sustained failure, work waits until retries/cooldowns complete or reaches
dead-letter. Backoff and circuit parameters must be tuned per adapter and made
observable.

## Evidence

- `src/Integration/Nop.IntegrationWorker/Resilience/`
- `docs/evidence/logs/warehouse-failure.txt`
- `docs/evidence/logs/warehouse-recovery.txt`
- `docs/evidence/screenshots/07-warehouse-failure-cb-open.png`

## Revisit When

Revisit if dead-letter delay is too slow for a business process, or if adapter
health signals become strong enough to drive dynamic retry/circuit parameters.
