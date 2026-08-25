-- 0018_domain_health — rollback

DROP FUNCTION IF EXISTS kernel.record_domain_check(text, text, text, timestamptz, text);
DROP FUNCTION IF EXISTS kernel.domains_needing_attention(int);

DROP INDEX IF EXISTS tenancy_org.idx_brand_domains_health_due;
ALTER TABLE tenancy_org.brand_domains DROP CONSTRAINT IF EXISTS brand_domains_health_status_check;

ALTER TABLE tenancy_org.brand_domains
    DROP COLUMN IF EXISTS ssl_expires_at,
    DROP COLUMN IF EXISTS ssl_issuer,
    DROP COLUMN IF EXISTS last_checked_at,
    DROP COLUMN IF EXISTS health_status,
    DROP COLUMN IF EXISTS health_detail,
    DROP COLUMN IF EXISTS consecutive_failures;
