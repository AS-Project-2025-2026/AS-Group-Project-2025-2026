# VerdeMart Demo Makefile
# Usage: make <target>
# Run 'make help' to see all targets.

COMPOSE       := docker compose
SQL_CMD       := $(COMPOSE) exec -T nopcommerce_database \
                   /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa \
                   -P "nopCommerce_db_password" -d NopCommerce
WORKER_LOG    := $(COMPOSE) logs --no-log-prefix -f integration_worker
INV_URL       := http://localhost:5083

GREEN  := \033[0;32m
YELLOW := \033[1;33m
RED    := \033[0;31m
CYAN   := \033[0;36m
BOLD   := \033[1m
NC     := \033[0m

.PHONY: help up down reset logs \
        order order-burst order-loop \
        demo-healthy demo-degraded \
        stale-start stale-stop \
        conflict-inject conflict-clear \
        warehouse-fail warehouse-recover \
        shipping-fail shipping-recover \
        status db-outbox db-deadletter db-inventory db-cb

# ─────────────────────────────────────────────────────────────────────────────
# Default
# ─────────────────────────────────────────────────────────────────────────────

help: ## Show this help
	@echo ""
	@echo "  $(BOLD)VerdeMart Integration Demo$(NC)"
	@echo ""
	@echo "  $(CYAN)Setup$(NC)"
	@printf "  %-28s %s\n" "make up"             "Build and start all containers"
	@printf "  %-28s %s\n" "make down"           "Stop all containers"
	@printf "  %-28s %s\n" "make reset"          "Full reset — stops and deletes volumes"
	@printf "  %-28s %s\n" "make logs"           "Follow integration_worker logs"
	@printf "  %-28s %s\n" "make status"         "Show health of all services"
	@echo ""
	@echo "  $(CYAN)Real orders (triggers outbox + worker)$(NC)"
	@printf "  %-28s %s\n" "make order"          "Place 1 real order through checkout"
	@printf "  %-28s %s\n" "make order-burst"    "Place 5 orders in sequence"
	@printf "  %-28s %s\n" "make order-loop"     "Place 1 order every 20s until Ctrl+C"
	@echo ""
	@echo "  $(CYAN)Demo snapshots (SQL-injected, no checkout)$(NC)"
	@printf "  %-28s %s\n" "make demo-healthy"   "Populate Operations View — all green"
	@printf "  %-28s %s\n" "make demo-degraded"  "Populate Operations View — failures visible"
	@echo ""
	@echo "  $(CYAN)QAS 2 — Stale Inventory$(NC)"
	@printf "  %-28s %s\n" "make stale-start"    "Cut inventory stub → triggers staleness in 30s"
	@printf "  %-28s %s\n" "make stale-stop"     "Restore inventory stub → staleness clears"
	@echo ""
	@echo "  $(CYAN)QAS 6 — Inventory Conflict (POS vs WMS)$(NC)"
	@printf "  %-28s %s\n" "make conflict-inject" "Inject POS stock that conflicts with WMS"
	@printf "  %-28s %s\n" "make conflict-clear"  "Clear conflict rows from DB"
	@echo ""
	@echo "  $(CYAN)QAS 1 — Warehouse Failure$(NC)"
	@printf "  %-28s %s\n" "make warehouse-fail"    "Set warehouse stub to failed mode"
	@printf "  %-28s %s\n" "make warehouse-recover" "Restore warehouse stub to normal"
	@echo ""
	@echo "  $(CYAN)QAS 4 — Shipping Outage$(NC)"
	@printf "  %-28s %s\n" "make shipping-fail"    "Set shipping stub to outage mode"
	@printf "  %-28s %s\n" "make shipping-recover" "Restore shipping stub to normal"
	@echo ""
	@echo "  $(CYAN)Inspect$(NC)"
	@printf "  %-28s %s\n" "make db-outbox"      "Show last 10 outbox records"
	@printf "  %-28s %s\n" "make db-deadletter"  "Show last 10 dead-letter records"
	@printf "  %-28s %s\n" "make db-inventory"   "Show all inventory projection records"
	@printf "  %-28s %s\n" "make db-cb"          "Show circuit breaker states"
	@echo ""
	@echo "  URLs: web=http://localhost:8080  ops=http://localhost:8080/Admin/Operations/List"
	@echo "        rabbitmq=http://localhost:15672 (guest/guest)"
	@echo ""

# ─────────────────────────────────────────────────────────────────────────────
# Setup
# ─────────────────────────────────────────────────────────────────────────────

up: ## Build and start all containers with accelerated demo parameters
	@echo "$(YELLOW)[...]$(NC)  Writing .env with accelerated demo parameters..."
	@if [ ! -f .env ]; then \
		printf '%s\n' \
			'RESILIENCE__INITIALDELAYSECONDS=2' \
			'RESILIENCE__MAXDELAYSECONDS=30' \
			'RESILIENCE__MAXRETRYATTEMPTS=5' \
			'RESILIENCE__CIRCUITFAILURETHRESHOLD=3' \
			'RESILIENCE__CIRCUITCOOLDOWNSECONDS=30' \
			'INVENTORY_STALENESS_THRESHOLD=30' \
			'INVENTORY_SYNC_INTERVAL=10' \
			'INVENTORY_CONFLICT_TOLERANCE=2' \
			> .env; \
		echo "$(GREEN)[OK]$(NC)    .env created"; \
	else \
		echo "$(YELLOW)[...]$(NC)  .env already exists — skipping"; \
	fi
	@echo "$(YELLOW)[...]$(NC)  Starting containers..."
	$(COMPOSE) up --build -d
	@echo ""
	@echo "$(YELLOW)[...]$(NC)  Waiting for SQL Server..."
	@until $(COMPOSE) exec -T nopcommerce_database \
		/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa \
		-P "nopCommerce_db_password" -Q "SELECT 1" > /dev/null 2>&1; do \
		printf "."; sleep 5; done
	@echo " $(GREEN)ready$(NC)"
	@echo "$(YELLOW)[...]$(NC)  Waiting for nopCommerce web..."
	@until curl -sf --max-time 3 http://localhost:8080 > /dev/null 2>&1; do \
		printf "."; sleep 5; done
	@echo " $(GREEN)ready$(NC)"
	@echo ""
	@echo "$(GREEN)[OK]$(NC)    Environment is up"
	@echo "       Web          →  http://localhost:8080"
	@echo "       Operations   →  http://localhost:8080/Admin/Operations/List"
	@echo "       RabbitMQ     →  http://localhost:15672  (guest/guest)"
	@echo ""

down: ## Stop all containers
	$(COMPOSE) down
	@echo "$(GREEN)[OK]$(NC)    Stopped"

reset: ## Full reset — stop containers and delete all volumes
	@echo "$(RED)[!]$(NC)    This will delete all data. Press Ctrl+C to cancel, Enter to continue."
	@read confirm
	$(COMPOSE) down -v
	@echo "$(GREEN)[OK]$(NC)    Reset complete"

logs: ## Follow integration_worker logs
	$(WORKER_LOG)

status: ## Show health status of all services
	@echo ""
	@echo "$(BOLD)  Service Health$(NC)"
	@echo ""
	@printf "  %-24s" "nopCommerce web"; \
		curl -sf --max-time 2 http://localhost:8080 > /dev/null 2>&1 \
		&& echo "$(GREEN)up$(NC)" || echo "$(RED)down$(NC)"
	@printf "  %-24s" "warehouse stub"; \
		curl -sf --max-time 2 http://localhost:5081/health > /dev/null 2>&1 \
		&& curl -s http://localhost:5081/health | python3 -c "import sys,json; d=json.load(sys.stdin); print('\033[0;32m' + d.get('mode','?') + '\033[0m')" 2>/dev/null \
		|| echo "$(RED)down$(NC)"
	@printf "  %-24s" "shipping stub"; \
		curl -sf --max-time 2 http://localhost:5082/health > /dev/null 2>&1 \
		&& curl -s http://localhost:5082/health | python3 -c "import sys,json; d=json.load(sys.stdin); print('\033[0;32m' + d.get('mode','?') + '\033[0m')" 2>/dev/null \
		|| echo "$(RED)down$(NC)"
	@printf "  %-24s" "inventory stub"; \
		curl -sf --max-time 2 http://localhost:5083/health > /dev/null 2>&1 \
		&& curl -s http://localhost:5083/health | python3 -c "import sys,json; d=json.load(sys.stdin); print('\033[0;32m' + d.get('mode','?') + '\033[0m')" 2>/dev/null \
		|| echo "$(RED)down$(NC)"
	@printf "  %-24s" "storepos stub"; \
		curl -sf --max-time 2 http://localhost:5084/health > /dev/null 2>&1 \
		&& curl -s http://localhost:5084/health | python3 -c "import sys,json; d=json.load(sys.stdin); print('\033[0;32m' + d.get('mode','?') + '\033[0m')" 2>/dev/null \
		|| echo "$(RED)down$(NC)"
	@printf "  %-24s" "customersupport stub"; \
		curl -sf --max-time 2 http://localhost:5085/health > /dev/null 2>&1 \
		&& curl -s http://localhost:5085/health | python3 -c "import sys,json; d=json.load(sys.stdin); print('\033[0;32m' + d.get('mode','?') + '\033[0m')" 2>/dev/null \
		|| echo "$(RED)down$(NC)"
	@printf "  %-24s" "rabbitmq"; \
		curl -sf --max-time 2 http://localhost:15672 > /dev/null 2>&1 \
		&& echo "$(GREEN)up$(NC)" || echo "$(RED)down$(NC)"
	@printf "  %-24s" "integration_worker"; \
		$(COMPOSE) ps integration_worker --format json 2>/dev/null \
		| python3 -c "import sys,json; rows=json.load(sys.stdin); st=rows[0].get('State','?') if rows else '?'; print('\033[0;32m' + st + '\033[0m' if st=='running' else '\033[0;31m' + st + '\033[0m')" 2>/dev/null \
		|| echo "$(RED)unknown$(NC)"
	@echo ""

# ─────────────────────────────────────────────────────────────────────────────
# Real orders
# ─────────────────────────────────────────────────────────────────────────────

order: ## Place 1 real order through the checkout (triggers outbox + worker)
	@./place-order.sh 1

order-burst: ## Place 5 orders in sequence (shows outbox filling up)
	@./place-order.sh 5

order-loop: ## Keep placing 1 order every 20s until Ctrl+C (run in a separate terminal)
	@echo "$(YELLOW)[...]$(NC)  Placing orders every 20s — Ctrl+C to stop"
	@while true; do \
		./place-order.sh 1 || echo "$(YELLOW)[WARN]$(NC) order failed, retrying next cycle"; \
		echo "$(YELLOW)[...]$(NC)  Next order in 20s..."; \
		sleep 20; \
	done

# ─────────────────────────────────────────────────────────────────────────────
# Demo data (SQL-injected snapshots — no real checkout)
# ─────────────────────────────────────────────────────────────────────────────

demo-healthy: ## Populate Operations View with all-green state (SQL snapshot)
	@./place-demo-orders.sh healthy

demo-degraded: ## Populate Operations View with failures, retries, dead letters (SQL snapshot)
	@./place-demo-orders.sh degraded

# ─────────────────────────────────────────────────────────────────────────────
# QAS 2 — Stale Inventory
# ─────────────────────────────────────────────────────────────────────────────

stale-start: ## Cut inventory stub (unavailable) — staleness triggers in ~30s
	@echo "$(YELLOW)[...]$(NC)  Setting inventory stub to unavailable..."
	INVENTORY_STUB_MODE=unavailable $(COMPOSE) up -d --no-deps --no-build inventory_stub
	@echo "$(GREEN)[OK]$(NC)    Inventory stub is now unavailable"
	@echo "       Wait ~30s then check: http://localhost:8080/Admin/Operations/List"
	@echo "       Worker log:  make logs"

stale-stop: ## Restore inventory stub — IsStale clears on next sync cycle
	@echo "$(YELLOW)[...]$(NC)  Restoring inventory stub to normal..."
	INVENTORY_STUB_MODE=normal $(COMPOSE) up -d --no-deps --no-build inventory_stub
	@echo "$(GREEN)[OK]$(NC)    Inventory stub restored"
	@echo "       IsStale will clear within ~10s (next sync cycle)"

# ─────────────────────────────────────────────────────────────────────────────
# QAS 6 — Inventory Conflict (POS vs WMS)
# ─────────────────────────────────────────────────────────────────────────────

conflict-inject: ## Inject a POS stock value that conflicts with WMS via stub API (diff > 2 units)
	@echo "$(YELLOW)[...]$(NC)  Reporting POS stock via stub API: product 1 = 3 units..."
	@result=$$(curl -s -X POST $(INV_URL)/stock/pos-report \
		-H "Content-Type: application/json" \
		-d '{"productId": 1, "quantity": 3}'); \
	echo "$$result" | python3 -c "import sys,json; d=json.load(sys.stdin); \
		print('$(GREEN)[OK]$(NC)    POS reported: productId=' + str(d.get('productId','?')) + \
		'  posQty=' + str(d.get('posQuantity','?')) + \
		'  wmsQty=' + str(d.get('wmsQuantity','?')) + \
		'  conflict=' + str(d.get('conflict','?')))" 2>/dev/null \
	|| echo "$(RED)[FAIL]$(NC)  Could not reach inventory stub at $(INV_URL)"
	@echo "       Wait ~10s for next sync cycle to detect conflict"
	@echo "       Operations → Inventory tab should show ConflictFlag=true (red row)"

conflict-clear: ## Clear POS stock from stub and reset conflict flags in DB
	@echo "$(YELLOW)[...]$(NC)  Clearing POS stock from stub..."
	@curl -s -X POST $(INV_URL)/stock/pos-report \
		-H "Content-Type: application/json" \
		-d '{"productId": 1, "quantity": 0}' > /dev/null 2>&1 || true
	@echo "$(YELLOW)[...]$(NC)  Resetting conflict flags in DB..."
	@$(SQL_CMD) -Q "\
		DELETE FROM InventoryProjectionRecord WHERE SourceSystem = 'pos'; \
		UPDATE InventoryProjectionRecord \
			SET ConflictFlag=0, PendingReconciliation=0, ResolvedAtUtc=GETUTCDATE(), UpdatedAtUtc=GETUTCDATE() \
		WHERE ConflictFlag=1;" > /dev/null 2>&1
	@echo "$(GREEN)[OK]$(NC)    Conflict cleared"

# ─────────────────────────────────────────────────────────────────────────────
# QAS 1 — Warehouse Failure
# ─────────────────────────────────────────────────────────────────────────────

warehouse-fail: ## Set warehouse stub to failed mode (triggers retries + circuit breaker)
	@echo "$(YELLOW)[...]$(NC)  Setting warehouse stub to failed..."
	WAREHOUSE_STUB_MODE=failed $(COMPOSE) up -d --no-deps --no-build warehouse_stub
	@echo "$(GREEN)[OK]$(NC)    Warehouse stub is now failing"
	@echo "       Place an order at http://localhost:8080 and watch: make logs"
	@echo "       After 3 failures the circuit breaker opens"

warehouse-recover: ## Restore warehouse stub to normal
	@echo "$(YELLOW)[...]$(NC)  Restoring warehouse stub to normal..."
	WAREHOUSE_STUB_MODE=normal $(COMPOSE) up -d --no-deps --no-build warehouse_stub
	@echo "$(GREEN)[OK]$(NC)    Warehouse stub restored — circuit will probe within cooldown window"

# ─────────────────────────────────────────────────────────────────────────────
# QAS 4 — Shipping Outage
# ─────────────────────────────────────────────────────────────────────────────

shipping-fail: ## Set shipping stub to outage mode
	@echo "$(YELLOW)[...]$(NC)  Setting shipping stub to outage..."
	SHIPPING_STUB_MODE=outage $(COMPOSE) up -d --no-deps --no-build shipping_stub
	@echo "$(GREEN)[OK]$(NC)    Shipping stub is now in outage"
	@echo "       Place an order and watch: make logs"
	@echo "       After max retries a Dead Letter is created"

shipping-recover: ## Restore shipping stub to normal
	@echo "$(YELLOW)[...]$(NC)  Restoring shipping stub to normal..."
	SHIPPING_STUB_MODE=normal $(COMPOSE) up -d --no-deps --no-build shipping_stub
	@echo "$(GREEN)[OK]$(NC)    Shipping stub restored"
	@echo "       Use Operations View to Requeue dead-letter records"

# ─────────────────────────────────────────────────────────────────────────────
# DB Inspection
# ─────────────────────────────────────────────────────────────────────────────

db-outbox: ## Show last 10 outbox records
	@$(SQL_CMD) -Q \
		"SET NOCOUNT ON; \
		 SELECT TOP 10 Id, OrderId, MessageType, Status, RetryCount, \
			CONVERT(VARCHAR(19), CreatedAtUtc, 120) AS Created, \
			LEFT(ISNULL(LastError,'—'), 60) AS LastError \
		 FROM OutboxRecord ORDER BY CreatedAtUtc DESC;"

db-deadletter: ## Show last 10 dead-letter records
	@$(SQL_CMD) -Q \
		"SET NOCOUNT ON; \
		 SELECT TOP 10 Id, Adapter, EscalationState, \
			CONVERT(VARCHAR(19), CreatedAtUtc, 120) AS Created, \
			LEFT(ISNULL(FailureReason,'—'), 60) AS FailureReason \
		 FROM DeadLetterRecord ORDER BY CreatedAtUtc DESC;"

db-inventory: ## Show all inventory projection records
	@$(SQL_CMD) -Q \
		"SET NOCOUNT ON; \
		 SELECT ProductId, SourceSystem, ReportedQuantity, \
			IsStale, ConflictFlag, PendingReconciliation, \
			CONVERT(VARCHAR(19), LastConfirmedUtc, 120) AS LastConfirmed, \
			CONVERT(VARCHAR(19), UpdatedAtUtc, 120) AS Updated \
		 FROM InventoryProjectionRecord \
		 ORDER BY ProductId, SourceSystem;"

db-cb: ## Show circuit breaker states
	@$(SQL_CMD) -Q \
		"SET NOCOUNT ON; \
		 SELECT Adapter, State, FailureCount, \
			CONVERT(VARCHAR(19), OpenedAtUtc, 120) AS OpenedAt, \
			CONVERT(VARCHAR(19), NextProbeAtUtc, 120) AS NextProbe, \
			LEFT(ISNULL(LastError,'—'), 50) AS LastError \
		 FROM CircuitBreakerStateRecord ORDER BY Adapter;"
