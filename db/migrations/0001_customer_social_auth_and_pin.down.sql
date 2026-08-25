-- 0001_customer_social_auth_and_pin — rollback: must exactly undo the .up.sql
--
-- NOTE: re-imposing NOT NULL on customers.phone_e164 fails if any phone-less
-- (Google-only) customer exists. That is deliberate — rolling back would
-- otherwise silently destroy accounts. Delete or backfill those rows first:
--   SELECT id, email FROM customer_catalog.customers WHERE phone_e164 IS NULL;

-- ─── 3. Restore phone_e164 as mandatory ─────────────────────────────────────
DROP INDEX IF EXISTS customer_catalog.idx_customers_brand_phone;
CREATE INDEX idx_customers_brand_phone
    ON customer_catalog.customers(brand_id, phone_e164)
    WHERE deleted_at IS NULL;

ALTER TABLE customer_catalog.customers
    ALTER COLUMN phone_e164 SET NOT NULL;

-- ─── 2. Drop PIN columns ────────────────────────────────────────────────────
ALTER TABLE customer_catalog.customers
    DROP COLUMN IF EXISTS pin_locked_until,
    DROP COLUMN IF EXISTS pin_failed_attempts,
    DROP COLUMN IF EXISTS pin_set_at,
    DROP COLUMN IF EXISTS pin_hash;

-- ─── 1. Drop federated identity links ───────────────────────────────────────
DROP TABLE IF EXISTS customer_catalog.customer_identities;
