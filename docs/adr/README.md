# Architectural Decision Records

This folder contains standalone ADRs for the VerdeMart omnichannel architecture.
The report PDF still contains the narrative version, but these files make each
decision directly navigable from the repository.

## Decision Index

| ADR | Decision | Status | Main forces |
|---|---|---|---|
| [ADR 001](001-asynchronous-fulfillment-propagation.md) | Asynchronous fulfillment propagation | Accepted | Availability, fault isolation, supportability |
| [ADR 002](002-transactional-outbox.md) | Transactional outbox for durable integration publishing | Accepted | Consistency, fault isolation, observability |
| [ADR 003](003-adapter-boundaries-no-shared-db.md) | Adapter pattern with no shared database across external boundaries | Accepted | Data ownership, modifiability, deployability |
| [ADR 004](004-retain-nopcommerce-monolith.md) | Retain nopCommerce monolith as the commerce core | Accepted | Operational complexity, delivery risk, reversibility |
| [ADR 005](005-idempotency-key-strategy.md) | Idempotency key strategy for integration messages | Accepted | Consistency, duplicate handling, supportability |
| [ADR 006](006-retry-circuit-breaker.md) | Retry policy and circuit breaker for adapter calls | Accepted | Fault isolation, recoverability, operational complexity |
| [ADR 007](007-dead-letter-operator-intervention.md) | Dead-letter queue and operator intervention model | Accepted | Supportability, recoverability, auditability |

## Forces Matrix

The cross-ADR evaluation matrix is in [forces-matrix.md](forces-matrix.md).
It follows the "evaluation forces matrix" style from the 04.02 data movement
and resilience patterns slides: compare the proposed solutions against the
dominant architectural forces before explaining why the accepted option wins.

## Traceability

| Source | Location |
|---|---|
| Report chapter | `docs/report/chapters/07-architectural-decisions.tex` |
| Feasibility evidence | `docs/evidence/` |
| Demo commands | `Makefile` and `docs/evidence/demo-capture-guide.md` |
| Observability plan | `docs/observability-plan.md` |
