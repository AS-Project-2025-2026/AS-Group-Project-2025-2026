# Evidence Pack — Omnichannel Integration Spike

This folder collects runtime evidence for the feasibility spike.

## Folder structure

```
docs/evidence/
├── README.md           — this file
├── demo-script.md      — step-by-step demo commands
├── screenshots/        — add .png/.jpg captures here
└── logs/               — add log excerpts here (.txt or .log)
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
