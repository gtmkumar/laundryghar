-- =============================================================================
-- pickup_declared_value.sql  (GH #22/#24 — declared value on estimated pickup lines)
--   pickup_requests.requested_items is a schema-less jsonb array of estimated cart
--   lines. This patch adds an optional `declaredValue` to that line shape so a
--   customer can declare a value-slab garment's value up front for a correct
--   estimate. No column/type change is needed (jsonb is schema-less); this only
--   refreshes the documentation COMMENT so the stored shape is discoverable.
--
--   Value is NOT enforced at pickup time — the store confirms it when the pickup
--   converts to an order, where it becomes the order line's declared_value (which
--   already exists via value_slab_pricing.sql → order_items.declared_value).
--
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/pickup_declared_value.sql
-- =============================================================================

BEGIN;

COMMENT ON COLUMN order_lifecycle.pickup_requests.requested_items IS
    'Estimated cart lines submitted by the customer at booking time. '
    'Schema: [{serviceId?, itemId?, displayLabel, quantity, estimatedUnitPrice?, declaredValue?}]. '
    'declaredValue is the optional garment value for value-slab (branded/luxury) items — not '
    'enforced here; the store confirms it when the pickup converts to an order. '
    'These are ESTIMATES — the authoritative order is created after weighing.';

COMMIT;

-- Verification.
SELECT col_description('order_lifecycle.pickup_requests'::regclass,
       (SELECT ordinal_position FROM information_schema.columns
        WHERE table_schema='order_lifecycle' AND table_name='pickup_requests'
          AND column_name='requested_items')) AS requested_items_comment;
