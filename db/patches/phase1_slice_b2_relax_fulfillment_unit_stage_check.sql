-- =============================================================================
-- db/patches/phase1_slice_b2_relax_fulfillment_unit_stage_check.sql
--
-- Multi-vertical Phase 1 · Slice B-2 — relax the enumerated current_stage CHECK on
-- laundry_fulfillment.fulfillment_unit. Companion to slice B (which relaxed the
-- orders.status CHECK): the blueprint §1 calls out that the garments.current_stage CHECK
-- "bakes the wash/QC pipeline into shared schema". The detailed stage vocabulary is owned
-- by the fulfilment STRATEGY in code (LaundryProcessStrategy), not the database — so the
-- schema should not hardcode the laundry stage list.
--
-- The table is now laundry-private (laundry_fulfillment schema), but a frozen enumerated
-- whitelist still blocks a strategy from evolving its own stages without a migration. We
-- replace it with a lightweight non-empty sanity constraint; correctness of the stage value
-- is enforced by the strategy.
--
-- The constraint was inline-named garments_current_stage_check at table-create time and kept
-- that name through the slice-F rename; we drop it by discovery to be robust.
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase1_slice_b2_relax_fulfillment_unit_stage_check.sql
-- =============================================================================

BEGIN;

-- 1. Drop any CHECK constraint on fulfillment_unit that pins current_stage to a value list.
DO $drop_check$
DECLARE c record;
BEGIN
    FOR c IN
        SELECT con.conname
        FROM pg_constraint con
        JOIN pg_class rel        ON rel.oid = con.conrelid
        JOIN pg_namespace nsp    ON nsp.oid = rel.relnamespace
        WHERE nsp.nspname = 'laundry_fulfillment'
          AND rel.relname = 'fulfillment_unit'
          AND con.contype = 'c'
          AND pg_get_constraintdef(con.oid) ILIKE '%current_stage%'
    LOOP
        EXECUTE format('ALTER TABLE laundry_fulfillment.fulfillment_unit DROP CONSTRAINT %I;', c.conname);
    END LOOP;
END
$drop_check$;

-- 2. Add a lightweight non-empty sanity constraint (strategy owns the real vocabulary).
ALTER TABLE laundry_fulfillment.fulfillment_unit
    ADD CONSTRAINT fulfillment_unit_current_stage_nonempty
    CHECK (char_length(current_stage) > 0);

-- 3. Verification gate: no enumerated list constraint remains on current_stage.
DO $verify$
DECLARE enumerated int;
BEGIN
    SELECT count(*) INTO enumerated
    FROM pg_constraint con
    JOIN pg_class rel     ON rel.oid = con.conrelid
    JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
    WHERE nsp.nspname = 'laundry_fulfillment'
      AND rel.relname = 'fulfillment_unit'
      AND con.contype = 'c'
      AND pg_get_constraintdef(con.oid) ILIKE '%current_stage IN (%';
    IF enumerated <> 0 THEN
        RAISE EXCEPTION 'Slice B-2: an enumerated current_stage CHECK still exists (% found)', enumerated;
    END IF;

    RAISE NOTICE 'Slice B-2 verification passed: current_stage CHECK relaxed to non-empty; stage vocabulary owned by the strategy.';
END
$verify$;

COMMIT;

SELECT 'phase1_slice_b2_relax_fulfillment_unit_stage_check.sql applied successfully.' AS result;
