#!/usr/bin/env bash
# ============================================================================
# build_from_scratch.sh — One-shot rebuild of laundry_ghar_db
# ----------------------------------------------------------------------------
# Runs the entire build pipeline in dependency order. Idempotent end-to-end:
# safe to re-run against an existing database (every patch is wrapped in
# DO/EXCEPTION blocks or uses IF NOT EXISTS guards).
#
# Stages:
#   1. database_scripts/apply_schemas.sh
#      → creates DB, extensions, 11 schemas, all 92 logical tables + 5 MVs,
#        and runs pg_partman create_parent for the 5 partitioned tables.
#   2. db/patches/apply_patches.sh
#      → applies the Category B 138-FK cross-aggregate completion set.
#   3. db/patches/triggers_set_updated_at.sql
#      → kernel.set_updated_at() + BEFORE UPDATE trigger on all 61 tables
#        with an updated_at column.
#   4. db/patches/polymorphic_location_discriminators.sql
#      → 5 <col>_type discriminator columns + vocabulary + pair CHECKs
#        for the polymorphic garment-location columns.
#   5. db/patches/auth_token_lineage_and_package_purchase_fk.sql
#      → refresh_tokens.family_id self-FK + customer_packages.purchase_order
#        composite FK to orders.
#   6. db/patches/rls_proposal.sql
#      → app_user / app_admin roles, kernel.current_* helpers, CRUD grants,
#        92 inert RLS policies. DOES NOT enable RLS on any table.
#   7. partman config + maintenance
#      → infinite_time_partitions=true on all 5 managed tables,
#        rider_location_pings premake bumped to 14 days,
#        partman.run_maintenance_proc() runs once.
#
# AFTER THIS SCRIPT — manual steps the user must opt into:
#   • Activate RLS table-by-table (rls_proposal.sql §5).
#   • Install launchd plist for daily partman maintenance
#     (db/tools/com.laundryghar.partman.plist).
#   • Switch backend DB connection from postgres → app_user member.
# ----------------------------------------------------------------------------
# Env overrides:
#   DB_NAME, DB_HOST, DB_PORT, DB_USER, DB_PASS  (same defaults as sub-scripts)
# ============================================================================
set -euo pipefail

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"

export DB_NAME DB_HOST DB_PORT DB_USER DB_PASS PGPASSWORD="$DB_PASS"

REPO="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPTS="$REPO/database_scripts"
PATCHES="$REPO/db/patches"

PSQL=(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME"
      -v ON_ERROR_STOP=1 --quiet)

banner() { printf '\n\033[1m▶ %s\033[0m\n' "$*"; }

# Detect whether the schemas+tables are already built. apply_schemas.sh is
# NOT idempotent (CREATE TABLE without IF NOT EXISTS), so we skip it on
# existing databases. Set FORCE_REBUILD=1 to override (will fail on dupes).
NEEDS_BUILD=1
if "${PSQL[@]}" -tAc "SELECT 1 FROM pg_namespace WHERE nspname='order_lifecycle'" 2>/dev/null | grep -q 1; then
    if "${PSQL[@]}" -tAc "SELECT 1 FROM information_schema.tables WHERE table_schema='order_lifecycle' AND table_name='orders'" 2>/dev/null | grep -q 1; then
        NEEDS_BUILD=0
    fi
fi

if [[ "${FORCE_REBUILD:-0}" == "1" || $NEEDS_BUILD -eq 1 ]]; then
    banner "Stage 1/7 — apply_schemas.sh (fresh build)"
    "$SCRIPTS/apply_schemas.sh"
else
    banner "Stage 1/7 — apply_schemas.sh (SKIPPED: tables exist; set FORCE_REBUILD=1 to override)"
fi

banner "Stage 2/7 — Category B FK completion patches"
"$PATCHES/apply_patches.sh"

banner "Stage 3/7 — updated_at triggers"
"${PSQL[@]}" --single-transaction -f "$PATCHES/triggers_set_updated_at.sql"

banner "Stage 4/7 — polymorphic-location discriminators"
"${PSQL[@]}" --single-transaction -f "$PATCHES/polymorphic_location_discriminators.sql"

banner "Stage 5/7 — auth token lineage + customer_packages purchase FK"
"${PSQL[@]}" --single-transaction -f "$PATCHES/auth_token_lineage_and_package_purchase_fk.sql"

banner "Stage 6/7 — RLS proposal (roles + helpers + inert policies)"
"${PSQL[@]}" --single-transaction -f "$PATCHES/rls_proposal.sql"

banner "Stage 7/7 — partman config + maintenance"
"${PSQL[@]}" <<'SQL'
UPDATE partman.part_config
   SET infinite_time_partitions = true
 WHERE parent_table IN (
       'order_lifecycle.orders','identity_access.audit_logs',
       'order_lifecycle.process_logs','engagement_cms.notifications_log',
       'logistics.rider_location_pings');
UPDATE partman.part_config SET premake = 14
 WHERE parent_table = 'logistics.rider_location_pings';
UPDATE partman.part_config SET premake = 6
 WHERE parent_table = 'engagement_cms.notifications_log';
CALL partman.run_maintenance_proc();
SQL

banner "Final verification"
"${PSQL[@]}" -P pager=off <<'SQL'
SELECT 'BC parent FKs total: ' || count(*)
FROM pg_constraint con
JOIN pg_class c ON c.oid=con.conrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE con.contype='f' AND c.relispartition=false
  AND n.nspname IN ('kernel','tenancy_org','identity_access','customer_catalog',
                    'order_lifecycle','logistics','commerce','finance_royalty',
                    'engagement_cms','analytics');

SELECT 'updated_at triggers on parent tables: ' || count(*)
FROM pg_trigger t
JOIN pg_class c ON c.oid=t.tgrelid
WHERE t.tgname LIKE 'trg_%_set_updated_at'
  AND NOT t.tgisinternal AND c.relispartition=false;

SELECT 'RLS policies (app_user): ' || count(*)
FROM pg_policies
WHERE policyname IN ('rls_brand','rls_brand_or_customer','rls_user_self','rls_admin_only');

WITH parts AS (
    SELECT inhparent::regclass::text AS parent,
           to_date(substring(c.relname FROM '_p(\d{8})$'),'YYYYMMDD') AS d
    FROM pg_inherits i JOIN pg_class c ON c.oid=i.inhrelid
    WHERE inhparent::regclass::text IN (
        'order_lifecycle.orders','identity_access.audit_logs',
        'order_lifecycle.process_logs','engagement_cms.notifications_log',
        'logistics.rider_location_pings')
)
SELECT parent, max(d) AS latest_partition, max(d)-current_date AS runway_days
FROM parts WHERE d IS NOT NULL GROUP BY parent ORDER BY parent;
SQL

printf '\n\033[32m✓ build complete\033[0m\n'
printf '\nManual next steps (opt-in):\n'
printf '  • Activate RLS per table — see db/patches/rls_proposal.sql §5\n'
printf '  • Install partman scheduler:\n'
printf '      cp db/tools/com.laundryghar.partman.plist ~/Library/LaunchAgents/\n'
printf '      launchctl load -w ~/Library/LaunchAgents/com.laundryghar.partman.plist\n'
