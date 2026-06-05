-- =============================================================================
-- db/patches/harden_app_user_and_rls_bypass.sql
--
-- PURPOSE (prod-hardening): make the non-superuser `app_user` role the runtime
-- DB principal for every microservice, so PostgreSQL Row-Level Security is the
-- ENFORCED tenant-isolation backstop at runtime (postgres/superuser bypasses RLS
-- natively, which left app-layer brand predicates as the only guard).
--
-- This patch does TWO things:
--   1. Fixes kernel.rls_bypass() so it recognises the boolean-ish value the
--      application actually emits. The RlsConnectionInterceptor sets
--      app.bypass_rls = 'true'/'false', but the old function only matched 'on'.
--      Result: platform-admin cross-brand reads (and the background worker)
--      silently returned ZERO rows whenever the runtime role was app_user.
--      The fix accepts on/true/1/yes/t (case-insensitive) and stays
--      backward-compatible with the previous 'on' contract.
--   2. Consolidates app_user GRANTs across ALL nine bounded-context schemas
--      (the legacy app_user_role.sql only covered kernel/tenancy_org/
--      identity_access/customer_catalog) plus analytics materialized views,
--      and sets ALTER DEFAULT PRIVILEGES so future objects are covered too.
--
-- Idempotent. Safe to re-run.
--
-- RUN (manual, as postgres):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/harden_app_user_and_rls_bypass.sql
-- =============================================================================

-- 1. ── Fix the RLS bypass predicate ──────────────────────────────────────────
CREATE OR REPLACE FUNCTION kernel.rls_bypass()
RETURNS boolean
LANGUAGE sql
STABLE
AS $function$
    SELECT lower(coalesce(current_setting('app.bypass_rls', true), 'false'))
           IN ('on', 'true', '1', 'yes', 't')
$function$;

-- 2. ── Ensure app_user can log in (idempotent repair) ────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user WITH LOGIN PASSWORD 'app_user' NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END$$;
ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user' NOSUPERUSER NOCREATEDB NOCREATEROLE;

-- 3. ── Grants across every bounded-context schema ────────────────────────────
DO $$
DECLARE
    s text;
    schemas text[] := ARRAY[
        'kernel', 'tenancy_org', 'identity_access', 'customer_catalog',
        'order_lifecycle', 'logistics', 'commerce', 'finance_royalty',
        'engagement_cms', 'analytics'
    ];
BEGIN
    FOREACH s IN ARRAY schemas LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO app_user', s);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO app_user', s);
        EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO app_user', s);
        EXECUTE format('GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA %I TO app_user', s);
        -- future objects
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_user', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO app_user', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT EXECUTE ON FUNCTIONS TO app_user', s);
    END LOOP;
END$$;

-- 4. ── Analytics materialized views (not covered by ON ALL TABLES) ───────────
DO $$
DECLARE
    mv record;
BEGIN
    FOR mv IN SELECT schemaname, matviewname FROM pg_matviews WHERE schemaname = 'analytics' LOOP
        EXECUTE format('GRANT SELECT ON %I.%I TO app_user', mv.schemaname, mv.matviewname);
    END LOOP;
END$$;

SELECT 'harden_app_user_and_rls_bypass.sql applied successfully.' AS result;
