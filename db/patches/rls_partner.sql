-- =============================================================================
-- db/patches/rls_partner.sql
--
-- PURPOSE (RaaS partner MVP, issue #14): add the partner-scoped RLS layer that
-- mirrors the existing brand RLS (kernel.current_brand_id() + rls_brand):
--   1. kernel.current_partner_id() — reads the app.current_partner_id session var
--      the RlsConnectionInterceptor already sets from ICurrentTenant.PartnerId.
--   2. rls_partner policies on the three logistics.partner_* tables.
--   3. ENABLE ROW LEVEL SECURITY on all three (unlike rls_proposal.sql, which
--      leaves policies inert — this MVP is locked down immediately).
--
-- ISOLATION MODEL
--   partner_users, partner_bookings — keyed by their partner_id column: a partner
--     session sees only rows for its own partner; platform admin uses rls_bypass.
--   partners — the isolation ROOT. Chosen model: a partner session may read/write
--     ONLY its own row (id = current partner); provisioning a NEW partner (and its
--     first partner_admin) is a platform-admin operation that must run under
--     app.bypass_rls = true, exactly like the admin-only brands/platforms tables.
--
-- Runtime enforcement requires the app to connect as app_user (non-owner):
-- superusers / table owners bypass RLS natively. See harden_app_user_and_rls_bypass.sql.
--
-- Idempotent / re-runnable (DROP POLICY IF EXISTS before CREATE; CREATE OR REPLACE fn).
--
-- RUN (manual, as postgres — AFTER raas_partner_schema.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/rls_partner.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- 1. ── Session-var reader: app.current_partner_id → uuid (NULL-safe, STABLE) ──
CREATE OR REPLACE FUNCTION kernel.current_partner_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_partner_id', true), '')::uuid $$;

DO $grants$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['app_user','app_admin'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('GRANT EXECUTE ON FUNCTION kernel.current_partner_id() TO %I', r);
        END IF;
    END LOOP;
END
$grants$;

-- 2. ── rls_partner policies ──────────────────────────────────────────────────

-- partner_bookings — isolate by partner_id
DROP POLICY IF EXISTS rls_partner ON logistics.partner_bookings;
CREATE POLICY rls_partner ON logistics.partner_bookings FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
    WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

-- partner_users — isolate by partner_id
DROP POLICY IF EXISTS rls_partner ON logistics.partner_users;
CREATE POLICY rls_partner ON logistics.partner_users FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
    WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

-- partners — isolation ROOT: match on id (a partner sees only its own org row).
-- New-partner provisioning runs under rls_bypass (platform-admin path).
DROP POLICY IF EXISTS rls_partner ON logistics.partners;
CREATE POLICY rls_partner ON logistics.partners FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR id = kernel.current_partner_id())
    WITH CHECK (kernel.rls_bypass() OR id = kernel.current_partner_id());

-- 3. ── Activate RLS ──────────────────────────────────────────────────────────
ALTER TABLE logistics.partners         ENABLE ROW LEVEL SECURITY;
ALTER TABLE logistics.partner_users    ENABLE ROW LEVEL SECURITY;
ALTER TABLE logistics.partner_bookings ENABLE ROW LEVEL SECURITY;

-- 4. ── Sanity check ──────────────────────────────────────────────────────────
SELECT tablename, count(*) AS rls_partner_policies
FROM   pg_policies
WHERE  schemaname = 'logistics'
  AND  policyname = 'rls_partner'
GROUP  BY tablename
ORDER  BY tablename;

SELECT 'rls_partner.sql applied successfully.' AS result;
