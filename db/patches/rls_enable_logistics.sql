-- ===========================================================================
-- rls_enable_logistics.sql  —  Enable RLS on BC-5 logistics tables
-- ---------------------------------------------------------------------------
-- Bounded context:  BC-5 logistics
-- Tables:           riders, rider_assignments, rider_capacity_config,
--                   rider_location_pings (PARTITIONED PARENT — propagates
--                   automatically to all partition children)
--
-- This file is IDEMPOTENT.
--
-- Pre-condition: rls_proposal.sql must have been applied first.
--   All four rls_brand policies are created by rls_proposal.sql §3 B1.
--   This file only ENABLES row security and then verifies the result.
--
-- Policy mirror (exact match with rls_proposal.sql):
--   DROP POLICY IF EXISTS rls_brand ON <table>;
--   CREATE POLICY rls_brand ON <table> FOR ALL TO app_user
--       USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
--       WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
--
-- For rider_location_pings: ALTER TABLE targets the partitioned PARENT only.
--   PostgreSQL propagates the RLS flag to existing and future partition children.
--
-- Apply order:  after fk_patch_05_logistics.sql and rls_proposal.sql
-- ===========================================================================

SET client_min_messages = WARNING;

-- ---------------------------------------------------------------------------
-- Step 1: Ensure rls_brand policies exist (idempotent re-create)
--   Mirrors rls_proposal.sql §3 B1 exactly so this file is self-contained
--   and safe to re-run even if rls_proposal.sql was partially applied.
-- ---------------------------------------------------------------------------
DO $policies$
DECLARE
    r RECORD;
BEGIN
    FOR r IN
        SELECT * FROM (VALUES
            ('logistics', 'riders'),
            ('logistics', 'rider_assignments'),
            ('logistics', 'rider_capacity_config'),
            ('logistics', 'rider_location_pings')
        ) AS v(schema_, tbl_)
    LOOP
        EXECUTE format(
            'DROP POLICY IF EXISTS rls_brand ON %I.%I',
            r.schema_, r.tbl_);
        EXECUTE format(
            'CREATE POLICY rls_brand ON %I.%I FOR ALL TO app_user '
            'USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id()) '
            'WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())',
            r.schema_, r.tbl_);
    END LOOP;
END
$policies$;

-- ---------------------------------------------------------------------------
-- Step 2: Enable RLS on all four logistics tables
--   rider_location_pings is the partitioned parent — enabling on the parent
--   propagates automatically to all existing and future partition children.
--   The three regular tables need FORCE ROW LEVEL SECURITY only when the
--   table owner (postgres/superuser) should also be filtered; we do NOT set
--   FORCE here because superuser bypass is the intended admin escape hatch.
-- ---------------------------------------------------------------------------
ALTER TABLE logistics.riders                ENABLE ROW LEVEL SECURITY;
ALTER TABLE logistics.rider_assignments     ENABLE ROW LEVEL SECURITY;
ALTER TABLE logistics.rider_capacity_config ENABLE ROW LEVEL SECURITY;
ALTER TABLE logistics.rider_location_pings  ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------
-- Step 3: Verify — report rowsecurity flag for parent tables
-- ---------------------------------------------------------------------------
SELECT
    n.nspname                                   AS schema,
    c.relname                                   AS table_name,
    c.relrowsecurity                            AS rowsecurity,
    CASE WHEN c.relkind = 'p' THEN 'partitioned parent'
         ELSE 'regular' END                     AS table_kind,
    (SELECT COUNT(*)
     FROM pg_policy p
     WHERE p.polrelid = c.oid
       AND p.polname  = 'rls_brand')            AS rls_brand_policy_count
FROM   pg_class c
JOIN   pg_namespace n ON n.oid = c.relnamespace
WHERE  n.nspname = 'logistics'
  AND  c.relkind IN ('r','p')
  AND  NOT c.relispartition
ORDER  BY c.relname;
