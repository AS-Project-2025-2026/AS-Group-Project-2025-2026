# Evidence Pack — Omnichannel Integration Spike

This folder collects runtime evidence for the final delivery.

Related architecture decisions are documented as standalone ADRs in
[`../adr/`](../adr/), with a cross-decision forces matrix in
[`../adr/forces-matrix.md`](../adr/forces-matrix.md).

## Folder structure

```
docs/evidence/
├── README.md                     — this file
├── demo-script.md                — step-by-step demo commands
├── known-limitations.md          — explicit scope cuts and architectural boundaries
├── inventory-staleness-implementation.md — implementation guide for QAS 2 & QAS 6
├── capture-guide.md              — instructions for capturing screenshots and logs
├── screenshots/                  — runtime screenshots
└── logs/                         — worker log excerpts
```

## What to capture

| Scenario | Expected evidence |
|---|---|
| Normal flow | Worker log showing `Adapter call succeeded`, outbox status `Published` |
| Warehouse slow | Log entries `Retry scheduled` with delay, checkout not blocked |
| Warehouse failed (5+) | Log `Circuit breaker OPENED`, subsequent calls skipped |
| Warehouse recovery | Log `Circuit breaker CLOSED/recovered` after probe |
| 10 retries exhausted | `Dead-letter created` log, row in `DeadLetterRecord` table |
| Requeue | Operations View requeue button, outbox back to `Pending`, processed again |
| RabbitMQ queues | Screenshot of `http://localhost:15672` with all 4 queues visible |
| Operations View | Screenshot of admin page with Outbox + Dead Letters + Circuit Breaker tabs |

## After running the demo

Add screenshots to `screenshots/` and relevant log lines to `logs/`.
File names are not enforced; use descriptive names (e.g., `01-normal-flow.png`).
