#!/usr/bin/env bash
# ============================================================================
# apply_saas_billing_patches.sh — apply the multi-vertical + SaaS tier-billing
# patch set (idempotent; safe to re-run). Run AFTER the baseline schema +
# fk_patch set are in place. See docs/SAAS_PLATFORM_ARCHITECTURE.md §10.
#
#   bash db/patches/apply_saas_billing_patches.sh
#   DB_HOST=… DB_PORT=… DB_NAME=… DB_USER=postgres DB_PASS=… \
#     bash db/patches/apply_saas_billing_patches.sh
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

# Dependency-correct order. Each patch is idempotent (ADD COLUMN IF NOT EXISTS /
# CREATE TABLE IF NOT EXISTS / ON CONFLICT) and ends with an in-transaction verify gate.
PATCHES=(
  phase4_role_vertical_key.sql              # roles.vertical_key + salon/logistics on-site roles
  phase4_user_vertical_key.sql              # users.vertical_key (home vertical, backfilled)
  phase4_bundle_pricing.sql                 # module_bundle = priced brand tier
  phase4_brand_platform_subscription.sql    # brand_platform_subscription + _invoice (+RLS)
  phase4_brand_platform_invoice_paylink.sql # invoice.razorpay_payment_link_id / payment_link_url
  phase4_platform_billing_nav.sql           # "Platform billing" nav module (Finance / saas.read)
)

echo "▶ applying ${#PATCHES[@]} SaaS-billing patches to ${DB_NAME}@${DB_HOST}:${DB_PORT}"
for p in "${PATCHES[@]}"; do
  echo "  → $p"
  "${PSQL[@]}" -f "$HERE/$p" >/dev/null
done
echo "✓ all SaaS-billing patches applied."
