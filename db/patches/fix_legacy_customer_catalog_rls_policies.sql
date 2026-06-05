-- ============================================================================
-- fix_legacy_customer_catalog_rls_policies.sql
-- ----------------------------------------------------------------------------
-- BC-3 (customer_catalog) DEF-002-class fix + policy uniformization.
--
-- PART A — DEF-002 legacy raw-cast policies (the bug):
--   5 legacy `*_tenant` policies (role `public`) on customer_catalog use the
--   raw `(current_setting('app.current_brand_id', true))::uuid` cast, which
--   throws `invalid input syntax for type uuid: ""` on an empty session var.
--   Since they apply to app_user too and PostgreSQL OR-combines permissive
--   policies, the throwing branch aborts the query before the safe `rls_brand`
--   branch can short-circuit. We drop them.
--     customers          -> customers_tenant
--     items              -> items_tenant
--     price_lists        -> pricelist_tenant
--     service_categories -> svccat_tenant
--     services           -> services_tenant
--
-- PART B — redundant rls_brand_or_customer policies (uniformization):
--   rls_proposal.sql also placed a `rls_brand_or_customer` policy on 5 tables.
--   Per the BC-3 directive, customer-self filtering is handled at the app layer,
--   not in RLS. We drop these so each table is left with a single, uniform
--   brand-only `rls_brand` policy (created by rls_enable_customer_catalog.sql).
--     account_deletion_requests, customer_addresses, customer_devices,
--     customers, dpdp_consents
--
-- SAFETY GUARD: before dropping ANY policy on a table, assert an equivalent
-- `rls_brand` policy exists on that table — never leave a table without
-- brand isolation. Raises and stops if the guard fails.
--
-- Idempotent: DROP POLICY IF EXISTS is a no-op when already gone; guard
-- re-confirms rls_brand each run.
-- ============================================================================

SET client_min_messages = WARNING;

DO $fix$
DECLARE
    t        RECORD;
    has_safe BOOLEAN;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            -- PART A: legacy raw-cast *_tenant policies
            ('customers',                 'customers_tenant'),
            ('items',                     'items_tenant'),
            ('price_lists',               'pricelist_tenant'),
            ('service_categories',        'svccat_tenant'),
            ('services',                  'services_tenant'),
            -- PART B: redundant brand_or_customer policies (app-layer handles self-filter)
            ('account_deletion_requests', 'rls_brand_or_customer'),
            ('customer_addresses',        'rls_brand_or_customer'),
            ('customer_devices',          'rls_brand_or_customer'),
            ('customers',                 'rls_brand_or_customer'),
            ('dpdp_consents',             'rls_brand_or_customer')
        ) AS v(tbl, doomed_policy)
    LOOP
        -- Guard: confirm an equivalent rls_brand policy exists before dropping.
        SELECT EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname='customer_catalog'
              AND tablename = t.tbl
              AND policyname = 'rls_brand'
        ) INTO has_safe;

        IF NOT has_safe THEN
            RAISE EXCEPTION
                'ABORT: customer_catalog.% has no rls_brand policy; refusing to drop % (would leave table without brand isolation)',
                t.tbl, t.doomed_policy;
        END IF;

        EXECUTE format(
            'DROP POLICY IF EXISTS %I ON customer_catalog.%I',
            t.doomed_policy, t.tbl);
        RAISE NOTICE 'Dropped policy % on customer_catalog.% (rls_brand confirmed present)',
            t.doomed_policy, t.tbl;
    END LOOP;
END
$fix$;

-- Post-condition report: remaining policies across customer_catalog.
SELECT tablename, policyname, roles
FROM   pg_policies
WHERE  schemaname='customer_catalog'
ORDER  BY tablename, policyname;
