#!/usr/bin/env bash
# ============================================================================
# restore.sh — restore a backup.sh archive into a PostgreSQL database
# ----------------------------------------------------------------------------
# SAFE BY DEFAULT: restores into a NEW database (created here). Restoring over
# an existing database requires the explicit --drop-existing flag AND typing
# the database name back when prompted (or RESTORE_FORCE=1 for automation).
#
# Usage:
#   ops/backup/restore.sh <dump-file> <target-db-name> [--drop-existing]
#
# Examples:
#   # Restore into a fresh side-by-side DB to inspect / verify:
#   ops/backup/restore.sh ~/laundryghar-backups/laundry_ghar_db_20260708_020000.dump lg_restore_check
#
#   # Disaster recovery — replace the live DB (interactive confirmation):
#   ops/backup/restore.sh <dump> laundry_ghar_db --drop-existing
#
# Env: DB_HOST (localhost)  DB_PORT (5432)  DB_USER (postgres)  DB_PASS (postgres)
#      — must be a role with CREATEDB (and ownership rights for --drop-existing).
#
# Roles (app_user / app_admin) are cluster-level, not part of the dump: on a
# brand-new cluster, first apply the matching globals_*.sql from the backup dir
#   psql -h HOST -U postgres -f globals_YYYYMMDD_HHMMSS.sql
# ============================================================================
set -euo pipefail

DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"
export PGPASSWORD="$DB_PASS"

DUMP="${1:-}"
TARGET_DB="${2:-}"
DROP_EXISTING="${3:-}"

[[ -n "$DUMP" && -n "$TARGET_DB" ]] || { sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 1; }
[[ -f "$DUMP" ]] || { echo "ERROR: dump file not found: $DUMP" >&2; exit 1; }

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

PSQL=(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres -qtA -v ON_ERROR_STOP=1)

# Sanity: the archive must list cleanly before we touch anything.
pg_restore --list "$DUMP" > /dev/null || { echo "ERROR: corrupt/unreadable archive" >&2; exit 1; }

exists=$("${PSQL[@]}" -c "SELECT 1 FROM pg_database WHERE datname='$TARGET_DB';")
if [[ "$exists" == "1" ]]; then
    if [[ "$DROP_EXISTING" != "--drop-existing" ]]; then
        echo "ERROR: database '$TARGET_DB' already exists." >&2
        echo "       Restore into a new name, or pass --drop-existing to replace it." >&2
        exit 1
    fi
    if [[ "${RESTORE_FORCE:-0}" != "1" ]]; then
        echo "About to DROP DATABASE \"$TARGET_DB\" @ $DB_HOST:$DB_PORT and restore from:"
        echo "  $DUMP"
        read -r -p "Type the database name to confirm: " confirm
        [[ "$confirm" == "$TARGET_DB" ]] || { echo "aborted."; exit 1; }
    fi
    log "dropping $TARGET_DB (terminating connections)"
    "${PSQL[@]}" -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='$TARGET_DB' AND pid <> pg_backend_pid();" > /dev/null
    "${PSQL[@]}" -c "DROP DATABASE \"$TARGET_DB\";" > /dev/null
fi

log "creating database $TARGET_DB"
"${PSQL[@]}" -c "CREATE DATABASE \"$TARGET_DB\";" > /dev/null

log "restoring $DUMP → $TARGET_DB"
# --no-owner: objects become DB_USER-owned (portable across clusters where the
# original owner role may not exist yet). Grants/RLS policies are restored.
pg_restore -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$TARGET_DB" \
    --no-owner --exit-on-error "$DUMP"

log "restore complete — verifying"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$TARGET_DB" -qtA -v ON_ERROR_STOP=1 \
    -c "SELECT 'tables: ' || count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');"
log "done"
