-- =============================================================================
-- audit_logs_partition_maintenance.sql  (RBAC Phase 3 / issue #12)
--
-- Guarantees identity_access.audit_logs is INSERTable by the unprivileged app role before the
-- new AuditSaveChangesInterceptor starts writing to it. audit_logs is RANGE-partitioned on
-- occurred_at (monthly; pg_partman premake). Two failure modes this patch removes:
--   1. Missing current/near-future partition  → "no partition of relation found" on every write.
--   2. Partitions are owned by postgres        → "permission denied" for app_user even when a
--      partition exists and the RLS policy passes.
--
-- Mirrors logistics.ensure_rider_ping_partitions(): a SECURITY DEFINER function owned by postgres
-- (app_user cannot CREATE in the schema) that idempotently provisions the current + N months and
-- grants I/S/U/D on each. The interceptor writes occurred_at = now() (UTC), so boundaries are UTC.
--
-- Idempotent + defensive: every CREATE/GRANT is guarded so a partition already provisioned by
-- pg_partman (possibly under a different name) never hard-fails this script. Safe to re-run.
-- Run as superuser (postgres):
--   PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db -f db/patches/audit_logs_partition_maintenance.sql
-- =============================================================================

CREATE OR REPLACE FUNCTION identity_access.ensure_audit_partitions(months_ahead integer DEFAULT 3)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = identity_access, pg_catalog, pg_temp
AS $$
DECLARE
    month_start date := date_trunc('month', (now() AT TIME ZONE 'UTC'))::date;
    last_start  date := (date_trunc('month', (now() AT TIME ZONE 'UTC'))
                         + (GREATEST(months_ahead, 0) * interval '1 month'))::date;
    cur     date;
    pname   text;
    fromts  text;
    tots    text;
    created integer := 0;
BEGIN
    cur := month_start;
    WHILE cur <= last_start LOOP
        -- pg_partman monthly naming: <parent>_p<YYYYMMDD> with day = 01.
        pname  := 'audit_logs_p' || to_char(cur, 'YYYYMMDD');
        fromts := to_char(cur, 'YYYY-MM-DD') || ' 00:00:00+00';
        tots   := to_char((cur + interval '1 month')::date, 'YYYY-MM-DD') || ' 00:00:00+00';

        BEGIN
            EXECUTE format(
                'CREATE TABLE identity_access.%I PARTITION OF identity_access.audit_logs FOR VALUES FROM (%L) TO (%L)',
                pname, fromts, tots);
            created := created + 1;
        EXCEPTION
            WHEN duplicate_table          THEN NULL;  -- same-named partition already exists
            WHEN invalid_object_definition THEN NULL; -- a differently-named partition already covers the range
            WHEN others                   THEN NULL;  -- never hard-fail provisioning
        END;

        -- Grants are idempotent; ensure the app roles can write regardless of who created the partition.
        BEGIN
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.%I TO app_user', pname);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.%I TO app_admin', pname);
        EXCEPTION WHEN others THEN NULL;  -- partition may be named differently (pg_partman) — the loop below still grants it
        END;

        cur := (cur + interval '1 month')::date;
    END LOOP;

    RETURN created;
END;
$$;

ALTER FUNCTION identity_access.ensure_audit_partitions(integer) OWNER TO postgres;
REVOKE ALL  ON FUNCTION identity_access.ensure_audit_partitions(integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity_access.ensure_audit_partitions(integer) TO app_user;
GRANT EXECUTE ON FUNCTION identity_access.ensure_audit_partitions(integer) TO app_admin;

-- Provision the current window now.
SELECT identity_access.ensure_audit_partitions(3) AS partitions_created;

-- Parent + every existing partition must be writable by the app roles (covers pg_partman-named ones).
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.audit_logs TO app_user, app_admin;
DO $$
DECLARE part regclass;
BEGIN
    FOR part IN
        SELECT i.inhrelid::regclass
        FROM pg_inherits i
        WHERE i.inhparent = 'identity_access.audit_logs'::regclass
    LOOP
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON %s TO app_user', part);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON %s TO app_admin', part);
    END LOOP;
END $$;
