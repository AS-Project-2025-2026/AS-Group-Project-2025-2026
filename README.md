# AS Group Project 2025-2026

This repository contains the Software Architectures group project for the
architectural evolution of nopCommerce. The work studies how nopCommerce can
evolve from an online storefront into an omnichannel commerce core for the
VerdeMart Retail scenario.

The codebase is based on nopCommerce and keeps the original application
structure, while the project documentation records the architectural analysis,
target architecture, decisions, risks, roadmap, and feasibility work.

## Quick Start

**Prerequisites:** Docker and Docker Compose installed.

### 1. Start the environment

```bash
make up
```

This builds all containers, writes a `.env` with accelerated demo parameters
(staleness threshold 30 s, circuit breaker opens after 3 failures), and waits
until nopCommerce and SQL Server are ready.

First run takes ~5 minutes to build. Subsequent runs are faster.

### 2. Install nopCommerce (first run only)

```bash
./install-nopcommerce.sh
```

This installs nopCommerce with sample data, creates the admin account
(`admin@verdemart.com` / `Admin1234!`), and runs the database migrations that
create the integration tables (outbox, dead-letter, circuit breaker, inventory
projection).

### 3. Place a real order

```bash
make order
```

This logs in, adds a product to the cart, and completes checkout via the
nopCommerce one-page checkout. The `OrderProcessingService` creates the order
and writes an outbox record atomically. The Integration Worker picks it up
within seconds and dispatches it to the warehouse and shipping stubs via
RabbitMQ.

### 4. Watch the worker process it

```bash
make logs
```

### 5. Open the Operations View

[http://localhost:8080/Admin/Operations/List](http://localhost:8080/Admin/Operations/List)

Shows outbox records, dead letters, circuit breaker state, and inventory
projection in real time.

---

### Demo scenarios

Run these in separate terminals alongside `make logs`:

| Scenario | Commands |
|---|---|
| Continuous orders | `make order-loop` |
| QAS 1 — Warehouse failure | `make warehouse-fail` → place order → `make warehouse-recover` |
| QAS 4 — Shipping outage | `make shipping-fail` → place order → `make shipping-recover` |
| QAS 2 — Stale inventory | `make stale-start` → wait 30 s → `make stale-stop` |
| QAS 6 — POS/WMS conflict | `make conflict-inject` → wait a few seconds → `make conflict-clear` |

See `docs/evidence/demo-script.md` for the full step-by-step demo guide and
`Makefile` (`make help`) for all available commands.

---

### URLs

| Service | URL | Credentials |
|---|---|---|
| nopCommerce storefront | http://localhost:8080 | — |
| Admin / Operations View | http://localhost:8080/Admin/Operations/List | admin@verdemart.com / Admin1234! |
| RabbitMQ management | http://localhost:15672 | guest / guest |
| Warehouse stub | http://localhost:5081/health | — |
| Shipping stub | http://localhost:5082/health | — |
| Inventory stub | http://localhost:5083/health | — |
| Store POS stub | http://localhost:5084/health | — |

### Stop / Reset

```bash
make down        # stop containers, keep data
make reset       # stop containers and delete all volumes (full reset)
```

---

## Authors

Group 107:

- Afonso Ferreira, 113480
- Tomás Brás, 112665
- Hugo Ribeiro, 113402
- Rodrigo Abreu, 113626

## Project Structure

```text
.
├── docs/
│   ├── Assignment 2 — Architectural Evolution of nopCommerce.pdf
│   ├── Group Assignment - Final Assignment.pdf
│   └── report/
│       ├── chapters/
│       ├── diagrams/
│       ├── images/
│       ├── report.tex
│       └── report.pdf
├── src/
│   ├── Build/
│   ├── Libraries/
│   ├── Plugins/
│   ├── Presentation/
│   ├── Tests/
│   └── NopCommerce.sln
├── upgradescripts/
├── docker-compose.yml
├── postgresql-docker-compose.yml
├── mysql-docker-compose.yml
├── Dockerfile
├── CONTRIBUTING.md
├── ISSUE_TEMPLATE.md
├── LICENSE.md
└── README.md
```

## Main Directories

- `docs/` contains the assignment PDFs and the group report material.
- `docs/report/` contains the LaTeX report source, diagrams, images, compiled
  report PDF, and the `compile.sh` helper script.
- `src/` contains the nopCommerce solution and application source code.
- `src/Libraries/` contains the core nopCommerce libraries, including
  `Nop.Core`, `Nop.Data`, and `Nop.Services`.
- `src/Presentation/` contains the web application and web framework projects.
- `src/Plugins/` contains nopCommerce plugins, including payment, shipping,
  tax, search, authentication, and miscellaneous integrations.
- `src/Tests/` contains the test project for the nopCommerce solution.
- `upgradescripts/` contains database upgrade scripts for historical
  nopCommerce version migrations.
- The Docker files at the repository root provide container-based setup options
  for the application and supported databases.

## Documentation

The `docs` folder is the main place for project documentation:

- Assignment briefs are stored directly under `docs/`.
- The architectural report is stored under `docs/report/`.
- Report chapters are split under `docs/report/chapters/`.
- Architecture diagrams are stored under `docs/report/diagrams/`.
- Rendered or supporting images are stored under `docs/report/images/`.
- The compiled report is available at `docs/report/report.pdf`.
