-- =============================================================================
-- db/patches/phase1_slice_f2_fulfillment_unit_attributes.sql
--
-- Multi-vertical Phase 1 · Slice F-2 — extract the laundry-private attributes off
-- the generic fulfillment_unit spine into a strategy-private `attributes` jsonb slice
-- (blueprint §4 "move has_ornaments/has_lining/is_designer_wear/weight_grams/
-- care_instructions/rewash_count into a strategy-private Attributes jsonb slice").
-- Slice F renamed the table; this completes the second half of that blueprint bullet.
--
--   * fabric_type_id is INTENTIONALLY KEPT as a real FK column — it is a referential
--     link (warehouse board joins fulfillment_unit → fabric_types for the fabric name),
--     not a free-form attribute. Moving a FK into jsonb would break referential
--     integrity and that join. The table is laundry-private (laundry_fulfillment schema)
--     so a laundry FK on it is correct.
--
-- DESTRUCTIVE + IRREVERSIBLE: backfills the 6 columns into `attributes`, then DROPs them.
-- Backfill-then-drop ordering is enforced in this single transaction. The jsonb keys match
-- the EF owned-type ToJson mapping (HasJsonPropertyName snake_case) in
-- FulfillmentUnitConfiguration.OwnsOne(e => e.Attributes).
--
-- The mv_warehouse_throughput materialized view reads rewash_count off this table, so it is
-- dropped (to unblock the column drop) and recreated to read (attributes->>'rewash_count').
--
-- Idempotent: re-running is a no-op (guards on column existence). RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase1_slice_f2_fulfillment_unit_attributes.sql
-- =============================================================================

BEGIN;

-- 0. Stash the schema of the dependent matview (if present) and drop it so the column
--    drop below is not blocked. Recreated in step 4 in the same schema.
CREATE TEMP TABLE _mvwt_schema ON COMMIT DROP AS
SELECT schemaname FROM pg_matviews WHERE matviewname = 'mv_warehouse_throughput';

DO $drop_mv$
DECLARE s text;
BEGIN
    SELECT schemaname INTO s FROM _mvwt_schema LIMIT 1;
    IF s IS NOT NULL THEN
        EXECUTE format('DROP MATERIALIZED VIEW %I.mv_warehouse_throughput;', s);
    END IF;
END
$drop_mv$;

-- 1. Add the attributes jsonb column (no-op if already present from a prior run) ----
ALTER TABLE laundry_fulfillment.fulfillment_unit
    ADD COLUMN IF NOT EXISTS attributes jsonb NOT NULL DEFAULT '{}'::jsonb;

-- 2. Backfill from the 6 flat columns — only if they still exist (idempotent) --------
DO $backfill$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='laundry_fulfillment' AND table_name='fulfillment_unit'
                 AND column_name='rewash_count') THEN
        UPDATE laundry_fulfillment.fulfillment_unit SET attributes = jsonb_build_object(
            'weight_grams',     weight_grams,
            'has_ornaments',    has_ornaments,
            'has_lining',       has_lining,
            'is_designer_wear', is_designer_wear,
            'rewash_count',     rewash_count,
            'care_instructions', care_instructions
        );
    END IF;
END
$backfill$;

-- 3. Drop the 6 now-migrated columns ------------------------------------------------
ALTER TABLE laundry_fulfillment.fulfillment_unit
    DROP COLUMN IF EXISTS weight_grams,
    DROP COLUMN IF EXISTS has_ornaments,
    DROP COLUMN IF EXISTS has_lining,
    DROP COLUMN IF EXISTS is_designer_wear,
    DROP COLUMN IF EXISTS rewash_count,
    DROP COLUMN IF EXISTS care_instructions;

-- 4. Recreate the matview (same schema) reading rewash_count from the jsonb ----------
DO $recreate_mv$
DECLARE s text;
BEGIN
    SELECT schemaname INTO s FROM _mvwt_schema LIMIT 1;
    IF s IS NOT NULL THEN
        EXECUTE format($mv$
            CREATE MATERIALIZED VIEW %I.mv_warehouse_throughput AS
            SELECT
                g.brand_id,
                g.warehouse_id,
                DATE(g.created_at AT TIME ZONE 'Asia/Kolkata') AS throughput_date,
                COUNT(*)                                                        AS garments_received,
                COUNT(*) FILTER (WHERE g.current_stage = 'delivered')           AS garments_delivered,
                COUNT(*) FILTER (WHERE g.current_stage IN ('lost','damaged'))   AS issues_count,
                COUNT(*) FILTER (WHERE (g.attributes->>'rewash_count')::int > 0) AS rewash_count,
                AVG(EXTRACT(EPOCH FROM (g.actual_completion_at - g.created_at))/3600)
                    FILTER (WHERE g.actual_completion_at IS NOT NULL)           AS avg_tat_hours
            FROM laundry_fulfillment.fulfillment_unit g
            WHERE g.warehouse_id IS NOT NULL
            GROUP BY g.brand_id, g.warehouse_id, DATE(g.created_at AT TIME ZONE 'Asia/Kolkata');
        $mv$, s);
        EXECUTE format(
            'CREATE UNIQUE INDEX idx_mvwt_unique ON %I.mv_warehouse_throughput(brand_id, warehouse_id, throughput_date);', s);
    END IF;
END
$recreate_mv$;

-- 5. Verification gate --------------------------------------------------------------
DO $verify$
DECLARE attr_col int; old_cols int; mv_expected int; mv_present int;
BEGIN
    SELECT count(*) INTO attr_col FROM information_schema.columns
    WHERE table_schema='laundry_fulfillment' AND table_name='fulfillment_unit' AND column_name='attributes';
    IF attr_col <> 1 THEN RAISE EXCEPTION 'Slice F-2: attributes jsonb column missing'; END IF;

    SELECT count(*) INTO old_cols FROM information_schema.columns
    WHERE table_schema='laundry_fulfillment' AND table_name='fulfillment_unit'
      AND column_name IN ('weight_grams','has_ornaments','has_lining','is_designer_wear','rewash_count','care_instructions');
    IF old_cols <> 0 THEN RAISE EXCEPTION 'Slice F-2: % laundry attribute column(s) still on the spine', old_cols; END IF;

    SELECT count(*) INTO mv_expected FROM _mvwt_schema;
    SELECT count(*) INTO mv_present  FROM pg_matviews WHERE matviewname='mv_warehouse_throughput';
    IF mv_present <> mv_expected THEN
        RAISE EXCEPTION 'Slice F-2: mv_warehouse_throughput not restored (expected %, found %)', mv_expected, mv_present;
    END IF;

    RAISE NOTICE 'Slice F-2 verification passed: 6 attributes moved to jsonb; fabric_type_id retained as FK; matview restored.';
END
$verify$;

COMMIT;

SELECT 'phase1_slice_f2_fulfillment_unit_attributes.sql applied successfully.' AS result;
