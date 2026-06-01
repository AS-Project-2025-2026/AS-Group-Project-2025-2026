# ADR 004: Retain nopCommerce Monolith as the Commerce Core

## Status

Accepted.

## Context

Adding omnichannel capabilities can tempt a full microservices decomposition.
nopCommerce already owns catalog, cart, checkout, payment, customer accounts,
orders, and admin workflows. The architectural pressure in this project is not
inside those stable commerce capabilities; it is around reliable integration
with operational systems.

## Decision

nopCommerce remains the commerce core. Checkout, catalog, payments, customers,
and canonical order management stay inside the monolith. The new independently
deployable component is the Integration Worker, supported by outbox,
dead-letter, circuit-breaker, idempotency, and inventory projection records.

## Options Considered

| Option | What it tries to preserve | Why rejected or accepted |
|---|---|---|
| Extract order management service | Independent deployment and scaling of orders | Rejected. It introduces a synchronous checkout dependency and distributed transactions. |
| Extract inventory service | Independent ownership of stock state | Rejected for this spike. Inventory freshness can be represented through projections without moving checkout to a network call. |
| Full microservices rewrite | Maximum decomposition | Rejected. It is a rewrite with high delivery risk and no direct benefit for the mandatory QAS. |
| Retain monolith plus integration layer | Stable commerce core with isolated integration reliability | Accepted. It solves the target risks incrementally and keeps existing nopCommerce behavior intact. |

## Forces Matrix

| Option | Availability | Consistency | Delivery Risk | Operational Complexity | Reversibility |
|---|---|---|---|---|---|
| Extract order service | Medium/poor for checkout dependency | Hard: distributed order/payment state | High | High | Low once extracted |
| Extract inventory service | Medium: network stock check risk | Medium/strong if built fully | High | High | Medium |
| Full microservices rewrite | Unknown | Hard: many distributed workflows | Very high | Very high | Low |
| Retain monolith | Strong for current checkout | Strong for canonical order state | Low/medium | Medium | Strong: later extraction remains possible |

## Consequences

The customer-facing commerce experience stays operationally simple and familiar.
The integration layer absorbs the new reliability and resilience requirements.
The monolith still shares one deployment and scaling unit for customer-facing
commerce behavior.

## Accepted Damage

The commerce core cannot be scaled independently by subdomain. Future extraction
may be needed if QAS 3 load targets require separate scaling of catalog,
checkout, or order-status reads.

## Evidence

- `src/Presentation/Nop.Web/`
- `src/Integration/Nop.IntegrationWorker/`
- `docker-compose.yml`
- `docs/report/chapters/10-feasibility-spike.tex`

## Revisit When

Revisit if load tests show the monolith cannot meet QAS 3 targets, or if a new
business capability requires fault isolation inside the commerce core rather
than around it.
