#!/usr/bin/env bash
# Build, install, and launch the rider Android app on a connected physical
# device (never the emulator) — auto-detects the device serial via adb so it
# works for any developer's phone without hardcoding a serial number.
# Run this from YOUR terminal (not via the agent) so it stays foregrounded
# and you can watch the Metro/Gradle logs live.
set -euo pipefail

cd "$(dirname "$0")/.."

export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
export ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-$ANDROID_HOME}"
export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"

if [ -z "${JAVA_HOME:-}" ] && [ -d "/Applications/Android Studio.app/Contents/jbr/Contents/Home" ]; then
  export JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
  export PATH="$JAVA_HOME/bin:$PATH"
fi

adb start-server >/dev/null 2>&1 || true

if [ -z "${ANDROID_SERIAL:-}" ]; then
  devices="$(adb devices | awk 'NR>1 && $2=="device" && $1 !~ /^emulator-/ {print $1}')"
  count="$(echo "$devices" | grep -c . || true)"

  if [ "$count" -eq 0 ]; then
    echo "No physical Android device found via adb." >&2
    echo "Plug in a device with USB debugging enabled and authorize the RSA prompt, or run 'npm run android' to target the emulator instead." >&2
    exit 1
  elif [ "$count" -gt 1 ]; then
    echo "Multiple physical devices found:" >&2
    echo "$devices" >&2
    echo "Set ANDROID_SERIAL=<serial> and re-run to pick one." >&2
    exit 1
  fi

  export ANDROID_SERIAL="$devices"
fi

echo "Targeting device: $ANDROID_SERIAL"

# core.WebApi/operations.WebApi run standalone on these ports in local dev
# (see ../../scripts/run-stack.sh) — the AppHost gateway (:8080) is not
# reliable for local dev and returns 502s. adb reverse tunnels the device's
# localhost:PORT to this Mac's localhost:PORT over USB, which works
# regardless of WiFi/LAN, then we point the app straight at those hosts so it
# doesn't fall back to the gateway.
PORT=8091
BACKEND_PORTS=(5056 5015 "$PORT")
for p in "${BACKEND_PORTS[@]}"; do
  adb -s "$ANDROID_SERIAL" reverse "tcp:$p" "tcp:$p"
done

for p in 5056 5015; do
  if ! curl -s -o /dev/null --max-time 2 "http://localhost:$p"; then
    echo "Warning: nothing answering on localhost:$p — start the backend hosts first (bash ../scripts/run-stack.sh or equivalent)." >&2
  fi
done

export IDENTITY_API_URL="http://localhost:5056"
export ENGAGEMENT_API_URL="http://localhost:5056"
export LOGISTICS_API_URL="http://localhost:5015"
export DEFAULT_BRAND_CODE="${DEFAULT_BRAND_CODE:-LG-MAIN}"

exec npx expo run:android --device "$ANDROID_SERIAL" --port "$PORT"
