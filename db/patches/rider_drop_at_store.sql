-- =============================================================================
-- rider_drop_at_store.sql  (Rider Ops Phase 2)
-- Models the laundry round-trip's two-part pickup leg + geofence auto-status:
--   pickup leg = customer (collect) → store/warehouse (drop the dirty clothes).
-- Adds two timestamps to order_lifecycle.delivery_assignments:
--   collected_at : items collected from the customer (set on pickup OTP verify)
--   dropped_at   : items dropped at the store/laundry (set on store geofence)
-- The existing arrived_at is now auto-stamped when the rider reaches the customer
-- (geofence on location ping). Idempotent. Safe to re-run.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/rider_drop_at_store.sql
-- =============================================================================

BEGIN;

ALTER TABLE order_lifecycle.delivery_assignments
    ADD COLUMN IF NOT EXISTS collected_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS dropped_at   timestamptz NULL;

COMMENT ON COLUMN order_lifecycle.delivery_assignments.collected_at IS
    'Pickup leg: when the rider collected items from the customer (set on pickup OTP verify).';
COMMENT ON COLUMN order_lifecycle.delivery_assignments.dropped_at IS
    'Pickup leg: when the rider dropped the collected items at the store/laundry (geofence at store).';

COMMIT;

-- Quick check
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema='order_lifecycle' AND table_name='delivery_assignments'
  AND column_name IN ('collected_at','dropped_at')
ORDER BY column_name;
