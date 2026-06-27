-- =============================================================================
-- db/patches/phase2_slice_a_catalog_kind.sql
--
-- Multi-vertical Phase 2 · Slice 2A — add the vertical-neutral catalog discriminator
-- to customer_catalog.items (blueprint §7.2 "Catalog CatalogKind/Attributes wired").
-- This is the foundational catalog seam that unblocks Phase-3 client branching.
--
--   * catalog_kind — what the item is (laundry_garment / service / parcel / product);
--     mirrors brand.vertical_key (Phase 0) at the item grain. Backfilled from the brand's
--     vertical so existing laundry catalogs become 'laundry_garment'.
--   * attributes — a kind-specific jsonb bag (empty for now; laundry-specific columns like
--     typical_weight_grams are demoted into it in a later slice).
--
-- Non-destructive + idempotent (IF NOT EXISTS guards, ADD COLUMN with DEFAULT). RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_a_catalog_kind.sql
-- =============================================================================

BEGIN;

-- 1. Columns ------------------------------------------------------------------
ALTER TABLE customer_catalog.items
    ADD COLUMN IF NOT EXISTS catalog_kind VARCHAR(20) NOT NULL DEFAULT 'laundry_garment',
    ADD COLUMN IF NOT EXISTS attributes   JSONB       NOT NULL DEFAULT '{}'::jsonb;

-- 2. Vocabulary CHECK (matches Enums.CatalogKind) -----------------------------
DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'items_catalog_kind_check') THEN
        ALTER TABLE customer_catalog.items
            ADD CONSTRAINT items_catalog_kind_check
            CHECK (catalog_kind IN ('laundry_garment','service','parcel','product'));
    END IF;
END
$chk$;

-- 3. Backfill from the brand's vertical (existing laundry brands → laundry_garment) --
UPDATE customer_catalog.items i
SET catalog_kind = CASE b.vertical_key
        WHEN 'salon'     THEN 'service'
        WHEN 'logistics' THEN 'parcel'
        ELSE 'laundry_garment'
    END
FROM tenancy_org.brands b
WHERE b.id = i.brand_id;

-- 4. Filtered index for kind-scoped catalog queries --------------------------
CREATE INDEX IF NOT EXISTS idx_items_brand_kind
    ON customer_catalog.items (brand_id, catalog_kind)
    WHERE deleted_at IS NULL;

-- 5. Verification gate --------------------------------------------------------
DO $verify$
DECLARE col_count int; bad_rows int;
BEGIN
    SELECT count(*) INTO col_count FROM information_schema.columns
    WHERE table_schema='customer_catalog' AND table_name='items'
      AND column_name IN ('catalog_kind','attributes');
    IF col_count <> 2 THEN RAISE EXCEPTION 'Slice 2A: expected catalog_kind + attributes columns, found %', col_count; END IF;

    SELECT count(*) INTO bad_rows FROM customer_catalog.items
    WHERE catalog_kind NOT IN ('laundry_garment','service','parcel','product');
    IF bad_rows <> 0 THEN RAISE EXCEPTION 'Slice 2A: % item(s) have an unknown catalog_kind', bad_rows; END IF;

    RAISE NOTICE 'Slice 2A verification passed: catalog_kind + attributes added and backfilled.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_a_catalog_kind.sql applied successfully.' AS result;
