# Presentation Demo Script — VerdeMart Omnichannel

> **Total demo time: ~6 minutes**
> Two terminals open side by side: one for commands, one for logs (`make logs`).
> Three browser tabs ready: storefront, Operations View, Grafana.

---

## Before the presentation (do this 10 minutes before)

```bash
# Start everything
make up

# Confirm all services are healthy
make status

# Clean baseline
make demo-healthy

# Open logs in a second terminal — leave running the whole demo
make logs
```

**Check all three tabs are working:**
- `http://localhost:8080` — storefront loads
- `http://localhost:8080/Admin/Operations/List` — green outbox records, all circuit breakers Closed, no dead letters
- `http://localhost:3000` — Grafana, log in `admin` / `admin`, open **VerdeMart — Integration Observability**, scroll to the **Resilience** row and leave it there

---

## Scene 1 — Warehouse Failure → Circuit Breaker → Recovery (3 min)

**What to say (while showing the Operations View baseline):**
> "Before we break anything — this is the normal state. Orders are flowing, outbox records are Published, all circuit breakers are Closed. The checkout never calls the warehouse directly; it writes an outbox record atomically with the order, and the Integration Worker dispatches it asynchronously. That's why warehouse availability is irrelevant to the customer."

**Step 1 — break the warehouse:**
```bash
make warehouse-fail
```

**Step 2 — place an order:**
```bash
make order
```

**Point at the logs and wait ~30 seconds:**
- `Adapter call failed` — first attempt
- `Retry scheduled` with `DelaySeconds` doubling (2s → 4s → 8s...)
- After 3 failures: `Circuit breaker OPENED`
- `Circuit breaker OPEN — skipping call`

**What to say:**
> "The checkout completed immediately — the customer has their confirmation. Behind the scenes the warehouse is failing, exponential backoff is kicking in, and after 3 consecutive failures the circuit breaker opens. It stops hammering a system that's clearly down. This is ADR 6."

**Switch to Operations View:**
- Outbox record in `Retrying` state with retry count and next attempt time
- Warehouse circuit breaker `Open`

**What to say:**
> "Operators see exactly what's happening — retry count, failure reason, when the next probe fires. No database query needed."

**Switch to Grafana (`http://localhost:3000`):**
- **"Adapter Failures (5m)"** stat — climbing
- **"Retry Attempts (5m)"** stat — climbing
- **"Adapter Call Rate by Outcome"** timeseries — visible failure spike

**What to say:**
> "And here's the architectural evidence. Prometheus scrapes the Integration Worker every 10 seconds. You can see the exact moment the warehouse went down — failures spike, retries climb, successful calls drop to zero. Operability isn't just a claim — it's visible."

**Step 3 — recover the warehouse:**
```bash
make warehouse-recover
```

**Wait ~30 seconds, point at logs:**
- `Circuit breaker HALF-OPEN probe`
- `Adapter call succeeded`
- `Circuit breaker CLOSED`

**Stay on Grafana:**
- Failure stat drops back to zero
- Success calls resume on the timeseries — recovery timestamp visible on the chart

**What to say:**
> "The probe succeeds, the circuit closes, and the pending order is dispatched automatically. No operator action. You can see the full lifecycle — degradation, sustained failure, recovery — right on the dashboard. This is the QAS 1 story."

---

## Scene 2 — Shipping Outage → Dead Letter → Operator Requeue (3 min)

**What to say:**
> "Now we show what happens when automatic recovery isn't enough — the shipping provider stays down long enough to exhaust all retry attempts."

**Step 1 — break shipping:**
```bash
make shipping-fail
```

**Step 2 — place an order:**
```bash
make order
```

**Point at logs as retries exhaust:**
- `Adapter call failed` (shipping)
- `Retry scheduled` with increasing delay
- `Dead-letter created` — adapter=shipping

**What to say:**
> "Warehouse succeeded — the warehouse got the order. But the shipping label couldn't be created and all retries are gone. Instead of silently dropping it, the system writes a dead-letter record. The paid order is never lost. This is ADR 7."

**Switch to Operations View → Dead Letters tab:**
- Entry with `EscalationState = New`
- Failure reason and correlation ID visible

**Step 3 — recover shipping:**
```bash
make shipping-recover
```

**In Operations View → Dead Letters tab — click Requeue:**

**Point at logs:**
- Worker picks up the requeued record
- `Adapter call succeeded` (shipping)

**Show Operations View:**
- `EscalationState = Requeued`, `ResolvedAtUtc` set
- New outbox record `Published`

**What to say:**
> "One button. No scripting, no database access. The operator requeues it, the worker dispatches it, the shipping label is created. This closes the recovery loop for QAS 4 — and it's the difference between a resilient system and one that just loses work silently."

---

## If things go wrong

| Problem | Fix |
|---|---|
| Operations View shows nothing | `make demo-healthy` to reset |
| Circuit breaker not opening | Place 2–3 more orders with `make order` |
| Logs too noisy | `make logs \| grep -E "OPEN\|CLOSED\|Dead-letter\|succeeded\|failed"` |
| Stack not responding | `make down && make up` |
| Full reset needed | `make reset && make up` |

---

## Browser tabs cheat sheet

| Tab | URL |
|---|---|
| Storefront | `http://localhost:8080` |
| Operations View | `http://localhost:8080/Admin/Operations/List` |
| Grafana | `http://localhost:3000` → VerdeMart — Integration Observability |

---

## One-line summary for each scene

| Scene | What it proves |
|---|---|
| Warehouse failure → recovery | Pressure point: degradation visible, checkout unblocked, auto-recovery. Covers ADR 1, ADR 2, ADR 6, QAS 1. |
| Shipping dead letter → requeue | Escalation path: permanent failure retained, operator resolves with one click. Covers ADR 7, QAS 4. |
