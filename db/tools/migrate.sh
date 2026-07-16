#!/usr/bin/env bash
# ============================================================================
# migrate.sh — versioned SQL migrations with rollback for laundry_ghar_db
# ----------------------------------------------------------------------------
# Replaces the manual "apply db/patches/*.sql by hand" workflow for all NEW
# schema changes. db/patches/ stays as the historical record and the
# fresh-environment bootstrap (build_from_scratch.sh); everything after the
# baseline goes through db/migrations/ as an up/down pair:
#
#   db/migrations/0001_add_thing.up.sql     — forward change
#   db/migrations/0001_add_thing.down.sql   — exact rollback of the pair
#
# Commands:
#   new <name>       scaffold the next-numbered up/down pair
#   status           applied vs pending, with checksum-drift warnings
#   up [N|all]       apply the next N pending migrations (default: all)
#   down [N]         roll back the last N applied migrations (default: 1)
#   baseline         mark every existing migration applied WITHOUT running it
#                    (one-time adoption on a DB whose schema already matches)
#   verify           exit non-zero if applied checksums differ from files
#
# Every migration runs inside a single transaction and its bookkeeping row is
# committed atomically with it — a failed migration leaves no trace. For
# statements that cannot run in a transaction (CREATE INDEX CONCURRENTLY,
# ALTER TYPE ... ADD VALUE), put this marker on the first line of the file:
#     -- migrate: no-transaction
#
# Connection (same convention as db/build_from_scratch.sh):
#   DB_NAME (laundry_ghar_db)  DB_HOST (localhost)  DB_PORT (5432)
#   DB_USER (postgres)         DB_PASS (postgres)
# Schema changes need a privileged role (DDL), NOT the RLS-scoped app_user.
# macOS note: use the postgresql@18 client —
#   export PATH="/opt/homebrew/opt/postgresql@18/bin:$PATH"
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MIGRATIONS_DIR="${MIGRATIONS_DIR:-$SCRIPT_DIR/../migrations}"

DB_NAME="${DB_NAME:-laundry_ghar_db}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
DB_PASS="${DB_PASS:-postgres}"
export PGPASSWORD="$DB_PASS"
export PGOPTIONS="--client-min-messages=warning"

PSQL=(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -qtA)

die() { echo "ERROR: $*" >&2; exit 1; }

checksum() {
  if command -v md5sum >/dev/null 2>&1; then md5sum "$1" | cut -d' ' -f1
  else md5 -q "$1"; fi
}

ensure_table() {
  "${PSQL[@]}" -c "
    CREATE TABLE IF NOT EXISTS public.schema_migrations (
      version     text PRIMARY KEY,
      name        text NOT NULL,
      checksum    text NOT NULL,
      applied_at  timestamptz NOT NULL DEFAULT now()
    );" >/dev/null
}

# Sorted list of migration versions present on disk (e.g. "0001 0002 ...").
disk_versions() {
  find "$MIGRATIONS_DIR" -name '*.up.sql' 2>/dev/null \
    | sed 's|.*/\([0-9][0-9]*\)_.*|\1|' | sort -u
}

up_file()   { find "$MIGRATIONS_DIR" -name "${1}_*.up.sql"   | head -1; }
down_file() { find "$MIGRATIONS_DIR" -name "${1}_*.down.sql" | head -1; }
mig_name()  { basename "$1" | sed -E 's/\.(up|down)\.sql$//'; }

applied_versions() { "${PSQL[@]}" -c "SELECT version FROM public.schema_migrations ORDER BY version;"; }

wants_transaction() {
  ! head -1 "$1" | grep -qi -- '-- *migrate: *no-transaction'
}

run_sql_file() {
  local file="$1" bookkeeping="$2"
  if wants_transaction "$file"; then
    "${PSQL[@]}" -1 -f "$file" -c "$bookkeeping" >/dev/null
  else
    # No transaction wrapper; apply then record (re-running after a partial
    # failure is the operator's responsibility — the file opted out).
    "${PSQL[@]}" -f "$file" >/dev/null
    "${PSQL[@]}" -c "$bookkeeping" >/dev/null
  fi
}

cmd_new() {
  local name="${1:-}"
  [[ -n "$name" ]] || die "usage: migrate.sh new <snake_case_name>"
  [[ "$name" =~ ^[a-z0-9_]+$ ]] || die "name must be snake_case: [a-z0-9_]"
  mkdir -p "$MIGRATIONS_DIR"
  local last next
  last=$(disk_versions | tail -1)
  next=$(printf '%04d' $(( ${last:-0} + 1 )) )
  local up="$MIGRATIONS_DIR/${next}_${name}.up.sql"
  local down="$MIGRATIONS_DIR/${next}_${name}.down.sql"
  printf -- '-- %s — forward migration\n-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)\n\n' "${next}_${name}" > "$up"
  printf -- '-- %s — rollback: must exactly undo the .up.sql\n\n' "${next}_${name}" > "$down"
  echo "created $up"
  echo "created $down"
}

cmd_status() {
  ensure_table
  local applied pending=0
  applied=$(applied_versions)
  echo "database: $DB_NAME @ $DB_HOST:$DB_PORT"
  for v in $(disk_versions); do
    local file name state
    file=$(up_file "$v"); name=$(mig_name "$file")
    if grep -qx "$v" <<<"$applied"; then
      local db_sum
      db_sum=$("${PSQL[@]}" -c "SELECT checksum FROM public.schema_migrations WHERE version='$v';")
      if [[ "$db_sum" == "$(checksum "$file")" ]]; then state="applied"
      else state="applied (CHECKSUM DRIFT — file changed after apply!)"; fi
    else
      state="pending"; pending=$((pending+1))
    fi
    printf '  %-40s %s\n' "$name" "$state"
  done
  # Applied in DB but missing on disk = deleted migration files.
  for v in $applied; do
    if [[ -z "$(up_file "$v")" ]]; then
      printf '  %-40s %s\n' "version $v" "applied (NO FILE ON DISK)"
    fi
  done
  echo "pending: $pending"
}

cmd_up() {
  ensure_table
  local limit="${1:-all}" applied count=0
  applied=$(applied_versions)
  for v in $(disk_versions); do
    grep -qx "$v" <<<"$applied" && continue
    [[ "$limit" != all && $count -ge $limit ]] && break
    local file name sum
    file=$(up_file "$v"); name=$(mig_name "$file"); sum=$(checksum "$file")
    [[ -n "$(down_file "$v")" ]] || die "$name has no .down.sql — every migration needs a rollback"
    echo "applying $name ..."
    run_sql_file "$file" \
      "INSERT INTO public.schema_migrations(version,name,checksum) VALUES ('$v','$name','$sum');"
    count=$((count+1))
  done
  echo "applied $count migration(s)"
}

cmd_down() {
  ensure_table
  local n="${1:-1}" reverted=0
  for v in $(applied_versions | sort -r | head -"$n"); do
    local file name
    file=$(down_file "$v")
    [[ -n "$file" ]] || die "no .down.sql on disk for applied version $v — cannot roll back"
    name=$(mig_name "$file")
    echo "rolling back $name ..."
    run_sql_file "$file" \
      "DELETE FROM public.schema_migrations WHERE version='$v';"
    reverted=$((reverted+1))
  done
  echo "rolled back $reverted migration(s)"
}

cmd_baseline() {
  ensure_table
  local applied count=0
  applied=$(applied_versions)
  for v in $(disk_versions); do
    grep -qx "$v" <<<"$applied" && continue
    local file name sum
    file=$(up_file "$v"); name=$(mig_name "$file"); sum=$(checksum "$file")
    "${PSQL[@]}" -c "INSERT INTO public.schema_migrations(version,name,checksum) VALUES ('$v','$name','$sum');" >/dev/null
    echo "baselined $name (marked applied, not run)"
    count=$((count+1))
  done
  echo "baselined $count migration(s)"
}

cmd_verify() {
  ensure_table
  local bad=0
  for v in $(applied_versions); do
    local file
    file=$(up_file "$v")
    if [[ -z "$file" ]]; then
      echo "FAIL: version $v applied but no file on disk"; bad=1; continue
    fi
    local db_sum
    db_sum=$("${PSQL[@]}" -c "SELECT checksum FROM public.schema_migrations WHERE version='$v';")
    if [[ "$db_sum" != "$(checksum "$file")" ]]; then
      echo "FAIL: $(mig_name "$file") checksum drift"; bad=1
    fi
  done
  [[ $bad -eq 0 ]] && echo "verify OK"
  exit $bad
}

case "${1:-}" in
  new)      shift; cmd_new "$@";;
  status)   cmd_status;;
  up)       shift; cmd_up "${1:-all}";;
  down)     shift; cmd_down "${1:-1}";;
  baseline) cmd_baseline;;
  verify)   cmd_verify;;
  *) sed -n '2,35p' "$0" | sed 's/^# \{0,1\}//'; exit 1;;
esac
