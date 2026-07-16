#!/usr/bin/env bash
# ============================================================================
# verify-backup.sh — prove the latest backup actually restores
# ----------------------------------------------------------------------------
# A backup that has never been restored is a hope, not a backup. This script:
#   1. Picks the newest ${DB_NAME}_*.dump in BACKUP_DIR (or takes one as $1).
#   2. Starts a throwaway PostgreSQL container (never touches a real server).
#   3. Restores the archive into it with --exit-on-error.
#   4. Sanity-checks: counts tables and non-empty schemas.
#   5. Tears the container down (always, via trap).
#
# Usage:  ops/backup/verify-backup.sh [dump-file]
# Env:    BACKUP_DIR (default ~/laundryghar-backups)
#         DB_NAME    (laundry_ghar_db — dump filename prefix)
#         PG_IMAGE   (postgres:18 — match the production major version)
# Requires: docker.
# Schedule weekly alongside the daily backup (see README.md).
# ============================================================================
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-$HOME/laundryghar-backups}"
DB_NAME="${DB_NAME:-laundry_ghar_db}"
PG_IMAGE="${PG_IMAGE:-postgres:18}"
CONTAINER="lg-backup-verify-$$"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

DUMP="${1:-$(ls -t "$BACKUP_DIR"/${DB_NAME}_*.dump 2>/dev/null | head -1 || true)}"
[[ -n "$DUMP" && -f "$DUMP" ]] || { echo "ERROR: no dump found (dir: $BACKUP_DIR)" >&2; exit 1; }

log "verifying restore of: $DUMP"

cleanup() { docker rm -f "$CONTAINER" > /dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$CONTAINER" \
    -e POSTGRES_PASSWORD=verify -e POSTGRES_DB=verify_db \
    "$PG_IMAGE" > /dev/null

log "waiting for scratch postgres ($PG_IMAGE)"
for _ in $(seq 1 30); do
    docker exec "$CONTAINER" pg_isready -U postgres -q && break
    sleep 1
done
docker exec "$CONTAINER" pg_isready -U postgres -q || { echo "ERROR: scratch postgres never came up" >&2; exit 1; }

# The dump contains CREATE EXTENSION pg_partman + postgis — install both from
# PGDG apt (the postgres image ships the repo pre-configured).
PG_MAJOR=$(docker exec "$CONTAINER" psql -U postgres -qtA -c "SHOW server_version;" | cut -d. -f1)
log "installing pg_partman + postgis for PG$PG_MAJOR in scratch container"
docker exec -e DEBIAN_FRONTEND=noninteractive "$CONTAINER" bash -c \
    "apt-get update -qq && apt-get install -y -qq postgresql-$PG_MAJOR-partman postgresql-$PG_MAJOR-postgis-3" > /dev/null 2>&1

# The app schemas reference the app_user/app_admin roles in GRANTs/policies —
# create them so pg_restore doesn't error on ACLs.
docker exec "$CONTAINER" psql -U postgres -q \
    -c "CREATE ROLE app_user LOGIN PASSWORD 'x';" \
    -c "CREATE ROLE app_admin LOGIN PASSWORD 'x';" 2>/dev/null || true

log "restoring into scratch container"
docker cp "$DUMP" "$CONTAINER:/tmp/verify.dump" > /dev/null
docker exec "$CONTAINER" pg_restore -U postgres -d verify_db \
    --no-owner --exit-on-error /tmp/verify.dump

log "sanity checks"
tables=$(docker exec "$CONTAINER" psql -U postgres -d verify_db -qtA \
    -c "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');")
schemas=$(docker exec "$CONTAINER" psql -U postgres -d verify_db -qtA \
    -c "SELECT count(DISTINCT schemaname) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');")

log "restored: $tables tables across $schemas schemas"
[[ "$tables" -gt 0 ]] || { echo "ERROR: restore produced zero tables" >&2; exit 1; }

log "VERIFY OK — $DUMP restores cleanly"
