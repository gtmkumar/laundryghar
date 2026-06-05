-- ============================================================================
-- fix_legacy_finance_royalty_rls_policies.sql  — BC-7 DEF-002 audit
-- ----------------------------------------------------------------------------
-- Checked 2026-06-05: zero legacy raw-cast *_tenant policies exist in the
-- finance_royalty schema.  All 8 tables carry only the rls_proposal.sql
-- kernel-helper policy (rls_brand, targeting app_user), which uses the
-- NULL-safe kernel.current_brand_id() helper — not a raw
-- current_setting(…)::uuid cast.  No DEF-002-class fixes are required.
--
-- This file is retained as an explicit record that the audit was performed
-- and the result was clean.  It is idempotent (runs the assertion query only).
-- ============================================================================
SET client_min_messages = WARNING;

-- Assertion: confirm no *_tenant raw-cast policies remain.
DO $$
DECLARE n integer;
BEGIN
    SELECT count(*) INTO n
    FROM   pg_policies
    WHERE  schemaname  = 'finance_royalty'
      AND  policyname  LIKE '%_tenant%';

    IF n > 0 THEN
        RAISE EXCEPTION
            'UNEXPECTED: found % legacy *_tenant policy/policies in '
            'finance_royalty — investigate before proceeding', n;
    END IF;

    RAISE NOTICE 'finance_royalty DEF-002 audit CLEAN: 0 legacy *_tenant policies found.';
END $$;

-- Informational: list all policies currently on finance_royalty.
SELECT tablename, policyname, roles, cmd
FROM   pg_policies
WHERE  schemaname = 'finance_royalty'
ORDER  BY tablename, policyname;
