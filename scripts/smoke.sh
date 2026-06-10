#!/usr/bin/env bash
# ============================================================================
# LaundryGhar live-stack smoke test — READ-ONLY
#
# Usage:
#   bash scripts/smoke.sh
#   BASE_HOST=192.168.1.x bash scripts/smoke.sh   # override host
#
# Requires: bash >= 3.2, curl, pgrep (all standard on macOS/Linux)
# ============================================================================

set -uo pipefail

HOST="${BASE_HOST:-localhost}"
BRAND_ID="5b375161-9b8b-4177-ab58-54848606aa2f"
ADMIN_EMAIL="admin@laundryghar.local"
ADMIN_PASSWORD="Admin@123"

PASS=0
FAIL=0

# Parallel arrays for result table (bash 3.2 compatible — no declare -A)
RESULT_LABELS=()
RESULT_STATUS=()

pass() {
  printf "  [PASS] %s\n" "$1"
  RESULT_LABELS+=("$1")
  RESULT_STATUS+=("PASS")
  PASS=$((PASS + 1))
}

fail() {
  printf "  [FAIL] %s\n" "$1"
  RESULT_LABELS+=("$1")
  RESULT_STATUS+=("FAIL")
  FAIL=$((FAIL + 1))
}

health_check() {
  local label="$1"
  local url="$2"
  local status
  status=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "${url}" 2>/dev/null || echo "000")
  if [ "${status}" = "200" ]; then
    pass "${label} [HTTP ${status}]"
  else
    fail "${label} [HTTP ${status}]"
  fi
}

probe_get() {
  local label="$1"
  local url="$2"
  local status
  status=$(curl -s -o /dev/null -w "%{http_code}" \
    -H "Authorization: Bearer ${TOKEN}" \
    -H "X-Brand-Id: ${BRAND_ID}" \
    --max-time 10 \
    "${url}" 2>/dev/null || echo "000")
  if [ "${status}" = "200" ]; then
    pass "${label} [HTTP ${status}]"
  else
    fail "${label} [HTTP ${status}]"
  fi
}

# ── 1. Health checks ─────────────────────────────────────────────────────────

echo ""
echo "=== Health checks ==="

health_check "Health:Identity:5050"   "http://${HOST}:5050/health/live"
health_check "Health:Catalog:5001"    "http://${HOST}:5001/health/live"
health_check "Health:Orders:5002"     "http://${HOST}:5002/health/live"
health_check "Health:Warehouse:5003"  "http://${HOST}:5003/health/live"
health_check "Health:Logistics:5004"  "http://${HOST}:5004/health/live"
health_check "Health:Commerce:5005"   "http://${HOST}:5005/health/live"
health_check "Health:Finance:5006"    "http://${HOST}:5006/health/live"
health_check "Health:Engagement:5007" "http://${HOST}:5007/health/live"
health_check "Health:Analytics:5008"  "http://${HOST}:5008/health/live"

# ── 2. Worker process check ───────────────────────────────────────────────────

echo ""
echo "=== Worker process ==="

if pgrep -f "laundryghar.Worker" >/dev/null 2>&1; then
  pass "Worker:pgrep"
else
  fail "Worker:pgrep (process not found)"
fi

# ── 3. Admin login ────────────────────────────────────────────────────────────

echo ""
echo "=== Admin login ==="

LOGIN_RESPONSE=$(curl -s -X POST \
  "http://${HOST}:5050/api/v1/auth/password/login" \
  -H "Content-Type: application/json" \
  --max-time 10 \
  -d "{\"identifier\":\"${ADMIN_EMAIL}\",\"password\":\"${ADMIN_PASSWORD}\"}" 2>/dev/null || echo "")

TOKEN=$(echo "${LOGIN_RESPONSE}" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')

if [ -n "${TOKEN}" ]; then
  pass "AdminLogin"
else
  fail "AdminLogin (no accessToken in response)"
  echo "  Login response: ${LOGIN_RESPONSE}"
  TOKEN="invalid"
fi

# ── 4. Per-service read probes ────────────────────────────────────────────────

echo ""
echo "=== Per-service reads ==="

probe_get "Identity:UsersList"         "http://${HOST}:5050/api/v1/admin/users?page=1&pageSize=1"
probe_get "Catalog:CustomersList"      "http://${HOST}:5001/api/v1/admin/customers?page=1&pageSize=1"
probe_get "Orders:OrdersList"          "http://${HOST}:5002/api/v1/admin/orders?page=1&pageSize=1"
probe_get "Orders:OpsQueues"           "http://${HOST}:5002/api/v1/admin/orders/ops-queues"
probe_get "Warehouse:GarmentsBoard"    "http://${HOST}:5003/api/v1/admin/garments/board"
probe_get "Logistics:RidersList"       "http://${HOST}:5004/api/v1/admin/riders?page=1&pageSize=1"
probe_get "Commerce:PaymentsList"      "http://${HOST}:5005/api/v1/admin/payments?page=1&pageSize=1"
probe_get "Finance:CashBooksList"      "http://${HOST}:5006/api/v1/admin/cash-books?page=1&pageSize=1"
probe_get "Finance:RoyaltyInvoices"    "http://${HOST}:5006/api/v1/admin/royalty-invoices?page=1&pageSize=1"
probe_get "Analytics:Dashboard"        "http://${HOST}:5008/api/v1/admin/analytics/dashboard"

# Engagement public app-config — no auth header
APP_CONFIG_STATUS=$(curl -s -o /dev/null -w "%{http_code}" \
  -H "X-Brand-Id: ${BRAND_ID}" \
  --max-time 10 \
  "http://${HOST}:5007/api/v1/public/app-config?appType=customer&platform=ios" 2>/dev/null || echo "000")
if [ "${APP_CONFIG_STATUS}" = "200" ]; then
  pass "Engagement:PublicAppConfig [HTTP ${APP_CONFIG_STATUS}]"
else
  fail "Engagement:PublicAppConfig [HTTP ${APP_CONFIG_STATUS}]"
fi

# ── 5. Summary table ──────────────────────────────────────────────────────────

echo ""
echo "======================================"
echo "  LaundryGhar Smoke Test Results"
echo "======================================"
printf "%-52s %s\n" "Check" "Result"
printf "%-52s %s\n" "----------------------------------------------------" "------"

i=0
while [ $i -lt ${#RESULT_LABELS[@]} ]; do
  printf "%-52s %s\n" "${RESULT_LABELS[$i]}" "${RESULT_STATUS[$i]}"
  i=$((i + 1))
done

echo ""
echo "  Total: PASS=${PASS}  FAIL=${FAIL}"
echo ""

if [ "${FAIL}" -gt 0 ]; then
  echo "SMOKE: FAILED (${FAIL} probe(s) failed)"
  exit 1
else
  echo "SMOKE: PASSED"
  exit 0
fi
