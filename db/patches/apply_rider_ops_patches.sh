#!/usr/bin/env bash
# ============================================================================
# apply_rider_ops_patches.sh — Apply the Rider Ops (Phases 1-4) DB patches to a
# target laundry_ghar_db. Run against ANY environment by supplying its DB_* vars.
#
# Each patch is idempotent (IF NOT EXISTS / WHERE NOT EXISTS / ON CONFLICT) and
# manages its own transaction, so this is safe to re-run.
#
# Usage:
#   ./apply_rider_ops_patches.sh                         # localhost defaults
#   DB_HOST=db.internal DB_USER=postgres DB_PASS=… \
#     DB_NAME=laundry_ghar_db ./apply_rider_ops_patches.sh
#   ./apply_rider_ops_patches.sh --with-demo             # ALSO seed dev demo data
#
# Notes:
#   * Needs a privileged (superuser/owner) role — these are DDL + GRANTs + a new
#     RLS policy. Use the admin role, NOT the RLS-scoped app_user.
#   * The demo seed (seed_rider_ops_demo.sql) is DEV-ONLY and is skipped unless
#     --with-demo is passed. Never seed demo data into production.
# ============================================================================
set -euo pipefail

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"
WITH_DEMO=0
[[ "${1:-}" == "--with-demo" ]] && WITH_DEMO=1

export PGPASSWORD="$DB_PASS"
HERE="$(cd "$(dirname "$0")" && pwd)"
PSQL=(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 --quiet)

# Schema/permission patches — order is independent (no cross-deps) but kept by phase.
PATCHES=(
  rider_verify_permission.sql   # Phase 1 — rider.verify permission + grants
  rider_drop_at_store.sql       # Phase 2 — collected_at / dropped_at
  rider_cod_settlement.sql      # Phase 3 — COD cols + rider_settlements + rider.settle
  rider_payout.sql              # Phase 4 — payout_amount
)

echo "▶ applying Rider Ops patches to ${DB_USER}@${DB_HOST}:${DB_PORT}/${DB_NAME}"
for p in "${PATCHES[@]}"; do
  if [[ ! -s "$HERE/$p" ]]; then
    echo "  ✗ $p not found — aborting"; exit 1
  fi
  echo "  · applying $p"
  "${PSQL[@]}" -f "$HERE/$p" >/dev/null
done

if [[ "$WITH_DEMO" == "1" ]]; then
  echo "  · applying seed_rider_ops_demo.sql (DEV demo data)"
  "${PSQL[@]}" -f "$HERE/seed_rider_ops_demo.sql" >/dev/null
else
  echo "  · skipping seed_rider_ops_demo.sql (dev-only; pass --with-demo to include)"
fi

echo ""
echo "▶ post-apply verification"
"${PSQL[@]}" -P pager=off <<'SQL'
SELECT 'delivery_assignments cols' AS check,
       string_agg(column_name, ', ' ORDER BY column_name) AS detail
FROM information_schema.columns
WHERE table_schema='order_lifecycle' AND table_name='delivery_assignments'
  AND column_name IN ('collected_at','dropped_at','cod_amount','cod_collected_at','settlement_id','payout_amount')
UNION ALL
SELECT 'rider_settlements table', coalesce(to_regclass('logistics.rider_settlements')::text, 'MISSING')
UNION ALL
SELECT 'permissions', string_agg(code, ', ' ORDER BY code)
FROM identity_access.permissions WHERE code IN ('rider.verify','rider.settle');
SQL

echo ""
echo "✓ Rider Ops patches applied to ${DB_NAME}"
