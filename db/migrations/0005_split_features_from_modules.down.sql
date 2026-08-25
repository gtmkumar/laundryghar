-- 0005_split_features_from_modules — rollback: must exactly undo the .up.sql
--
-- Rebuilds the module-keyed entitlement tables from the feature-keyed ones and removes the
-- features catalogue.
--
-- ─── One asymmetry, stated plainly ───────────────────────────────────────────────────────────
-- The forward migration MERGED where several modules share a feature (analytics + report both map
-- to advanced_analytics). Rolling back FANS THAT BACK OUT: a brand that held only `analytics`
-- before will hold both `analytics` and `report` afterwards, because after the split those two are
-- indistinguishable — the information that separated them no longer exists.
--
-- That direction is deliberate. The alternative would be to guess which of the two to drop, and
-- guessing wrong revokes access an operator is using. A rollback that grants slightly too much is
-- recoverable in one click; one that silently revokes is a support ticket at best.
--
-- Entitlements bought against a module-less feature (custom_domain, api_access, white_label_app,
-- online_payments, wallet, loyalty, item_tracking, whatsapp_bot) have NO module to map back to and
-- are therefore LOST by this rollback — the old schema had nowhere to put them. Check first:
--   SELECT b.code, bf.feature_key FROM identity_access.brand_feature bf
--     JOIN tenancy_org.brands b ON b.id = bf.brand_id
--    WHERE bf.enabled
--      AND NOT EXISTS (SELECT 1 FROM identity_access.modules m WHERE m.feature_key = bf.feature_key);

-- ─── 1. Recreate the module-keyed entitlement table ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.brand_module (
    brand_id    UUID    NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    module_key  VARCHAR NOT NULL REFERENCES identity_access.modules(key) ON DELETE CASCADE,
    enabled     BOOLEAN NOT NULL DEFAULT true,
    valid_until DATE,
    source      VARCHAR NOT NULL DEFAULT 'manual' CHECK (source IN ('bundle','manual')),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by  UUID NULL,
    updated_by  UUID NULL,
    PRIMARY KEY (brand_id, module_key)
);
CREATE INDEX IF NOT EXISTS ix_brand_module_brand
    ON identity_access.brand_module (brand_id) WHERE enabled;

ALTER TABLE identity_access.brand_module ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON identity_access.brand_module;
CREATE POLICY rls_brand ON identity_access.brand_module
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.brand_module TO app_user, app_admin;

-- Fan every entitled feature back out to each module that pointed at it (see the note above).
INSERT INTO identity_access.brand_module
    (brand_id, module_key, enabled, valid_until, source, created_at, updated_at, created_by, updated_by)
SELECT bf.brand_id, m.key, bf.enabled, bf.valid_until, bf.source,
       bf.created_at, bf.updated_at, bf.created_by, bf.updated_by
FROM   identity_access.brand_feature bf
JOIN   identity_access.modules m ON m.feature_key = bf.feature_key
ON CONFLICT (brand_id, module_key) DO NOTHING;

-- ─── 2. Recreate the module-keyed bundle items ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.module_bundle_item (
    bundle_code VARCHAR NOT NULL REFERENCES identity_access.module_bundle(code) ON DELETE CASCADE,
    module_key  VARCHAR NOT NULL REFERENCES identity_access.modules(key) ON DELETE CASCADE,
    PRIMARY KEY (bundle_code, module_key)
);
GRANT SELECT ON identity_access.module_bundle_item TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.module_bundle_item TO app_admin;

INSERT INTO identity_access.module_bundle_item (bundle_code, module_key)
SELECT DISTINCT bfe.bundle_code, m.key
FROM   identity_access.bundle_feature bfe
JOIN   identity_access.modules m ON m.feature_key = bfe.feature_key
ON CONFLICT DO NOTHING;

-- ─── 3. Drop the feature-keyed side ──────────────────────────────────────────────────────────
-- feature_key first: it is the FK holding `features` in place.
ALTER TABLE identity_access.modules DROP COLUMN IF EXISTS feature_key;

DROP TABLE IF EXISTS identity_access.bundle_feature;
DROP TABLE IF EXISTS identity_access.brand_feature;
DROP TABLE IF EXISTS identity_access.features;
