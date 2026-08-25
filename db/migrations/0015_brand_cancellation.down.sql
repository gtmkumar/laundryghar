-- 0015_brand_cancellation — rollback

DROP FUNCTION IF EXISTS kernel.set_brand_cancellation_state(uuid, text);
DROP FUNCTION IF EXISTS kernel.purge_brand(uuid);
DROP FUNCTION IF EXISTS kernel.export_brand(uuid);
DROP FUNCTION IF EXISTS kernel.brand_scoped_tables();

DROP TABLE IF EXISTS tenancy_org.brand_cancellations;

-- Any brand left mid-wind-down goes back to suspended rather than active: reversing the migration
-- must not silently put a cancelled tenant back into service.
UPDATE tenancy_org.brands SET status = 'suspended' WHERE status = 'cancelled';

ALTER TABLE tenancy_org.brands DROP CONSTRAINT IF EXISTS brands_status_check;
ALTER TABLE tenancy_org.brands ADD CONSTRAINT brands_status_check
    CHECK (status IN ('active', 'suspended', 'archived'));
