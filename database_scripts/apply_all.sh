#!/usr/bin/env bash
# Apply the Laundry Ghar schema in dependency order.
#
#   DATABASE_URL=postgres://user:pw@host:5432/laundry_ghar ./apply_all.sh
#
# Stops on first error (ON_ERROR_STOP=1) and runs each file as a single
# transaction so a mid-file failure leaves the DB clean.

set -euo pipefail

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "ERROR: DATABASE_URL not set" >&2
  exit 2
fi

# Apply order is FK-dependency-correct (NOT lexical).
# 00_kernel.sql has columns referencing brands/franchises/stores (defined in 01),
# so 01 must land before 00. Likewise 02 references brands (from 01).
FILES=(
  01_bc1_tenancy_org.sql       # Wave 0  BC-1  (no external deps — must be first)
  02_bc2_identity_access.sql   # Wave 0  BC-2  (FKs brands from 01)
  00_kernel.sql                # Wave 0  BC-0  (FKs brands/franchises/stores from 01)
  03_bc3_customer_catalog.sql  # Wave 1  BC-3
  04_bc4_order_lifecycle.sql   # Wave 1  BC-4
  05_bc5_logistics.sql         # Wave 1  BC-5
  06_bc6_commerce.sql          # Wave 1  BC-6
  07_bc7_finance_royalty.sql   # Wave 1  BC-7
  08_bc8_engagement_cms.sql    # Wave 2  BC-8
  09_bc9_analytics.sql         # Wave 2  BC-9
  99_cross_cutting.sql         # Wave 2  pg_partman + RLS docs
)

HERE="$(cd "$(dirname "$0")" && pwd)"

for f in "${FILES[@]}"; do
  echo "▶ applying $f"
  psql "$DATABASE_URL" \
    --single-transaction \
    -v ON_ERROR_STOP=1 \
    -f "$HERE/$f"
done

echo "✓ schema applied"
