-- ===========================================================================
-- rls_enable_engagement_cms.sql
-- BC-8 engagement_cms — Enable RLS on all brand-scoped logical tables
-- ---------------------------------------------------------------------------
-- Pre-conditions (verified before writing):
--   • All 8 logical tables have brand_id (has_brand=t)
--   • All 8 already have a kernel-helper policy (rls_brand or
--     rls_brand_or_customer) created by rls_proposal.sql
--   • None are currently enabled (relrowsecurity=f)
--   • notifications_log is PARTITIONED — enabling on the parent propagates
--     to all 8 existing partition children and future ones automatically
--
-- Safety guard: the DO block confirms a policy exists before enabling.
-- If no policy exists for a table, it is skipped with a NOTICE (not an error)
-- so the script never enables a naked table.
--
-- Idempotent: ALTER TABLE ENABLE ROW LEVEL SECURITY is a no-op if already on.
-- ===========================================================================

SET client_min_messages = WARNING;
SET search_path = engagement_cms, kernel, public;

DO $enable$
DECLARE
    r RECORD;
    pol_count INT;
BEGIN
    FOR r IN
        SELECT * FROM (VALUES
            ('engagement_cms','app_banners'),
            ('engagement_cms','mobile_app_config'),
            ('engagement_cms','notification_preferences'),
            ('engagement_cms','notification_templates'),
            ('engagement_cms','notifications_log'),        -- PARTITIONED; propagates to children
            ('engagement_cms','notifications_outbox'),
            ('engagement_cms','onboarding_slides'),
            ('engagement_cms','whatsapp_message_log')
        ) AS v(schm, tbl)
    LOOP
        -- Safety guard: never enable if no policy exists
        SELECT count(*) INTO pol_count
        FROM pg_policies
        WHERE schemaname = r.schm
          AND tablename  = r.tbl;

        IF pol_count = 0 THEN
            RAISE NOTICE 'SKIP % — no policies found; RLS NOT enabled', format('%I.%I', r.schm, r.tbl);
            CONTINUE;
        END IF;

        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.schm, r.tbl);
        RAISE NOTICE 'ENABLED RLS on %.% (% polic%)',
            r.schm, r.tbl, pol_count, CASE WHEN pol_count=1 THEN 'y' ELSE 'ies' END;
    END LOOP;
END
$enable$;

-- ---------------------------------------------------------------------------
-- Verification: confirm rowsecurity=t for all 8 logical tables
-- ---------------------------------------------------------------------------
SELECT c.relname                                           AS table_name,
       CASE c.relkind WHEN 'p' THEN 'PARTITIONED' ELSE 'regular' END AS kind,
       c.relrowsecurity                                    AS rls_enabled,
       (SELECT count(*) FROM pg_policies p
        WHERE p.schemaname='engagement_cms'
          AND p.tablename=c.relname)                       AS policy_count,
       (SELECT string_agg(DISTINCT policyname,', ' ORDER BY policyname)
        FROM pg_policies p
        WHERE p.schemaname='engagement_cms'
          AND p.tablename=c.relname)                       AS policies
FROM   pg_class c
JOIN   pg_namespace n ON n.oid=c.relnamespace
WHERE  n.nspname='engagement_cms'
  AND  c.relkind IN ('r','p')
  AND  c.relispartition=false
ORDER  BY c.relname;
