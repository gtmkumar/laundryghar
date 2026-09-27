-- Down for 0027.
--
-- Note what this rollback does: it turns row-level security back OFF on nine tables that carry
-- brand_id. That is a widening, and it is only correct as a rollback of this specific migration —
-- it restores the state the database was in before, which was a state with a real cross-tenant
-- exposure on subscription billing, usage, franchise subscription events and OAuth codes. Do not
-- run it to "fix" an application problem; fix the application.

DELETE FROM authz.policy WHERE key LIKE 'scope.deny.%';

COMMENT ON COLUMN identity_access.permissions.requires_scope IS NULL;

DROP POLICY IF EXISTS rls_brand_or_platform ON kernel.system_settings;
DROP POLICY IF EXISTS rls_brand_or_platform ON kernel.feature_flags;
DROP POLICY IF EXISTS rls_brand_or_platform ON finance_royalty.platform_plans;

-- Restore the original (inert) kernel policies so the pre-0027 shape is reproduced exactly.
CREATE POLICY rls_brand ON kernel.system_settings FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
CREATE POLICY rls_brand ON kernel.feature_flags FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

DROP POLICY IF EXISTS rls_brand ON commerce.subscription_billing_attempts;
DROP POLICY IF EXISTS rls_brand ON commerce.subscription_usage_ledger;
DROP POLICY IF EXISTS rls_brand ON finance_royalty.franchise_subscription_events;
DROP POLICY IF EXISTS rls_brand ON identity_access.oauth_authorization_codes;

ALTER TABLE kernel.system_settings                        DISABLE ROW LEVEL SECURITY;
ALTER TABLE kernel.feature_flags                          DISABLE ROW LEVEL SECURITY;
ALTER TABLE kernel.file_attachments                       DISABLE ROW LEVEL SECURITY;
ALTER TABLE kernel.outbox_events                          DISABLE ROW LEVEL SECURITY;
ALTER TABLE finance_royalty.platform_plans                DISABLE ROW LEVEL SECURITY;
ALTER TABLE finance_royalty.franchise_subscription_events DISABLE ROW LEVEL SECURITY;
ALTER TABLE commerce.subscription_billing_attempts        DISABLE ROW LEVEL SECURITY;
ALTER TABLE commerce.subscription_usage_ledger            DISABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.oauth_authorization_codes     DISABLE ROW LEVEL SECURITY;
