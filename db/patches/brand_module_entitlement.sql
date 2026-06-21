-- =============================================================================
-- brand_module_entitlement.sql  (PaaS entitlement dimension — Phase 1)
--   Adds the missing ENTITLEMENT axis so effective access becomes
--   (entitlement ∩ authorization). Mirrors the reference `tenant_module`, with
--   tenant = brand. See docs/rbac-entitlement-plan.md.
--
--   1. identity_access.modules.is_core  — always-on modules bypass entitlement
--      (a brand can never lock its own admins out: dashboard, settings, users).
--   2. identity_access.brand_module     — authoritative per-brand module licensing
--      (brand-scoped, RLS). One table the nav + token filters intersect against.
--   3. identity_access.module_bundle / module_bundle_item — platform catalog of
--      plan→modules, used to EXPAND into brand_module rows at onboarding /
--      plan-change (global, no RLS — like `modules`/`permissions`).
--   4. Backfill: grandfather every existing brand into every active non-core
--      module, so this patch is a NO-OP on current behaviour (Phase 1).
--
--   This patch ONLY lays the schema + data foundation. The GetNavigator and
--   ScopeResolver intersection (Phases 2–3) ship behind a feature flag later.
--
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/brand_module_entitlement.sql
-- =============================================================================

BEGIN;

-- ── 1. Core flag on the global module catalog ────────────────────────────────
ALTER TABLE identity_access.modules
    ADD COLUMN IF NOT EXISTS is_core boolean NOT NULL DEFAULT false;

-- Admin tooling a brand can never "unbuy".
UPDATE identity_access.modules
SET is_core = true
WHERE key IN ('dashboard', 'settings', 'users');

-- ── 2. Entitlement: which modules a brand has licensed (brand-scoped) ─────────
CREATE TABLE IF NOT EXISTS identity_access.brand_module (
    brand_id    UUID    NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    module_key  VARCHAR NOT NULL REFERENCES identity_access.modules(key) ON DELETE CASCADE,
    enabled     BOOLEAN NOT NULL DEFAULT true,
    valid_until DATE,                                       -- NULL = perpetual
    source      VARCHAR NOT NULL DEFAULT 'manual'
                    CHECK (source IN ('bundle', 'manual')), -- 'bundle' = from a plan; 'manual' = per-brand add-on/exception
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by  UUID NULL,
    updated_by  UUID NULL,
    PRIMARY KEY (brand_id, module_key)
);
CREATE INDEX IF NOT EXISTS ix_brand_module_brand
    ON identity_access.brand_module (brand_id) WHERE enabled;

-- ── 3. Plan→module bundle catalog (platform-global, like `modules`) ───────────
CREATE TABLE IF NOT EXISTS identity_access.module_bundle (
    code        VARCHAR PRIMARY KEY,                        -- 'starter','pro','enterprise'
    name        VARCHAR NOT NULL,
    description TEXT
);
CREATE TABLE IF NOT EXISTS identity_access.module_bundle_item (
    bundle_code VARCHAR NOT NULL REFERENCES identity_access.module_bundle(code) ON DELETE CASCADE,
    module_key  VARCHAR NOT NULL REFERENCES identity_access.modules(key) ON DELETE CASCADE,
    PRIMARY KEY (bundle_code, module_key)
);

-- ── 4. RLS + grants ──────────────────────────────────────────────────────────
-- brand_module is brand-scoped → same policy shape as every other tenant table.
ALTER TABLE identity_access.brand_module ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON identity_access.brand_module;
CREATE POLICY rls_brand ON identity_access.brand_module
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.brand_module TO app_user, app_admin;

-- Bundle catalog is global (no RLS): readable by all, writable by admin.
GRANT SELECT ON identity_access.module_bundle, identity_access.module_bundle_item TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.module_bundle, identity_access.module_bundle_item TO app_admin;

-- ── 5. Seed bundles (tier → modules). Core modules are always-on, so omitted. ─
INSERT INTO identity_access.module_bundle (code, name, description) VALUES
    ('starter',    'Starter',    'Core operations: orders, customers, catalogue, POS, support.'),
    ('pro',        'Pro',        'Starter + warehouse, riders, marketing, subscriptions, analytics, finance.'),
    ('enterprise', 'Enterprise', 'Pro + royalty, franchise management, and platform plans.')
ON CONFLICT (code) DO NOTHING;

-- starter
INSERT INTO identity_access.module_bundle_item (bundle_code, module_key)
SELECT 'starter', k FROM unnest(ARRAY[
    'orders','customers','pricing','packages','cms','pos','support'
]) AS k
WHERE EXISTS (SELECT 1 FROM identity_access.modules m WHERE m.key = k)
ON CONFLICT DO NOTHING;

-- pro = starter + ops/marketing/finance
INSERT INTO identity_access.module_bundle_item (bundle_code, module_key)
SELECT 'pro', k FROM unnest(ARRAY[
    'orders','customers','pricing','packages','cms','pos','support',
    'warehouse','riders','coupons','promotions','subscriptions','analytics','cashbook','expenses'
]) AS k
WHERE EXISTS (SELECT 1 FROM identity_access.modules m WHERE m.key = k)
ON CONFLICT DO NOTHING;

-- enterprise = pro + royalty/franchises/platform_plans
INSERT INTO identity_access.module_bundle_item (bundle_code, module_key)
SELECT 'enterprise', k FROM unnest(ARRAY[
    'orders','customers','pricing','packages','cms','pos','support',
    'warehouse','riders','coupons','promotions','subscriptions','analytics','cashbook','expenses',
    'royalty','franchises','platform_plans'
]) AS k
WHERE EXISTS (SELECT 1 FROM identity_access.modules m WHERE m.key = k)
ON CONFLICT DO NOTHING;

-- ── 6. Backfill: grandfather every brand into every active non-core module ────
--      Keeps current behaviour identical once Phases 2–3 turn enforcement on.
INSERT INTO identity_access.brand_module (brand_id, module_key, enabled, source)
SELECT b.id, m.key, true, 'manual'
FROM   tenancy_org.brands b
CROSS JOIN identity_access.modules m
WHERE  m.status = 'active' AND m.is_core = false
ON CONFLICT (brand_id, module_key) DO NOTHING;

COMMIT;

-- ── Quick checks ─────────────────────────────────────────────────────────────
SELECT 'core modules' AS what, string_agg(key, ', ' ORDER BY key) AS detail
FROM identity_access.modules WHERE is_core
UNION ALL
SELECT 'bundles', string_agg(code || '(' || cnt || ')', ', ')
FROM (SELECT bundle_code AS code, count(*) cnt FROM identity_access.module_bundle_item GROUP BY 1) s
UNION ALL
SELECT 'brand_module rows', count(*)::text FROM identity_access.brand_module;
