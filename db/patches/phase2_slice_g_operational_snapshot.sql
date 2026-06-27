-- =============================================================================
-- db/patches/phase2_slice_g_operational_snapshot.sql
--
-- Multi-vertical Phase 2 · Slice 2G — demote the laundry/logistics fulfilment-leg shift
-- counters (pickups_remaining / deliveries_remaining) on finance_royalty.shift_handovers
-- into an operational_snapshot jsonb off the generic handover spine (blueprint §7.2 Finance;
-- §8 Risk #5: the destructive backfill MUST run before the column drop or the historical
-- shift counters are lost). Backfill-then-drop ordering is enforced in this transaction.
--
-- DESTRUCTIVE + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_g_operational_snapshot.sql
-- =============================================================================

BEGIN;

ALTER TABLE finance_royalty.shift_handovers
    ADD COLUMN IF NOT EXISTS operational_snapshot jsonb NOT NULL DEFAULT '{}'::jsonb;

-- Backfill FIRST (Risk #5) — only while the source columns still exist.
DO $backfill$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='finance_royalty' AND table_name='shift_handovers'
                 AND column_name='pickups_remaining') THEN
        UPDATE finance_royalty.shift_handovers SET operational_snapshot = jsonb_build_object(
            'pickups_remaining',    pickups_remaining,
            'deliveries_remaining', deliveries_remaining);
    END IF;
END
$backfill$;

-- Then drop the demoted columns.
ALTER TABLE finance_royalty.shift_handovers
    DROP COLUMN IF EXISTS pickups_remaining,
    DROP COLUMN IF EXISTS deliveries_remaining;

-- Verification gate -----------------------------------------------------------
DO $verify$
DECLARE has_new int; has_old int;
BEGIN
    SELECT count(*) INTO has_new FROM information_schema.columns
    WHERE table_schema='finance_royalty' AND table_name='shift_handovers' AND column_name='operational_snapshot';
    IF has_new <> 1 THEN RAISE EXCEPTION 'Slice 2G: operational_snapshot column missing'; END IF;

    SELECT count(*) INTO has_old FROM information_schema.columns
    WHERE table_schema='finance_royalty' AND table_name='shift_handovers'
      AND column_name IN ('pickups_remaining','deliveries_remaining');
    IF has_old <> 0 THEN RAISE EXCEPTION 'Slice 2G: % leg-counter column(s) still on the spine', has_old; END IF;

    RAISE NOTICE 'Slice 2G verification passed: shift leg counters moved to operational_snapshot jsonb.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_g_operational_snapshot.sql applied successfully.' AS result;
