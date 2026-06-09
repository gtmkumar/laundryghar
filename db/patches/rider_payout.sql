-- =============================================================================
-- rider_payout.sql  (Rider Ops Phase 4)
-- Persists the computed per-leg rider payout (₹) on the delivery leg, replacing
-- the display-only estimate. Rates are configurable in Admin → Settings → Payouts
-- (kernel.system_settings category 'payout', key 'rider'); the value here is the
-- amount computed at completion time from whatever the rates were then.
-- Idempotent. Additive only.
--   PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/rider_payout.sql
-- =============================================================================

BEGIN;

ALTER TABLE order_lifecycle.delivery_assignments
    ADD COLUMN IF NOT EXISTS payout_amount numeric(10,2) NULL;

COMMENT ON COLUMN order_lifecycle.delivery_assignments.payout_amount IS
    'Rider earning for this leg (₹), computed from the payout rates at completion. Null until completed.';

COMMIT;

SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema='order_lifecycle' AND table_name='delivery_assignments'
  AND column_name='payout_amount';
