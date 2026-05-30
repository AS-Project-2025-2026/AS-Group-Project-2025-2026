#!/usr/bin/env bash
# place-demo-orders.sh
# Populates the Operations View with demo data.
#
# Usage:
#   ./place-demo-orders.sh          → healthy state (all green) — screenshot 01
#   ./place-demo-orders.sh degraded → degraded state (failures, open circuit) — screenshot 02+
#
set -euo pipefail

MODE="${1:-healthy}"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
ok()   { echo -e "${GREEN}[OK]${NC}    $*"; }
info() { echo -e "${YELLOW}[...]${NC}   $*"; }

COMPOSE="docker compose -f /home/alof/Desktop/AS/AS-Group-Project-2025-2026/docker-compose.yml"
SQL() { $COMPOSE exec nopcommerce_database \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "nopCommerce_db_password" -d NopCommerce -Q "$1" 2>/dev/null; }

echo ""
echo "========================================"
echo "  VerdeMart — Populate Demo Data ($MODE)"
echo "========================================"
echo ""

# ── check orders exist ────────────────────────────────────────────────────────
ORDER_COUNT=$(SQL "SET NOCOUNT ON; SELECT COUNT(*) FROM [Order];" | grep -E "^\s*[0-9]" | tr -d ' \r')
if [[ -z "$ORDER_COUNT" || "$ORDER_COUNT" -eq 0 ]]; then
  echo -e "${RED}[FAIL]${NC}  No orders found in DB. Install with sample data first." >&2
  exit 1
fi
info "Found $ORDER_COUNT orders in DB"

# ── 1. clear previous demo data ───────────────────────────────────────────────
info "Clearing previous integration demo data..."
SQL "DELETE FROM OutboxRecord; DELETE FROM DeadLetterRecord; DELETE FROM CircuitBreakerStateRecord;" > /dev/null
ok "Cleared"

# ── 2. insert data based on mode ─────────────────────────────────────────────
if [[ "$MODE" == "healthy" ]]; then

  info "Inserting HEALTHY state (all green)..."
  SQL "
  -- All orders published successfully
  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (3, 'FulfillmentRequested',
    '{\"orderId\":3,\"correlationId\":\"order-3\",\"items\":[{\"productId\":4,\"quantity\":1}]}',
    'order-3', NEWID(), 'Published', 0,
    DATEADD(MINUTE,-20,GETUTCDATE()), NULL, DATEADD(MINUTE,-19,GETUTCDATE()), NULL);

  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (4, 'FulfillmentRequested',
    '{\"orderId\":4,\"correlationId\":\"order-4\",\"items\":[{\"productId\":5,\"quantity\":2}]}',
    'order-4', NEWID(), 'Published', 0,
    DATEADD(MINUTE,-15,GETUTCDATE()), NULL, DATEADD(MINUTE,-14,GETUTCDATE()), NULL);

  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (5, 'FulfillmentRequested',
    '{\"orderId\":5,\"correlationId\":\"order-5\",\"items\":[{\"productId\":3,\"quantity\":1}]}',
    'order-5', NEWID(), 'Published', 0,
    DATEADD(MINUTE,-10,GETUTCDATE()), NULL, DATEADD(MINUTE,-9,GETUTCDATE()), NULL);

  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (5, 'ShippingRequested',
    '{\"orderId\":5,\"correlationId\":\"order-5-shipping\",\"items\":[{\"productId\":3,\"quantity\":1}]}',
    'order-5-shipping', NEWID(), 'Published', 0,
    DATEADD(MINUTE,-9,GETUTCDATE()), NULL, DATEADD(MINUTE,-8,GETUTCDATE()), NULL);

  -- All circuit breakers closed
  INSERT INTO CircuitBreakerStateRecord (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
  VALUES ('warehouse', 'Closed', 0, NULL, NULL, NULL, DATEADD(MINUTE,-1,GETUTCDATE()));
  INSERT INTO CircuitBreakerStateRecord (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
  VALUES ('shipping', 'Closed', 0, NULL, NULL, NULL, DATEADD(MINUTE,-1,GETUTCDATE()));
  INSERT INTO CircuitBreakerStateRecord (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
  VALUES ('inventory', 'Closed', 0, NULL, NULL, NULL, DATEADD(SECOND,-10,GETUTCDATE()));
  " > /dev/null
  ok "Healthy state inserted — all Published, all Closed, no Dead Letters, no Inventory alerts"

else

  info "Inserting DEGRADED state (failures visible)..."
  SQL "
  -- Two published, one retrying, one failed
  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (3, 'FulfillmentRequested',
    '{\"orderId\":3,\"correlationId\":\"order-3\",\"items\":[{\"productId\":4,\"quantity\":1}]}',
    'order-3', NEWID(), 'Published', 0,
    DATEADD(MINUTE,-15,GETUTCDATE()), NULL, DATEADD(MINUTE,-14,GETUTCDATE()), NULL);

  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (4, 'FulfillmentRequested',
    '{\"orderId\":4,\"correlationId\":\"order-4\",\"items\":[{\"productId\":5,\"quantity\":2}]}',
    'order-4', NEWID(), 'Published', 0,
    DATEADD(MINUTE,-10,GETUTCDATE()), NULL, DATEADD(MINUTE,-9,GETUTCDATE()), NULL);

  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (5, 'FulfillmentRequested',
    '{\"orderId\":5,\"correlationId\":\"order-5\",\"items\":[{\"productId\":3,\"quantity\":1}]}',
    'order-5', NEWID(), 'Retrying', 3,
    DATEADD(MINUTE,-5,GETUTCDATE()), DATEADD(MINUTE,2,GETUTCDATE()), NULL,
    'Warehouse adapter timeout after 30s (attempt 3)');

  INSERT INTO OutboxRecord (OrderId, MessageType, Payload, CorrelationId, IdempotencyKey, Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError)
  VALUES (5, 'ShippingRequested',
    '{\"orderId\":5,\"correlationId\":\"order-5-shipping\",\"items\":[{\"productId\":3,\"quantity\":1}]}',
    'order-5-shipping', NEWID(), 'Failed', 10,
    DATEADD(MINUTE,-8,GETUTCDATE()), NULL, NULL,
    'Max retry attempts reached. Shipping stub unavailable (HTTP 503).');

  -- Dead letter
  INSERT INTO DeadLetterRecord (OriginalOutboxRecordId, Payload, IdempotencyKey, CorrelationId, Adapter, FailureReason, FirstAttemptAtUtc, LastAttemptAtUtc, EscalationState, CreatedAtUtc)
  VALUES (NULL,
    '{\"orderId\":5,\"correlationId\":\"order-5-shipping\",\"items\":[{\"productId\":3,\"quantity\":1}]}',
    NEWID(), 'order-5-shipping', 'shipping',
    'HTTP 503 from shipping stub after 10 retry attempts over 8 minutes. Circuit breaker opened.',
    DATEADD(MINUTE,-8,GETUTCDATE()), DATEADD(MINUTE,-1,GETUTCDATE()),
    'New', DATEADD(MINUTE,-1,GETUTCDATE()));

  -- warehouse closed, shipping open, inventory closed
  INSERT INTO CircuitBreakerStateRecord (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
  VALUES ('warehouse', 'Closed', 0, NULL, NULL, NULL, DATEADD(MINUTE,-1,GETUTCDATE()));
  INSERT INTO CircuitBreakerStateRecord (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
  VALUES ('shipping', 'Open', 5,
    DATEADD(MINUTE,-3,GETUTCDATE()), DATEADD(MINUTE,2,GETUTCDATE()),
    'HTTP 503 Service Unavailable — shipping stub in outage mode',
    DATEADD(MINUTE,-3,GETUTCDATE()));
  INSERT INTO CircuitBreakerStateRecord (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
  VALUES ('inventory', 'Closed', 0, NULL, NULL, NULL, DATEADD(SECOND,-10,GETUTCDATE()));
  " > /dev/null
  ok "Degraded state inserted — Retrying + Failed outbox, Dead Letter, shipping Open"

fi

# ── 5. verify ─────────────────────────────────────────────────────────────────
echo ""
info "Verification:"
SQL "
SET NOCOUNT ON;
SELECT 'Outbox' AS [Table], COUNT(*) AS Rows FROM OutboxRecord
UNION ALL SELECT 'DeadLetter', COUNT(*) FROM DeadLetterRecord
UNION ALL SELECT 'CircuitBreaker', COUNT(*) FROM CircuitBreakerStateRecord
UNION ALL SELECT 'InventoryProjection', COUNT(*) FROM InventoryProjectionRecord;
" 2>/dev/null

echo ""
echo "========================================"
echo -e "${GREEN}  Done — refresh the Operations View${NC}"
echo "========================================"
echo ""
echo "  Operations View  →  http://localhost:8080/Admin/Operations/List"
echo ""
