#!/usr/bin/env bash
# ============================================================================
# run_partman_maintenance.sh
# ----------------------------------------------------------------------------
# Runs partman.run_maintenance_proc() against laundry_ghar_db.
#
# Intended to be invoked daily by a scheduler (launchd / cron / pg_cron).
# Logs to db/tools/partman_maintenance.log (timestamps + outcomes).
#
# Exit code: 0 on success, 1 on psql failure.
#
# Environment overrides (all optional):
#   DB_NAME (default: laundry_ghar_db)
#   DB_HOST (default: localhost)
#   DB_PORT (default: 5432)
#   DB_USER (default: postgres)
#   DB_PASS (default: postgres)
#   LOG_DIR (default: alongside this script)
# ============================================================================
set -euo pipefail

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"

HERE="$(cd "$(dirname "$0")" && pwd)"
LOG_DIR="${LOG_DIR:-$HERE}"
LOG_FILE="$LOG_DIR/partman_maintenance.log"

export PGPASSWORD="$DB_PASS"

ts() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }

{
    echo "[$(ts)] ▶ partman maintenance start (db=$DB_NAME host=$DB_HOST)"
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" \
         -v ON_ERROR_STOP=1 --quiet -t -A <<'SQL'
CALL partman.run_maintenance_proc();
WITH parts AS (
    SELECT inhparent::regclass::text AS parent,
           substring(c.relname FROM '_p(\d{8})$') AS date_str
    FROM pg_inherits i JOIN pg_class c ON c.oid=i.inhrelid
    WHERE inhparent::regclass::text IN (
        'order_lifecycle.orders','identity_access.audit_logs',
        'order_lifecycle.process_logs','engagement_cms.notifications_log',
        'logistics.rider_location_pings')
)
SELECT parent || ' latest=' || to_date(max(date_str),'YYYYMMDD')
              || ' runway_days=' || (to_date(max(date_str),'YYYYMMDD') - current_date)
FROM parts WHERE date_str IS NOT NULL
GROUP BY parent ORDER BY parent;
SQL
    echo "[$(ts)] ✓ partman maintenance done"
} >> "$LOG_FILE" 2>&1
