# AS Group Project 2025-2026

This repository contains the Software Architectures group project for the
architectural evolution of nopCommerce. The work studies how nopCommerce can
evolve from an online storefront into an omnichannel commerce core for the
VerdeMart Retail scenario.

The codebase is based on nopCommerce and keeps the original application
structure, while the project documentation records the architectural analysis,
target architecture, decisions, risks, roadmap, and feasibility work.

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
