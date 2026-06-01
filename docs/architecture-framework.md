# Chosen Architecture Framework and How It Was Applied

The project combines two lightweight architecture methods instead of following a
single heavyweight framework end to end.

## 1. Quality-Attribute-Driven Design

The main design driver is quality-attribute reasoning. The report defines QAS
for availability, consistency, performance, recoverability, modifiability, and
inventory reconciliation. Those scenarios are then used to evaluate architecture
choices:

- Checkout must remain available during warehouse failure.
- Inventory must expose stale and conflicting stock state.
- Shipping outages must be retryable and recoverable.
- New operational systems must enter through adapter boundaries.

This is why the target architecture is organized around an Integration Worker,
transactional outbox, adapters, idempotency, retry/circuit breaker, dead letters,
and inventory projections.

## 2. ACDM-Style Decision Documentation

ACDM was used as a documentation and evaluation style for the ADRs:

- identify the architectural issue;
- list candidate solutions;
- compare them against dominant forces;
- record the accepted damage and revisit conditions.

The standalone ADRs in [`adr/`](adr/) follow that structure. Each ADR includes
the alternatives that were considered and a small forces matrix. The global
cross-decision matrix is in [`adr/forces-matrix.md`](adr/forces-matrix.md).

## 3. How the Framework Maps to Repository Artefacts

| Concern | Artefact |
|---|---|
| Quality attribute scenarios | `docs/report/chapters/04-quality-attribute-scenarios.tex` |
| Target architecture | `docs/report/chapters/06-target-architecture.tex` |
| ADR narrative in report | `docs/report/chapters/07-architectural-decisions.tex` |
| Standalone ADRs | `docs/adr/` |
| Forces matrix | `docs/adr/forces-matrix.md` |
| Runtime validation | `docs/evidence/` |
| Observability validation | `docs/observability-plan.md` |

## 4. Practical Outcome

The framework was not used as a template exercise. It directly shaped the code:

- QAS 1 and QAS 4 led to asynchronous integration, outbox, retry, circuit
  breaker, and dead-letter handling.
- QAS 2 and QAS 6 led to `InventoryProjectionRecord`, `IsStale`,
  `ConflictFlag`, and `PendingReconciliation`.
- QAS 5 led to adapter boundaries and the no-shared-database rule.
- QAS 3 led to keeping checkout free of remote warehouse/shipping calls.
