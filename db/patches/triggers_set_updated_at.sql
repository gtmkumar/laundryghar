-- ============================================================================
-- triggers_set_updated_at.sql
-- ----------------------------------------------------------------------------
-- Defines kernel.set_updated_at() and attaches a BEFORE UPDATE trigger to
-- every table that has an `updated_at` column across the 11 BC schemas.
--
-- Behaviour: if the caller did NOT change updated_at as part of the UPDATE,
-- the trigger bumps it to now(). Callers that explicitly set updated_at
-- (e.g. migrations, backfills) keep their value. This satisfies the
-- HANDOFF §11 convention without breaking deliberate timestamp overrides.
--
-- Idempotent: re-running drops and re-creates triggers cleanly. New tables
-- with an updated_at column get a trigger on the next run.
--
-- Partitioned parents (orders, audit_logs, process_logs, notifications_log,
-- rider_location_pings) — only audit_logs has no updated_at; the others
-- get the trigger on the parent and it propagates to every partition.
-- ============================================================================
SET client_min_messages = WARNING;

CREATE OR REPLACE FUNCTION kernel.set_updated_at() RETURNS trigger
LANGUAGE plpgsql AS $fn$
BEGIN
    IF NEW.updated_at IS NOT DISTINCT FROM OLD.updated_at THEN
        NEW.updated_at := now();
    END IF;
    RETURN NEW;
END
$fn$;

DO $apply$
DECLARE
    r        RECORD;
    trg_name TEXT;
    n_done   INT := 0;
BEGIN
    FOR r IN
        SELECT n.nspname AS schema, c.relname AS tbl
        FROM   pg_class     c
        JOIN   pg_namespace n ON n.oid = c.relnamespace
        JOIN   pg_attribute a ON a.attrelid = c.oid
        WHERE  c.relkind IN ('r','p')
          AND  c.relispartition = false
          AND  a.attname = 'updated_at'
          AND  NOT a.attisdropped
          AND  n.nspname IN (
                 'kernel','tenancy_org','identity_access','customer_catalog',
                 'order_lifecycle','logistics','commerce','finance_royalty',
                 'engagement_cms','analytics')
        ORDER  BY n.nspname, c.relname
    LOOP
        trg_name := 'trg_' || r.tbl || '_set_updated_at';
        EXECUTE format(
            'DROP TRIGGER IF EXISTS %I ON %I.%I',
            trg_name, r.schema, r.tbl);
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE UPDATE ON %I.%I '
            'FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at()',
            trg_name, r.schema, r.tbl);
        n_done := n_done + 1;
    END LOOP;
    RAISE NOTICE 'attached set_updated_at trigger to % tables', n_done;
END
$apply$;
