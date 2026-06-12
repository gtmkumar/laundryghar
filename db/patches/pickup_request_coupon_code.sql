-- ============================================================================
-- PATCH: add coupon_code column to order_lifecycle.pickup_requests
-- ============================================================================
-- Purpose: store the optional coupon code submitted by the customer at
--          pickup-booking time so it can be threaded into the admin
--          pickup→order conversion (R3-BE-2).
-- Idempotent: uses IF NOT EXISTS guard.
-- Apply after: all existing patches (fk_patch_04_order_lifecycle.sql, etc.)
-- ============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM information_schema.columns
         WHERE table_schema = 'order_lifecycle'
           AND table_name   = 'pickup_requests'
           AND column_name  = 'coupon_code'
    ) THEN
        ALTER TABLE order_lifecycle.pickup_requests
            ADD COLUMN coupon_code VARCHAR(50);
    END IF;
END
$$;
