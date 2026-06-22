-- =============================================================================
-- pricing_change_log.sql  (pricing audit trail + revert — Items/Pricing redesign Phase 3)
--   Records every pricing change (fabric multiplier, price-list item rate) with a
--   before/after snapshot, actor, and human summary — for the Change history tab and
--   one-click Revert. Brand-scoped (RLS). Immutable except the reverted_at/by stamp.
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/pricing_change_log.sql
-- =============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS customer_catalog.pricing_change_log (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id     UUID NOT NULL,
    target_kind  VARCHAR(40) NOT NULL CHECK (target_kind IN ('fabric_type','price_list_item','add_on')),
    target_id    UUID NOT NULL,
    summary      TEXT NOT NULL,
    before_json  JSONB,
    after_json   JSONB,
    actor_id     UUID,
    actor_name   VARCHAR(200),
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    reverted_at  TIMESTAMPTZ,
    reverted_by  UUID
);
CREATE INDEX IF NOT EXISTS ix_pricing_change_log_brand ON customer_catalog.pricing_change_log (brand_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pricing_change_log_target ON customer_catalog.pricing_change_log (target_kind, target_id);

ALTER TABLE customer_catalog.pricing_change_log ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON customer_catalog.pricing_change_log;
CREATE POLICY rls_brand ON customer_catalog.pricing_change_log
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON customer_catalog.pricing_change_log TO app_user, app_admin;

COMMIT;

SELECT 'pricing_change_log' AS table,
       (SELECT count(*) FROM information_schema.tables WHERE table_schema='customer_catalog' AND table_name='pricing_change_log') AS present;
