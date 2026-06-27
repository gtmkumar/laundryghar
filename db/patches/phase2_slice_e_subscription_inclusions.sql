-- =============================================================================
-- db/patches/phase2_slice_e_subscription_inclusions.sql
--
-- Multi-vertical Phase 2 · Slice 2E — generalize the Commerce subscription plan:
--   1. widen quota_type to add the vertical-neutral 'job_count' (order_count retained),
--   2. demote the fulfilment-leg flags (pickup/delivery/express_included) into a
--      fulfillment_inclusions jsonb off the generic plan spine — a salon appointment plan
--      has no pickup leg. jsonb keys match the EF owned-type ToJson mapping.
--
-- DESTRUCTIVE (drops the 3 flag columns after backfill) + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_e_subscription_inclusions.sql
-- =============================================================================

BEGIN;

-- 1. Widen quota_type CHECK to add job_count -------------------------------------
DO $relax$
DECLARE c record;
BEGIN
    FOR c IN
        SELECT con.conname FROM pg_constraint con
        JOIN pg_class rel     ON rel.oid = con.conrelid
        JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
        WHERE nsp.nspname='commerce' AND rel.relname='subscription_plans' AND con.contype='c'
          AND pg_get_constraintdef(con.oid) ILIKE '%quota_type%'
    LOOP
        EXECUTE format('ALTER TABLE commerce.subscription_plans DROP CONSTRAINT %I;', c.conname);
    END LOOP;
    ALTER TABLE commerce.subscription_plans
        ADD CONSTRAINT subscription_plans_quota_type_check
        CHECK (quota_type IN ('credit','order_count','job_count','weight_kg','unlimited'));
END
$relax$;

-- 2. fulfillment_inclusions jsonb + backfill + drop the 3 flag columns -----------
ALTER TABLE commerce.subscription_plans
    ADD COLUMN IF NOT EXISTS fulfillment_inclusions jsonb NOT NULL DEFAULT '{}'::jsonb;

DO $backfill$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='commerce' AND table_name='subscription_plans'
                 AND column_name='pickup_included') THEN
        UPDATE commerce.subscription_plans SET fulfillment_inclusions = jsonb_build_object(
            'pickup_included',   pickup_included,
            'delivery_included', delivery_included,
            'express_included',  express_included
        );
    END IF;
END
$backfill$;

ALTER TABLE commerce.subscription_plans
    DROP COLUMN IF EXISTS pickup_included,
    DROP COLUMN IF EXISTS delivery_included,
    DROP COLUMN IF EXISTS express_included;

-- 3. Verification gate ----------------------------------------------------------
DO $verify$
DECLARE jsonb_col int; old_cols int; def text;
BEGIN
    SELECT count(*) INTO jsonb_col FROM information_schema.columns
    WHERE table_schema='commerce' AND table_name='subscription_plans' AND column_name='fulfillment_inclusions';
    IF jsonb_col <> 1 THEN RAISE EXCEPTION 'Slice 2E: fulfillment_inclusions column missing'; END IF;

    SELECT count(*) INTO old_cols FROM information_schema.columns
    WHERE table_schema='commerce' AND table_name='subscription_plans'
      AND column_name IN ('pickup_included','delivery_included','express_included');
    IF old_cols <> 0 THEN RAISE EXCEPTION 'Slice 2E: % flag column(s) still on the plan spine', old_cols; END IF;

    SELECT pg_get_constraintdef(con.oid) INTO def FROM pg_constraint con
    JOIN pg_class rel     ON rel.oid = con.conrelid
    JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
    WHERE nsp.nspname='commerce' AND rel.relname='subscription_plans' AND con.conname='subscription_plans_quota_type_check';
    IF def IS NULL OR def NOT ILIKE '%job_count%' THEN RAISE EXCEPTION 'Slice 2E: quota_type CHECK missing job_count'; END IF;

    RAISE NOTICE 'Slice 2E verification passed: job_count added; fulfilment flags moved to jsonb.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_e_subscription_inclusions.sql applied successfully.' AS result;
