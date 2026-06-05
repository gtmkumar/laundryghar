#!/usr/bin/env bash
# ============================================================================
# apply_patches.sh — Apply the FK-completion patch set to laundry_ghar_db
# ----------------------------------------------------------------------------
# Each patch:
#   * targets exactly one BC schema
#   * uses schema-qualified table names (does NOT rely on search_path)
#   * wraps every ALTER TABLE in a DO/EXCEPTION block (idempotent)
#   * adds an unconditional companion index per FK (FK enforcement uses it)
# ----------------------------------------------------------------------------
# Apply order is BC-dependency-correct (same order used by apply_schemas.sh):
#   01 → 02 → 00 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 99
# This ensures every FK target schema exists with its tables when we add the
# constraint that references it.
# ----------------------------------------------------------------------------
# Usage:
#   ./apply_patches.sh                # defaults below
#   DB_NAME=foo DB_PASS=… ./apply_patches.sh
# ============================================================================
set -euo pipefail

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"

export PGPASSWORD="$DB_PASS"
HERE="$(cd "$(dirname "$0")" && pwd)"

PSQL=(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 --quiet)

# BC-dependency-correct order
PATCHES=(
  fk_patch_01_tenancy_org.sql
  fk_patch_02_identity_access.sql
  fk_patch_00_kernel.sql
  fk_patch_03_customer_catalog.sql
  fk_patch_04_order_lifecycle.sql
  fk_patch_05_logistics.sql
  fk_patch_06_commerce.sql
  fk_patch_07_finance_royalty.sql
  fk_patch_08_engagement_cms.sql
  fk_patch_09_analytics.sql
)

echo "▶ applying FK completion patches to $DB_NAME"
for p in "${PATCHES[@]}"; do
  if [[ ! -s "$HERE/$p" ]]; then
    echo "  · $p (empty, skipped)"
    continue
  fi
  # Detect actionable FK statements. Use single-quoted patterns so the
  # regex metacharacters reach grep untouched by the shell.
  if ! grep -Eq '^[[:space:]]*(DO[[:space:]]+\$\$|ALTER[[:space:]]+TABLE)' "$HERE/$p" 2>/dev/null; then
    echo "  · $p (no actionable FKs, skipped)"
    continue
  fi
  echo "  · applying $p"
  "${PSQL[@]}" --single-transaction -f "$HERE/$p"
done

echo ""
echo "▶ post-apply verification"
"${PSQL[@]}" -P pager=off <<'SQL'
SELECT
    n.nspname        AS schema,
    count(*)         AS fk_count
FROM   pg_constraint c
JOIN   pg_namespace  n ON n.oid = c.connamespace
WHERE  c.contype = 'f'
  AND  n.nspname IN (
       'kernel','tenancy_org','identity_access','customer_catalog',
       'order_lifecycle','logistics','commerce','finance_royalty',
       'engagement_cms','analytics'
       )
GROUP BY n.nspname
ORDER BY n.nspname;
SQL

echo ""
echo "✓ patches applied — review fk_patch_review_polymorphic.sql for remaining decisions"
