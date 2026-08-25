-- 0012_recurring_fulfillment_mode — forward migration
--
-- PLATFORM_STRATEGY.md §3 Mode 3, "Recurring Schedule — repeat delivery calendar (tiffin · milk ·
-- water cans)". The last of the three product modes to exist.
--
-- ─── OQ-2 resolved: one order PER OCCURRENCE, not one long-lived order ───────────────────────
-- The open question was whether a recurring booking materialises an order per delivery, or a single
-- long-lived order carrying occurrence rows. The architecture already answers it:
--
--   `order_lifecycle.orders` is the spine EVERYTHING hangs off — payments, delivery_assignments,
--   invoices, order_status_history, rider tasks. A long-lived order would need a parallel structure
--   for each of those (which payment settled which occurrence? which rider took which delivery?),
--   duplicating the entire order surface for one vertical. One order per occurrence reuses all of it
--   unchanged: a tiffin delivery is dispatched, paid for, and tracked exactly like any other order.
--
-- So a SCHEDULE is the recurring thing, and it emits ordinary orders. That also keeps the monthly
-- range-partitioning of `orders` meaningful — occurrences land in the month they happen, rather than
-- one row living forever in the month it was created.
--
-- Billing is untouched: commerce.customer_subscriptions already exists for what the customer PAYS.
-- This is what they RECEIVE. The two are deliberately separate — a paused subscription should stop
-- generating deliveries, but a missed delivery should not cancel a subscription.

-- ─── 1. Register the mode ────────────────────────────────────────────────────────────────────
DO $$
DECLARE c record;
BEGIN
    FOR c IN
        SELECT con.conname, rel.relname, nsp.nspname
        FROM   pg_constraint con
        JOIN   pg_class rel     ON rel.oid = con.conrelid
        JOIN   pg_namespace nsp ON nsp.oid = rel.relnamespace
        WHERE  con.contype = 'c'
          AND  pg_get_constraintdef(con.oid) ILIKE '%process_deliver%'
          AND  pg_get_constraintdef(con.oid) NOT ILIKE '%recurring%'
          -- Scope to the orders spine ONLY. identity_access.vertical_templates carries its own
          -- fulfillment_mode CHECK (migration 0011) that legitimately includes 'recurring' for the
          -- tiffin template; sweeping it up here made the rollback fail against its own seed row.
          AND  rel.relname = 'orders'
          AND  NOT rel.relispartition
    LOOP
        EXECUTE format('ALTER TABLE %I.%I DROP CONSTRAINT %I', c.nspname, c.relname, c.conname);
        EXECUTE format(
            'ALTER TABLE %I.%I ADD CONSTRAINT %I CHECK (fulfillment_mode::text = ANY '
            '(ARRAY[''process_deliver'',''appointment'',''point_to_point'',''recurring'']::text[]))',
            c.nspname, c.relname, c.conname);
        RAISE NOTICE 'widened %.% (%)', c.nspname, c.relname, c.conname;
    END LOOP;
END $$;

-- ─── 2. The schedule that emits them ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS order_lifecycle.delivery_schedules (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id       UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE RESTRICT,
    customer_id    UUID NOT NULL REFERENCES customer_catalog.customers(id) ON DELETE CASCADE,
    store_id       UUID NULL REFERENCES tenancy_org.stores(id) ON DELETE SET NULL,
    address_id     UUID NULL REFERENCES customer_catalog.customer_addresses(id) ON DELETE SET NULL,

    -- What recurs. Kept as a snapshot rather than a live join to the catalogue: a price or item
    -- rename must not retroactively change deliveries already scheduled.
    plan_snapshot  JSONB NOT NULL DEFAULT '{}'::jsonb,

    cadence        VARCHAR(20) NOT NULL
                   CHECK (cadence IN ('daily','weekdays','weekly','custom')),
    -- ISO-8601 weekday numbers, 1 = Monday .. 7 = Sunday. Empty for `daily`.
    days_of_week   SMALLINT[] NOT NULL DEFAULT '{}',
    -- Local delivery time; the brand's timezone lives on tenancy_org.brands.
    delivery_time  TIME NOT NULL DEFAULT '09:00',

    starts_on      DATE NOT NULL,
    ends_on        DATE NULL,                      -- NULL = open-ended
    -- The next date an order should be materialised for. Advanced by the generator; also the thing
    -- that makes generation idempotent and restartable.
    next_run_on    DATE NULL,

    status         VARCHAR(20) NOT NULL DEFAULT 'active'
                   CHECK (status IN ('active','paused','cancelled','completed')),

    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by     UUID, updated_by UUID,

    CHECK (ends_on IS NULL OR ends_on >= starts_on),
    -- `custom` and `weekly` are meaningless without days; `daily`/`weekdays` derive their own.
    CHECK (cadence NOT IN ('weekly','custom') OR array_length(days_of_week, 1) > 0)
);

CREATE INDEX IF NOT EXISTS idx_delivery_schedules_due
    ON order_lifecycle.delivery_schedules (next_run_on)
    WHERE status = 'active';
CREATE INDEX IF NOT EXISTS idx_delivery_schedules_customer
    ON order_lifecycle.delivery_schedules (customer_id);

ALTER TABLE order_lifecycle.delivery_schedules ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON order_lifecycle.delivery_schedules;
CREATE POLICY rls_brand ON order_lifecycle.delivery_schedules
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON order_lifecycle.delivery_schedules TO app_user, app_admin;

DROP TRIGGER IF EXISTS trg_delivery_schedules_set_updated_at ON order_lifecycle.delivery_schedules;
CREATE TRIGGER trg_delivery_schedules_set_updated_at
    BEFORE UPDATE ON order_lifecycle.delivery_schedules
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ─── 3. Link each generated order back to the schedule that produced it ──────────────────────
ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS delivery_schedule_id UUID NULL;
ALTER TABLE order_lifecycle.orders
    ADD COLUMN IF NOT EXISTS scheduled_for DATE NULL;

COMMENT ON COLUMN order_lifecycle.orders.delivery_schedule_id IS
    'The recurring schedule that materialised this order (Mode 3). NULL for every one-off order.';

-- ─── 4. The occurrence ledger — where idempotency actually lives ─────────────────────────────
-- The obvious design is a partial UNIQUE index on orders(delivery_schedule_id, scheduled_for). It
-- is not available: `orders` is RANGE-partitioned by created_at, and Postgres requires every unique
-- index on a partitioned table to include the partition key —
--     ERROR: unique constraint on partitioned table must include all partitioning columns
--     DETAIL: UNIQUE constraint on table "orders" lacks column "created_at"
-- Adding created_at would defeat the point: two orders for the same schedule and date created in
-- different months would both satisfy it, which is exactly the double-delivery being prevented.
--
-- So the guarantee lives in its own un-partitioned ledger. The generator claims an occurrence here
-- FIRST and creates the order second; a re-run, a retry, or two workers racing all collide on this
-- primary key and skip. It also gives failed generation somewhere to be recorded rather than
-- vanishing.
CREATE TABLE IF NOT EXISTS order_lifecycle.delivery_schedule_occurrences (
    schedule_id   UUID NOT NULL REFERENCES order_lifecycle.delivery_schedules(id) ON DELETE CASCADE,
    scheduled_for DATE NOT NULL,
    brand_id      UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE RESTRICT,
    -- Set once the order exists. NULL + status='failed' is a generation that needs attention.
    order_id      UUID NULL,
    status        VARCHAR(20) NOT NULL DEFAULT 'generated'
                  CHECK (status IN ('generated','skipped','failed')),
    failure_reason TEXT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (schedule_id, scheduled_for)
);

CREATE INDEX IF NOT EXISTS idx_schedule_occurrences_brand
    ON order_lifecycle.delivery_schedule_occurrences (brand_id, scheduled_for);

ALTER TABLE order_lifecycle.delivery_schedule_occurrences ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON order_lifecycle.delivery_schedule_occurrences;
CREATE POLICY rls_brand ON order_lifecycle.delivery_schedule_occurrences
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE
    ON order_lifecycle.delivery_schedule_occurrences TO app_user, app_admin;

COMMENT ON TABLE order_lifecycle.delivery_schedule_occurrences IS
    'Idempotency ledger for Mode 3 generation: one row per (schedule, date). Exists because orders '
    'is range-partitioned and cannot carry the unique index this guarantee needs.';

-- Tiffin can now be offered: its template''s fulfilment mode exists.
UPDATE identity_access.vertical_templates SET is_public = true, updated_at = now()
WHERE  key = 'tiffin';
