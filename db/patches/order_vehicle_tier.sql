-- =============================================================================
-- order_vehicle_tier.sql  (Laundry + Logistics marketplace — Wave 1.2)
-- Lets a job request a vehicle tier so dispatch can match it to a rider whose
-- vehicle is large enough (upgrade ladder: a bigger vehicle may take a smaller job).
-- Vocabulary matches logistics.riders.vehicle_type.
--   NULL = no constraint = current behaviour (any eligible rider).
-- Denormalised onto pickup_requests too, so the auto-dispatch worker can read the
-- required tier without joining back to the (partitioned) orders table.
-- Additive + idempotent. Safe to re-run.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/order_vehicle_tier.sql
-- =============================================================================

BEGIN;

-- Canonical home: the order.
ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS requested_vehicle_tier VARCHAR(20) NULL;
ALTER TABLE order_lifecycle.orders
    DROP CONSTRAINT IF EXISTS orders_requested_vehicle_tier_check;
ALTER TABLE order_lifecycle.orders
    ADD CONSTRAINT orders_requested_vehicle_tier_check
    CHECK (requested_vehicle_tier IS NULL OR requested_vehicle_tier IN
        ('foot','cycle','two_wheeler','three_wheeler','four_wheeler'));
COMMENT ON COLUMN order_lifecycle.orders.requested_vehicle_tier IS
    'Vehicle tier this job requires (matches riders.vehicle_type). NULL = no constraint.';

-- Dispatch convenience: denormalised onto pickup_requests so the worker avoids the
-- partitioned-orders join when ranking riders.
ALTER TABLE order_lifecycle.pickup_requests
    ADD COLUMN IF NOT EXISTS requested_vehicle_tier VARCHAR(20) NULL;
ALTER TABLE order_lifecycle.pickup_requests
    DROP CONSTRAINT IF EXISTS pickup_requests_requested_vehicle_tier_check;
ALTER TABLE order_lifecycle.pickup_requests
    ADD CONSTRAINT pickup_requests_requested_vehicle_tier_check
    CHECK (requested_vehicle_tier IS NULL OR requested_vehicle_tier IN
        ('foot','cycle','two_wheeler','three_wheeler','four_wheeler'));
COMMENT ON COLUMN order_lifecycle.pickup_requests.requested_vehicle_tier IS
    'Denormalised copy of the job''s required vehicle tier, for auto-dispatch matching.';

COMMIT;

-- Quick check
SELECT table_name, column_name
FROM information_schema.columns
WHERE table_schema='order_lifecycle'
  AND column_name='requested_vehicle_tier'
ORDER BY table_name;
