# Demo Capture Guide — Evidence Collection

## Setup — abrir 3 terminais

**Terminal 1 — logs do worker (fica sempre visível)**
```bash
cd /home/alof/Desktop/AS/AS-Group-Project-2025-2026
make logs
```

**Terminal 3 — browser aberto em:**
```
http://localhost:8080/Admin/Operations/List
```

---

## Ponto 1 — Use case end-to-end normal

**Terminal 2:**
```bash
./install-nopcommerce.sh   # só se ainda não instalado
make order
```

Espera ~10s. No Terminal 1 vais ver:
```
Outbox picked up
Adapter call succeeded  (warehouse)
Adapter call succeeded  (shipping)
```

**Capturas:**
- Screenshot do Operations View → Outbox com status **Published** (verde) → `05-normal-flow.png`
- Screenshot do `http://localhost:15672` → Queues com `fulfillment.requests` e `shipping.requests` visíveis → `06-rabbitmq-queues.png`

**Guarda os logs:**
```bash
docker compose logs --no-log-prefix integration_worker 2>/dev/null \
  | grep -E "Outbox|succeeded|published" | head -20 \
  > docs/evidence/logs/normal-flow.txt
```

---

## Pontos 2 + 4 — Pressure point + comportamento durante degradação

**Terminal 2:**
```bash
make warehouse-fail
make order
```

Espera ~30-60s. No Terminal 1 vais ver retries e o circuit breaker abrir:
```
Adapter call failed  (warehouse)
Retry scheduled  DelaySeconds=2
Adapter call failed  (warehouse)
Circuit breaker OPENED
Circuit breaker OPEN — skipping call
```

**Capturas:**
- Screenshot do Operations View → Outbox com **Retrying** ou **Failed** (amarelo/vermelho)
- Screenshot do Operations View → Circuit Breakers com warehouse **Open** (vermelho) → `07-warehouse-failure-cb-open.png`

**Guarda os logs:**
```bash
docker compose logs --no-log-prefix integration_worker 2>/dev/null \
  | grep -E "failed|Retry|Circuit|OPEN|skipping" | head -30 \
  > docs/evidence/logs/warehouse-failure.txt
```

---

## Ponto 5 — Recovery path

**Terminal 2:**
```bash
make warehouse-recover
```

Espera ~30s (o cooldown configurado). No Terminal 1:
```
Circuit breaker HALF-OPEN probe
Adapter call succeeded
Circuit breaker CLOSED
```

**Capturas:**
- Screenshot do Operations View → Circuit Breakers com warehouse **Closed** (verde) → `08-warehouse-recovery.png`

**Guarda os logs:**
```bash
docker compose logs --no-log-prefix integration_worker 2>/dev/null \
  | grep -E "HALF-OPEN|CLOSED|recovered|succeeded" | head -20 \
  > docs/evidence/logs/warehouse-recovery.txt
```

---

## Ponto 2 (shipping) + Dead letter + Requeue

**Terminal 2:**
```bash
make shipping-fail
make order
```

Espera ~2 minutos (5 retries com backoff). No Terminal 1:
```
Dead-letter created  adapter=shipping
```

**Captura A:**
- Screenshot → Dead Letters tab com **EscalationState = New** (vermelho) → `09-deadletter-created.png`

**Faz o requeue no browser** — clica o botão **Requeue** na dead letter.

```bash
make shipping-recover
```

Espera ~10s.

**Captura B:**
- Screenshot → Dead Letters com **EscalationState = Requeued** → `10-deadletter-requeued.png`
- Screenshot → Outbox com novo registo **Published**

---

## Ponto 6 — Evidence que a arquitectura melhorou os quality attributes

> Não precisa de medições de performance — precisa de provar **estruturalmente** que os QAS foram satisfeitos.

### QAS 1 — Checkout não bloqueia quando armazém falha

```bash
make warehouse-fail
time make order

# Guarda o output (tempo deve ser < 5s)
time ./place-order.sh 1 2>&1 | tee docs/evidence/logs/checkout-not-blocked.txt
```

O tempo deve ser **< 5s** — o checkout não espera pelo armazém.

### QAS 2 — Staleness detectada em 30s

```bash
make stale-start
# espera 35s
make db-inventory >> docs/evidence/logs/staleness-evidence.txt
```

A tabela deve mostrar `IsStale=1`.

### QAS 6 — Conflito detectado e checkout limitado ao mínimo

```bash
make conflict-inject
# espera 15s
make db-inventory >> docs/evidence/logs/conflict-evidence.txt
```

Deve mostrar `ConflictFlag=1` e `ReportedQuantity` limitado ao valor mais baixo.

---

## Resumo dos ficheiros a criar

| Ficheiro | Captura quando |
|---|---|
| `screenshots/05-normal-flow.png` | Outbox Published após `make order` |
| `screenshots/06-rabbitmq-queues.png` | RabbitMQ UI com filas visíveis |
| `screenshots/07-warehouse-failure-cb-open.png` | CB Open após `make warehouse-fail` |
| `screenshots/08-warehouse-recovery.png` | CB Closed após `make warehouse-recover` |
| `screenshots/09-deadletter-created.png` | Dead Letter New após shipping fail |
| `screenshots/10-deadletter-requeued.png` | Dead Letter Requeued + novo outbox |
| `logs/normal-flow.txt` | Worker a processar normalmente |
| `logs/warehouse-failure.txt` | Retries + CB Open |
| `logs/warehouse-recovery.txt` | CB probe + Closed |
| `logs/checkout-not-blocked.txt` | Tempo de checkout < 5s com armazém em falha |
| `logs/staleness-evidence.txt` | IsStale=1 na tabela |
| `logs/conflict-evidence.txt` | ConflictFlag=1 + qty limitada |
