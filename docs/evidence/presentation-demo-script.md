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


**Switch to Operations View:**
- Outbox record in `Retrying` state with retry count and next attempt time
- Warehouse circuit breaker `Open`


**Switch to Grafana (`http://localhost:3000`):**
- **"Adapter Failures (5m)"** stat — climbing
- **"Retry Attempts (5m)"** stat — climbing
- **"Adapter Call Rate by Outcome"** timeseries — visible failure spike

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


---

## Scene 2 — Shipping Outage → Dead Letter → Operator Requeue (3 min)


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

---

## Scene 3 — POS vs Online Conflict: 1 unit, two buyers (2 min)

**What this proves:** when a physical POS sale races an online checkout for the same last unit, the system detects the discrepancy, caps online checkout at the lower (POS-reported) quantity, and surfaces the conflict in the Operations View. POS always wins.

**Step 1 — establish a baseline (WMS reports 5 units for product 1):**
```bash
# Check current inventory projection in Operations View
# → Inventory tab: no conflict, no stale rows
```
Operations View → Inventory Projection tab: all rows show `ConflictFlag=OK`, `Stale=OK`.

**Step 2 — POS sells units in-store (WMS still shows a higher count), creating a conflict:**
```bash
make conflict-inject
# Sends: POST http://localhost:5084/stock/report {"productId": 1, "quantity": 3}
# WMS currently reports 5+ units; diff=2+ exceeds the tolerance of 2
```

**Watch the logs:**
- `PosStockConsumerService` receives the `pos.stock.reported` event
- Diff between WMS and POS quantities exceeds tolerance of 2
- `POS/WMS inventory conflict on ProductId=1: wms=X vs pos=3 — checkout capped at 3`
- `Product.StockQuantity` updated to the lower value (POS wins)

**Step 3 — switch to Operations View → Inventory Projection tab, click Refresh:**
- Two rows for ProductId=1: `wms` and `pos`
- Both show `ConflictFlag=Conflict` (red) and `PendingReconciliation=Yes`
- `wms` row: ReportedQty=5 | `pos` row: ReportedQty=1
- The **checkout is now bounded to 1** — a second online buyer cannot oversell

**Step 4 — switch to Grafana:**
- **Consistency** driver metric → `Degraded` (conflicts > 0)
- `ConflictedInventoryProjections` stat climbs to 1

**Step 5 — resolve by clearing the conflict (operator action or SQL reset):**
```bash
make conflict-clear
```

Operations View → after next sync cycle: `ConflictFlag=OK`, `ResolvedAtUtc` set. Grafana consistency metric returns to `Healthy`.

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
| POS vs online conflict | Consistency: POS physical sale detected, checkout capped at POS qty, conflict visible in Operations View. Covers QAS 6. |
