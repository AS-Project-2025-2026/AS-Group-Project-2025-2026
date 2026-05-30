# Guia de Captura de Evidências

Este ficheiro explica passo a passo como correr o sistema e capturar cada screenshot e log necessário.
Tempo total estimado: **30–45 minutos**.

---

## Pré-requisito — Parâmetros acelerados para demo

Antes de arrancar, cria um ficheiro `.env` na raiz do projecto (mesma pasta que `docker-compose.yml`):

```bash
# Raiz do projecto
cat > .env << 'EOF'
# Resilience — acelerado para demo
RESILIENCE__INITIALDELAYSECONDS=2
RESILIENCE__MAXDELAYSECONDS=30
RESILIENCE__MAXRETRYATTEMPTS=5
RESILIENCE__CIRCUITFAILURETHRESHOLD=3
RESILIENCE__CIRCUITCOOLDOWNSECONDS=30

# Inventory — staleness trigga em 30s em vez de 120s
INVENTORY_STALENESS_THRESHOLD=30
INVENTORY_SYNC_INTERVAL=10
INVENTORY_CONFLICT_TOLERANCE=2
EOF
```

---

## FASE 1 — Arrancar o sistema

### 1.1 Build e start

```bash
docker compose up --build
```

Aguarda até todos os serviços estarem saudáveis. Demora 2–5 minutos.
Podes confirmar com:

```bash
docker compose ps
```

Todos os serviços devem ter `STATUS` = `running` ou `healthy`.

### 1.2 Verificar que o nopCommerce está pronto

Abre `http://localhost:8080` no browser.
Deves ver a loja VerdeMart. Se aparecer o wizard de instalação, aguarda mais 1–2 min e refresca.

### 1.3 Abrir a Operations View

Vai a `http://localhost:8080/Admin/Operations/List`
(Login admin: e-mail e password configurados durante a instalação inicial. Se nunca instalaste, o wizard pede-os na primeira vez.)

---

## FASE 2 — Screenshot 01: Inventory normal (sem stale)

**O que capturar:** Operations View com o card "Inventory Projection" a mostrar "No stale or conflicted inventory records."

### Passos:

1. Garante que o inventory stub está em modo `normal` (default — não precisas de fazer nada).
2. Abre `http://localhost:8080/Admin/Operations/List`.
3. O card "Inventory Projection — Stale & Conflicted" deve mostrar a mensagem vazia.
4. Tira screenshot da página inteira ou pelo menos do card Inventory.
5. Guarda como `docs/evidence/screenshots/01-inventory-normal.png`.

> **Se o card mostrar linhas:** o worker ainda não fez o primeiro sync ou há dados de uma run anterior.
> Espera 15s e clica **Refresh** no card. Se persistir, faz `docker compose down -v && docker compose up --build` para limpar os volumes.

---

## FASE 3 — Screenshot 02: Staleness trigado

**O que capturar:** Operations View com uma ou mais linhas amarelas, coluna `Stale = YES`.

### Passos:

1. Para o inventory stub e arranca-o em modo `unavailable`:

```bash
INVENTORY_STUB_MODE=unavailable docker compose up -d --no-deps inventory_stub
```

2. Aguarda `INVENTORY_STALENESS_THRESHOLD` segundos (= **30s** com o `.env` acima).
   O worker tenta sincronizar a cada 10s, não consegue (stub indisponível), e ao fim de 30s marca as projecções como stale.

3. Abre `http://localhost:8080/Admin/Operations/List` e clica **Refresh** no card Inventory.
   Deves ver linhas com fundo amarelo e `Stale = YES`.

4. Tira screenshot.
   Guarda como `docs/evidence/screenshots/02-staleness-triggered.png`.

5. Restaura o stub para normal:

```bash
INVENTORY_STUB_MODE=normal docker compose up -d --no-deps inventory_stub
```

6. Aguarda 15s, clica **Refresh** — as linhas devem desaparecer (stale limpo após sync bem-sucedido).

---

## FASE 4 — Screenshot 03: Conflito POS vs WMS

**O que capturar:** Operations View com uma linha vermelha, `ConflictFlag = YES`.

O stub de inventário só reporta valores WMS. Para simular um conflito POS precisas de inserir uma linha directamente na base de dados com `SourceSystem = 'pos'` e uma quantidade que difira da WMS em mais de 2 unidades.

### Passos:

**4.1 Verificar qual é a quantidade WMS actual para o produto 1:**

```bash
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "SELECT ProductId, SourceSystem, ReportedQuantity FROM InventoryProjectionRecord"
```

Anota o valor de `ReportedQuantity` para `ProductId = 1`.

**4.2 Inserir linha POS com quantidade divergente (diferença > 2):**

Se o WMS reportou, por exemplo, 50 unidades para o produto 1, insere POS com 10 (diferença = 40 > tolerância de 2):

```bash
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "INSERT INTO InventoryProjectionRecord (ProductId, SourceSystem, ReportedQuantity, LastConfirmedUtc, IsStale, ConflictFlag, PendingReconciliation, UpdatedAtUtc) VALUES (1, 'pos', 10, GETUTCDATE(), 0, 0, 0, GETUTCDATE())"
```

**4.3 Aguardar o próximo ciclo de sync (até 10s) e verificar:**

O worker, no próximo ciclo, detecta a divergência WMS vs POS e activa `ConflictFlag = 1` em ambas as linhas.

```bash
docker compose logs -f integration_worker 2>&1 | grep -i "conflict\|Inventory conflict"
```

Deves ver uma linha como:
```
Inventory conflict on ProductId=1: wms=50 vs pos=10 (diff=40, tolerance=2) — checkout capped at 10
```

**4.4 Screenshot:**

Abre `http://localhost:8080/Admin/Operations/List` e clica **Refresh** no card Inventory.
Deves ver linha(s) com fundo vermelho e `Conflict = YES`.

Guarda como `docs/evidence/screenshots/03-conflict-flagged.png`.

**4.5 Limpar o conflito (opcional, para deixar o sistema limpo):**

```bash
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "DELETE FROM InventoryProjectionRecord WHERE SourceSystem = 'pos'"
```

---

## FASE 5 — Screenshot 04: Circuit Breakers

**O que capturar:** Card "Circuit Breaker State" com pelo menos um adaptador em estado `Open` ou `HalfOpen`.

### Passos:

**5.1 Colocar o warehouse stub em modo failed:**

```bash
WAREHOUSE_STUB_MODE=failed docker compose up -d --no-deps warehouse_stub
```

**5.2 Fazer uma order para triggar o circuit breaker:**

1. Vai a `http://localhost:8080`.
2. Adiciona um produto ao carrinho e faz checkout (completa a ordem).
3. O worker tenta contactar o warehouse, falha repetidamente.
4. Com `RESILIENCE__CIRCUITFAILURETHRESHOLD=3` e `RESILIENCE__MAXRETRYATTEMPTS=5`, ao fim de ~1 min o circuit abre.

Monitoriza:

```bash
docker compose logs -f integration_worker 2>&1 | grep -i "circuit\|Circuit"
```

**5.3 Screenshot:**

Abre `http://localhost:8080/Admin/Operations/List` e clica **Refresh** no card Circuit Breaker State.
Deves ver o adaptador `warehouse` em estado `Open` (texto vermelho).

Guarda como `docs/evidence/screenshots/04-circuit-breakers.png`.

**5.4 Restaurar:**

```bash
WAREHOUSE_STUB_MODE=normal docker compose up -d --no-deps warehouse_stub
```

---

## FASE 6 — Screenshot 05: Dead Letters

**O que capturar:** Tab Dead Letters com pelo menos um registo, `EscalationState = New`.

O dead letter é criado automaticamente após `MaxRetryAttempts` falhados (default demo: 5).
Se fizeste a FASE 5, já deve haver um dead letter. Verifica:

```bash
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "SELECT TOP 5 Id, Adapter, EscalationState, FailureReason FROM DeadLetterRecord ORDER BY CreatedAtUtc DESC"
```

### Passos:

1. Abre `http://localhost:8080/Admin/Operations/List`.
2. Clica no tab **Dead Letters**.
3. Deves ver pelo menos um registo com `EscalationState = New` e `Adapter = warehouse` (ou `fulfillment`).
4. Screenshot.
   Guarda como `docs/evidence/screenshots/05-dead-letters.png`.

> Se não houver dead letters ainda, repete a FASE 5 e aguarda até o worker esgotar as retries (pode demorar 2–5 min dependendo dos `MaxDelaySeconds`).

---

## FASE 7 — Log: Staleness detection

**O que capturar:** Excerto do log do worker com `MarkedStale=true` ou `IsStale`.

### Passos:

1. Repete a FASE 3 (stub unavailable por 30s).
2. Enquanto aguardas, corre num terminal:

```bash
docker compose logs -f integration_worker 2>&1 | grep -i "stale\|Stale\|IsStale"
```

3. Quando aparecer uma linha como:
   ```
   Inventory staleness: 5 projection(s) marked IsStale=true (threshold=30s)
   ```
   Copia-a (e as 2–3 linhas de contexto à volta) para um ficheiro de texto.

4. Guarda como `docs/evidence/logs/staleness-detection.txt`.

**Alternativa — exportar directamente para ficheiro:**

```bash
docker compose logs integration_worker 2>&1 | grep -A2 -B2 -i "stale" > docs/evidence/logs/staleness-detection.txt
```

---

## FASE 8 — Log: Conflict detection

**O que capturar:** Excerto do log com `Inventory conflict on ProductId`.

### Passos:

1. Repete a FASE 4 (inserção da linha POS).
2. Aguarda o próximo ciclo de sync.
3. Exporta o log:

```bash
docker compose logs integration_worker 2>&1 | grep -A2 -B2 -i "conflict" > docs/evidence/logs/conflict-detected.txt
```

4. Confirma que o ficheiro não está vazio:

```bash
cat docs/evidence/logs/conflict-detected.txt
```

---

## Comandos de diagnóstico úteis

```bash
# Ver todos os logs do worker em tempo real
docker compose logs -f integration_worker

# Estado da tabela InventoryProjectionRecord
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "SELECT Id, ProductId, SourceSystem, ReportedQuantity, IsStale, ConflictFlag, PendingReconciliation, LastConfirmedUtc FROM InventoryProjectionRecord ORDER BY UpdatedAtUtc DESC"

# Estado do outbox
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "SELECT TOP 10 Id, OrderId, Status, RetryCount, LastError FROM OutboxRecord ORDER BY CreatedAtUtc DESC"

# Estado dos circuit breakers
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" \
  -Q "SELECT Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc FROM CircuitBreakerStateRecord"

# Health do inventory stub
curl http://localhost:5083/health

# Stock actual do inventory stub
curl http://localhost:5083/stock

# Parar tudo e limpar volumes (fresh start)
docker compose down -v
```

---

## Ordem recomendada de captura

| # | Screenshot/Log | Fase | Pré-condição |
|---|---|---|---|
| 1 | `01-inventory-normal.png` | 2 | Sistema recém-arrancado, stub normal |
| 2 | `02-staleness-triggered.png` | 3 | Stub unavailable por 30s |
| 3 | `staleness-detection.txt` | 7 | Durante a FASE 3 |
| 4 | `03-conflict-flagged.png` | 4 | Inserção SQL da linha POS |
| 5 | `conflict-detected.txt` | 8 | Durante a FASE 4 |
| 6 | `04-circuit-breakers.png` | 5 | Order feita com warehouse em failed |
| 7 | `05-dead-letters.png` | 6 | Após circuit breaker abrir e retries esgotarem |

---

## Nota sobre o sqlcmd

Se o comando `sqlcmd` falhar com "command not found", tenta com o path alternativo:

```bash
# Versão nova (mssql-tools18)
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" -Q "SELECT 1"

# Versão antiga (mssql-tools)
docker exec -it $(docker compose ps -q nopcommerce_database) \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "nopCommerce_db_password" -Q "SELECT 1"
```
