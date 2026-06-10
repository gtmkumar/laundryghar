-- db/patches/subscriptions_rls_fix.sql
-- Fix DEFECT-CLOSING-001: subscriptions tables used an unprotected
-- `current_setting('app.current_brand_id', true)::uuid` cast that throws
-- ERROR 22P02 when the setting is '' (platform-admin tokens and the Worker).
--
-- Root cause: ADR-010 policies inlined the bypass + brand checks instead of
-- delegating to kernel.rls_bypass() and kernel.current_brand_id(), which
-- already wraps the cast in NULLIF(..., '').
--
-- House pattern (every other table):
--   kernel.rls_bypass() OR brand_id = kernel.current_brand_id()
--
-- This patch DROPs and recreates all 6 affected policies to match that pattern.
-- Idempotent: DROP IF EXISTS is safe whether or not the old policy exists.
-- Affects: subscription_plans, payment_mandates, customer_subscriptions,
--          subscription_invoices (commerce schema) and franchise_subscriptions,
--          franchise_subscription_invoices (finance_royalty schema).
-- subscription_billing_attempts, subscription_usage_ledger, platform_plans,
-- franchise_subscription_events intentionally have no RLS — unchanged.

-- ── commerce.subscription_plans ───────────────────────────────────────────────
DROP POLICY IF EXISTS subplan_tenant ON commerce.subscription_plans;
CREATE POLICY subplan_tenant ON commerce.subscription_plans
    AS PERMISSIVE FOR ALL TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- ── commerce.payment_mandates ─────────────────────────────────────────────────
DROP POLICY IF EXISTS mandate_tenant ON commerce.payment_mandates;
CREATE POLICY mandate_tenant ON commerce.payment_mandates
    AS PERMISSIVE FOR ALL TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- ── commerce.customer_subscriptions ──────────────────────────────────────────
DROP POLICY IF EXISTS custsub_tenant ON commerce.customer_subscriptions;
CREATE POLICY custsub_tenant ON commerce.customer_subscriptions
    AS PERMISSIVE FOR ALL TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- ── commerce.subscription_invoices ───────────────────────────────────────────
DROP POLICY IF EXISTS subinv_tenant ON commerce.subscription_invoices;
CREATE POLICY subinv_tenant ON commerce.subscription_invoices
    AS PERMISSIVE FOR ALL TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- ── finance_royalty.franchise_subscriptions ───────────────────────────────────
DROP POLICY IF EXISTS fransub_tenant ON finance_royalty.franchise_subscriptions;
CREATE POLICY fransub_tenant ON finance_royalty.franchise_subscriptions
    AS PERMISSIVE FOR ALL TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- ── finance_royalty.franchise_subscription_invoices ───────────────────────────
DROP POLICY IF EXISTS fransubinv_tenant ON finance_royalty.franchise_subscription_invoices;
CREATE POLICY fransubinv_tenant ON finance_royalty.franchise_subscription_invoices
    AS PERMISSIVE FOR ALL TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
