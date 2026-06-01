# Demo Capture Guide — Evidence Collection

## Setup — abrir 2 terminais

**Terminal 1 — logs do worker (fica sempre visível)**
```bash
cd /home/alof/Desktop/AS/AS-Group-Project-2025-2026
make logs
```

**Browser aberto em:**
```
http://localhost:8080/Admin/Operations/List
```

---

## Fase 1 — Fluxo normal → `05-normal-flow.png` + `06-rabbitmq-queues.png`

**Terminal 2:**
```bash
make order
```

Espera ~10s. No Terminal 1 vais ver:
```
Outbox picked up
Adapter call succeeded  (warehouse)
Adapter call succeeded  (shipping)
```

**Capturas:**
- Operations View → Outbox com status **Published** (verde) → `05-normal-flow.png`
- `http://localhost:15672` → login guest/guest → tab **Queues** → filas visíveis → `06-rabbitmq-queues.png`

**Guarda os logs:**
```bash
docker compose logs --no-log-prefix integration_worker 2>/dev/null \
  | grep -E "Outbox|succeeded|Published" | head -20 \
  > docs/evidence/logs/normal-flow.txt
```

---

## Fase 2 — Warehouse failure → `07-warehouse-failure-cb-open.png`

**Terminal 2:**
```bash
make warehouse-fail
make order
```

Espera ~60s. No Terminal 1 vais ver (por esta ordem):
```
Adapter call failed  (warehouse)
Retry scheduled  DelaySeconds=2
Adapter call failed  (warehouse)
Retry scheduled  DelaySeconds=4
Adapter call failed  (warehouse)
Circuit breaker OPENED
Circuit breaker OPEN — skipping call
Dead-letter created  adapter=warehouse
```

**Capturas (tira as 3 no mesmo screenshot ou em separado):**
- Operations View → Outbox com **Published** (saiu do nopCommerce — não se perdeu)
- Operations View → Dead Letters com **EscalationState = New** (adapter esgotou retries)
- Operations View → Circuit Breakers com warehouse **Open** (vermelho) → `07-warehouse-failure-cb-open.png`

> O outbox mostra `Published` porque a sua responsabilidade termina quando a mensagem chega ao RabbitMQ. A falha acontece do lado do adapter ao chamar o armazém — por isso aparece na Dead Letter.

**Guarda os logs:**
```bash
docker compose logs --no-log-prefix integration_worker 2>/dev/null \
  | grep -E "failed|Retry|Circuit|OPEN|skipping|Dead-letter" | head -30 \
  > docs/evidence/logs/warehouse-failure.txt
```

---

## Fase 3 — Recovery → `08-warehouse-recovery.png`

**Terminal 2:**
```bash
make warehouse-recover
make order
```

O `make order` é necessário para o worker fazer a probe. Espera ~30s. No Terminal 1:
```
Circuit breaker HALF-OPEN probe
Adapter call succeeded
```

**Captura:**
- Operations View → Circuit Breakers com warehouse de volta a **Closed** (verde) → `08-warehouse-recovery.png`

**Guarda os logs:**
```bash
docker compose logs --no-log-prefix integration_worker 2>/dev/null \
  | grep -E "HALF-OPEN|probe|CLOSED" | head -20 \
  > docs/evidence/logs/warehouse-recovery.txt
```

---

## Fase 4 — Shipping dead letter + requeue → `09-deadletter-created.png` + `10-deadletter-requeued.png`

**Terminal 2:**
```bash
make shipping-fail
make order
```

Espera ~2 minutos (5 retries: 2s→4s→8s→16s→30s). No Terminal 1:
```
Dead-letter created  adapter=shipping
```

**Captura A:**
- Operations View → Dead Letters tab → **EscalationState = New** → `09-deadletter-created.png`

**No browser:** clica o botão **Requeue** na dead letter de shipping.

**Terminal 2:**
```bash
make shipping-recover
```

Espera ~10s para o worker processar o record requeued.

**Captura B:**
- Operations View → Dead Letters → **EscalationState = Requeued**
- Operations View → Outbox → novo registo **Published**
- Guarda como `10-deadletter-requeued.png`

---

## Fase 5 — QAS evidence (logs de prova estrutural)

### QAS 1 — Checkout não bloqueia com armazém em falha

```bash
# warehouse já está normal depois da Fase 3
# se não, corre: make warehouse-recover
time ./place-order.sh 1 2 2>&1 | tee docs/evidence/logs/checkout-not-blocked.txt
```

O tempo total deve ser **< 5s** — prova que o checkout não espera pelo armazém.

### QAS 2 — Staleness detectada em 30s

```bash
make stale-start
```
Espera 35s, depois:
```bash
make db-inventory >> docs/evidence/logs/staleness-evidence.txt
make stale-stop
```
A tabela deve mostrar `IsStale=1` nas linhas de inventário.

### QAS 6 — Conflito POS/WMS detectado e checkout limitado

```bash
make conflict-inject
```
Espera 15s, depois:
```bash
make db-inventory >> docs/evidence/logs/conflict-evidence.txt
make conflict-clear
```
Deve mostrar `ConflictFlag=1` e quantidade limitada ao valor mais baixo.

---

## Resumo dos ficheiros a criar

| Ficheiro | Captura quando |
|---|---|
| `screenshots/05-normal-flow.png` | Outbox Published após `make order` |
| `screenshots/06-rabbitmq-queues.png` | RabbitMQ UI com filas visíveis |
| `screenshots/07-warehouse-failure-cb-open.png` | Dead Letter New + CB Open após `make warehouse-fail` |
| `screenshots/08-warehouse-recovery.png` | CB Closed após `make warehouse-recover` + `make order` |
| `screenshots/09-deadletter-created.png` | Dead Letter New após shipping fail |
| `screenshots/10-deadletter-requeued.png` | Dead Letter Requeued + novo outbox Published |
| `logs/normal-flow.txt` | Worker a processar normalmente |
| `logs/warehouse-failure.txt` | Retries + CB Open + Dead Letter |
| `logs/warehouse-recovery.txt` | CB HALF-OPEN probe + Closed |
| `logs/checkout-not-blocked.txt` | Tempo de checkout < 5s com armazém em falha |
| `logs/staleness-evidence.txt` | IsStale=1 na tabela |
| `logs/conflict-evidence.txt` | ConflictFlag=1 + qty limitada |
