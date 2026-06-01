# Presentation Demo Script — VerdeMart Omnichannel

> **Total demo time: ~8 minutes**
> Keep two terminals open side by side: one for commands, one for logs.
> Browser open at `http://localhost:8080` and `http://localhost:8080/Admin/Operations/List`.

---

## Before the presentation (do this 10 minutes before)

```bash
# Start everything
make up

# Confirm all services are healthy
make status

# Clear any leftover demo data and set a clean baseline
make demo-healthy

# Open logs in a second terminal (leave this running the whole demo)
make logs
```

**Check:**
- `http://localhost:8080` loads the storefront
- `http://localhost:8080/Admin/Operations/List` shows green outbox records, all circuit breakers Closed, no dead letters

---

## Scene 1 — Normal Flow (1 min)

**What to say:**
> "This is VerdeMart's storefront running on nopCommerce. When a customer places an order, the checkout completes instantly — the warehouse is never called synchronously. Instead, an outbox record is written atomically with the order, and the Integration Worker picks it up in the background."

**Commands:**
```bash
make order
```

**Then show in browser:** `http://localhost:8080/Admin/Operations/List`

**What to point at:**
- New outbox record with status `Published`
- All circuit breakers `Closed`
- The worker log shows: `Outbox picked up` → `Adapter call succeeded`

**What to say:**
> "The order was accepted, the fulfillment request reached the warehouse, and the checkout was never waiting on it. This is ADR 1 — async fulfillment — and ADR 2 — the transactional outbox."

---

## Scene 2 — Warehouse Failure + Circuit Breaker (3 min)

**What to say:**
> "Now we simulate the warehouse going down — this is our mandatory pressure point. Watch what happens to the checkout and to the integration layer separately."

**Step 1 — break the warehouse:**
```bash
make warehouse-fail
```

**Step 2 — place an order while it's down:**
```bash
make order
```

**What to point at in the logs (leave 30–60 seconds for this to play out):**
- `Adapter call failed` — first attempt
- `Retry scheduled` with `DelaySeconds` doubling each time (2s → 4s → 8s...)
- After 3 failures: `Circuit breaker OPENED`
- Then: `Circuit breaker OPEN — skipping call`

**What to say:**
> "The checkout completed immediately — the customer got their confirmation. Behind the scenes the warehouse adapter is failing, the exponential backoff is kicking in, and after 3 failures the circuit breaker opens to stop hammering a system that's clearly down. This is ADR 6."

**Show in Operations View:**
- Outbox record in `Retrying` state with retry count and next attempt time
- Warehouse circuit breaker showing `Open`

**What to say:**
> "Operators can see exactly what's happening. They don't need to query the database — the Operations View surfaces retry count, failure reason, and when the next probe will happen."

**Step 3 — recover the warehouse:**
```bash
make warehouse-recover
```

**Wait ~30 seconds, then point at logs:**
- `Circuit breaker HALF-OPEN probe`
- `Adapter call succeeded`
- `Circuit breaker CLOSED`

**Show in Operations View:**
- Outbox record flips to `Published`
- Circuit breaker back to `Closed`

**What to say:**
> "Once the warehouse comes back, the circuit breaker sends a single probe. It succeeds, the circuit closes, and the pending order is dispatched automatically. No operator action required. This is the recovery story for QAS 1."

---

## Scene 3 — Shipping Outage → Dead Letter → Requeue (2 min)

**What to say:**
> "Now we show what happens when retries are exhausted — the shipping provider goes down and stays down long enough to exceed our maximum retry attempts."

**Step 1 — break shipping:**
```bash
make shipping-fail
```

**Step 2 — place an order:**
```bash
make order
```

**Wait for retries to exhaust (with demo params this is faster). Point at logs:**
- `Adapter call failed` (shipping)
- `Dead-letter created` — adapter=shipping

**Show in Operations View → Dead Letters tab:**
- New entry with `EscalationState = New`, failure reason, correlation ID

**What to say:**
> "The fulfillment side succeeded — the warehouse got the order. But the shipping label couldn't be created. Rather than silently dropping it, the system creates a dead-letter record. The paid order is never lost. This is ADR 7."

**Step 3 — recover shipping and requeue:**
```bash
make shipping-recover
```

**In Operations View → Dead Letters tab:**
- Click **Requeue** on the failed record

**Point at logs:**
- Worker picks up the requeued record
- `Adapter call succeeded` (shipping)

**Show Operations View:**
- `EscalationState = Requeued`, `ResolvedAtUtc` set
- New outbox record `Published`

**What to say:**
> "One button in the UI. No scripting, no database access. The operator requeues it, the worker dispatches it, and the shipping label is created. This closes the recovery loop for QAS 4."

---

## Scene 4 — Inventory Conflict / Staleness (2 min)

> Pick **one** of these two — don't do both, no time. Conflict is more visually striking.

### Option A — Inventory Conflict (recommended)

**What to say:**
> "Our last scenario: the WMS says we have 50 units of a product, but the store's POS reports 3 — someone's been selling in-store. The difference exceeds our tolerance of 2 units."

```bash
make conflict-inject
```

**Wait ~15 seconds, then show Operations View → Inventory tab:**
- `ConflictFlag = true` on affected rows
- `PendingReconciliation = true`
- `StockQuantity` in nopCommerce updated to the lower (conservative) value

**What to say:**
> "The system detected the conflict within one sync cycle, flagged it, and capped the checkout quantity at the lower number to prevent overselling. The operator can see it here and investigate. This is QAS 6."

**To clean up:**
```bash
make conflict-clear
```

### Option B — Inventory Staleness

**What to say:**
> "What if the WMS stops sending updates entirely? After 30 seconds without a confirmed stock update, we mark those records as stale."

```bash
make stale-start
```

**Wait ~35 seconds, show Operations View → Inventory tab:**
- Rows highlighted with `IsStale = true`

**What to say:**
> "Operators can see which stock figures are stale and act before a customer hits an oversell. When the WMS comes back the flag clears automatically on the next sync cycle."

```bash
make stale-stop
```

---

## If things go wrong

| Problem | Fix |
|---|---|
| Operations View shows nothing | `make demo-healthy` to reset to clean state |
| Circuit breaker not opening | Check `make logs` — may need to place 2–3 more orders |
| Logs too noisy | Filter in the terminal: `make logs \| grep -E "OPEN\|CLOSED\|Dead-letter\|succeeded\|failed"` |
| Stack not responding | `make down && make up` |
| Want a full reset | `make reset && make up` |

---

## Useful URLs

| URL | What it shows |
|---|---|
| `http://localhost:8080` | VerdeMart storefront |
| `http://localhost:8080/Admin/Operations/List` | Operations View (outbox, dead letters, circuit breakers, inventory) |
| `http://localhost:15672` | RabbitMQ management (guest/guest) — queues and message rates |
| `http://localhost:3000` | Grafana dashboards — metrics and logs |

---

## One-line summary for each scene

| Scene | One sentence |
|---|---|
| Normal flow | Checkout completes; warehouse notified asynchronously via outbox. |
| Warehouse failure | Retries, circuit breaker opens, checkout never blocked, auto-recovers. |
| Shipping dead letter | Max retries exhausted, dead letter created, operator requeues, resolved. |
| Inventory conflict | WMS/POS disagree, conflict flagged, checkout capped at lower quantity. |
