# ADR 003: Adapter Pattern with No Shared Database Across External Boundaries

## Status

Accepted.

## Context

VerdeMart integrates with warehouse, inventory, store/POS, shipping, and support
systems. Each has its own protocol, data shape, availability profile, and
ownership. Allowing those systems to read or write the nopCommerce database
directly would make the commerce schema a shared integration contract, which
would block independent change and blur data ownership.

QAS 5 requires new operational channels to be integrated through a bounded
adapter without changing checkout logic.

## Decision

Each external system is reached through a dedicated adapter or stub-facing
client. Adapters translate between VerdeMart integration contracts and external
system-specific APIs. No external system reads or writes nopCommerce tables
directly. State that must be visible in the commerce core is stored through
explicit records or projections, such as `InventoryProjectionRecord`.

## Options Considered

| Option | What it tries to preserve | Why rejected or accepted |
|---|---|---|
| Direct database access | Shortest read/write path and fewer components | Rejected. It hides ownership coupling and makes schema changes cross-team incidents. |
| Owner API for every access | Strict ownership and synchronous correctness | Partially useful, but rejected as the default for high-volume operational flows because it couples availability and latency to the owner. |
| Replicated projection/read model | Local reads with explicit freshness | Accepted for inventory state. It supports stale/conflict metadata instead of pretending all reads are authoritative. |
| Single generic integration service | Fewer deployable components | Rejected. It mixes unrelated failure modes and weakens adapter ownership. |
| Dedicated adapters | Clear ownership, replaceability, and protocol isolation | Accepted. One adapter can change without touching checkout or other adapters. |

## Forces Matrix

| Option | Data Ownership | Modifiability | Availability | Performance | Operational Complexity |
|---|---|---|---|---|---|
| Direct DB access | Poor | Poor | Medium until schema changes | Strong read speed | Hidden high burden |
| Owner API only | Strong | Strong | Medium: owner outage affects reads | Medium/poor for high-volume reads | Medium |
| Replicated projection | Strong if freshness is visible | Strong | Strong for local reads | Strong | Medium: replay, repair, staleness |
| Generic integration service | Medium | Medium/poor as scope grows | Medium | Medium | High concentration of concerns |
| Dedicated adapters | Strong | Strong | Strong with retry/circuit breaker | Medium | Medium per adapter |

## Consequences

External systems are replaceable behind explicit boundaries. New integrations
add an adapter and contract mapping instead of touching checkout. Inventory and
conflict state remain explicit through projections and source metadata.

## Accepted Damage

Every new external system requires adapter work. Projections can become stale,
so freshness, conflict flags, and reconciliation state must be visible.

## Evidence

- `src/Integration/Nop.IntegrationWorker/Clients/*Client.cs`
- `src/Stubs/*Stub/`
- `src/Libraries/Nop.Core/Domain/Integration/InventoryProjectionRecord.cs`
- `docs/evidence/logs/staleness-evidence.txt`
- `docs/evidence/logs/conflict-evidence.txt`

## Revisit When

Revisit if the adapter count grows beyond what the team can operate, or if a
specific external system becomes stable and close enough to justify a different
integration contract.
