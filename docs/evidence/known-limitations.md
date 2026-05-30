# Known Limitations — VerdeMart Omnichannel Integration

This document records the known limitations, scope cuts, and boundaries of the implemented
architecture. It is part of the evidence pack for the final delivery of the group assignment.

Each entry includes: what the limitation is, why it exists, and what the architectural
consequence is. Limitations are not defects — they are explicit scope decisions that a
production team would address in later phases.

---

## 1. POS source is injected manually, not via a live stub

**What:** The store/POS system is not backed by a running stub that emits events
automatically. Conflict detection (QAS 6) is demonstrated by inserting a `pos` row
directly into `InventoryProjectionRecord` via SQL (`make conflict-inject`), or via the
`POST /stock/pos-report` endpoint on the inventory stub.

**Why:** The POS context was scoped as an external supporting system in the bounded-context
model (Section 3.3.6). The architectural goal was to show that a POS-originated stock change
reaches the inventory projection layer *without direct database access* — not to reproduce
a full POS lifecycle. The conflict detection and checkout-capping logic are fully implemented;
the missing part is an autonomous POS event emitter.

**Consequence:** The QAS 6 demo requires a manual trigger. The architecture is correct; the
surrounding system representation is reduced to the minimum needed to exercise the pressure
point.

---

## 2. Outbox polling introduces a propagation delay under normal conditions

**What:** The Integration Worker polls the outbox table on a configurable interval
(`Worker__PollingIntervalSeconds`, default 5 s, demo 5 s). An order placed at time T will
not have its fulfillment request dispatched until T + up to 5 s.

**Why:** This is the expected cost of the outbox pattern (ADR 2). Atomic write-with-order
and asynchronous dispatch are the architectural trade-off. The alternative — synchronous
dispatch during checkout — would break QAS 1 and QAS 3.

**Consequence:** Under normal conditions, fulfillment propagation is not instantaneous.
This is documented as an expected characteristic, not a failure. The QAS 1 response measure
("first retry attempt starts within 5 minutes") accounts for this delay.

---

## 3. Auto-resolution of inventory conflicts requires both sources to be active

**What:** The conflict auto-clear logic (`ResolveConflictsAsync`) clears `ConflictFlag`
only when both the `wms` and `pos` rows for the same product agree within the tolerance
threshold for at least 30 minutes. If the POS source is never updated again (because the
stub is not running), the conflict flag persists indefinitely.

**Why:** The design deliberately avoids clearing a conflict on one source's say-so alone
(ADR 5, idempotency and source trust). A conflict requires two disagreeing sources; resolution
requires those same sources to agree.

**Consequence:** In the demo environment, after `make conflict-inject`, the conflict flag
will not auto-clear unless a POS row is also updated to a matching value. The intended
resolution path is operator action via the Operations View, or manual SQL
(`make conflict-clear`). The 30-minute auto-clear window is set for a production pace;
for the demo it can be observed by updating both rows to matching values.

---

## 4. Dead-letter queue has no alert or notification mechanism

**What:** When a fulfillment or shipping request exhausts all retries and moves to
`DeadLetterRecord`, no external alert (email, Slack, webhook) is triggered. The operator
must check the Operations View or run `make db-deadletter` to discover dead-letter entries.

**Why:** Alerting infrastructure (SMTP relay, webhook endpoint, monitoring agent) is outside
the scope of the integration spike. The architectural decision (ADR 7) documents the
dead-letter model and operator intervention flow; the notification channel is deferred
to Phase 4 of the roadmap.

**Consequence:** In a production deployment, dead-letter accumulation would go undetected
without proactive monitoring. The Operations View and `make db-deadletter` provide the
manual visibility path. This is an identified risk in Section 8.2 of the report
("Dead-letter work silently grows without monitoring").

---

## 5. Idempotency records are not cleaned up automatically

**What:** The `IdempotencyRecord` table grows unboundedly. Records are inserted with an
`ExpiresAtUtc` value of 48 hours but there is no scheduled cleanup task that deletes
expired rows.

**Why:** The cleanup task was not included in the integration spike scope. The expiry
column is present and populated; a `DELETE WHERE ExpiresAtUtc < GETUTCDATE()` task
would be straightforward to add.

**Consequence:** In a long-running environment the table will accumulate rows. For the
demo environment (`docker compose down -v` on teardown) this has no practical effect.
In production this would require a periodic cleanup job.

---

## 6. Circuit breaker state is not persisted across worker restarts

**What:** The in-memory `CircuitBreakerRegistry` resets to `Closed` when the
`integration_worker` container restarts. The `CircuitBreakerStateRecord` table is updated
by the worker during normal operation, but on startup the registry always initialises fresh
from code — it does not read the last persisted state from the database.

**Why:** The registry is an in-process resilience primitive (Polly). Loading state from
the database on startup would require a seeded initialisation step and add complexity to
the worker startup path. For the spike, the operational visibility goal (state is visible
in the Operations View) is met; the recovery continuity goal (resume from last known state)
is deferred.

**Consequence:** After a worker restart following an open circuit, the worker will
immediately attempt the adapter call rather than waiting for the remaining cooldown window.
This could cause a short burst of retries before the circuit opens again. The `UpdatedAtUtc`
timestamp in the database allows an operator to see when the state last changed.

---

## 7. Performance measurements are under demo-scale load only

**What:** The QAS 3 response measures (checkout p95 < 2 s, order-status p95 < 1 s) have
not been validated under sustained load (e.g., 5× normal traffic as specified in the
scenario). The demo environment runs on a single Docker host with shared resources.

**Why:** Load testing infrastructure (k6, Locust, JMeter) was not in scope for the
integration spike. The architectural argument for performance — async work outside the
checkout path — is structural and visible in the code; the numeric validation requires
a dedicated load test environment.

**Consequence:** The performance claim is architectural (checkout does not synchronously
wait for warehouse or shipping) rather than measured. The evidence pack contains structural
evidence (async dispatch, no blocking adapter calls in the checkout path) but not latency
percentile measurements.

---

## 8. The Integration Worker shares the nopCommerce SQL Server instance

**What:** The `integration_worker` connects to the same SQL Server instance as
`nopcommerce_web`. It reads from `OutboxRecord`, `DeadLetterRecord`, and
`InventoryProjectionRecord` tables that live in the `NopCommerce` database.

**Why:** ADR 3 prohibits external systems from reading or writing nopCommerce tables
directly. The Integration Worker is not an external system — it is the integration
coordination layer that owns those tables. The tables it reads are integration metadata
tables added for Scenario C, not nopCommerce core tables (`Order`, `Product`, etc.).

**Consequence:** This is not a boundary violation. However, in a full production deployment
the integration tables could be moved to a dedicated integration database to improve
deployment independence. The current design keeps operational complexity low while
preserving the boundary rule that matters: no warehouse adapter, shipping adapter, or
POS system reads nopCommerce core tables.

---

## 9. nopCommerce storefront does not surface stale or conflict state to customers

**What:** The `IsStale` and `ConflictFlag` fields on `InventoryProjectionRecord` are
visible in the Operations View (admin) but are not surfaced on the product pages or
cart validation flow visible to customers.

**Why:** Exposing inventory freshness state on the storefront requires changes to the
product listing and cart controllers in `Nop.Web`, which is outside the integration spike
scope. The architecture defines this as a projection that *can* be consumed by the
storefront; the wire-up to the customer-facing UI is a Phase 3/4 item on the roadmap.

**Consequence:** Customers see the `Product.StockQuantity` value, which is already
updated to the lower (conservative) quantity when a conflict is detected. They are
protected from overselling but do not see a "stock may be outdated" indicator on the
product page.

---

## Summary table

| # | Limitation | Architectural impact | Phase to address |
|---|---|---|---|
| 1 | POS source requires manual injection | Demo trigger is manual; detection logic is correct | Phase 3 |
| 2 | Outbox polling adds propagation delay | Expected cost of async pattern; documented in QAS 1 | By design |
| 3 | Conflict auto-clear needs both sources active | Manual resolution path exists via Operations View | Phase 3 |
| 4 | No dead-letter alert/notification | Operator must poll Operations View | Phase 4 |
| 5 | Idempotency records not cleaned up | Table grows in long-running environments | Phase 4 |
| 6 | Circuit breaker resets on worker restart | Short burst of retries after restart | Phase 2 follow-up |
| 7 | No load-test measurements | Performance claim is structural, not measured | Phase 4 |
| 8 | Worker shares nopCommerce SQL instance | Not a boundary violation; integration tables are separate | Optional Phase 4 |
| 9 | Stale/conflict state not shown to customers | Customers see conservative quantity; no UI indicator | Phase 3/4 |
