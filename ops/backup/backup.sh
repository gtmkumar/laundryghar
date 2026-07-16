#!/usr/bin/env bash
# ============================================================================
# backup.sh — automated PostgreSQL backup for laundry_ghar_db
# ----------------------------------------------------------------------------
# Produces a timestamped pg_dump custom-format archive (compressed, works with
# pg_restore's selective/parallel restore), a globals dump (roles — app_user /
# app_admin grants live outside the DB), verifies archive integrity, prunes
# old local backups, and optionally uploads offsite.
#
# Usage:      ops/backup/backup.sh
# Schedule:   see ops/backup/README.md (launchd on macOS, cron on Linux)
#
# Env:
#   DB_NAME (laundry_ghar_db)  DB_HOST (localhost)  DB_PORT (5432)
#   DB_USER (postgres)         DB_PASS (postgres)
#     — pg_dump needs a role that can read every schema (RLS-scoped app_user
#       can NOT produce a complete backup; use postgres or app_admin).
#   BACKUP_DIR            (default: ~/laundryghar-backups)
#   BACKUP_RETENTION_DAYS (default: 14 — local prune horizon)
#   BACKUP_S3_URI         (optional: s3://bucket/prefix — uploads via aws cli)
#   RCLONE_REMOTE         (optional: remote:path — uploads via rclone)
# ============================================================================
set -euo pipefail

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"
export PGPASSWORD="$DB_PASS"

BACKUP_DIR="${BACKUP_DIR:-$HOME/laundryghar-backups}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"

STAMP="$(date +%Y%m%d_%H%M%S)"
DUMP="$BACKUP_DIR/${DB_NAME}_${STAMP}.dump"
GLOBALS="$BACKUP_DIR/globals_${STAMP}.sql"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

# A partial/truncated file must never linger looking like a valid backup.
failed=1
cleanup_on_failure() {
    if [[ $failed -eq 1 ]]; then
        rm -f "$DUMP" "$GLOBALS"
        echo "BACKUP FAILED — partial files removed" >&2
    fi
}
trap cleanup_on_failure EXIT

mkdir -p "$BACKUP_DIR"

log "backing up $DB_NAME @ $DB_HOST:$DB_PORT → $DUMP"
pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" \
    --format=custom --compress=6 --no-password --file="$DUMP"

log "dumping globals (roles) → $GLOBALS"
pg_dumpall -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" \
    --globals-only --no-password > "$GLOBALS"

# Integrity check: a truncated/corrupt archive fails to list.
log "verifying archive integrity"
pg_restore --list "$DUMP" > /dev/null
log "archive OK ($(du -h "$DUMP" | cut -f1) )"

# Offsite upload (optional — set one of the two).
if [[ -n "${BACKUP_S3_URI:-}" ]]; then
    log "uploading to $BACKUP_S3_URI"
    aws s3 cp "$DUMP"    "$BACKUP_S3_URI/" --only-show-errors
    aws s3 cp "$GLOBALS" "$BACKUP_S3_URI/" --only-show-errors
elif [[ -n "${RCLONE_REMOTE:-}" ]]; then
    log "uploading to $RCLONE_REMOTE"
    rclone copy "$DUMP"    "$RCLONE_REMOTE/" --quiet
    rclone copy "$GLOBALS" "$RCLONE_REMOTE/" --quiet
else
    log "no offsite target configured (BACKUP_S3_URI / RCLONE_REMOTE) — local only"
fi

# Prune local backups past the retention horizon.
log "pruning local backups older than $BACKUP_RETENTION_DAYS day(s)"
find "$BACKUP_DIR" -name "${DB_NAME}_*.dump" -mtime +"$BACKUP_RETENTION_DAYS" -delete
find "$BACKUP_DIR" -name "globals_*.sql"     -mtime +"$BACKUP_RETENTION_DAYS" -delete

failed=0
log "done: $DUMP"
