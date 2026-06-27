-- =============================================================================
-- db/patches/phase2_slice_h_warehouse_capabilities.sql
--
-- Multi-vertical Phase 2 · Slice 2H — demote the laundry processing-capability flags on
-- tenancy_org.warehouses (has_dry_clean / has_steam_iron / has_shoe_cleaning /
-- has_carpet_cleaning) into a processing_capabilities jsonb off the generic warehouse spine
-- (blueprint §7.2 Tenancy "strip Warehouse capability booleans → jsonb"). A non-laundry hub
-- carries its own capability shape; the generic spine stays clean.
--
-- DESTRUCTIVE (backfill-then-drop) + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_h_warehouse_capabilities.sql
-- =============================================================================

BEGIN;

ALTER TABLE tenancy_org.warehouses
    ADD COLUMN IF NOT EXISTS processing_capabilities jsonb NOT NULL DEFAULT '{}'::jsonb;

DO $backfill$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='tenancy_org' AND table_name='warehouses' AND column_name='has_dry_clean') THEN
        UPDATE tenancy_org.warehouses SET processing_capabilities = jsonb_build_object(
            'has_dry_clean',      has_dry_clean,
            'has_steam_iron',     has_steam_iron,
            'has_shoe_cleaning',  has_shoe_cleaning,
            'has_carpet_cleaning', has_carpet_cleaning);
    END IF;
END
$backfill$;

ALTER TABLE tenancy_org.warehouses
    DROP COLUMN IF EXISTS has_dry_clean,
    DROP COLUMN IF EXISTS has_steam_iron,
    DROP COLUMN IF EXISTS has_shoe_cleaning,
    DROP COLUMN IF EXISTS has_carpet_cleaning;

DO $verify$
DECLARE has_new int; has_old int;
BEGIN
    SELECT count(*) INTO has_new FROM information_schema.columns
    WHERE table_schema='tenancy_org' AND table_name='warehouses' AND column_name='processing_capabilities';
    IF has_new <> 1 THEN RAISE EXCEPTION 'Slice 2H: processing_capabilities column missing'; END IF;

    SELECT count(*) INTO has_old FROM information_schema.columns
    WHERE table_schema='tenancy_org' AND table_name='warehouses'
      AND column_name IN ('has_dry_clean','has_steam_iron','has_shoe_cleaning','has_carpet_cleaning');
    IF has_old <> 0 THEN RAISE EXCEPTION 'Slice 2H: % capability column(s) still on the spine', has_old; END IF;

    RAISE NOTICE 'Slice 2H verification passed: warehouse capability flags moved to processing_capabilities jsonb.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_h_warehouse_capabilities.sql applied successfully.' AS result;
