#!/usr/bin/env bash
# =============================================================================
# run-stack.sh — launch the full local test stack (multi-vertical build).
# Run this from YOUR terminal (not via the agent) so the processes persist.
#   bash scripts/run-stack.sh            # start everything
#   bash scripts/run-stack.sh stop       # stop everything (free the ports)
#
# Prereq (already done): laundry_ghar_db migrated + seeded; app_user pw = app_user.
# Logs: /tmp/{core,ops,commerce}_host.log, /tmp/admin_web.log, /tmp/customer_metro.log
# =============================================================================
set -uo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
BE="$REPO/backend/laundryghar"
ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
ADB="$ANDROID_HOME/platform-tools/adb"
EMU="$ANDROID_HOME/emulator/emulator"
AVD="${AVD:-snap_pixel}"

ports=(5056 5015 5242 5174 8081 5000)
free_ports() { for p in "${ports[@]}"; do for pid in $(lsof -nP -iTCP:$p -sTCP:LISTEN -t 2>/dev/null); do kill -9 "$pid" 2>/dev/null; done; done; }

if [[ "${1:-start}" == "stop" ]]; then
  echo "▶ stopping stack (freeing ports ${ports[*]})"; free_ports
  pkill -f "qemu-system" 2>/dev/null  # emulator
  echo "✓ stopped"; exit 0
fi

echo "▶ clearing stale listeners"; free_ports; sleep 1

# Only core.WebApi ships ConnectionStrings in appsettings.Development.json; operations/commerce
# normally get them injected by the Aspire AppHost. Running standalone we inject them here.
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=laundry_ghar_db;Username=app_user;Password=app_user"
export ConnectionStrings__Admin="Host=localhost;Port=5432;Database=laundry_ghar_db;Username=postgres;Password=postgres"

# Razorpay keys for tier-invoice collection — loaded from the gitignored Keys/ CSV if present
# (never committed). CSV: header "key_id,key_secret" then one data row.
RZP_CSV="$REPO/Keys/rzp-test-key.csv"
if [ -f "$RZP_CSV" ]; then
  RZP_LINE="$(tail -n +2 "$RZP_CSV" | head -1 | tr -d '\r')"
  export Razorpay__KeyId="${RZP_LINE%%,*}"
  export Razorpay__KeySecret="${RZP_LINE##*,}"
  echo "▶ Razorpay keys loaded from Keys/rzp-test-key.csv (${Razorpay__KeyId})"
fi

echo "▶ backend hosts (CORE 5056 / OPERATIONS 5015 / COMMERCE 5242)"
( cd "$BE" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5056 \
    nohup dotnet run --project core.WebApi/core.WebApi.csproj --no-launch-profile >/tmp/core_host.log 2>&1 & )
( cd "$BE" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5015 \
    nohup dotnet run --project operations.WebApi/operations.WebApi.csproj --no-launch-profile >/tmp/ops_host.log 2>&1 & )
( cd "$BE" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5242 \
    nohup dotnet run --project commerce.WebApi/commerce.WebApi.csproj --no-launch-profile >/tmp/commerce_host.log 2>&1 & )

echo "▶ admin-web (Vite :5174 — uses .env.local proxy)"
( cd "$REPO/admin-web" && nohup npm run dev >/tmp/admin_web.log 2>&1 & )

echo "▶ Android emulator ($AVD)"
if ! "$ADB" devices 2>/dev/null | grep -q "emulator.*device"; then
  ( nohup "$EMU" -avd "$AVD" -no-snapshot-load -gpu swiftshader_indirect >/tmp/emulator.log 2>&1 & )
fi

echo "▶ waiting for hosts to boot..."
for i in $(seq 1 40); do
  n=$(grep -l "Now listening on" /tmp/core_host.log /tmp/ops_host.log /tmp/commerce_host.log 2>/dev/null | wc -l | tr -d ' ')
  [ "$n" = "3" ] && break; sleep 5
done
echo "  hosts listening: $(grep -h 'Now listening on' /tmp/{core,ops,commerce}_host.log 2>/dev/null | wc -l | tr -d ' ')/3"

echo "▶ waiting for emulator boot..."
"$ADB" wait-for-device 2>/dev/null
for i in $(seq 1 40); do [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ] && break; sleep 5; done

echo "▶ Metro (customer-mobile → 10.0.2.2 hosts)"
( cd "$REPO/customer-mobile" && \
  IDENTITY_API_URL=http://10.0.2.2:5056 ENGAGEMENT_API_URL=http://10.0.2.2:5056 \
  CATALOG_API_URL=http://10.0.2.2:5015 ORDERS_API_URL=http://10.0.2.2:5015 COMMERCE_API_URL=http://10.0.2.2:5242 \
  DEFAULT_BRAND_CODE=LG-MAIN \
  PATH="$ANDROID_HOME/platform-tools:$PATH" \
  nohup npx expo start >/tmp/customer_metro.log 2>&1 & )
for i in $(seq 1 20); do lsof -nP -iTCP:8081 -sTCP:LISTEN -t >/dev/null 2>&1 && break; sleep 3; done
sleep 3
"$ADB" shell am start -a android.intent.action.VIEW -d "exp://10.0.2.2:8081" host.exp.exponent >/dev/null 2>&1

cat <<EOF

✅ stack up:
   CORE        http://localhost:5056
   OPERATIONS  http://localhost:5015   (GET /api/v1/fulfillment-config)
   COMMERCE    http://localhost:5242
   admin-web   http://localhost:5174   (admin@laundryghar.local / Admin@123)
   customer    Android emulator ($AVD) — Metro :8081
   stop with:  bash scripts/run-stack.sh stop
EOF
