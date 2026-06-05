-- ============================================================================
-- rls_enable_customer_catalog.sql
-- ----------------------------------------------------------------------------
-- BC-3 (customer_catalog) RLS gap-close. Enables brand-scoped Row-Level
-- Security on the 9 customer_catalog tables that currently have rowsecurity=f,
-- and (re)creates a uniform `rls_brand` policy on each — mirroring exactly the
-- `rls_brand` pattern and kernel helpers defined in rls_proposal.sql:
--
--   USING/WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
--
-- where:
--   kernel.current_brand_id() = NULLIF(current_setting('app.current_brand_id',true),'')::uuid
--                               (NULLIF-safe: empty string -> NULL, never throws)
--   kernel.rls_bypass()       = COALESCE(current_setting('app.bypass_rls',true),'off')='on'
--
-- All 14 customer_catalog tables have a brand_id column (verified), so plain
-- brand-scoping is valid for every table. Per the BC-3 directive, customer-self
-- filtering is handled at the application layer, NOT in RLS — so we use the
-- brand-only `rls_brand` pattern uniformly (the redundant `rls_brand_or_customer`
-- policies from rls_proposal.sql are removed by
-- fix_legacy_customer_catalog_rls_policies.sql).
--
-- Idempotent: DROP POLICY IF EXISTS before CREATE; ENABLE ROW LEVEL SECURITY is
-- a no-op when already enabled.
-- ============================================================================

SET client_min_messages = WARNING;

DO $enable$
DECLARE
    tbl text;
BEGIN
    FOREACH tbl IN ARRAY ARRAY[
        'customer_addresses',
        'customer_devices',
        'dpdp_consents',
        'account_deletion_requests',
        'fabric_types',
        'item_groups',
        'item_variants',
        'price_list_items',
        'add_ons',
        -- already rowsecurity=t, but currently lacks a uniform `rls_brand`
        -- policy (only had rls_brand_or_customer + legacy customers_tenant).
        -- Adding rls_brand here makes the legacy/redundant policy drop in
        -- fix_legacy_customer_catalog_rls_policies.sql safe to run.
        'customers'
    ]
    LOOP
        -- (Re)create the uniform brand-only policy.
        EXECUTE format('DROP POLICY IF EXISTS rls_brand ON customer_catalog.%I', tbl);
        EXECUTE format(
            'CREATE POLICY rls_brand ON customer_catalog.%I FOR ALL TO app_user '
            'USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id()) '
            'WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())',
            tbl);
        -- Activate RLS.
        EXECUTE format('ALTER TABLE customer_catalog.%I ENABLE ROW LEVEL SECURITY', tbl);
        RAISE NOTICE 'RLS enabled + rls_brand (re)created on customer_catalog.%', tbl;
    END LOOP;
END
$enable$;

-- Post-condition: rowsecurity for all 14 customer_catalog tables.
SELECT tablename, rowsecurity
FROM   pg_tables
WHERE  schemaname='customer_catalog'
ORDER  BY tablename;
