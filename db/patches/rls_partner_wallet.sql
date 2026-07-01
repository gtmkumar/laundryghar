-- =============================================================================
-- db/patches/rls_partner_wallet.sql
--
-- PURPOSE (RaaS full build, FULL-9 / issue #14): add the partner-scoped RLS layer for the
-- commerce partner wallet tables, mirroring db/patches/rls_partner.sql (the logistics
-- partner_* tables). Both new tables carry a partner_id column, so isolation is uniform:
--   USING/WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
--
--   • commerce.partner_wallet_accounts     — a partner session sees/writes only its own wallet.
--   • commerce.partner_wallet_transactions — a partner session sees/appends only its own ledger.
--
-- kernel.current_partner_id() (app.current_partner_id session var, set by RlsConnectionInterceptor
-- from ICurrentTenant.PartnerId) is created by rls_partner.sql — run that FIRST (or it is created
-- there; this patch assumes it exists). Platform admin / worker use rls_bypass.
--
-- Runtime enforcement requires the app to connect as app_user (non-owner): superusers / table
-- owners bypass RLS natively. See harden_app_user_and_rls_bypass.sql.
--
-- Idempotent / re-runnable (DROP POLICY IF EXISTS before CREATE).
--
-- RUN (manual, as postgres — AFTER raas_partner_wallet_schema.sql AND rls_partner.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/rls_partner_wallet.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- Safety net: the session-var reader is normally created by rls_partner.sql. Recreate it here
-- (CREATE OR REPLACE is idempotent) so this patch is self-contained regardless of apply order.
CREATE OR REPLACE FUNCTION kernel.current_partner_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_partner_id', true), '')::uuid $$;

-- 1. ── rls_partner policies ──────────────────────────────────────────────────

-- partner_wallet_accounts — isolate by partner_id.
DROP POLICY IF EXISTS rls_partner ON commerce.partner_wallet_accounts;
CREATE POLICY rls_partner ON commerce.partner_wallet_accounts FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
    WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

-- partner_wallet_transactions — isolate by partner_id.
DROP POLICY IF EXISTS rls_partner ON commerce.partner_wallet_transactions;
CREATE POLICY rls_partner ON commerce.partner_wallet_transactions FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
    WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

-- 2. ── Activate RLS ──────────────────────────────────────────────────────────
ALTER TABLE commerce.partner_wallet_accounts     ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerce.partner_wallet_transactions ENABLE ROW LEVEL SECURITY;

-- 3. ── Sanity check ──────────────────────────────────────────────────────────
SELECT tablename, count(*) AS rls_partner_policies
FROM   pg_policies
WHERE  schemaname = 'commerce'
  AND  policyname = 'rls_partner'
  AND  tablename IN ('partner_wallet_accounts','partner_wallet_transactions')
GROUP  BY tablename
ORDER  BY tablename;

SELECT 'rls_partner_wallet.sql applied successfully.' AS result;
