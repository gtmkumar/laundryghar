-- =============================================================================
-- db/patches/phase1_slice_f_fulfillment_unit_rename.sql
--
-- Multi-vertical Phase 1 · Slice F — promote the laundry Garment* family to the
-- vertical-neutral fulfillment_unit naming (blueprint Phase 1, "promote garments→
-- fulfillment_unit"). Renames the 5 family tables and the garment_id FK columns that
-- reference the unit (incl. in process_logs / quality_checks / stock_reconciliation_items).
--
-- Non-destructive: ALTER TABLE/COLUMN RENAME is metadata-only and preserves data, FKs,
-- indexes, and RLS policies. Constraint/index NAMES are intentionally left as-is
-- (e.g. garments_pkey on fulfillment_unit) — names don't affect behaviour, EF maps by
-- table+column, and renaming the ~200 auto-named constraints/indexes adds risk for no gain.
-- RENAME COLUMN on the partitioned process_logs parent cascades to all partitions.
--
-- All 5 tables live in schema laundry_fulfillment (relocated in slice C).
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase1_slice_f_fulfillment_unit_rename.sql
-- =============================================================================

BEGIN;

-- 1. Tables -------------------------------------------------------------------
DO $rename$
DECLARE p record;
BEGIN
    FOR p IN SELECT * FROM (VALUES
        ('garments',                  'fulfillment_unit'),
        ('garment_tags',              'fulfillment_unit_tags'),
        ('garment_inspections',       'fulfillment_unit_inspections'),
        ('garment_conditions',        'fulfillment_unit_conditions'),
        ('garment_inspection_photos', 'fulfillment_unit_inspection_photos')
    ) AS m(old_name, new_name) LOOP
        IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                   WHERE n.nspname='laundry_fulfillment' AND c.relname=p.old_name) THEN
            EXECUTE format('ALTER TABLE laundry_fulfillment.%I RENAME TO %I;', p.old_name, p.new_name);
        END IF;
    END LOOP;
END
$rename$;

-- 2. FK columns referencing the unit: garment_id → fulfillment_unit_id --------
--    (RENAME COLUMN on the partitioned process_logs parent propagates to partitions.)
DO $cols$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'fulfillment_unit_inspections','fulfillment_unit_inspection_photos',
        'process_logs','quality_checks','stock_reconciliation_items'
    ] LOOP
        IF EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='laundry_fulfillment' AND table_name=t AND column_name='garment_id') THEN
            EXECUTE format('ALTER TABLE laundry_fulfillment.%I RENAME COLUMN garment_id TO fulfillment_unit_id;', t);
        END IF;
    END LOOP;
    -- the tag→unit assignment link
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='laundry_fulfillment' AND table_name='fulfillment_unit_tags'
                 AND column_name='assigned_to_garment_id') THEN
        ALTER TABLE laundry_fulfillment.fulfillment_unit_tags
            RENAME COLUMN assigned_to_garment_id TO assigned_to_fulfillment_unit_id;
    END IF;
END
$cols$;

-- 3. Verification gate --------------------------------------------------------
DO $verify$
DECLARE new_tables int; old_tables int; old_cols int;
BEGIN
    SELECT count(*) INTO new_tables FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='laundry_fulfillment' AND c.relkind IN ('r','p')
      AND c.relname IN ('fulfillment_unit','fulfillment_unit_tags','fulfillment_unit_inspections',
                        'fulfillment_unit_conditions','fulfillment_unit_inspection_photos');
    IF new_tables <> 5 THEN RAISE EXCEPTION 'Slice F: expected 5 fulfillment_unit* tables, found %', new_tables; END IF;

    SELECT count(*) INTO old_tables FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='laundry_fulfillment' AND c.relkind IN ('r','p')
      AND c.relname IN ('garments','garment_tags','garment_inspections','garment_conditions','garment_inspection_photos');
    IF old_tables <> 0 THEN RAISE EXCEPTION 'Slice F: % old garment* table(s) remain', old_tables; END IF;

    SELECT count(*) INTO old_cols FROM information_schema.columns
    WHERE table_schema='laundry_fulfillment'
      AND (column_name='garment_id' OR column_name='assigned_to_garment_id');
    IF old_cols <> 0 THEN RAISE EXCEPTION 'Slice F: % old garment_id column(s) remain', old_cols; END IF;

    RAISE NOTICE 'Slice F verification passed: 5 tables + FK columns renamed garment→fulfillment_unit.';
END
$verify$;

COMMIT;

SELECT 'phase1_slice_f_fulfillment_unit_rename.sql applied successfully.' AS result;
