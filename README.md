# nopCommerce: Omnichannel Commerce Core

Architectural evolution of [nopCommerce](https://www.nopcommerce.com/) towards an omnichannel commerce platform, developed as part of the Software Architectures group assignment.

**Scenario C — Omnichannel Commerce Core**: VerdeMart Retail began with nopCommerce as a web storefront. The business now needs web sales, warehouse execution, shipping, store operations, and customer visibility to work together — with nopCommerce becoming the commerce core of a wider enterprise ecosystem.

---

## Table of Contents

- [Repository Structure](#repository-structure)
- [Documentation](#documentation)
  - [Architecture Report](#architecture-report)
  - [Architecture Decision Records](#architecture-decision-records)
  - [Evidence Pack](#evidence-pack)
- [Demo Setup](#demo-setup)
  - [Prerequisites](#prerequisites)
  - [Step 1: Start the environment](#step-1-start-the-environment)
  - [Step 2: Install nopCommerce](#step-2-install-nopcommerce-first-run-only)
  - [Step 3: Place a real order](#step-3-place-a-real-order)
  - [Step 4: Warehouse failure scenario](#step-4-warehouse-failure--circuit-breaker--recovery)
  - [Step 5: Shipping dead letter scenario](#step-5-shipping-outage--dead-letter--requeue)
  - [Step 6: Inventory scenarios](#step-6-inventory-scenarios)
  - [Available Commands](#available-commands)
  - [Useful URLs](#useful-urls)
- [Authors](#authors)
- [Original nopCommerce](#original-nopcommerce)

---

## Repository Structure

```
AS-Group-Project-2025-2026/
├── docs/
│   ├── adr/                                            # Architecture Decision Records
│   │   ├── README.md                                   # ADR index
│   │   ├── 001-asynchronous-fulfillment-propagation.md # ADR 1 — Async fulfillment
│   │   ├── 002-transactional-outbox.md                 # ADR 2 — Transactional outbox
│   │   ├── 003-adapter-boundaries-no-shared-db.md      # ADR 3 — Adapter pattern
│   │   ├── 004-retain-nopcommerce-monolith.md          # ADR 4 — Monolith retained
│   │   ├── 005-idempotency-key-strategy.md             # ADR 5 — Idempotency keys
│   │   ├── 006-retry-circuit-breaker.md                # ADR 6 — Retry + circuit breaker
│   │   ├── 007-dead-letter-operator-intervention.md    # ADR 7 — Dead-letter queue
│   │   └── forces-matrix.md                            # Architectural forces comparison
│   ├── evidence/                                       # Runtime evidence pack
│   │   ├── screenshots/                                # Annotated runtime screenshots
│   │   ├── logs/                                       # Worker log excerpts + load test results
│   │   ├── load-test.js                                # k6 QAS 3 load test script
│   │   └── README.md                                   # Evidence documentation
│   └── report/                                         # Architecture report (LaTeX source + PDF)
│       ├── chapters/                                   # Report chapters (01-10)
│       ├── images/                                     # Architecture diagrams and figures
│       ├── report.tex                                  # Main LaTeX file
│       └── report.pdf                                  # Compiled report
├── src/
│   ├── Integration/
│   │   └── Nop.IntegrationWorker/                      # Integration Worker (independently deployable)
│   ├── Stubs/                                          # Surrogate adapter stubs
│   │   ├── WarehouseStub/                              # WMS stub (normal / slow / failed modes)
│   │   ├── ShippingStub/                               # Shipping stub (normal / slow / outage modes)
│   │   ├── InventoryStub/                              # Inventory/WMS stock stub
│   │   ├── StorePosStub/                               # Store POS stub
│   │   └── CustomerSupportStub/                        # Customer support stub
│   ├── Libraries/                                      # nopCommerce core libraries
│   ├── Plugins/                                        # nopCommerce plugins
│   ├── Presentation/                                   # nopCommerce web application
│   └── Tests/                                          # nopCommerce test projects
├── docker/                                             # Grafana, Prometheus, Loki provisioning
├── docker-compose.yml                                  # Full environment (nopCommerce + stubs + observability)
├── Makefile                                            # All demo commands (run `make help`)
└── README.md
```

---

## Documentation

### Architecture Report

The full architecture report is at [`docs/report/report.pdf`](docs/report/report.pdf) and covers:

| Chapter | Content |
|---|---|
| 1 — Introduction and Scenario | Scenario C choice, business drivers, surrounding systems |
| 2 — Current-State Analysis | How baseline nopCommerce supports or conflicts with the scenario |
| 3 — Domain and Bounded Contexts | Relevant subdomains, bounded contexts, data ownership |
| 4 — Quality Attribute Scenarios | 6 QAS with stimulus/response/measure (availability, consistency, performance, recoverability, modifiability) |
| 5 — Architectural Approach | ADD framework selection and application |
| 6 — Target Architecture | Components, data ownership, sync vs async interactions, diagrams |
| 7 — Architectural Decisions | ADR 1–7 with rejected alternatives |
| 8 — Risk and Validation Plan | Main risks and validation evidence |
| 9 — Evolution Roadmap | Phase-by-phase implementation path |
| 10 — Feasibility Spike | Runtime evidence for all 8 tested spike questions |

Supporting diagrams:

| Diagram | File |
|---|---|
| Target architecture | [`docs/report/images/target-architecture-scenario-c.png`](docs/report/images/target-architecture-scenario-c.png) |
| Current-state architecture | [`docs/report/images/current-implementation-scenario-c.png`](docs/report/images/current-implementation-scenario-c.png) |
| Bounded context model | [`docs/report/images/boundary_model.png`](docs/report/images/boundary_model.png) |
| Sequence diagram — warehouse failure | [`docs/report/images/sequence-warehouse-failure.png`](docs/report/images/sequence-warehouse-failure.png) |
| Evolution roadmap | [`docs/report/images/evolution-roadmap.png`](docs/report/images/evolution-roadmap.png) |

### Architecture Decision Records

Standalone ADR files in [`docs/adr/`](docs/adr/):

| ADR | Decision | QAS |
|---|---|---|
| [ADR 1](docs/adr/001-asynchronous-fulfillment-propagation.md) | Asynchronous fulfillment propagation | QAS 1, QAS 4 |
| [ADR 2](docs/adr/002-transactional-outbox.md) | Transactional outbox for durable task publishing | QAS 1, QAS 4 |
| [ADR 3](docs/adr/003-adapter-boundaries-no-shared-db.md) | Adapter pattern — no shared database across external boundaries | QAS 5 |
| [ADR 4](docs/adr/004-retain-nopcommerce-monolith.md) | nopCommerce monolith retained as the commerce core | QAS 3 |
| [ADR 5](docs/adr/005-idempotency-key-strategy.md) | Idempotency key strategy for integration messages | QAS 1, QAS 4 |
| [ADR 6](docs/adr/006-retry-circuit-breaker.md) | Retry policy with exponential backoff and circuit breaker | QAS 1, QAS 4 |
| [ADR 7](docs/adr/007-dead-letter-operator-intervention.md) | Dead-letter queue and operator intervention model | QAS 1, QAS 4 |

### Evidence Pack

Runtime evidence in [`docs/evidence/`](docs/evidence/):

| File | Description |
|---|---|
| [`screenshots/`](docs/evidence/screenshots/) | 15 annotated screenshots: system baseline, normal flow, warehouse failure/recovery, circuit breaker states, dead letters, inventory staleness and conflict |
| [`logs/warehouse-failure.txt`](docs/evidence/logs/warehouse-failure.txt) | Retry attempts, exponential delay, circuit breaker open/close |
| [`logs/warehouse-recovery.txt`](docs/evidence/logs/warehouse-recovery.txt) | Half-open probe and circuit close after recovery |
| [`logs/checkout-not-blocked.txt`](docs/evidence/logs/checkout-not-blocked.txt) | Checkout completing while warehouse integration is asynchronous |
| [`logs/conflict-evidence.txt`](docs/evidence/logs/conflict-evidence.txt) | POS vs WMS conflict detection, ConflictFlag, checkout bounded |
| [`logs/staleness-evidence.txt`](docs/evidence/logs/staleness-evidence.txt) | IsStale set after threshold exceeded |
| [`logs/qas3-load-test-results.json`](docs/evidence/logs/qas3-load-test-results.json) | k6 load test: checkout p95 = 74 ms, order-status p95 = 87 ms at 25 VUs — both QAS 3 thresholds passed |
| [`load-test.js`](docs/evidence/load-test.js) | k6 script to reproduce the QAS 3 load test |

---

## Demo Setup

### Prerequisites

- Docker and Docker Compose
- `make` (standard on Linux/macOS)

### Step 1: Start the environment

```bash
make up
```

Builds all containers and starts nopCommerce, the Integration Worker, all stubs (warehouse, shipping, inventory, POS, customer support), RabbitMQ, Prometheus, Loki, and Grafana. First run takes ~5 minutes to build; subsequent runs are faster.

Accelerated demo parameters are applied automatically:
- Circuit breaker opens after **3 failures** (production default: 5)
- Circuit breaker cooldown: **30 s** (production default: 300 s)
- Staleness threshold: **30 s** (production default: 120 s)

### Step 2: Install nopCommerce (first run only)

```bash
./install-nopcommerce.sh
```

Installs nopCommerce with sample data, creates the admin account, and runs the database migrations that create the integration tables (outbox, dead-letter, circuit breaker, inventory projection).

Admin credentials: `admin@verdemart.com` / `Admin1234!`

### Step 3: Place a real order

```bash
make order
```

Logs in, adds a product to cart, and completes checkout. The `OrderProcessingService` writes the order and an outbox record atomically. The Integration Worker picks it up within seconds and dispatches it to the warehouse and shipping stubs via RabbitMQ.

Watch it happen:

```bash
make logs
```

Then open the Operations View at `http://localhost:8080/Admin/Operations/List` — the outbox record should show `Published` and all circuit breakers `Closed`.

### Step 4: Warehouse failure → circuit breaker → recovery

```bash
make warehouse-fail      # set warehouse stub to failed mode
make order               # place an order while it's down
make logs                # watch retries, then circuit breaker OPENED
make warehouse-recover   # restore — circuit probes and closes automatically
```

Expected log sequence:
- `Adapter call failed` → `Retry scheduled` (delay doubles each attempt: 2s → 4s → 8s...)
- After 3 failures: `Circuit breaker OPENED`
- `Circuit breaker OPEN — skipping call`
- After cooldown: `Circuit breaker HALF-OPEN probe` → `Adapter call succeeded` → `Circuit breaker CLOSED`

Operations View shows the outbox record moving from `Retrying` → `Published` automatically.
Grafana (`http://localhost:3000`) shows the failure spike and recovery on the **Resilience** row.

### Step 5: Shipping outage → dead letter → requeue

```bash
make shipping-fail       # set shipping stub to outage mode
make order               # place an order
make logs                # watch retries exhaust — Dead-letter created
make shipping-recover    # restore shipping stub
```

Then open the Operations View → **Dead Letters** tab and click **Requeue**. The worker picks it up and dispatches it on the next cycle.

### Step 6: Inventory scenarios

**Staleness (QAS 2):**

```bash
make stale-start    # cuts inventory stub — staleness triggers in ~30 s
make stale-stop     # restores stub — IsStale clears on next sync cycle
```

**POS/WMS conflict (QAS 6):**

```bash
make conflict-inject   # reports POS stock diverging >2 units from WMS
                       # wait ~15 s — Operations View → Inventory tab shows ConflictFlag = true
make conflict-clear    # clears conflict
```

### Available Commands

```bash
make help             # list all available commands
make up               # start full environment
make down             # stop containers, keep data
make reset            # full reset — stop and delete all volumes
make logs             # follow Integration Worker logs
make status           # health status of all services
make order            # place one real order
make order-burst      # place 5 orders in sequence
make demo-healthy     # populate Operations View with all-green state
make demo-degraded    # populate Operations View with failures and dead letters
make warehouse-fail   # set warehouse stub to failed mode
make warehouse-recover # restore warehouse stub
make shipping-fail    # set shipping stub to outage mode
make shipping-recover # restore shipping stub
make stale-start      # trigger inventory staleness
make stale-stop       # restore inventory stub
make conflict-inject  # inject POS/WMS stock conflict
make conflict-clear   # clear conflict
make db-outbox        # show last 10 outbox records
make db-deadletter    # show last 10 dead-letter records
make db-inventory     # show all inventory projection records
make db-cb            # show circuit breaker states
```

### Useful URLs

| Service | URL | Credentials |
|---|---|---|
| VerdeMart storefront | http://localhost:8080 | — |
| Operations View | http://localhost:8080/Admin/Operations/List | admin@verdemart.com / Admin1234! |
| RabbitMQ management | http://localhost:15672 | guest / guest |
| Grafana dashboards | http://localhost:3000 | admin / admin |
| Prometheus | http://localhost:9090 | — |
| Warehouse stub | http://localhost:5081/health | — |
| Shipping stub | http://localhost:5082/health | — |
| Inventory stub | http://localhost:5083/health | — |
| Store POS stub | http://localhost:5084/health | — |
| Customer support stub | http://localhost:5085/health | — |

---

## Authors

Group — Software Architectures 2025/2026:

- Afonso Ferreira, 113480
- Tomás Brás, 112665
- Hugo Ribeiro, 113402
- Rodrigo Abreu, 113626

---

## Original nopCommerce

This repository is based on [nopSolutions/nopCommerce](https://github.com/nopSolutions/nopCommerce). nopCommerce is a free, open-source ASP.NET Core eCommerce platform. See the original project for full platform documentation.
