#!/usr/bin/env bash
# place-order.sh — places real orders through the nopCommerce checkout
#
# Usage:
#   ./place-order.sh            → place 1 order
#   ./place-order.sh 3          → place 3 orders in sequence
#   ./place-order.sh 3 1        → place 3 orders for product ID 1
#
# Prerequisites: nopCommerce must be running and installed (make up)
#
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
BASE_URL="${BASE_URL%/}"
COUNT="${1:-1}"
PRODUCT_ID="${2:-2}"    # product 2 = Digital Storm VANQUISH (no required attributes)
QUANTITY="${3:-1}"
ORDER_PAUSE_SECONDS="${ORDER_PAUSE_SECONDS:-65}"

EMAIL="${DEMO_EMAIL:-admin@verdemart.com}"
PASSWORD="${DEMO_PASSWORD:-Admin1234!}"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
ok()   { echo -e "${GREEN}[OK]${NC}    $*"; }
info() { echo -e "${YELLOW}[...]${NC}   $*"; }
fail() { echo -e "${RED}[FAIL]${NC}  $*" >&2; }

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
COOKIES="$WORK_DIR/cookies.txt"

# ── helpers ───────────────────────────────────────────────────────────────────

get_token() {
    local file="$1"
    grep -oP 'name="__RequestVerificationToken"[^>]*value="\K[^"]+' "$file" \
        | head -1 || \
    grep -oP '"__RequestVerificationToken",\s*"\K[^"]+' "$file" \
        | head -1 || true
}

curl_get() {
    curl -sS --max-time 15 -L \
        -b "$COOKIES" -c "$COOKIES" \
        "$@"
}

curl_post() {
    curl -sS --max-time 15 -L \
        -b "$COOKIES" -c "$COOKIES" \
        -X POST "$@"
}

save_checkout_attributes() {
    local cart_page="$WORK_DIR/cart-after-add.html"
    curl_get "$BASE_URL/cart" -o "$cart_page" > /dev/null

    local token
    token="$(get_token "$cart_page")"
    if [[ -z "$token" ]]; then
        fail "Could not extract CSRF token from cart page"
        exit 1
    fi

    local attribute_name
    attribute_name="$(grep -oP 'name="checkout_attribute_\d+"' "$cart_page" | head -1 | cut -d'"' -f2 || true)"
    if [[ -z "$attribute_name" ]]; then
        return 0
    fi

    local attribute_value
    attribute_value="$(grep -A10 "name=\"$attribute_name\"" "$cart_page" \
        | grep -oP '<option selected="selected" value="\K[^"]+' \
        | head -1 || true)"
    if [[ -z "$attribute_value" ]]; then
        attribute_value="$(grep -A10 "name=\"$attribute_name\"" "$cart_page" \
            | grep -oP '<option value="\K[^"]+' \
            | head -1 || true)"
    fi

    if [[ -z "$attribute_value" ]]; then
        fail "Could not determine checkout attribute value for $attribute_name"
        exit 1
    fi

    info "  Saving checkout attributes ..."
    curl_post "$BASE_URL/shoppingcart/checkoutattributechange/%7BisEditable%7D?isEditable=True" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "$attribute_name=$attribute_value" \
        -o "$WORK_DIR/checkout-attributes.json" > /dev/null
}

# ── step 1: login ─────────────────────────────────────────────────────────────

do_login() {
    info "Logging in as $EMAIL ..."
    local login_page="$WORK_DIR/login.html"

    curl_get "$BASE_URL/login" -o "$login_page"
    local token
    token="$(get_token "$login_page")"

    if [[ -z "$token" ]]; then
        fail "Could not extract CSRF token from login page. Is nopCommerce running?"
        exit 1
    fi

    local result
    result="$(curl_post "$BASE_URL/login" \
        -d "__RequestVerificationToken=$token" \
        -d "Email=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$EMAIL'))")" \
        -d "Password=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$PASSWORD'))")" \
        -d "RememberMe=false" \
        -w "%{http_code}" -o "$WORK_DIR/login-result.html")"

    if echo "$result" | grep -q "^3\|^2"; then
        # check we're actually logged in — logged-in page won't have a login form
        if grep -q "logout\|Log out\|account/logout" "$WORK_DIR/login-result.html" 2>/dev/null || \
           ! grep -qi "login\|sign in" "$WORK_DIR/login-result.html" 2>/dev/null; then
            ok "Logged in"
            return 0
        fi
    fi

    # Try checking if already logged in
    curl_get "$BASE_URL/customer/info" -o "$WORK_DIR/customer-info.html"
    if grep -qi "logout\|Log out" "$WORK_DIR/customer-info.html" 2>/dev/null; then
        ok "Already logged in"
        return 0
    fi

    fail "Login failed — check credentials or that nopCommerce is fully installed"
    exit 1
}

# ── step 2: add product to cart ───────────────────────────────────────────────

add_to_cart() {
    local product_id="$1"
    local quantity="$2"
    info "Adding product $product_id (qty=$quantity) to cart ..."

    # Clear cart first to avoid leftover items from previous runs
    curl_get "$BASE_URL/cart" -o "$WORK_DIR/cart.html" > /dev/null
    local token
    token="$(get_token "$WORK_DIR/cart.html")"

    if [[ -z "$token" ]]; then
        fail "Could not extract CSRF token from cart page"
        exit 1
    fi

    local result
    result="$(curl_post "$BASE_URL/addproducttocart/catalog/$product_id/1/$quantity" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -w "%{http_code}" -o "$WORK_DIR/add-cart-result.json")"

    if grep -q '"success":true' "$WORK_DIR/add-cart-result.json" 2>/dev/null; then
        ok "Product added to cart"
        save_checkout_attributes
        return 0
    fi

    # Some products need the details endpoint
    curl_get "$BASE_URL/$(python3 -c "print('$product_id')" 2>/dev/null || echo "$product_id")" \
        -o "$WORK_DIR/product-page.html" 2>/dev/null || true

    result="$(curl_post "$BASE_URL/addproducttocart/details/$product_id/1" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "addtocart_$product_id.EnteredQuantity=$quantity" \
        -w "%{http_code}" -o "$WORK_DIR/add-cart-result2.json")"

    if grep -q '"success":true' "$WORK_DIR/add-cart-result2.json" 2>/dev/null; then
        ok "Product added to cart (details endpoint)"
        save_checkout_attributes
        return 0
    fi

    fail "Failed to add product $product_id to cart"
    cat "$WORK_DIR/add-cart-result.json" >&2
    exit 1
}

# ── step 3: one-page checkout ─────────────────────────────────────────────────

do_checkout() {
    info "Starting one-page checkout ..."
    local opc_page="$WORK_DIR/opc.html"
    curl_get "$BASE_URL/onepagecheckout/" -o "$opc_page"

    local token
    token="$(get_token "$opc_page")"
    if [[ -z "$token" ]]; then
        # try fetching without lang prefix
        curl_get "$BASE_URL/en/onepagecheckout/" -o "$opc_page"
        token="$(get_token "$opc_page")"
    fi

    # ── billing address ───────────────────────────────────────────────────────
    info "  Saving billing address ..."
    local billing_result="$WORK_DIR/billing.json"
    curl_post "$BASE_URL/checkout/OpcSaveBilling/" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "BillingNewAddress.FirstName=Demo" \
        -d "BillingNewAddress.LastName=Customer" \
        -d "BillingNewAddress.Email=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$EMAIL'))")" \
        -d "BillingNewAddress.Company=" \
        -d "BillingNewAddress.CountryId=1" \
        -d "BillingNewAddress.StateProvinceId=0" \
        -d "BillingNewAddress.City=Aveiro" \
        -d "BillingNewAddress.Address1=Rua de Demo+123" \
        -d "BillingNewAddress.ZipPostalCode=3800-001" \
        -d "BillingNewAddress.PhoneNumber=912345678" \
        -d "shipToSameAddress=true" \
        -o "$billing_result"

    # extract updated token from response if present
    local new_token
    new_token="$(grep -oP '"__RequestVerificationToken","Value":"\K[^"]+' "$billing_result" 2>/dev/null || true)"
    [[ -n "$new_token" ]] && token="$new_token"

    # ── shipping method ───────────────────────────────────────────────────────
    info "  Saving shipping method ..."
    local shipping_result="$WORK_DIR/shipping.json"
    curl_post "$BASE_URL/checkout/OpcSaveShipping/" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "BillingNewAddress.FirstName=Demo" \
        -d "BillingNewAddress.LastName=Customer" \
        -d "BillingNewAddress.Email=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$EMAIL'))")" \
        -d "BillingNewAddress.CountryId=1" \
        -d "BillingNewAddress.City=Aveiro" \
        -d "BillingNewAddress.Address1=Rua+de+Demo+123" \
        -d "BillingNewAddress.ZipPostalCode=3800-001" \
        -d "BillingNewAddress.PhoneNumber=912345678" \
        -o "$shipping_result" 2>/dev/null || true

    # ── shipping method selection ─────────────────────────────────────────────
    info "  Selecting shipping method ..."
    local shipping_option
    shipping_option="$(grep -oP '"Name":"\K[^"]+' "$shipping_result" 2>/dev/null | head -1 || echo "Ground___Shipping.FixedByWeightByTotal")"

    curl_post "$BASE_URL/checkout/OpcSaveShippingMethod/" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "shippingoption=$shipping_option" \
        -o "$WORK_DIR/shippingmethod.json" 2>/dev/null || true

    # ── payment method: Check / Money Order ───────────────────────────────────
    info "  Selecting payment method (CheckMoneyOrder) ..."
    curl_post "$BASE_URL/checkout/OpcSavePaymentMethod/" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "paymentmethod=Payments.CheckMoneyOrder" \
        -d "UseRewardPoints=false" \
        -o "$WORK_DIR/payment.json" 2>/dev/null || true

    # Check/Money Order has no fields, but nopCommerce still expects the
    # payment-info step to store a ProcessPaymentRequest before confirmation.
    info "  Saving payment information ..."
    curl_post "$BASE_URL/checkout/OpcSavePaymentInfo/" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -o "$WORK_DIR/payment-info.json" 2>/dev/null || true

    # ── confirm order ─────────────────────────────────────────────────────────
    info "  Confirming order ..."
    local confirm_result="$WORK_DIR/confirm.json"
    curl_post "$BASE_URL/checkout/OpcConfirmOrder/" \
        -H "X-Requested-With: XMLHttpRequest" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "__RequestVerificationToken=$token" \
        -d "captchaValid=false" \
        -o "$confirm_result"

    # check for success — either redirect to order details or success JSON
    if grep -q '"redirect"' "$confirm_result" 2>/dev/null || \
       grep -q '"success":1' "$confirm_result" 2>/dev/null || \
       grep -q 'orderdetails\|order/details\|completed' "$confirm_result" 2>/dev/null; then
        local order_url
        order_url="$(grep -oP '"redirect":"\K[^"]+' "$confirm_result" 2>/dev/null | head -1 || true)"
        local order_id
        order_id="$(echo "$order_url" | grep -oP '\d+$' || true)"
        ok "Order placed! ${order_id:+ID=$order_id  }→ ${BASE_URL}/Admin/Operations/List"
        return 0
    fi

    fail "Order confirmation failed"
    cat "$confirm_result" >&2
    if grep -q 'Please wait several seconds\|MinOrderPlacementInterval' "$confirm_result" 2>/dev/null; then
        return 2
    fi
    return 1
}

place_one_order() {
    local product_id="$1"
    local quantity="$2"

    add_to_cart "$product_id" "$quantity"

    local checkout_status
    set +e
    do_checkout
    checkout_status=$?
    set -e

    if [[ "$checkout_status" -eq 2 ]]; then
        info "Waiting ${ORDER_PAUSE_SECONDS}s for nopCommerce order throttle ..."
        sleep "$ORDER_PAUSE_SECONDS"
        do_checkout
        return $?
    fi

    return "$checkout_status"
}

# ── main ──────────────────────────────────────────────────────────────────────

echo ""
echo "════════════════════════════════════════"
echo "  VerdeMart — Place Demo Orders"
echo "════════════════════════════════════════"
echo "  Target:   $BASE_URL"
echo "  Orders:   $COUNT"
echo "  Product:  $PRODUCT_ID  (qty $QUANTITY each)"
echo ""

do_login

SUCCESS=0
FAIL=0

for i in $(seq 1 "$COUNT"); do
    if [[ "$i" -gt 1 ]]; then
        info "Waiting ${ORDER_PAUSE_SECONDS}s for nopCommerce order throttle ..."
        sleep "$ORDER_PAUSE_SECONDS"
    fi

    echo ""
    echo "── Order $i / $COUNT ──────────────────────────"
    if place_one_order "$PRODUCT_ID" "$QUANTITY"; then
        SUCCESS=$((SUCCESS + 1))
    else
        FAIL=$((FAIL + 1))
    fi
done

echo ""
echo "════════════════════════════════════════"
if [[ $SUCCESS -gt 0 ]]; then
    echo -e "${GREEN}  $SUCCESS order(s) placed successfully${NC}"
fi
if [[ $FAIL -gt 0 ]]; then
    echo -e "${RED}  $FAIL order(s) failed${NC}"
fi
echo ""
echo "  Operations View → $BASE_URL/Admin/Operations/List"
echo "  Worker logs     → make logs"
echo ""

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
