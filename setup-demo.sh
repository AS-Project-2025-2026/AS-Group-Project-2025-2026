#!/usr/bin/env bash
# setup-demo.sh — builds the full environment and waits until everything is ready.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# ── colours ──────────────────────────────────────────────────────────────────
GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
ok()   { echo -e "${GREEN}[OK]${NC}    $*"; }
info() { echo -e "${YELLOW}[...]${NC}   $*"; }
fail() { echo -e "${RED}[FAIL]${NC}  $*"; }

# ── .env ─────────────────────────────────────────────────────────────────────
write_env() {
  if [[ -f .env ]]; then
    info ".env already exists — skipping (delete it to regenerate)"
    return
  fi
  info "Creating .env with accelerated demo parameters..."
  cat > .env << 'EOF'
# Resilience — accelerated for demo
RESILIENCE__INITIALDELAYSECONDS=2
RESILIENCE__MAXDELAYSECONDS=30
RESILIENCE__MAXRETRYATTEMPTS=5
RESILIENCE__CIRCUITFAILURETHRESHOLD=3
RESILIENCE__CIRCUITCOOLDOWNSECONDS=30

# Inventory — staleness triggers in 30s instead of 120s
INVENTORY_STALENESS_THRESHOLD=30
INVENTORY_SYNC_INTERVAL=10
INVENTORY_CONFLICT_TOLERANCE=2
EOF
  ok ".env written"
}

# ── wait helpers ──────────────────────────────────────────────────────────────
wait_http() {
  local label="$1" url="$2" max="${3:-120}" interval=5 elapsed=0
  info "Waiting for $label at $url ..."
  while true; do
    if curl -sf --max-time 3 "$url" > /dev/null 2>&1; then
      ok "$label is up"
      return 0
    fi
    elapsed=$((elapsed + interval))
    if [[ $elapsed -ge $max ]]; then
      fail "$label did not respond after ${max}s"
      return 1
    fi
    sleep $interval
    echo -n "."
  done
}

wait_docker_healthy() {
  local label="$1" service="$2" max="${3:-120}" interval=5 elapsed=0
  info "Waiting for $label to be healthy..."
  while true; do
    status=$(docker compose ps -q "$service" 2>/dev/null \
      | xargs -r docker inspect --format '{{.State.Health.Status}}' 2>/dev/null || true)
    if [[ "$status" == "healthy" ]]; then
      ok "$label is healthy"
      return 0
    fi
    elapsed=$((elapsed + interval))
    if [[ $elapsed -ge $max ]]; then
      fail "$label did not become healthy after ${max}s (current: ${status:-unknown})"
      return 1
    fi
    sleep $interval
    echo -n "."
  done
}

# ── main ─────────────────────────────────────────────────────────────────────
echo ""
echo "========================================"
echo "  VerdeMart Demo Setup"
echo "========================================"
echo ""

# 1. .env
write_env

# 2. build + start (detached)
info "Building and starting all containers (this may take a few minutes)..."
docker compose up --build -d
ok "docker compose started"

# 3. wait for infrastructure
echo ""
info "Waiting for infrastructure services..."
wait_docker_healthy "SQL Server"  "nopcommerce_database" 180
wait_docker_healthy "RabbitMQ"    "rabbitmq"             120

# 4. wait for stubs
echo ""
info "Waiting for stubs..."
wait_http "Warehouse stub"       "http://localhost:5081/health" 60
wait_http "Shipping stub"        "http://localhost:5082/health" 60
wait_http "Inventory stub"       "http://localhost:5083/health" 60
wait_http "StorePos stub"        "http://localhost:5084/health" 60
wait_http "CustomerSupport stub" "http://localhost:5085/health" 60

# 5. wait for nopCommerce web (no healthcheck — poll HTTP)
echo ""
wait_http "nopCommerce web" "http://localhost:8080" 300

# 6. confirm worker is running (it has no exposed port, check container state)
echo ""
info "Checking integration_worker container..."
worker_status=$(docker compose ps integration_worker --format json 2>/dev/null \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0].get('State','unknown'))" 2>/dev/null \
  || docker compose ps integration_worker | grep -oE 'running|exited|restarting' | head -1 || true)

if [[ "$worker_status" == "running" ]]; then
  ok "integration_worker is running"
else
  fail "integration_worker state: ${worker_status:-unknown} — check: docker compose logs integration_worker"
fi

# 7. summary
echo ""
echo "========================================"
echo -e "${GREEN}  Setup complete${NC}"
echo "========================================"
echo ""
echo "  nopCommerce web   →  http://localhost:8080"
echo "  Operations View   →  http://localhost:8080/Admin/Operations/List"
echo "  RabbitMQ UI       →  http://localhost:15672  (guest / guest)"
echo "  Warehouse stub    →  http://localhost:5081/health"
echo "  Shipping stub     →  http://localhost:5082/health"
echo "  Inventory stub    →  http://localhost:5083/health"
echo ""
echo "  Follow worker logs:   docker compose logs -f integration_worker"
echo "  Stop everything:      docker compose down"
echo "  Full reset:           docker compose down -v"
echo ""
echo "  Next step: follow docs/evidence/capture-guide.md to capture evidence."
echo ""
