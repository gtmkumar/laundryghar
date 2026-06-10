-- Patch: pickup_requested_items
-- Adds requested_items (jsonb array of estimated cart lines) and
-- payment_preference (wallet/cod/upi-deferred) to pickup_requests.
-- Idempotent: uses IF NOT EXISTS column additions (Postgres 9.6+).
-- Applied to: localhost:5432/laundry_ghar_db

DO $$
BEGIN
    -- requested_items: estimated cart lines submitted by the customer at booking time.
    -- Schema: [{ serviceId?, itemId?, displayLabel, quantity, estimatedUnitPrice? }]
    -- These are ESTIMATES only — the authoritative order is created at weighing.
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'order_lifecycle'
          AND table_name   = 'pickup_requests'
          AND column_name  = 'requested_items'
    ) THEN
        ALTER TABLE order_lifecycle.pickup_requests
            ADD COLUMN requested_items jsonb NOT NULL DEFAULT '[]'::jsonb;
        COMMENT ON COLUMN order_lifecycle.pickup_requests.requested_items IS
            'Estimated cart lines submitted by the customer at booking time. '
            'Schema: [{serviceId?, itemId?, displayLabel, quantity, estimatedUnitPrice?}]. '
            'These are ESTIMATES — the authoritative order is created after weighing.';
    END IF;

    -- payment_preference: wallet/cod/upi-deferred. Not a payment record — just
    -- an intent signal so ops can prepare the right collection method at pickup.
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'order_lifecycle'
          AND table_name   = 'pickup_requests'
          AND column_name  = 'payment_preference'
    ) THEN
        ALTER TABLE order_lifecycle.pickup_requests
            ADD COLUMN payment_preference character varying(20) NOT NULL DEFAULT 'cod';
        COMMENT ON COLUMN order_lifecycle.pickup_requests.payment_preference IS
            'Customer payment intent at booking: wallet | cod | upi-deferred. '
            'UPI/card selections are recorded as upi-deferred; actual collection '
            'is always handled when the order is confirmed after weighing.';
    END IF;
END
$$;
