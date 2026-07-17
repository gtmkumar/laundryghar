#!/usr/bin/env bash
# =============================================================================
# run-tunnels.sh — expose the local dev stack (run-stack.sh) publicly via
# https://dev-*.trywavio.in, tunneled through the VPS frps server.
# Run this from YOUR terminal (not via the agent) so the process persists.
#   bash scripts/run-tunnels.sh          # start
#   bash scripts/run-tunnels.sh stop     # stop
#
# Prereq: frpc installed (`brew install frp`), ops/tunnels/frpc.local.toml
# created from frpc.toml with the real auth token, and the matching frps
# server + nginx vhost + cert already set up on the VPS — see
# ops/tunnels/README.md.
# Logs: /tmp/frpc.log
# =============================================================================
set -uo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
FRPC_BIN="${FRPC_BIN:-frpc}"
CONFIG="$REPO/ops/tunnels/frpc.local.toml"
[ -f "$CONFIG" ] || CONFIG="$REPO/ops/tunnels/frpc.toml"

if [[ "${1:-start}" == "stop" ]]; then
  echo "▶ stopping tunnels"
  pkill -f "frpc -c $CONFIG" 2>/dev/null
  echo "✓ stopped"; exit 0
fi

if ! command -v "$FRPC_BIN" >/dev/null; then
  echo "✗ frpc not found. Install: brew install frp"
  exit 1
fi

if [[ "$CONFIG" == *frpc.toml && ! -f "$REPO/ops/tunnels/frpc.local.toml" ]]; then
  echo "✗ ops/tunnels/frpc.local.toml not found — copy frpc.toml to frpc.local.toml"
  echo "  and set the real auth token first (see ops/tunnels/README.md)."
  exit 1
fi

echo "▶ starting frpc tunnel ($CONFIG)"
nohup "$FRPC_BIN" -c "$CONFIG" >/tmp/frpc.log 2>&1 &
sleep 2
grep -q "login to server success" /tmp/frpc.log 2>/dev/null && echo "✓ connected to frps" || echo "… not confirmed yet, check /tmp/frpc.log"

cat <<EOF

✅ tunnels up (once DNS + certs are live on the VPS):
   CORE        https://dev-core.trywavio.in
   OPERATIONS  https://dev-ops.trywavio.in
   COMMERCE    https://dev-commerce.trywavio.in
   admin-web   https://dev-admin.trywavio.in
   metro       https://dev-metro.trywavio.in
   stop with:  bash scripts/run-tunnels.sh stop
EOF
