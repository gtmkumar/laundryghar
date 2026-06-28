-- =============================================================================
-- db/patches/phase4_brand_platform_subscription.sql
--
-- SaaS connective tissue (P0 §6 of docs/SAAS_PLATFORM_ARCHITECTURE.md): bill the BRAND (tenant) for
-- its platform tier. Adds the PLATFORM→BRAND subscription + its invoices, next to the entitlement
-- data they derive from (identity_access). ApplyBundleToBrand upserts the subscription (snapshotting
-- the module_bundle's price) and issues the first invoice; BrandPlatformBillingService issues renewals.
--
-- Distinct from commerce.customer_subscriptions (customer of a brand) and
-- finance_royalty.franchise_subscriptions (a franchise's SaaS fee).
--
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_brand_platform_subscription.sql
-- =============================================================================

BEGIN;

-- 1. The brand's platform subscription (one per brand) ------------------------
CREATE TABLE IF NOT EXISTS identity_access.brand_platform_subscription (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id             UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    bundle_code          VARCHAR(50)  NOT NULL,
    plan_name            VARCHAR(100) NOT NULL,
    price                NUMERIC(14,2) NOT NULL,
    billing_interval     VARCHAR(20)  NOT NULL DEFAULT 'monthly',
    currency_code        CHARACTER(3) NOT NULL DEFAULT 'INR',
    status               VARCHAR(20)  NOT NULL DEFAULT 'active',
    current_period_start TIMESTAMPTZ  NOT NULL,
    current_period_end   TIMESTAMPTZ  NOT NULL,
    next_billing_at      TIMESTAMPTZ  NOT NULL,
    auto_renew           BOOLEAN      NOT NULL DEFAULT true,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by           UUID,
    updated_by           UUID
);
CREATE UNIQUE INDEX IF NOT EXISTS brand_platform_subscription_brand_id_key
    ON identity_access.brand_platform_subscription (brand_id);

-- 2. Periodic invoices for that subscription ---------------------------------
CREATE TABLE IF NOT EXISTS identity_access.brand_platform_invoice (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    subscription_id      UUID NOT NULL REFERENCES identity_access.brand_platform_subscription(id) ON DELETE CASCADE,
    brand_id             UUID NOT NULL,
    billing_period_start TIMESTAMPTZ  NOT NULL,
    billing_period_end   TIMESTAMPTZ  NOT NULL,
    amount               NUMERIC(14,2) NOT NULL,
    currency_code        CHARACTER(3) NOT NULL DEFAULT 'INR',
    status               VARCHAR(20)  NOT NULL DEFAULT 'issued',
    issued_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    due_at               TIMESTAMPTZ  NOT NULL,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS brand_platform_invoice_period_key
    ON identity_access.brand_platform_invoice (subscription_id, billing_period_start);
CREATE INDEX IF NOT EXISTS brand_platform_invoice_brand_idx
    ON identity_access.brand_platform_invoice (brand_id);

-- 3. RLS — brand-scoped, mirroring identity_access.brand_module ----------------
ALTER TABLE identity_access.brand_platform_subscription ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.brand_platform_invoice      ENABLE ROW LEVEL SECURITY;

DO $rls$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policy WHERE polname = 'rls_brand'
                   AND polrelid = 'identity_access.brand_platform_subscription'::regclass) THEN
        CREATE POLICY rls_brand ON identity_access.brand_platform_subscription
            USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policy WHERE polname = 'rls_brand'
                   AND polrelid = 'identity_access.brand_platform_invoice'::regclass) THEN
        CREATE POLICY rls_brand ON identity_access.brand_platform_invoice
            USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
    END IF;
END
$rls$;

-- 4. Grants to the app role ---------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.brand_platform_subscription TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.brand_platform_invoice      TO app_user;

-- 5. Verification gate --------------------------------------------------------
DO $verify$
DECLARE n int;
BEGIN
    SELECT count(*) INTO n FROM information_schema.tables
     WHERE table_schema='identity_access'
       AND table_name IN ('brand_platform_subscription','brand_platform_invoice');
    IF n <> 2 THEN RAISE EXCEPTION 'brand_platform_subscription: expected 2 tables, found %', n; END IF;
    RAISE NOTICE 'brand_platform_subscription verification passed: subscription + invoice tables ready (RLS + grants).';
END
$verify$;

COMMIT;

SELECT 'phase4_brand_platform_subscription.sql applied successfully.' AS result;
