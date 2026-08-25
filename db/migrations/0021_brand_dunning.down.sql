-- 0021_brand_dunning — rollback

DROP FUNCTION IF EXISTS kernel.set_brand_suspension(uuid, boolean, text);

DROP INDEX IF EXISTS identity_access.idx_brand_platform_invoice_dunning;
ALTER TABLE identity_access.brand_platform_invoice
    DROP COLUMN IF EXISTS attempt_count,
    DROP COLUMN IF EXISTS last_attempt_at,
    DROP COLUMN IF EXISTS next_attempt_at;

ALTER TABLE tenancy_org.brands DROP CONSTRAINT IF EXISTS brands_suspension_reason_check;
ALTER TABLE tenancy_org.brands DROP COLUMN IF EXISTS suspension_reason;
