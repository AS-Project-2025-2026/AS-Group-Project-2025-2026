#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
BASE_URL="${BASE_URL%/}"

ADMIN_EMAIL="${INSTALL_ADMIN_EMAIL:-admin@verdemart.com}"
ADMIN_PASSWORD="${INSTALL_ADMIN_PASSWORD:-Admin1234!}"
INSTALL_SAMPLE_DATA="${INSTALL_SAMPLE_DATA:-true}"

DB_SERVER="${INSTALL_DB_SERVER:-nopcommerce_database}"
DB_NAME="${INSTALL_DB_NAME:-NopCommerce}"
DB_USER="${INSTALL_DB_USER:-sa}"
DB_PASSWORD="${INSTALL_DB_PASSWORD:-nopCommerce_db_password}"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

cookie_jar="$work_dir/cookies.txt"
install_page="$work_dir/install.html"
install_result="$work_dir/install-result.html"

bool_to_form() {
  case "${1,,}" in
    true|1|yes|on) printf "true" ;;
    *) printf "false" ;;
  esac
}

extract_token() {
  perl -ne 'if (/name="__RequestVerificationToken"[^>]*value="([^"]+)"/) { print $1; exit 0 }' "$1"
}

extract_selected_country() {
  perl -0ne '
    if (/<select[^>]*id="Country"[^>]*>(.*?)<\/select>/s) {
      my $block = $1;
      if ($block =~ /<option[^>]*selected="selected"[^>]*value="([^"]*)"/s) {
        print $1;
      }
    }
  ' "$1"
}

# ── 1. wait for install page ──────────────────────────────────────────────────
echo "Waiting for $BASE_URL/install ..."
for attempt in $(seq 1 90); do
  if curl -fsS --max-time 5 -c "$cookie_jar" "$BASE_URL/install" -o "$install_page" 2>/dev/null; then
    break
  fi
  if [[ $attempt -eq 90 ]]; then
    echo "Timed out waiting for install page (3 min). Is the app running?" >&2
    exit 1
  fi
  sleep 2
  printf "."
done
echo ""

# ── 2. already installed? ─────────────────────────────────────────────────────
if ! grep -qi "nopCommerce installation" "$install_page"; then
  echo "Install page not active — store is already installed."
  echo ""
  echo "  Store  →  $BASE_URL"
  echo "  Admin  →  $BASE_URL/admin"
  exit 0
fi

# ── 3. extract token and country ─────────────────────────────────────────────
token="$(extract_token "$install_page")"
if [[ -z "$token" ]]; then
  echo "Could not extract anti-forgery token." >&2
  exit 1
fi

country="$(extract_selected_country "$install_page")"

# ── 4. POST the install form ──────────────────────────────────────────────────
echo "Submitting installation..."
echo "  Admin : $ADMIN_EMAIL"
echo "  DB    : $DB_SERVER / $DB_NAME"
echo "  Sample: $INSTALL_SAMPLE_DATA"

curl -fsS -b "$cookie_jar" -c "$cookie_jar" \
  -X POST "$BASE_URL/install" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode "__RequestVerificationToken=$token" \
  --data-urlencode "AdminEmail=$ADMIN_EMAIL" \
  --data-urlencode "AdminPassword=$ADMIN_PASSWORD" \
  --data-urlencode "ConfirmPassword=$ADMIN_PASSWORD" \
  --data-urlencode "UseCustomCollation=false" \
  --data-urlencode "Collation=" \
  --data-urlencode "CharacterSet=" \
  --data-urlencode "CreateDatabaseIfNotExists=true" \
  --data-urlencode "InstallSampleData=$(bool_to_form "$INSTALL_SAMPLE_DATA")" \
  --data-urlencode "ConnectionStringRaw=false" \
  --data-urlencode "InstallRegionalResources=true" \
  --data-urlencode "SubscribeNewsletters=false" \
  --data-urlencode "DatabaseName=$DB_NAME" \
  --data-urlencode "ServerName=$DB_SERVER" \
  --data-urlencode "IntegratedSecurity=false" \
  --data-urlencode "Username=$DB_USER" \
  --data-urlencode "Password=$DB_PASSWORD" \
  --data-urlencode "ConnectionString=" \
  --data-urlencode "DataProvider=1" \
  --data-urlencode "Country=$country" \
  -o "$install_result"

# ── 5. check result ───────────────────────────────────────────────────────────
if grep -qi "Setup failed:" "$install_result"; then
  echo "Installation failed:" >&2
  grep -i "Setup failed:" "$install_result" | sed 's/<[^>]*>//g' | head -5 >&2
  exit 1
fi

if ! grep -q "/install/restartapplication" "$install_result"; then
  echo "Unexpected response — install may have failed." >&2
  sed 's/<[^>]*>//g' "$install_result" | grep -v '^[[:space:]]*$' | head -20 >&2
  exit 1
fi

# ── 6. trigger restart ────────────────────────────────────────────────────────
echo "Triggering app restart..."
curl -fsS "$BASE_URL/install/restartapplication" >/dev/null || true

# nopCommerce shuts down the ASP.NET process after restart — the container exits.
# Wait briefly then bring it back up.
sleep 5
docker compose up -d nopcommerce_web >/dev/null 2>&1 || true

# ── 7. wait for store to come back up ─────────────────────────────────────────
echo "Waiting for store to come back up (up to 3 min)..."
for attempt in $(seq 1 90); do
  # ensure container is running in case it exited again
  docker compose up -d nopcommerce_web >/dev/null 2>&1 || true

  if html="$(curl -fsS -L --max-time 5 "$BASE_URL" 2>/dev/null)"; then
    if ! grep -qi "nopCommerce installation" <<<"$html"; then
      echo ""
      echo "========================================"
      echo "  Installation complete"
      echo "========================================"
      echo ""
      echo "  Store             →  $BASE_URL"
      echo "  Admin             →  $BASE_URL/admin"
      echo "  Admin email       :  $ADMIN_EMAIL"
      echo "  Admin password    :  $ADMIN_PASSWORD"
      echo "  Operations View   →  $BASE_URL/Admin/Operations/List"
      echo ""
      exit 0
    fi
  fi
  sleep 2
  printf "."
done

echo ""
echo "Timed out waiting for store after restart. Check: docker compose logs nopcommerce_web" >&2
exit 1
