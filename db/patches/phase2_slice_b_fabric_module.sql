-- =============================================================================
-- db/patches/phase2_slice_b_fabric_module.sql
--
-- Multi-vertical Phase 2 · Slice 2B — demote laundry FabricType management behind a
-- vertical-gated module (blueprint §4 "FabricType ... gated behind ModuleBundle
-- entitlement so non-laundry brands never see it. Physical table retained").
--
--   * Adds identity_access.modules.vertical_key (nullable = vertical-neutral / all brands).
--   * Carves fabric management out of the generic `pricing` module into a dedicated
--     `fabrics` module tagged vertical_key='laundry', so the navigator hides it from
--     salon/logistics brands (GetNavigator filters by VerticalKey.IsAvailableTo).
--
--   The fabric_types table + its FKs on item_variants / price_list_items / garments
--   are RETAINED physically (per the blueprint) — this slice gates VISIBILITY, not data.
--
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_b_fabric_module.sql
-- =============================================================================

BEGIN;

-- 1. vertical_key column on the module registry (null = neutral) --------------
ALTER TABLE identity_access.modules
    ADD COLUMN IF NOT EXISTS vertical_key VARCHAR(20);

DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'modules_vertical_key_check') THEN
        ALTER TABLE identity_access.modules
            ADD CONSTRAINT modules_vertical_key_check
            CHECK (vertical_key IS NULL OR vertical_key IN ('laundry','salon','logistics'));
    END IF;
END
$chk$;

-- 2. Dedicated laundry `fabrics` nav module (gates catalog.fabric.manage) -----
--    show_in_matrix=false: the fabric permission stays in the catalog matrix group;
--    this row only adds a laundry-only sidebar entry.
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order,
     show_in_nav, show_in_matrix, required_permission, permission_modules, vertical_key, status)
VALUES
    ('fabrics', 'Fabrics', 'Layers', '/catalog/fabrics', 'Catalogue', 24, 999,
     true, false, 'catalog.fabric.manage', '{fabric}', 'laundry', 'active')
ON CONFLICT (key) DO UPDATE
    SET vertical_key        = EXCLUDED.vertical_key,
        required_permission = EXCLUDED.required_permission,
        route               = EXCLUDED.route,
        section             = EXCLUDED.section,
        show_in_nav         = EXCLUDED.show_in_nav,
        updated_at          = now();

-- 3. Verification gate --------------------------------------------------------
DO $verify$
DECLARE has_col int; fabric_vertical text;
BEGIN
    SELECT count(*) INTO has_col FROM information_schema.columns
    WHERE table_schema='identity_access' AND table_name='modules' AND column_name='vertical_key';
    IF has_col <> 1 THEN RAISE EXCEPTION 'Slice 2B: modules.vertical_key column missing'; END IF;

    SELECT vertical_key INTO fabric_vertical FROM identity_access.modules WHERE key='fabrics';
    IF fabric_vertical IS DISTINCT FROM 'laundry' THEN
        RAISE EXCEPTION 'Slice 2B: fabrics module not tagged laundry (got %)', fabric_vertical;
    END IF;

    RAISE NOTICE 'Slice 2B verification passed: modules.vertical_key added; fabrics module gated to laundry.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_b_fabric_module.sql applied successfully.' AS result;
