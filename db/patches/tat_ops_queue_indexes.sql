-- ===========================================================================
-- TAT engine + ops queue — supporting indexes
-- ===========================================================================
-- Task #19: promised_delivery_at column already exists in the base schema
-- (04_bc4_order_lifecycle.sql line 100) as promised_delivery_at TIMESTAMPTZ.
-- EF maps it as PromisedDeliveryAt via OrderConfiguration.
--
-- This patch adds:
--   1. Partial index for the ops queue "overdue" bucket
--      (non-terminal orders with a past promised_delivery_at)
--   2. Partial index for the ops queue "due today" bucket
--      (non-terminal orders with promised_delivery_at IS NOT NULL)
--
-- Both indexes are idempotent (CREATE INDEX IF NOT EXISTS).
-- Safe to re-run; apply to: laundry_ghar_db, schema order_lifecycle.
-- ===========================================================================

-- ── 1. Overdue index — supports WHERE promised_delivery_at < now() AND status not terminal
CREATE INDEX IF NOT EXISTS idx_orders_overdue
    ON order_lifecycle.orders (promised_delivery_at ASC, brand_id)
    WHERE promised_delivery_at IS NOT NULL
      AND deleted_at IS NULL
      AND status NOT IN ('delivered', 'cancelled', 'closed', 'returned');

-- ── 2. Due-today / general promised_at index — supports both dueToday and overdue buckets
CREATE INDEX IF NOT EXISTS idx_orders_promised_delivery
    ON order_lifecycle.orders (brand_id, promised_delivery_at)
    WHERE promised_delivery_at IS NOT NULL AND deleted_at IS NULL;
