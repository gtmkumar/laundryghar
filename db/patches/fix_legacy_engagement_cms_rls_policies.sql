-- ===========================================================================
-- fix_legacy_engagement_cms_rls_policies.sql
-- BC-8 engagement_cms — Legacy *_tenant policy remediation
-- ---------------------------------------------------------------------------
-- Status: NO LEGACY *_TENANT POLICIES FOUND in engagement_cms schema.
--
-- Verified (2026-06-05) with:
--   SELECT schemaname, tablename, policyname FROM pg_policies
--   WHERE schemaname='engagement_cms' AND policyname LIKE '%_tenant%';
--   → 0 rows
--
-- All 8 logical tables carry kernel-helper policies exclusively
-- (rls_brand or rls_brand_or_customer), created by rls_proposal.sql.
-- No DEF-002 raw-cast tenant policies exist; no remediation needed.
--
-- This file is a confirmed no-op placeholder kept for audit continuity
-- with the BC-1..BC-7 pattern.
-- ===========================================================================

-- Confirm no legacy policies exist (idempotent check; raises NOTICE if any slip in)
DO $check_legacy$
DECLARE
    cnt INT;
BEGIN
    SELECT count(*) INTO cnt
    FROM pg_policies
    WHERE schemaname='engagement_cms'
      AND policyname LIKE '%_tenant%';

    IF cnt > 0 THEN
        RAISE WARNING 'Unexpected: % legacy *_tenant polic% found in engagement_cms — investigate!',
            cnt, CASE WHEN cnt=1 THEN 'y' ELSE 'ies' END;
    ELSE
        RAISE NOTICE 'Confirmed: 0 legacy *_tenant policies in engagement_cms — nothing to fix.';
    END IF;
END
$check_legacy$;
