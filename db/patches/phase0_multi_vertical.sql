-- =============================================================================
-- db/patches/phase0_multi_vertical.sql
--
-- PHASE 0 of the multi-vertical SaaS migration (docs/MULTI_VERTICAL_BLUEPRINT.md).
-- ADDITIVE ONLY — introduces the three discriminators with laundry-preserving
-- defaults. No column drops, no data relocation (those are the Phase-1 XL blockers).
--
--   1. tenancy_org.brands.vertical_key      — the single source of truth (one vertical/brand)
--   2. order_lifecycle.orders.vertical_key  — denormalized from the brand (strategy resolution)
--   3. order_lifecycle.orders.fulfillment_mode
--
-- Phase-0 GATE DECISIONS encoded here:
--   OQ-A.1 (partition key): vertical_key is a PLAIN denormalized column. It does NOT
--     join the orders range-partition key. `orders` stays PARTITION BY RANGE (created_at);
--     a brand has exactly one vertical, so every partition is already vertical-homogeneous
--     and time-range pruning is preserved. Adding vertical_key to the PK would force a
--     re-partition with zero benefit.
--   OQ-A.2 (immutability): a SECURITY DEFINER trigger forbids changing brands.vertical_key
--     once any order exists for that brand (one-vertical-per-brand, enforced cheaply now).
--
-- Existing rows backfill to laundry / process_deliver, so behaviour is unchanged.
-- Idempotent. RUN as postgres (DDL + SECURITY DEFINER owner):
--   PGPASSWORD=postgres psql "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase0_multi_vertical.sql
-- =============================================================================

BEGIN;

-- 1. Brand.vertical_key ───────────────────────────────────────────────────────
ALTER TABLE tenancy_org.brands
    ADD COLUMN IF NOT EXISTS vertical_key VARCHAR(20) NOT NULL DEFAULT 'laundry';

DO $$ BEGIN
    ALTER TABLE tenancy_org.brands
        ADD CONSTRAINT brands_vertical_key_check
        CHECK (vertical_key IN ('laundry','salon','logistics'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- 2. Order.vertical_key + fulfillment_mode ─────────────────────────────────────
-- ADD COLUMN on the partitioned parent cascades to every partition. The DEFAULT
-- backfills existing rows in place.
ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS vertical_key VARCHAR(20) NOT NULL DEFAULT 'laundry';

ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS fulfillment_mode VARCHAR(20) NOT NULL DEFAULT 'process_deliver';

DO $$ BEGIN
    ALTER TABLE order_lifecycle.orders
        ADD CONSTRAINT orders_vertical_key_check
        CHECK (vertical_key IN ('laundry','salon','logistics'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE order_lifecycle.orders
        ADD CONSTRAINT orders_fulfillment_mode_check
        CHECK (fulfillment_mode IN ('process_deliver','appointment','point_to_point'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- Backfill fulfillment_mode from the legacy job_type so the per-order leg topology is exact:
-- parcel jobs are point-to-point; everything else is the laundry process_deliver pipeline.
-- (vertical_key stays the brand's vertical — a laundry brand still runs parcel jobs.)
UPDATE order_lifecycle.orders
   SET fulfillment_mode = 'point_to_point'
 WHERE job_type = 'parcel' AND fulfillment_mode <> 'point_to_point';

-- Partition-pruning-friendly secondary index (vertical first, then the partition key).
CREATE INDEX IF NOT EXISTS idx_orders_vertical
    ON order_lifecycle.orders (vertical_key, created_at DESC)
    WHERE deleted_at IS NULL;

-- 3. OQ-A.2 — brands.vertical_key immutability once orders exist ────────────────
-- SECURITY DEFINER so the existence check is not filtered by the caller's RLS.
CREATE OR REPLACE FUNCTION tenancy_org.enforce_brand_vertical_immutable()
    RETURNS trigger
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = tenancy_org, order_lifecycle, pg_temp
AS $$
BEGIN
    IF NEW.vertical_key IS DISTINCT FROM OLD.vertical_key THEN
        IF EXISTS (SELECT 1 FROM order_lifecycle.orders o WHERE o.brand_id = OLD.id) THEN
            RAISE EXCEPTION
                'brand % vertical_key is immutable once orders exist (% -> %)',
                OLD.id, OLD.vertical_key, NEW.vertical_key
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_brand_vertical_immutable ON tenancy_org.brands;
CREATE TRIGGER trg_brand_vertical_immutable
    BEFORE UPDATE OF vertical_key ON tenancy_org.brands
    FOR EACH ROW
    EXECUTE FUNCTION tenancy_org.enforce_brand_vertical_immutable();

COMMIT;
