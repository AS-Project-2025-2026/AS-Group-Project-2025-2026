# Demo Script — Omnichannel Integration Spike (~15 min)

> **Accelerated parameters for demo**: set the environment variables below to speed up retries and circuit breaker.
> The architectural defaults (2s/300s/10/5/300s) are in `appsettings.json`.

```bash
# Paste into a .env or prepend to docker compose commands
export RESILIENCE__INITIALDELAYSECONDS=2
export RESILIENCE__MAXDELAYSECONDS=30
export RESILIENCE__MAXRETRYATTEMPTS=5
export RESILIENCE__CIRCUITFAILURETHRESHOLD=3
export RESILIENCE__CIRCUITCOOLDOWNSECONDS=30
```

---

## 1. Build and start the full environment

```bash
docker compose up --build
```

Wait until all services are healthy (watch for `nopcommerce_web` to start).

---

## 2. Confirm stubs and RabbitMQ are up

```bash
curl http://localhost:5081/health
# Expected: {"status":"ok","service":"warehouse_stub","mode":"normal"}

curl http://localhost:5082/health
# Expected: {"status":"ok","service":"shipping_stub","mode":"normal"}
```

Open `http://localhost:15672` (guest/guest) and confirm:
- Exchange `verdemart.integration` exists
- Queues: `fulfillment.requests`, `shipping.requests`, `fulfillment.dead`, `shipping.dead`

---

## 3. Normal flow

1. Open `http://localhost:8080` and place an order.
2. Watch the `integration_worker` container logs:

```bash
docker compose logs -f integration_worker
```

3. Expected log events (in order):
   - `Outbox picked up`
   - `Adapter call started` (rabbitmq)
   - `Message published`
   - `Adapter call started` (warehouse)
   - `Adapter call succeeded`
   - `Adapter call started` (shipping)
   - `Adapter call succeeded`

4. Open Operations View at `http://localhost:8080/Admin/Operations/List`.
   Outbox record should show `Published`.

---

## 4. Warehouse failure + retries

In a new terminal, restart the warehouse stub in `failed` mode:

```bash
WAREHOUSE_STUB_MODE=failed docker compose up -d --no-deps --build warehouse_stub
```

Place another order and watch the worker logs:

```bash
docker compose logs -f integration_worker
```

Expected:
- `Adapter call failed` repeated with `RetryAttempt` incrementing
- `Retry scheduled` with `DelaySeconds` doubling each time
- After `CircuitFailureThreshold` (default 5, demo: 3) failures: `Circuit breaker OPENED`
- Subsequent calls: `Circuit breaker OPEN — skipping call`
- After `CircuitCooldownSeconds`: `Circuit breaker HALF-OPEN probe`

Check Operations View — Outbox record shows `Failed`, Dead Letter tab has a new row.

---

## 5. Warehouse recovery

```bash
WAREHOUSE_STUB_MODE=normal docker compose up -d --no-deps --build warehouse_stub
```

Wait for the circuit cooldown (`RESILIENCE__CIRCUITCOOLDOWNSECONDS`, demo: 30s).
Worker log should show:
- `Circuit breaker HALF-OPEN probe`
- `Adapter call succeeded`
- `Circuit breaker CLOSED/recovered`

---

## 6. Shipping outage

```bash
SHIPPING_STUB_MODE=outage docker compose up -d --no-deps --build shipping_stub
```

Place a new order. Watch logs for:
- Warehouse succeeds
- Shipping: `Adapter call failed` + retries
- After max attempts: `Dead-letter created` (adapter=shipping)

Operations View → Dead Letters shows the new entry with `EscalationState = New`.

---

## 7. Requeue via Operations View

1. Open `http://localhost:8080/Admin/Operations/List`.
2. Go to Dead Letters tab.
3. Click **Requeue** on the failed record.
4. Expected: `EscalationState` → `Requeued`, new `OutboxRecord` created.

Restore shipping stub to normal:

```bash
SHIPPING_STUB_MODE=normal docker compose up -d --no-deps --build shipping_stub
```

Watch logs — the requeued record is picked up and processed successfully.

---

## 8. Confirm in Operations View

- Outbox tab: processed records show `Published`
- Dead Letters tab: requeued record shows `EscalationState = Requeued`, `ResolvedAtUtc` set
- Circuit Breaker section: adapter states updated (refresh button available)

---

## Useful commands

```bash
# Stop everything
docker compose down

# Reset to clean state (removes volumes)
docker compose down -v

# Worker logs only
docker compose logs -f integration_worker

# Check outbox table directly
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P nopCommerce_db_password \
  -Q "SELECT TOP 10 Id, OrderId, Status, RetryCount, LastError FROM OutboxRecord ORDER BY CreatedAtUtc DESC"

# Check dead letter table
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P nopCommerce_db_password \
  -Q "SELECT TOP 10 Id, Adapter, EscalationState, FailureReason FROM DeadLetterRecord ORDER BY CreatedAtUtc DESC"

# Check circuit breaker state
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P nopCommerce_db_password \
  -Q "SELECT Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc FROM CircuitBreakerStateRecord"
```
