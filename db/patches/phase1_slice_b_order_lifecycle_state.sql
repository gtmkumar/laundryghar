-- =============================================================================
-- db/patches/phase1_slice_b_order_lifecycle_state.sql
--
-- Multi-vertical Phase 1 · Slice B — widen OrderStatus into "generic super-states
-- + strategy sub-status" (blueprint §7 Phase 1, XL blocker #1).
--
--   * orders.lifecycle_state  NEW generic, vertical-neutral super-state
--                             (created|active|completed|cancelled|closed) — the spine's
--                             status; CHECK-constrained (neutral vocabulary, safe to pin).
--   * orders.status           stays the strategy's DETAILED sub-status, but its closed
--                             laundry-vocabulary CHECK (orders_status_check) is DROPPED —
--                             the IFulfillmentStrategy (EnsureTransition / IsKnownStatus) is
--                             now the source of truth for sub-status validity, so future
--                             verticals (salon, …) need no DB change to add statuses.
--
-- The backfill CASE MUST stay in lockstep with
--   laundryghar.SharedDataModel.Enums.OrderLifecycleState.ForOrderStatus(...)
-- and StateMachineStrategyBase.LifecycleStateFor(...).
--
-- `orders` is RANGE-partitioned: ADD COLUMN / ADD CONSTRAINT / SET NOT NULL on the parent
-- propagate to all partitions; the UPDATE backfill routes through the parent. The DROP of
-- orders_status_check is done on the parent (cascades to inherited children) plus a defensive
-- sweep for any independently-defined per-partition copies.
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase1_slice_b_order_lifecycle_state.sql
-- =============================================================================

BEGIN;

-- 1. Column (nullable + default for expand-contract rollout safety) -----------
--    DEFAULT 'created' so any insert during the rollout window (DB migrated before
--    app deploy) lands a valid value — new orders start at placed → created.
ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS lifecycle_state varchar(20) NOT NULL DEFAULT 'created';

-- 2. Backfill existing rows from their detailed status (mirrors ForOrderStatus). --
--    Routes through the partitioned parent to every partition.
UPDATE order_lifecycle.orders
SET    lifecycle_state = CASE status
           WHEN 'placed'    THEN 'created'
           WHEN 'cancelled' THEN 'cancelled'
           WHEN 'closed'    THEN 'closed'
           WHEN 'delivered' THEN 'completed'
           WHEN 'returned'  THEN 'completed'
           ELSE 'active'
       END
WHERE  lifecycle_state IS DISTINCT FROM CASE status
           WHEN 'placed'    THEN 'created'
           WHEN 'cancelled' THEN 'cancelled'
           WHEN 'closed'    THEN 'closed'
           WHEN 'delivered' THEN 'completed'
           WHEN 'returned'  THEN 'completed'
           ELSE 'active'
       END;

-- 3. Constrain the NEW neutral super-state column ----------------------------
DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'orders_lifecycle_state_check'
                     AND conrelid = 'order_lifecycle.orders'::regclass) THEN
        ALTER TABLE order_lifecycle.orders
            ADD CONSTRAINT orders_lifecycle_state_check
            CHECK (lifecycle_state IN ('created','active','completed','cancelled','closed'));
    END IF;
END
$chk$;

-- 4. RELAX the detailed-status vocabulary: drop the closed laundry CHECK -------
--    Strategy layer now owns sub-status validity. Parent drop cascades to inherited
--    children; the loop mops up any independently-defined per-partition copies.
ALTER TABLE order_lifecycle.orders DROP CONSTRAINT IF EXISTS orders_status_check;
DO $drop$
DECLARE r record;
BEGIN
    FOR r IN
        SELECT conrelid::regclass AS tbl
        FROM   pg_constraint
        WHERE  conname = 'orders_status_check' AND contype = 'c'
    LOOP
        EXECUTE format('ALTER TABLE %s DROP CONSTRAINT IF EXISTS orders_status_check;', r.tbl);
    END LOOP;
END
$drop$;

-- 5. Verification gate -------------------------------------------------------
DO $verify$
DECLARE
    bad_state      int;
    drift          int;
    status_chk_cnt int;
BEGIN
    -- 5a. Every row carries a valid neutral super-state.
    SELECT count(*) INTO bad_state FROM order_lifecycle.orders
    WHERE lifecycle_state NOT IN ('created','active','completed','cancelled','closed');
    IF bad_state <> 0 THEN
        RAISE EXCEPTION 'Slice B: % order(s) with an invalid lifecycle_state', bad_state;
    END IF;

    -- 5b. lifecycle_state agrees with the mapping for every row (no drift).
    SELECT count(*) INTO drift FROM order_lifecycle.orders
    WHERE lifecycle_state <> CASE status
              WHEN 'placed' THEN 'created' WHEN 'cancelled' THEN 'cancelled'
              WHEN 'closed' THEN 'closed'  WHEN 'delivered' THEN 'completed'
              WHEN 'returned' THEN 'completed' ELSE 'active' END;
    IF drift <> 0 THEN
        RAISE EXCEPTION 'Slice B: lifecycle_state disagrees with status mapping on % row(s)', drift;
    END IF;

    -- 5c. The laundry-vocabulary status CHECK is fully gone (parent + partitions).
    SELECT count(*) INTO status_chk_cnt FROM pg_constraint
    WHERE conname = 'orders_status_check' AND contype = 'c';
    IF status_chk_cnt <> 0 THEN
        RAISE EXCEPTION 'Slice B: orders_status_check still present on % relation(s)', status_chk_cnt;
    END IF;

    RAISE NOTICE 'Slice B verification passed: lifecycle_state backfilled & constrained; status vocabulary relaxed.';
END
$verify$;

COMMIT;

SELECT 'phase1_slice_b_order_lifecycle_state.sql applied successfully.' AS result;
