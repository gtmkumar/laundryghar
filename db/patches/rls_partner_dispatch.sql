-- =============================================================================
-- db/patches/rls_partner_dispatch.sql
--
-- PURPOSE (RaaS full build, FULL-11b / issue #14): add the COMBINED partner-or-brand
-- RLS layer for logistics.partner_dispatches, giving a partner-booking dispatch DUAL
-- visibility:
--   • the OWNING PARTNER (partner session: app.current_partner_id set, no brand) can
--     TRACK its own dispatch, and
--   • the SERVING BRAND's fleet staff (brand session: app.current_brand_id set, no
--     partner) can DISPATCH/manage the dispatches their fleet serves.
--
-- The policy is a single COMBINED predicate — bypass OR partner-arm OR brand-arm:
--     kernel.rls_bypass()
--       OR partner_id = kernel.current_partner_id()
--       OR (brand_id IS NOT NULL AND brand_id = kernel.current_brand_id())
-- The brand arm is guarded by brand_id IS NOT NULL so a NULL brand_id can never match a
-- brand session whose current_brand_id() also happens to be NULL.
--
-- Reuses the existing session-var readers kernel.current_partner_id() (rls_partner.sql) and
-- kernel.current_brand_id() (brand RLS) — no new functions. ENABLE ROW LEVEL SECURITY is applied
-- here (this table is locked down immediately, exactly like rls_partner.sql).
--
-- Runtime enforcement requires the app to connect as app_user (non-owner): superusers / table
-- owners bypass RLS natively. See harden_app_user_and_rls_bypass.sql.
--
-- Idempotent / re-runnable (DROP POLICY IF EXISTS before CREATE).
--
-- RUN (manual, as postgres — AFTER raas_partner_dispatch_schema.sql + rls_partner.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/rls_partner_dispatch.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- 1. ── Combined partner-or-brand policy ─────────────────────────────────────
DROP POLICY IF EXISTS rls_partner_or_brand ON logistics.partner_dispatches;
CREATE POLICY rls_partner_or_brand ON logistics.partner_dispatches FOR ALL TO app_user
    USING      (kernel.rls_bypass()
                OR partner_id = kernel.current_partner_id()
                OR (brand_id IS NOT NULL AND brand_id = kernel.current_brand_id()))
    WITH CHECK (kernel.rls_bypass()
                OR partner_id = kernel.current_partner_id()
                OR (brand_id IS NOT NULL AND brand_id = kernel.current_brand_id()));

-- 2. ── Activate RLS ─────────────────────────────────────────────────────────
ALTER TABLE logistics.partner_dispatches ENABLE ROW LEVEL SECURITY;

-- 3. ── Sanity check ─────────────────────────────────────────────────────────
SELECT tablename, policyname
FROM   pg_policies
WHERE  schemaname = 'logistics'
  AND  tablename  = 'partner_dispatches'
ORDER  BY policyname;

SELECT 'rls_partner_dispatch.sql applied successfully.' AS result;
