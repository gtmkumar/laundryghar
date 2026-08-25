-- 0005_split_features_from_modules — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- OQ-3 answered by Goutam 2026-08-25: "split features and modules".
--
-- ─── The problem this fixes ──────────────────────────────────────────────────────────────────
-- `identity_access.modules` was doing two unrelated jobs at once: it was the SIDEBAR (label, icon,
-- route, section, nav_order) and simultaneously the SELLABLE CATALOGUE that `brand_module` licensed
-- and `module_bundle_item` packaged into plans. That conflation is exactly why 8 of the 17 features
-- PLATFORM_STRATEGY.md §5 says we sell had nowhere to exist: `custom_domain`, `white_label_app`,
-- `api_access`, `online_payments`, `wallet`, `loyalty`, `item_tracking` and `whatsapp_bot` are all
-- things a provider BUYS but none of them is a menu entry, so none of them could be modelled.
--
-- ─── The split ───────────────────────────────────────────────────────────────────────────────
--   identity_access.features        the sellable catalogue — what a plan grants and a brand owns
--   identity_access.brand_feature   per-brand entitlement          (was brand_module)
--   identity_access.bundle_feature  plan -> features               (was module_bundle_item)
--   identity_access.modules.feature_key   the navigation entry now POINTS AT the feature that
--                                         must be owned for it to appear. Many modules may share
--                                         one feature; a feature may have no module at all.
--
-- ─── Preserving every existing entitlement ───────────────────────────────────────────────────
-- The mapping rule is deliberately conservative, because losing an entitlement silently would lock
-- a real operator out of their own console:
--   * 3 core modules (dashboard, users, settings) get feature_key = NULL and stay always-on.
--   * 10 modules whose meaning is named by a §5 feature adopt that key (orders->bookings,
--     riders->fleet, warehouse->processing_facility, …). `analytics` and `report` deliberately
--     SHARE `advanced_analytics` — the first many-to-one, which is the point of the split.
--   * every other module gets a feature of the SAME KEY, so it is impossible for a module to end
--     up unlicensed by accident.
--   * the 8 module-less §5 features are created too, sellable with no menu.
-- brand_feature is then backfilled through that map, and the migration ASSERTS per brand that the
-- module count it can reach afterwards is not lower than before. See the verification block at the
-- end — it aborts the whole transaction rather than let a brand quietly lose access.

-- ─── 1. The sellable catalogue ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.features (
    key          VARCHAR(64) PRIMARY KEY,
    name         VARCHAR(128) NOT NULL,
    description  TEXT,
    -- A feature may be specific to one vertical (fabrics is laundry-only, scheduling is salon's
    -- appointment book). NULL = available to every vertical.
    vertical_key VARCHAR(20)
                 CHECK (vertical_key IS NULL
                        OR vertical_key::text = ANY (ARRAY['laundry','salon','logistics','tiffin']::text[])),
    -- Always-on: a brand can never "unbuy" it and lock its own admins out. Mirrors modules.is_core.
    is_core      BOOLEAN NOT NULL DEFAULT false,
    -- Is this on the price list at all? A feature that merely groups modules is not sold on its own.
    is_sellable  BOOLEAN NOT NULL DEFAULT true,
    status       VARCHAR(32) NOT NULL DEFAULT 'active',
    sort_order   INTEGER NOT NULL DEFAULT 100,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Global catalogue, like `modules` and `permissions`: readable by all, writable by admin. No RLS.
GRANT SELECT ON identity_access.features TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.features TO app_admin;

COMMENT ON TABLE identity_access.features IS
    'The sellable catalogue (PLATFORM_STRATEGY.md §5). What a plan grants and a brand owns. '
    'Distinct from identity_access.modules, which is the navigation catalogue — a feature may have '
    'many modules, or none at all (custom_domain, api_access, white_label_app).';

-- ─── 2. The 17 features PLATFORM_STRATEGY.md §5 actually sells ───────────────────────────────
INSERT INTO identity_access.features (key, name, description, vertical_key, sort_order) VALUES
    ('bookings',               'Bookings',               'Take and manage customer bookings.',                     NULL, 10),
    ('scheduling',             'Scheduling',             'Time-slot and appointment scheduling with capacity.',    NULL, 20),
    ('fleet',                  'Fleet',                  'Riders, dispatch, GPS tracking, proof of delivery.',     NULL, 30),
    ('item_tracking',          'Item tracking',          'Per-item tagging and traceability through processing.',  NULL, 40),
    ('processing_facility',    'Processing facility',    'On-site processing: batches, stages, QC.',               NULL, 50),
    ('online_payments',        'Online payments',        'Take payment online through a payment gateway.',         NULL, 60),
    ('wallet',                 'Wallet',                 'Customer wallet balance and top-ups.',                   NULL, 70),
    ('loyalty',                'Loyalty',                'Points, tiers and rewards.',                             NULL, 80),
    ('coupons',                'Coupons',                'Discount codes and redemption rules.',                   NULL, 90),
    ('customer_subscriptions', 'Customer subscriptions', 'Recurring plans sold to end customers.',                 NULL, 100),
    ('raas_partner',           'Rider-as-a-Service',     'Serve external partners'' delivery jobs.',               NULL, 110),
    ('whatsapp_bot',           'WhatsApp bot',           'Conversational booking and notifications on WhatsApp.',  NULL, 120),
    ('multi_location',         'Multiple locations',     'Run more than one location.',                            NULL, 130),
    ('advanced_analytics',     'Advanced analytics',     'Dashboards, cohorts and exportable reporting.',           NULL, 140),
    ('api_access',             'API access',             'Programmatic access for the provider''s own systems.',   NULL, 150),
    ('custom_domain',          'Custom domain',          'Serve the business on its own web address.',             NULL, 160),
    ('white_label_app',        'White-label app',        'The provider''s own branded mobile app.',                NULL, 170)
ON CONFLICT (key) DO NOTHING;

-- ─── 3. A feature for every remaining module, so nothing can be orphaned ─────────────────────
-- Same key as the module; inherits its vertical and core flag. These are the ones §5 does not name.
-- is_sellable=false: they exist so entitlement is total, not because they are on the price list.
INSERT INTO identity_access.features (key, name, description, vertical_key, is_core, is_sellable, sort_order, status)
SELECT m.key,
       m.label,
       'Auto-created from the ' || m.label || ' module when features were split from modules (0005).',
       m.vertical_key,
       m.is_core,
       false,
       200 + m.nav_order,
       m.status
FROM   identity_access.modules m
WHERE  NOT m.is_core
  AND  m.key NOT IN ('orders','appointments','riders','warehouse','coupons',
                     'subscriptions','partner_booking','analytics','report','stores')
ON CONFLICT (key) DO NOTHING;

-- ─── 4. Point each navigation module at the feature that gates it ────────────────────────────
ALTER TABLE identity_access.modules
    ADD COLUMN IF NOT EXISTS feature_key VARCHAR(64) REFERENCES identity_access.features(key);

-- 4a. The ten whose meaning is named by a §5 feature. `analytics` and `report` BOTH map to
--     advanced_analytics — the many-modules-to-one-feature case the split exists to allow.
UPDATE identity_access.modules m SET feature_key = v.feature
FROM (VALUES
    ('orders',          'bookings'),
    ('appointments',    'scheduling'),
    ('riders',          'fleet'),
    ('warehouse',       'processing_facility'),
    ('coupons',         'coupons'),
    ('subscriptions',   'customer_subscriptions'),
    ('partner_booking', 'raas_partner'),
    ('analytics',       'advanced_analytics'),
    ('report',          'advanced_analytics'),
    ('stores',          'multi_location')
) AS v(module_key, feature)
WHERE m.key = v.module_key;

-- 4b. Everything else non-core points at its same-named feature. Core modules stay NULL = always on.
UPDATE identity_access.modules m SET feature_key = m.key
WHERE  m.feature_key IS NULL AND NOT m.is_core
  AND  EXISTS (SELECT 1 FROM identity_access.features f WHERE f.key = m.key);

-- A non-core module with no feature would be permanently invisible once enforcement is on.
DO $$
DECLARE orphans text;
BEGIN
    SELECT string_agg(key, ', ') INTO orphans
    FROM identity_access.modules WHERE NOT is_core AND feature_key IS NULL;
    IF orphans IS NOT NULL THEN
        RAISE EXCEPTION 'modules left with no feature_key: %', orphans;
    END IF;
END $$;

-- ─── 5. Entitlement moves from modules to features ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.brand_feature (
    brand_id    UUID    NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    feature_key VARCHAR(64) NOT NULL REFERENCES identity_access.features(key) ON DELETE CASCADE,
    enabled     BOOLEAN NOT NULL DEFAULT true,
    valid_until DATE,                                        -- NULL = perpetual
    source      VARCHAR(32) NOT NULL DEFAULT 'manual'
                CHECK (source IN ('bundle','manual')),        -- 'bundle' = from a plan; 'manual' = add-on
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by  UUID,
    updated_by  UUID,
    PRIMARY KEY (brand_id, feature_key)
);
CREATE INDEX IF NOT EXISTS ix_brand_feature_brand
    ON identity_access.brand_feature (brand_id) WHERE enabled;

ALTER TABLE identity_access.brand_feature ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON identity_access.brand_feature;
CREATE POLICY rls_brand ON identity_access.brand_feature
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.brand_feature TO app_user, app_admin;

-- Backfill through the module->feature map. DISTINCT ON collapses the many-to-one cases
-- (analytics + report both -> advanced_analytics) and keeps the MOST permissive row, so a brand
-- that held either module ends up entitled to the shared feature.
INSERT INTO identity_access.brand_feature
    (brand_id, feature_key, enabled, valid_until, source, created_at, updated_at, created_by, updated_by)
SELECT DISTINCT ON (bm.brand_id, m.feature_key)
       bm.brand_id, m.feature_key, bm.enabled, bm.valid_until, bm.source,
       bm.created_at, bm.updated_at, bm.created_by, bm.updated_by
FROM   identity_access.brand_module bm
JOIN   identity_access.modules m ON m.key = bm.module_key
WHERE  m.feature_key IS NOT NULL
ORDER  BY bm.brand_id, m.feature_key,
          bm.enabled DESC,                                   -- an enabled row beats a disabled one
          (bm.valid_until IS NULL) DESC, bm.valid_until DESC  -- perpetual beats dated; later beats earlier
ON CONFLICT (brand_id, feature_key) DO NOTHING;

-- ─── 6. Plans package features, not modules ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.bundle_feature (
    bundle_code VARCHAR NOT NULL REFERENCES identity_access.module_bundle(code) ON DELETE CASCADE,
    feature_key VARCHAR(64) NOT NULL REFERENCES identity_access.features(key) ON DELETE CASCADE,
    PRIMARY KEY (bundle_code, feature_key)
);
GRANT SELECT ON identity_access.bundle_feature TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.bundle_feature TO app_admin;

INSERT INTO identity_access.bundle_feature (bundle_code, feature_key)
SELECT DISTINCT mbi.bundle_code, m.feature_key
FROM   identity_access.module_bundle_item mbi
JOIN   identity_access.modules m ON m.key = mbi.module_key
WHERE  m.feature_key IS NOT NULL
ON CONFLICT DO NOTHING;

-- ─── 7. Verification — abort rather than silently downgrade anyone ───────────────────────────
-- For every brand, the set of NON-CORE modules reachable through the new feature entitlement must
-- be a superset of what it could reach through brand_module. (Superset, not equal: a brand holding
-- `analytics` now also reaches `report`, because they share advanced_analytics. That is a
-- deliberate widening of the many-to-one merge, never a loss.)
DO $$
DECLARE bad text;
BEGIN
    SELECT string_agg(x.brand_id::text || ' lost: ' || x.lost, '; ')
    INTO   bad
    FROM (
        SELECT before.brand_id,
               string_agg(before.module_key, ',') AS lost
        FROM (
            SELECT bm.brand_id, bm.module_key
            FROM   identity_access.brand_module bm
            JOIN   identity_access.modules m ON m.key = bm.module_key
            WHERE  bm.enabled AND NOT m.is_core
        ) before
        LEFT JOIN (
            SELECT bf.brand_id, m.key AS module_key
            FROM   identity_access.brand_feature bf
            JOIN   identity_access.modules m ON m.feature_key = bf.feature_key
            WHERE  bf.enabled
        ) after ON after.brand_id = before.brand_id AND after.module_key = before.module_key
        WHERE after.module_key IS NULL
        GROUP BY before.brand_id
    ) x;

    IF bad IS NOT NULL THEN
        RAISE EXCEPTION 'entitlement would be LOST by the features split — %', bad;
    END IF;

    RAISE NOTICE 'entitlement parity verified: no brand loses a module';
END $$;

-- ─── 8. Retire the module-keyed entitlement tables ───────────────────────────────────────────
-- Dropped rather than left behind: two sources of truth for "what does this brand own" is exactly
-- the drift that produced the 7 unlicensed modules on LG-MAIN. The .down.sql rebuilds and
-- repopulates both from brand_feature/bundle_feature.
DROP TABLE IF EXISTS identity_access.brand_module;
DROP TABLE IF EXISTS identity_access.module_bundle_item;

COMMENT ON COLUMN identity_access.modules.feature_key IS
    'The feature that must be entitled for this navigation module to appear. NULL only for core '
    'modules, which are always on. Many modules may share one feature (analytics + report).';
