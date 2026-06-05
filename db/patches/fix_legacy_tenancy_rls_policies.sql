-- ============================================================================
-- fix_legacy_tenancy_rls_policies.sql
-- ----------------------------------------------------------------------------
-- DEF-002 FIX. Drops the 5 legacy `*_tenant` RLS policies on tenancy_org that
-- use a raw `(current_setting('app.current_brand_id', true))::uuid` cast.
--
-- THE BUG: that raw cast throws `invalid input syntax for type uuid: ""` when
-- the session var is empty. These legacy policies target role `public`, which
-- includes `app_user`, so the error surfaces for app_user queries even though
-- the newer `rls_brand` policy (NULLIF-safe via kernel.current_brand_id())
-- would have returned 0 rows cleanly. Because PostgreSQL OR-combines multiple
-- permissive policies for the same role, the throwing legacy branch is still
-- evaluated and aborts the query before the safe branch can short-circuit.
--
-- THE FIX: drop the 5 redundant legacy policies. The `rls_brand` policies from
-- rls_proposal.sql already provide correct brand isolation for app_user using
-- kernel.current_brand_id() (NULLIF-safe). We do NOT touch the rls_brand
-- policies.
--
-- SAFETY GUARD: before dropping each legacy policy, this script asserts that an
-- equivalent `rls_brand` policy still exists on the same table, so we never
-- remove the only remaining isolation policy. If the guard fails it raises an
-- exception and drops nothing further (each statement is its own; run-order is
-- guard-then-drop per table).
--
-- Idempotent: DROP POLICY IF EXISTS is a no-op when the policy is already gone;
-- the guard re-confirms rls_brand each run.
-- ============================================================================

SET client_min_messages = WARNING;

DO $fix$
DECLARE
    t        RECORD;
    has_safe BOOLEAN;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('franchises',           'franchises_tenant'),
            ('territories',          'territories_tenant'),
            ('franchise_agreements', 'franagree_tenant'),
            ('stores',               'stores_tenant'),
            ('warehouses',           'warehouses_tenant')
        ) AS v(tbl, legacy_policy)
    LOOP
        -- Guard: confirm an equivalent rls_brand policy exists before dropping.
        SELECT EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname = 'tenancy_org'
              AND tablename  = t.tbl
              AND policyname = 'rls_brand'
        ) INTO has_safe;

        IF NOT has_safe THEN
            RAISE EXCEPTION
                'ABORT: tenancy_org.% has no rls_brand policy; refusing to drop legacy policy % (would leave table without isolation)',
                t.tbl, t.legacy_policy;
        END IF;

        EXECUTE format(
            'DROP POLICY IF EXISTS %I ON tenancy_org.%I',
            t.legacy_policy, t.tbl);
        RAISE NOTICE 'Dropped legacy policy % on tenancy_org.% (rls_brand confirmed present)',
            t.legacy_policy, t.tbl;
    END LOOP;
END
$fix$;

-- Post-condition report: remaining policies on the 5 tables.
SELECT tablename, policyname, roles
FROM   pg_policies
WHERE  schemaname = 'tenancy_org'
  AND  tablename IN ('franchises','territories','franchise_agreements','stores','warehouses')
ORDER  BY tablename, policyname;
