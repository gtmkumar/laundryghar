-- =============================================================================
-- db/patches/phase1_slice_c_laundry_fulfillment.sql
--
-- Multi-vertical Phase 1 · Slice C — relocate the 11 laundry-fulfilment tables
-- out of the shared `order_lifecycle` schema (where they sit beside the
-- vertical-neutral order spine) into their own `laundry_fulfillment` schema, so
-- the laundry wash/QC pipeline becomes a private detail of LaundryProcessStrategy.
-- See docs/MULTI_VERTICAL_SLICE_C.md and docs/MULTI_VERTICAL_BLUEPRINT.md §7 (P1).
--
-- The 11 tables (9 parents + 2 child tables):
--   garments, garment_tags, garment_conditions, garment_inspections,
--   garment_inspection_photos, warehouse_processes, warehouse_batches,
--   stock_reconciliations, stock_reconciliation_items, process_logs, quality_checks
-- STAYS in order_lifecycle (shared spine): orders, order_items, order_addons,
--   order_notes, order_status_history, invoices, pickup_requests,
--   delivery_assignments, delivery_slots, delivery_slot_bookings.
--
-- Non-destructive: ALTER TABLE ... SET SCHEMA is metadata-only. It carries each
-- table's indexes, constraints, RLS policies (their bodies reference no schema),
-- and owned sequences across, and PRESERVES every FK by OID (inbound + outbound) —
-- nothing breaks despite the qualified name change. Brief ACCESS EXCLUSIVE lock
-- per table; no data copy.
--
-- PARTITIONING: `process_logs` is RANGE-partitioned. SET SCHEMA on a partitioned
-- parent does NOT move its child partitions (each is an independent table; a
-- cross-schema partition hierarchy is legal). We move the parent AND every
-- attached partition dynamically via pg_inherits, so it stays correct as new
-- monthly partitions are added.
--
-- Idempotent: re-running is a no-op (tables already in laundry_fulfillment are
-- skipped). RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase1_slice_c_laundry_fulfillment.sql
-- =============================================================================

BEGIN;

-- 1. Target schema -----------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS laundry_fulfillment;

-- 2. Pre-move snapshot (for the post-move parity assertions in §6) -----------
CREATE TEMP TABLE _slice_c_pre ON COMMIT DROP AS
SELECT c.relname,
       (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid)         AS n_policies,
       (SELECT count(*) FROM pg_constraint k WHERE k.conrelid = c.oid
                                              AND k.contype = 'f')         AS n_fks_out,
       (SELECT count(*) FROM pg_constraint k WHERE k.confrelid = c.oid
                                              AND k.contype = 'f')         AS n_fks_in
FROM   pg_class c
JOIN   pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'order_lifecycle'
WHERE  c.relkind IN ('r','p')
  AND  c.relname IN (
        'garments','garment_tags','garment_conditions','garment_inspections',
        'garment_inspection_photos','warehouse_processes','warehouse_batches',
        'stock_reconciliations','stock_reconciliation_items','process_logs','quality_checks');

-- 3. Move the 11 logical tables (parents; process_logs is the partitioned one).
--    Lookups/parents first for readability — order is irrelevant to SET SCHEMA.
DO $move$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'garment_conditions','warehouse_processes','warehouse_batches',
        'garments','garment_tags','garment_inspections','garment_inspection_photos',
        'stock_reconciliations','stock_reconciliation_items','process_logs','quality_checks'
    ] LOOP
        IF EXISTS (SELECT 1 FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'order_lifecycle' AND c.relname = t) THEN
            EXECUTE format('ALTER TABLE order_lifecycle.%I SET SCHEMA laundry_fulfillment;', t);
        END IF;
    END LOOP;
END
$move$;

-- 4. Move process_logs' child partitions (still in order_lifecycle) to follow
--    their parent, now in laundry_fulfillment. Dynamic — covers every partition
--    regardless of name or how many monthly ones exist.
DO $parts$
DECLARE part record;
BEGIN
    FOR part IN
        SELECT child.relname
        FROM   pg_inherits ih
        JOIN   pg_class  parent  ON parent.oid = ih.inhparent
        JOIN   pg_namespace pn   ON pn.oid = parent.relnamespace
        JOIN   pg_class  child   ON child.oid  = ih.inhrelid
        JOIN   pg_namespace cn   ON cn.oid = child.relnamespace
        WHERE  parent.relname = 'process_logs'
          AND  pn.nspname = 'laundry_fulfillment'
          AND  cn.nspname = 'order_lifecycle'
    LOOP
        EXECUTE format('ALTER TABLE order_lifecycle.%I SET SCHEMA laundry_fulfillment;', part.relname);
    END LOOP;
END
$parts$;

-- 5. Grants — table-level CRUD grants MOVED WITH each table (grants are per-object,
--    not per-schema), so only the schema-level USAGE + default privileges are new.
--    Mirrors rls_proposal.sql §2.5 for app_user / app_admin. Re-grant on tables is
--    additive/idempotent and harmless.
GRANT USAGE ON SCHEMA laundry_fulfillment TO app_user, app_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA laundry_fulfillment TO app_user, app_admin;
GRANT USAGE, SELECT                     ON ALL SEQUENCES IN SCHEMA laundry_fulfillment TO app_user, app_admin;
ALTER DEFAULT PRIVILEGES IN SCHEMA laundry_fulfillment GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES    TO app_user, app_admin;
ALTER DEFAULT PRIVILEGES IN SCHEMA laundry_fulfillment GRANT USAGE, SELECT                  ON SEQUENCES TO app_user, app_admin;

-- 6. Verification gate — fail the transaction if anything diverged ------------
DO $verify$
DECLARE
    moved_parents int;
    still_behind  int;
    pol_drift     int;
    fk_drift      int;
    parent_parts  int;
BEGIN
    -- 6a. All 11 logical tables now live in laundry_fulfillment.
    SELECT count(*) INTO moved_parents
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'laundry_fulfillment'
      AND c.relname IN ('garments','garment_tags','garment_conditions','garment_inspections',
            'garment_inspection_photos','warehouse_processes','warehouse_batches',
            'stock_reconciliations','stock_reconciliation_items','process_logs','quality_checks');
    IF moved_parents <> 11 THEN
        RAISE EXCEPTION 'Slice C: expected 11 tables in laundry_fulfillment, found %', moved_parents;
    END IF;

    -- 6b. None of the 11 left behind in order_lifecycle.
    SELECT count(*) INTO still_behind
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'order_lifecycle'
      AND c.relname IN ('garments','garment_tags','garment_conditions','garment_inspections',
            'garment_inspection_photos','warehouse_processes','warehouse_batches',
            'stock_reconciliations','stock_reconciliation_items','process_logs','quality_checks');
    IF still_behind <> 0 THEN
        RAISE EXCEPTION 'Slice C: % of the 11 tables still in order_lifecycle', still_behind;
    END IF;

    -- 6c. No process_logs partition left stranded in order_lifecycle.
    SELECT count(*) INTO parent_parts
    FROM pg_inherits ih
    JOIN pg_class parent ON parent.oid = ih.inhparent AND parent.relname = 'process_logs'
    JOIN pg_class child  ON child.oid  = ih.inhrelid
    JOIN pg_namespace cn ON cn.oid = child.relnamespace AND cn.nspname = 'order_lifecycle';
    IF parent_parts <> 0 THEN
        RAISE EXCEPTION 'Slice C: % process_logs partition(s) stranded in order_lifecycle', parent_parts;
    END IF;

    -- 6d. RLS policy count per table unchanged across the move.
    SELECT count(*) INTO pol_drift
    FROM _slice_c_pre pre
    JOIN pg_class c ON c.relname = pre.relname
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'laundry_fulfillment'
    WHERE pre.n_policies <> (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid);
    IF pol_drift <> 0 THEN
        RAISE EXCEPTION 'Slice C: RLS policy count drifted on % table(s)', pol_drift;
    END IF;

    -- 6e. FK count (inbound + outbound) per table unchanged across the move.
    SELECT count(*) INTO fk_drift
    FROM _slice_c_pre pre
    JOIN pg_class c ON c.relname = pre.relname
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'laundry_fulfillment'
    WHERE pre.n_fks_out <> (SELECT count(*) FROM pg_constraint k WHERE k.conrelid = c.oid AND k.contype='f')
       OR pre.n_fks_in  <> (SELECT count(*) FROM pg_constraint k WHERE k.confrelid = c.oid AND k.contype='f');
    IF fk_drift <> 0 THEN
        RAISE EXCEPTION 'Slice C: FK count drifted on % table(s)', fk_drift;
    END IF;

    RAISE NOTICE 'Slice C verification passed: 11 tables + all process_logs partitions relocated; policies & FKs intact.';
END
$verify$;

COMMIT;

SELECT 'phase1_slice_c_laundry_fulfillment.sql applied successfully.' AS result;
