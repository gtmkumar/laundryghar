-- =============================================================================
-- db/patches/phase4_bundle_pricing.sql
--
-- SaaS connective tissue (P0 §5 of docs/SAAS_PLATFORM_ARCHITECTURE.md): make a module_bundle a
-- BRAND-LEVEL PRICED TIER, so applying a bundle both (a) licenses its features (brand_module) and
-- (b) records what that tier costs the tenant — price ↔ features on ONE object, in the same core
-- (identity_access) context as entitlement.
--
--   * Adds price / billing_interval / currency_code / is_public to identity_access.module_bundle.
--   * Backfills illustrative monthly INR prices for the seeded tiers (idempotent: only where null).
--
-- Distinct from finance_royalty.platform_plans (the FRANCHISE-level SaaS tiers — a different payer).
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_bundle_pricing.sql
-- =============================================================================

BEGIN;

-- 1. Brand-tier commercial columns -------------------------------------------
ALTER TABLE identity_access.module_bundle
    ADD COLUMN IF NOT EXISTS price            NUMERIC(14,2),
    ADD COLUMN IF NOT EXISTS billing_interval VARCHAR(20),
    ADD COLUMN IF NOT EXISTS currency_code    CHARACTER(3),
    ADD COLUMN IF NOT EXISTS is_public        BOOLEAN NOT NULL DEFAULT true;

DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'module_bundle_billing_interval_check') THEN
        ALTER TABLE identity_access.module_bundle
            ADD CONSTRAINT module_bundle_billing_interval_check
            CHECK (billing_interval IS NULL OR billing_interval IN ('monthly','quarterly','half_yearly','yearly'));
    END IF;
END
$chk$;

-- 2. Illustrative pricing for the seeded tiers (only where unset) -------------
UPDATE identity_access.module_bundle
   SET price            = v.price,
       billing_interval = COALESCE(billing_interval, 'monthly'),
       currency_code    = COALESCE(currency_code, 'INR')
FROM (VALUES
    ('starter',       999.00),
    ('pro',          2999.00),
    ('enterprise',   7999.00),
    ('salon-starter',1499.00)
) AS v(code, price)
WHERE module_bundle.code = v.code
  AND module_bundle.price IS NULL;

-- 3. Verification gate -------------------------------------------------------
DO $verify$
DECLARE has_cols int; priced int;
BEGIN
    SELECT count(*) INTO has_cols FROM information_schema.columns
     WHERE table_schema='identity_access' AND table_name='module_bundle'
       AND column_name IN ('price','billing_interval','currency_code','is_public');
    IF has_cols <> 4 THEN RAISE EXCEPTION 'bundle_pricing: expected 4 new columns, found %', has_cols; END IF;

    SELECT count(*) INTO priced FROM identity_access.module_bundle WHERE price IS NOT NULL;
    IF priced < 1 THEN RAISE EXCEPTION 'bundle_pricing: no bundles were priced'; END IF;

    RAISE NOTICE 'bundle_pricing verification passed: % bundle(s) now carry a brand-tier price.', priced;
END
$verify$;

COMMIT;

SELECT 'phase4_bundle_pricing.sql applied successfully.' AS result;
