-- ============================================================================
-- fix_legacy_order_lifecycle_rls_policies.sql
-- ----------------------------------------------------------------------------
-- BC-4 (order_lifecycle) DEF-002-class fix.
--
-- THE BUG: legacy `*_tenant` policies (role `public`) use the raw
-- `(current_setting('app.current_brand_id', true))::uuid` cast, which throws
-- `invalid input syntax for type uuid: ""` when the session var is empty.
-- Because they apply to app_user too and PostgreSQL OR-combines permissive
-- policies, the throwing branch aborts the query before the safe `rls_brand`
-- branch (kernel.current_brand_id(), NULLIF-safe) can short-circuit.
--
-- order_lifecycle currently has exactly ONE such legacy policy:
--   garments -> garments_tenant
-- (The other 19 tables were created RLS-ready by rls_proposal.sql with only the
-- kernel-helper rls_brand policy.)
--
-- THE FIX: drop garments_tenant. SAFETY GUARD: assert an equivalent kernel-helper
-- `rls_brand` policy exists on the table first; never leave a table without
-- brand isolation. Raises and stops if the guard fails.
--
-- NOTE on order_addons: it has NO brand_id column and therefore no rls_brand
-- policy and RLS remains OFF (see rls_enable_order_lifecycle.sql header). It is
-- intentionally NOT touched here.
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
            ('garments', 'garments_tenant')
        ) AS v(tbl, legacy_policy)
    LOOP
        SELECT EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname='order_lifecycle'
              AND tablename = t.tbl
              AND policyname = 'rls_brand'
        ) INTO has_safe;

        IF NOT has_safe THEN
            RAISE EXCEPTION
                'ABORT: order_lifecycle.% has no rls_brand policy; refusing to drop legacy policy % (would leave table without isolation)',
                t.tbl, t.legacy_policy;
        END IF;

        EXECUTE format(
            'DROP POLICY IF EXISTS %I ON order_lifecycle.%I',
            t.legacy_policy, t.tbl);
        RAISE NOTICE 'Dropped legacy policy % on order_lifecycle.% (rls_brand confirmed present)',
            t.legacy_policy, t.tbl;
    END LOOP;
END
$fix$;

-- Post-condition report: any remaining legacy raw-cast policies in the schema?
SELECT tablename, policyname, roles, qual AS using_expr
FROM   pg_policies
WHERE  schemaname='order_lifecycle'
  AND  qual LIKE '%current_setting(''app.current_brand_id''%::uuid%'
ORDER  BY tablename, policyname;
