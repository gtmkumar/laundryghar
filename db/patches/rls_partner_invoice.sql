-- =============================================================================
-- db/patches/rls_partner_invoice.sql
--
-- PURPOSE (RaaS full build, FULL-10 / issue #14): add the partner-scoped RLS layer for
-- commerce.partner_invoices, mirroring db/patches/rls_partner_wallet.sql. The table carries a
-- partner_id column, so isolation is uniform:
--   USING/WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
--
--   • commerce.partner_invoices — a partner session sees/writes only its own invoices.
--
-- kernel.current_partner_id() (app.current_partner_id session var, set by RlsConnectionInterceptor
-- from ICurrentTenant.PartnerId) is created by rls_partner.sql — run that FIRST (or it is recreated
-- here CREATE OR REPLACE so this patch is self-contained). Platform admin / worker / the anonymous
-- partner paylink webhook use rls_bypass.
--
-- Runtime enforcement requires the app to connect as app_user (non-owner): superusers / table owners
-- bypass RLS natively. See harden_app_user_and_rls_bypass.sql.
--
-- Idempotent / re-runnable (DROP POLICY IF EXISTS before CREATE).
--
-- RUN (manual, as postgres — AFTER raas_partner_invoice_schema.sql AND rls_partner.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/rls_partner_invoice.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- Safety net: the session-var reader is normally created by rls_partner.sql. Recreate it here
-- (CREATE OR REPLACE is idempotent) so this patch is self-contained regardless of apply order.
CREATE OR REPLACE FUNCTION kernel.current_partner_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_partner_id', true), '')::uuid $$;

-- 1. ── rls_partner policy ─────────────────────────────────────────────────────
DROP POLICY IF EXISTS rls_partner ON commerce.partner_invoices;
CREATE POLICY rls_partner ON commerce.partner_invoices FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
    WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

-- 2. ── Activate RLS ────────────────────────────────────────────────────────────
ALTER TABLE commerce.partner_invoices ENABLE ROW LEVEL SECURITY;

-- 3. ── Sanity check ────────────────────────────────────────────────────────────
SELECT tablename, count(*) AS rls_partner_policies
FROM   pg_policies
WHERE  schemaname = 'commerce'
  AND  policyname = 'rls_partner'
  AND  tablename = 'partner_invoices'
GROUP  BY tablename;

SELECT 'rls_partner_invoice.sql applied successfully.' AS result;
