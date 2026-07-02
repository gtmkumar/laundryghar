-- =============================================================================
-- value_slab_pricing.sql  (GH #22 — value-slab pricing for branded/luxury garments)
--   Branded/luxury garments are priced by DECLARED GARMENT VALUE slabs, not flat
--   rates. A brand authors its own slabs (e.g. ₹2k–5k → ₹200, 5k–10k → ₹300, …);
--   at order time the customer declares the garment's value and the matching slab's
--   price becomes the line's base price. Slabs are brand data (NOT seeded).
--
--   Adds:
--     1. customer_catalog.value_price_slabs — the per-brand slab table (RLS + CHECKs).
--     2. customer_catalog.items.pricing_mode — 'standard' (default) | 'value_slab'.
--     3. order_lifecycle.order_items.declared_value + applied_slab_price — line snapshot.
--     4. Extends customer_catalog.pricing_change_log.target_kind CHECK with
--        'value_price_slab' so slab edits are audit-logged and revertable.
--     5. Permission pricing.slab.manage — granted to every role that already holds
--        pricing.pricelist.update (mirrors core.Infrastructure/Seeders/IdentitySeeder.cs).
--
--   Everything is additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/value_slab_pricing.sql
-- =============================================================================

BEGIN;

-- ── 1. value_price_slabs ─────────────────────────────────────────────────────
--   Overlap prevention is handled in the APPLICATION layer, not by a DB EXCLUDE
--   constraint: a range-overlap EXCLUDE on (min_value, max_value) with an open-ended
--   top slab (max_value NULL) needs a numrange + btree_gist extension and a NULL→+inf
--   coercion. Keeping it app-side avoids the extension dependency and lets the two
--   resolution "lanes" (service-specific vs. brand-wide/null-service) overlap each
--   other intentionally (service-specific wins at resolution) while each lane stays
--   internally non-overlapping. See ValueSlabResolver.EnsureNoOverlapAsync.
CREATE TABLE IF NOT EXISTS customer_catalog.value_price_slabs (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id    UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE RESTRICT,
    -- NULL service_id = brand-wide lane (applies to any service); a non-null value
    -- scopes the slab to one service. Service-specific slabs win over the null lane.
    service_id  UUID REFERENCES customer_catalog.services(id) ON DELETE CASCADE,
    min_value   NUMERIC(14,2) NOT NULL,
    -- NULL max_value = open-ended top slab (matches any declared value >= min_value).
    max_value   NUMERIC(14,2),
    price       NUMERIC(14,2) NOT NULL,
    status      TEXT NOT NULL DEFAULT 'active'
                CHECK (status IN ('active','inactive','archived')),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by  UUID,
    updated_by  UUID,
    version     INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT value_price_slabs_min_nonneg CHECK (min_value >= 0),
    CONSTRAINT value_price_slabs_price_nonneg CHECK (price >= 0),
    CONSTRAINT value_price_slabs_range CHECK (max_value IS NULL OR max_value > min_value)
);
CREATE INDEX IF NOT EXISTS ix_value_price_slabs_lookup
    ON customer_catalog.value_price_slabs (brand_id, service_id, min_value)
    WHERE status = 'active';

ALTER TABLE customer_catalog.value_price_slabs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON customer_catalog.value_price_slabs;
CREATE POLICY rls_brand ON customer_catalog.value_price_slabs FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
GRANT SELECT, INSERT, UPDATE, DELETE ON customer_catalog.value_price_slabs TO app_user, app_admin;

-- updated_at trigger (matches every other audited table; triggers_set_updated_at.sql
-- would also attach this on its next run — done here so a targeted apply is complete).
DROP TRIGGER IF EXISTS trg_set_updated_at ON customer_catalog.value_price_slabs;
CREATE TRIGGER trg_set_updated_at BEFORE UPDATE ON customer_catalog.value_price_slabs
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ── 2. items.pricing_mode ────────────────────────────────────────────────────
ALTER TABLE customer_catalog.items
    ADD COLUMN IF NOT EXISTS pricing_mode TEXT NOT NULL DEFAULT 'standard';
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'items_pricing_mode_check'
          AND conrelid = 'customer_catalog.items'::regclass
    ) THEN
        ALTER TABLE customer_catalog.items
            ADD CONSTRAINT items_pricing_mode_check
            CHECK (pricing_mode IN ('standard','value_slab'));
    END IF;
END $$;

-- ── 3. order_items line snapshot ─────────────────────────────────────────────
--   declared_value: the value the customer declared for a value_slab garment.
--   applied_slab_price: the slab price that resolved to this line's base price.
--   Both NULL for standard-priced lines. Immutable snapshot (survives later slab edits).
ALTER TABLE order_lifecycle.order_items
    ADD COLUMN IF NOT EXISTS declared_value     NUMERIC(14,2);
ALTER TABLE order_lifecycle.order_items
    ADD COLUMN IF NOT EXISTS applied_slab_price NUMERIC(14,2);

-- ── 4. Extend pricing_change_log.target_kind CHECK with 'value_price_slab' ────
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'pricing_change_log_target_kind_check'
          AND conrelid = 'customer_catalog.pricing_change_log'::regclass
    ) THEN
        ALTER TABLE customer_catalog.pricing_change_log
            DROP CONSTRAINT pricing_change_log_target_kind_check;
    END IF;
    ALTER TABLE customer_catalog.pricing_change_log
        ADD CONSTRAINT pricing_change_log_target_kind_check
        CHECK (target_kind IN ('fabric_type','price_list_item','add_on','value_price_slab'));
END $$;

-- ── 5. Permission pricing.slab.manage ────────────────────────────────────────
--   Mirrors the PermissionDefs entry added to IdentitySeeder.cs (module 'pricing').
--   The 'pricing' navigator/matrix module already owns permission_modules '{pricing,catalog}',
--   so AssignPermissionModuleKeysAsync auto-owns this new code — no module row needed.
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'pricing.slab.manage', 'pricing', 'slab.manage',
     'Manage value-price slabs', 'Manage value-price slabs',
     true, true, 'normal', 'active', now(), now())
ON CONFLICT (code) DO NOTHING;

-- Grant to every role that already holds pricing.pricelist.update (platform_admin via
-- its wildcard grant, brand_admin, catalogue_manager, …). Join keeps the grant set in
-- lockstep with pricelist authoring without hardcoding role codes.
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), rp.role_id, slab.id, now(), now()
FROM identity_access.role_permissions rp
JOIN identity_access.permissions upd  ON upd.id = rp.permission_id AND upd.code = 'pricing.pricelist.update'
JOIN identity_access.permissions slab ON slab.code = 'pricing.slab.manage'
ON CONFLICT (role_id, permission_id) DO NOTHING;

COMMIT;

-- Verification.
SELECT 'value_price_slabs' AS obj,
       (SELECT count(*) FROM information_schema.tables
        WHERE table_schema='customer_catalog' AND table_name='value_price_slabs') AS present
UNION ALL
SELECT 'items.pricing_mode',
       (SELECT count(*) FROM information_schema.columns
        WHERE table_schema='customer_catalog' AND table_name='items' AND column_name='pricing_mode')
UNION ALL
SELECT 'order_items.declared_value',
       (SELECT count(*) FROM information_schema.columns
        WHERE table_schema='order_lifecycle' AND table_name='order_items' AND column_name='declared_value')
UNION ALL
SELECT 'perm pricing.slab.manage',
       (SELECT count(*) FROM identity_access.permissions WHERE code='pricing.slab.manage')
UNION ALL
SELECT 'slab.manage grants',
       (SELECT count(*) FROM identity_access.role_permissions rp
        JOIN identity_access.permissions p ON p.id=rp.permission_id WHERE p.code='pricing.slab.manage');
