# Evidence Retake Checklist

This document lists every screenshot and report change needed before final submission.
Work through it top to bottom — the order matters because some screenshots depend on
the state left by the previous step.

---

## How to read this document

- **RETAKE** — screenshot exists but shows the wrong thing; must be replaced
- **NEW** — screenshot does not exist; must be captured fresh
- **REPORT FIX** — a change needed in the `.tex` file regardless of screenshots
- **OK** — already correct, no action needed

---

## Part 1 — Screenshots to retake or capture

### 1. `04-inventory-conflict.png` — RETAKE (most important)

**What it currently shows:**  
A mixed mess — WMS rows with `Conflict` and POS rows with `Stale`. They are from two
different moments stitched together. No single product shows both its `wms` and `pos`
rows simultaneously in conflict.

**What it must show:**  
- Product #1, source `wms`, ReportedQty ≈ 115, Stale = **OK**, Conflict = **Conflict** (red), PendingReconciliation = Yes  
- Product #1, source `pos`, ReportedQty = **3**, Stale = **OK**, Conflict = **Conflict** (red), PendingReconciliation = Yes  
- All other products: Stale = OK, Conflict = OK (no spurious rows)

Both rows for the same product, both sources, both red `Conflict` badges, POS quantity
clearly lower than WMS. This is the visual proof of QAS 6.

**Steps to capture:**
```bash
make up
make demo-healthy        # wipes all stale/conflict rows
make conflict-inject     # POST /stock/report {"productId":1,"quantity":3}
# wait 10-15 seconds
# open http://localhost:8080/Admin/Operations/List
# scroll to "Inventory Projection — Stale & Conflicted"
# click Refresh
# screenshot — must show product #1 wms + pos rows both in red
```

---

### 2. `03-inventory-stale.png` — RETAKE (minor)

**What it currently shows:**  
Only `wms` rows with `Stale` badges — this part is correct. But the screenshot is
very small and the text is barely readable. The Inventory Projection section header
is not visible.

**What it must show:**  
- The full "Inventory Projection — Stale & Conflicted" card heading visible at the top  
- Multiple `wms` rows with `Stale` badge (yellow) and `PendingReconciliation = Yes`  
- `Conflict = OK` (green) on all rows — staleness only, no conflict  
- `LastConfirmedUtc` timestamps clearly older than `UpdatedAtUtc` (proves the threshold fired)

**Steps to capture:**
```bash
make demo-healthy
make stale-start         # sets INVENTORY_STUB_MODE=unavailable
# wait 35 seconds (staleness threshold is 30s in demo env)
# open Operations View → Inventory Projection tab
# click Refresh
# screenshot — rows should be yellow (Stale), not red
make stale-stop          # restore to normal before next step
```

---

### 3. `06-rabbitmq-queues-1.png` and `06-rabbitmq-queues-2.png` — RETAKE

**What they currently show:**  
`fulfillment.requests` and `shipping.requests` queues individually — correct queues,
but the report's evidence table says these screenshots prove
`fulfillment.dead` and `shipping.dead` queues are "present and active."
Neither current screenshot shows a dead-letter queue.

**What they must show:**  
- `06-rabbitmq-queues-1.png`: RabbitMQ Queues list page showing **all queues** —
  `fulfillment.requests`, `pos.stock.updates`, `shipping.requests`, and ideally
  `storeops.requests` all visible in the same table
- `06-rabbitmq-queues-2.png`: the `pos.stock.updates` queue detail page, showing
  the `x-dead-letter-exchange: verdemart.integration.dlx` and
  `x-dead-letter-routing-key: pos.stock.dead` config in the Details section

**Why:** The report claims dead queues are present. The actual dead-letter handling
for fulfillment/shipping goes to the database (`DeadLetterRecord`), not a broker queue.
The only broker-side DLX is on `pos.stock.updates`. Show what actually exists rather
than what the report incorrectly claimed.

> **Note:** Also update the report caption (see Part 2, item 1).

**Steps to capture:**
```bash
# open http://localhost:15672 (RabbitMQ management UI, guest/guest)
# Queues and Streams tab
# screenshot 1: the full queue list showing all declared queues
# click on pos.stock.updates
# screenshot 2: the queue detail page showing the DLX config in Details section
```

---

### 4. `05-normal-flow.png` — RETAKE (minor)

**What it currently shows:**  
Outbox section only — 2 Published records, no circuit breaker section visible,
no Inventory Projection section visible.

**What it must show:**  
The full Operations View in healthy state:
- Outbox: all records `Published`, 0 retries, no errors
- Dead Letters: empty ("No data available")
- Circuit Breaker State: all adapters `Closed`, 0/5 failures
- Inventory Projection: "No stale or conflicted inventory records"

This is the baseline "everything green" screenshot. Currently it only shows
the top half.

**Steps to capture:**
```bash
make demo-healthy
# open Operations View
# scroll so all four sections are visible (zoom out browser to ~75% if needed)
# screenshot the full page
```

---

### 5. `07-warehouse-failure-cb-open.png` — OK with one gap

**What it currently shows:**  
Outbox with one `Published` record, Dead Letters with 1 entry (`warehouse`, State=New),
Circuit Breaker with `rabbitmq` Closed and `warehouse` Open (3/5, Opened timestamp,
Next Probe timestamp, last error visible). This is good.

**Gap:** The Inventory Projection section is not visible in the screenshot.
It should show "No stale or conflicted inventory records" to confirm the conflict
state is clean during this scenario.

**Fix:** Scroll down slightly to include the Inventory Projection section header,
or zoom out to show the full page. This is minor — only retake if time permits.

---

### 6. `08-warehouse-recovery1.png` — RETAKE

**What it currently shows:**  
Outbox with 2 Published records, Dead Letters still showing the warehouse dead letter
(State=New), no Circuit Breaker section visible.

**What it must show:**  
The moment after recovery — Circuit Breaker section must be visible and show
`warehouse` back to `Closed`. The dead letter remaining in `New` state is fine
(it was created before recovery; the operator still needs to requeue it).

**Steps to capture:**
```bash
# continuing from warehouse-fail scenario
make warehouse-recover
# wait 35 seconds for probe + close
# screenshot showing circuit breaker warehouse = Closed
# both the outbox (now dispatching) and circuit breaker section visible
```

---

### 7. `08-warehouse-recovery2.png` — OK

**What it shows:** All 4 circuit breakers `Closed` (customersupport, rabbitmq,
shipping, warehouse), all 0/5. This is correct and clear.

---

### 8. `09-deadletter-created.png` — OK

**What it shows:** Dead Letters with 2 entries — shipping (State=New) and warehouse
(State=New). Circuit Breaker showing shipping=Open (3/5). This correctly proves
QAS 4. Good.

---

### 9. `10-deadletter-requeued1.png` — OK with minor issue

**What it shows:** Dead Letters with 3 entries — two shipping `Requeued` and one
warehouse `New`. Circuit Breakers all `Closed`. This is correct.

**Minor issue:** The two requeued entries show `order-8` as both correlation IDs,
which looks odd. Not a blocker but slightly confusing.

---

### 10. `10-deadletter-requeued2.png` — OK

**What it shows:** Outbox with 5 Published records. This proves the requeued
dead-letter created a new outbox record that was dispatched. Good.

---

## Part 2 — Report `.tex` changes needed

### 1. `10-feasibility-spike.tex` — RabbitMQ queues caption (line ~170)

**Current text:**
```
\texttt{fulfillment.requests}, \texttt{shipping.requests}, \texttt{fulfillment.dead}, and
\texttt{shipping.dead} queues present and active.
```

**Change to:**
```
All declared queues present: \texttt{fulfillment.requests}, \texttt{shipping.requests},
\texttt{pos.stock.updates}, and \texttt{storeops.requests}. The \texttt{pos.stock.updates}
queue is configured with a dead-letter exchange; fulfillment and shipping dead-letter
handling uses the database \texttt{DeadLetterRecord} table rather than a broker queue.
```

**Why:** The current caption claims `fulfillment.dead` and `shipping.dead` broker queues
exist. They do not. The report was already corrected in the prose but the evidence table
caption still claims it.

---

### 2. `10-feasibility-spike.tex` — add `pos.stock.updates` queue to spike question table

The spike question table (lines ~26–80) covers 8 questions. None of them explicitly
mentions the POS event path end-to-end (stub → RabbitMQ → consumer → DB). Consider
adding or updating the last POS conflict row to mention the queue name:

**Current:**
```
POS stock reported through the Store POS stub's \texttt{POST /stock/report} endpoint,
which publishes a \texttt{pos.stock.reported} message with a quantity diverging from
the WMS value by more than the configured tolerance.
```

**Change to:**
```
POS stock reported through the Store POS stub's \texttt{POST /stock/report} endpoint,
which publishes a \texttt{pos.stock.reported} message to the \texttt{pos.stock.updates}
queue with a quantity diverging from the WMS value by more than the configured tolerance (2 units).
```

---

## Part 3 — Summary checklist

| # | Item | Action | Priority |
|---|------|--------|----------|
| 1 | `04-inventory-conflict.png` | **RETAKE** — must show wms+pos rows for same product both in Conflict | Critical |
| 2 | `03-inventory-stale.png` | **RETAKE** — must show section heading + readable text | High |
| 3 | `06-rabbitmq-queues-1.png` | **RETAKE** — show full queue list, not just fulfillment.requests | High |
| 4 | `06-rabbitmq-queues-2.png` | **RETAKE** — show pos.stock.updates DLX config | High |
| 5 | `05-normal-flow.png` | **RETAKE** — show all four sections in one screenshot | Medium |
| 6 | `08-warehouse-recovery1.png` | **RETAKE** — must show circuit breaker Closed after recovery | High |
| 7 | `07-warehouse-failure-cb-open.png` | Minor — include Inventory Projection section if time permits | Low |
| 8 | Report caption: RabbitMQ queues | **REPORT FIX** — remove `fulfillment.dead`/`shipping.dead` claim | Critical |
| 9 | Report: POS spike question row | **REPORT FIX** — add queue name to how-tested column | Low |

---

## Capture order (do these in sequence)

```
1. make up + make demo-healthy
2. Screenshot 05-normal-flow.png          (healthy baseline, all green)
3. make stale-start → wait 35s
4. Screenshot 03-inventory-stale.png      (yellow stale rows)
5. make stale-stop + make demo-healthy
6. make conflict-inject → wait 15s
7. Screenshot 04-inventory-conflict.png   (red conflict rows, wms+pos for product 1)
8. make conflict-clear + make demo-healthy
9. Screenshot 06-rabbitmq-queues-1.png    (RabbitMQ queue list)
10. Screenshot 06-rabbitmq-queues-2.png   (pos.stock.updates DLX config)
11. make warehouse-fail + make order
12. Screenshot 07-warehouse-failure-cb-open.png (circuit Open, retrying)
13. make warehouse-recover → wait 35s
14. Screenshot 08-warehouse-recovery1.png (circuit back to Closed)
```
