#!/usr/bin/env bash
# ============================================================================
# apply_schemas.sh — Build laundry_ghar_db with one PostgreSQL schema per
#                    bounded-context SQL file in this directory.
# ----------------------------------------------------------------------------
# Usage:
#   ./apply_schemas.sh                 # defaults below
#   DB_NAME=foo DB_USER=bar ./apply_schemas.sh
# ----------------------------------------------------------------------------
# Connection defaults (override via env):
#   DB_NAME=laundry_ghar_db
#   DB_HOST=localhost
#   DB_PORT=5432
#   DB_USER=postgres
#   DB_PASS=postgres
# ----------------------------------------------------------------------------
# What it does:
#   1. Creates the database if it does not exist.
#   2. Installs required extensions in `public`.
#   3. Creates 11 schemas (one per source SQL file).
#   4. Applies each source SQL file with search_path set so its CREATE TABLE
#      statements land in the dedicated schema. Cross-schema FK references
#      resolve via the search_path that includes every schema.
#   5. Applies the schema-qualified, error-tolerant cross-cutting file.
#   6. Prints a per-schema table count for verification.
# ----------------------------------------------------------------------------
# Apply order is FK-dependency-correct (NOT the README's lexical order):
#   01 → 02 → 00 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 99
# The README order would fail because 00_kernel.sql FKs `brands` (lives in 01).
# ============================================================================
set -euo pipefail

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"

export PGPASSWORD="$DB_PASS"

HERE="$(cd "$(dirname "$0")" && pwd)"

PSQL_CONN=(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER")
PSQL_DB=("${PSQL_CONN[@]}" -d "$DB_NAME" -v ON_ERROR_STOP=1 --quiet)

# ---- Search path includes every bounded-context schema -----------------------
SEARCH_PATH="tenancy_org, identity_access, kernel, customer_catalog, order_lifecycle, logistics, commerce, finance_royalty, engagement_cms, analytics, partman, public"

# ---- File → schema pairs in FK-dependency order -----------------------------
PAIRS=(
  "01_bc1_tenancy_org.sql:tenancy_org"
  "02_bc2_identity_access.sql:identity_access"
  "00_kernel.sql:kernel"
  "03_bc3_customer_catalog.sql:customer_catalog"
  "04_bc4_order_lifecycle.sql:order_lifecycle"
  "05_bc5_logistics.sql:logistics"
  "06_bc6_commerce.sql:commerce"
  "07_bc7_finance_royalty.sql:finance_royalty"
  "08_bc8_engagement_cms.sql:engagement_cms"
  "09_bc9_analytics.sql:analytics"
)

# ---- Step 1: ensure database exists -----------------------------------------
echo "▶ checking database '$DB_NAME'"
if ! "${PSQL_CONN[@]}" -d postgres -tAc \
       "SELECT 1 FROM pg_database WHERE datname='$DB_NAME';" | grep -q 1; then
  echo "  creating database $DB_NAME"
  createdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME"
else
  echo "  database $DB_NAME already exists"
fi

# ---- Step 2: bootstrap extensions + schemas ---------------------------------
echo "▶ bootstrap: extensions + schemas"
"${PSQL_DB[@]}" -f "$HERE/_00_bootstrap.sql"

# ---- Step 3: apply each source file into its schema -------------------------
for pair in "${PAIRS[@]}"; do
  file="${pair%%:*}"
  schema="${pair##*:}"
  echo "▶ applying $file  →  schema $schema"
  "${PSQL_DB[@]}" \
    --single-transaction \
    -c "SET search_path TO $schema, $SEARCH_PATH;" \
    -f "$HERE/$file"
done

# ---- Step 4: cross-cutting (partman.create_parent calls; schema-qualified) --
echo "▶ applying 99_cross_cutting_schema_qualified.sql"
"${PSQL_DB[@]}" \
  --single-transaction \
  -c "SET search_path TO $SEARCH_PATH;" \
  -f "$HERE/99_cross_cutting_schema_qualified.sql"

# ---- Step 5: verification ---------------------------------------------------
echo ""
echo "▶ per-schema object count"
"${PSQL_CONN[@]}" -d "$DB_NAME" -P pager=off <<'SQL'
SELECT
    n.nspname                                                 AS schema,
    count(*) FILTER (WHERE c.relkind = 'r')                   AS tables,
    count(*) FILTER (WHERE c.relkind = 'm')                   AS mat_views,
    count(*) FILTER (WHERE c.relkind = 'v')                   AS views,
    count(*) FILTER (WHERE c.relkind = 'i')                   AS indexes
FROM pg_namespace n
LEFT JOIN pg_class c ON c.relnamespace = n.oid
WHERE n.nspname IN (
    'kernel','tenancy_org','identity_access','customer_catalog',
    'order_lifecycle','logistics','commerce','finance_royalty',
    'engagement_cms','analytics'
)
GROUP BY n.nspname
ORDER BY n.nspname;
SQL

echo ""
echo "✓ done — laundry_ghar_db built across 11 schemas"
