-- =============================================================================
-- order_job_type.sql  (Laundry + Logistics marketplace — Wave 1.1)
-- Introduces a first-class job_type on orders so laundry stays the default and a
-- new point-to-point 'parcel' delivery can ride the SAME order / state-machine /
-- payment / dispatch spine without forking the schema.
--   job_type = 'laundry' (default, current behaviour) | 'parcel' (point-to-point)
-- Extensible later to 'truck','intercity'. Additive + idempotent. Safe to re-run.
-- Backward-compat keystone: DEFAULT 'laundry' => every existing row and every
-- existing insert path that does not set job_type behaves exactly as today.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/order_job_type.sql
-- =============================================================================

BEGIN;

ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS job_type VARCHAR(20) NOT NULL DEFAULT 'laundry';

-- Constrain the vocabulary (drop+recreate so re-runs widen cleanly if needed).
ALTER TABLE order_lifecycle.orders
    DROP CONSTRAINT IF EXISTS orders_job_type_check;
ALTER TABLE order_lifecycle.orders
    ADD CONSTRAINT orders_job_type_check
    CHECK (job_type IN ('laundry','parcel'));

COMMENT ON COLUMN order_lifecycle.orders.job_type IS
    'Marketplace job kind: laundry (default) or parcel (point-to-point delivery). '
    'Orthogonal to order_type, which is the laundry sub-classification.';

-- Partial index: parcel jobs are the minority; index only the non-laundry slice
-- for the dispatch / ops queries that filter on it.
CREATE INDEX IF NOT EXISTS idx_orders_job_type
    ON order_lifecycle.orders (job_type, status, created_at)
    WHERE job_type <> 'laundry';

COMMIT;

-- Quick check
SELECT column_name, data_type, column_default
FROM information_schema.columns
WHERE table_schema='order_lifecycle' AND table_name='orders'
  AND column_name='job_type';
