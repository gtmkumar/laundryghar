-- =============================================================================
-- db/patches/phase2_slice_c_bundle_vertical.sql
--
-- Multi-vertical Phase 2 · Slice 2C — extend module entitlement with vertical scoping
-- (blueprint §7.2 "Module Entitlement: modules.vertical_key + module_bundle.vertical_key;
-- per-vertical bundles; vertical filters"). Slice 2B added modules.vertical_key + the
-- laundry `fabrics` module; this slice:
--
--   1. adds module_bundle.vertical_key (null = vertical-neutral tier bundle),
--   2. tags the laundry-only `warehouse` module vertical_key='laundry' so a shared tier
--      bundle never licenses it to a non-laundry brand (enforced in ApplyBundleToBrand),
--   3. leaves starter/pro/enterprise as neutral tier bundles — the per-module vertical gate
--      does the filtering, so the same tiers work across verticals.
--
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_c_bundle_vertical.sql
-- =============================================================================

BEGIN;

-- 1. module_bundle.vertical_key (null = neutral) ------------------------------
ALTER TABLE identity_access.module_bundle
    ADD COLUMN IF NOT EXISTS vertical_key VARCHAR(20);

DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'module_bundle_vertical_key_check') THEN
        ALTER TABLE identity_access.module_bundle
            ADD CONSTRAINT module_bundle_vertical_key_check
            CHECK (vertical_key IS NULL OR vertical_key IN ('laundry','salon','logistics'));
    END IF;
END
$chk$;

-- 2. Tag the laundry-only modules. `fabrics` was tagged in slice 2B; `warehouse`
--    (wash/QC/stock-recon processing board) is laundry's processing pipeline.
UPDATE identity_access.modules
SET vertical_key = 'laundry', updated_at = now()
WHERE key = 'warehouse' AND vertical_key IS DISTINCT FROM 'laundry';

-- 3. Verification gate --------------------------------------------------------
DO $verify$
DECLARE has_col int; warehouse_vertical text;
BEGIN
    SELECT count(*) INTO has_col FROM information_schema.columns
    WHERE table_schema='identity_access' AND table_name='module_bundle' AND column_name='vertical_key';
    IF has_col <> 1 THEN RAISE EXCEPTION 'Slice 2C: module_bundle.vertical_key column missing'; END IF;

    SELECT vertical_key INTO warehouse_vertical FROM identity_access.modules WHERE key='warehouse';
    -- warehouse module may legitimately be absent in a minimal DB; only assert when present.
    IF FOUND AND warehouse_vertical IS DISTINCT FROM 'laundry' THEN
        RAISE EXCEPTION 'Slice 2C: warehouse module not tagged laundry (got %)', warehouse_vertical;
    END IF;

    RAISE NOTICE 'Slice 2C verification passed: module_bundle.vertical_key added; laundry modules tagged.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_c_bundle_vertical.sql applied successfully.' AS result;
