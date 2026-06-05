-- =============================================================================
-- db/patches/app_user_role.sql
-- Idempotent: Creates a non-superuser login role `app_user` and grants it
-- the minimum privileges needed for the Identity microservice to operate
-- under PostgreSQL RLS (Row-Level Security).
--
-- PURPOSE:
--   Phase 3 RLS isolation test: swap ConnectionStrings:Default to
--   ConnectionStrings:AppRuntime (app_user / app_user) so the service runs
--   as a restricted role instead of postgres (superuser bypasses RLS).
--
-- HOW TO RUN (manual, one-time):
--   PGPASSWORD=postgres psql -h localhost -p 5432 -U postgres -d laundry_ghar_db \
--     -f db/patches/app_user_role.sql
--
-- DO NOT run in the application startup path — this is a DB-level admin task.
-- =============================================================================

-- 1. Create the role if it doesn't exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user WITH LOGIN PASSWORD 'app_user' NOSUPERUSER NOCREATEDB NOCREATEROLE;
        RAISE NOTICE 'Created role: app_user';
    ELSE
        RAISE NOTICE 'Role app_user already exists — skipping CREATE ROLE.';
    END IF;
END$$;

-- 1a. DEF-001 FIX: unconditionally ensure app_user can log in with a password.
-- rls_proposal.sql creates app_user as NOLOGIN (no password); the IF NOT EXISTS
-- guard above then skips it, leaving the role unable to authenticate. This
-- unconditional ALTER is idempotent and repairs that state on every run.
ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user' NOSUPERUSER NOCREATEDB NOCREATEROLE;

-- 2. Schema USAGE grants
GRANT USAGE ON SCHEMA kernel           TO app_user;
GRANT USAGE ON SCHEMA tenancy_org      TO app_user;
GRANT USAGE ON SCHEMA identity_access  TO app_user;
GRANT USAGE ON SCHEMA customer_catalog TO app_user;

-- 3. Table-level DML on owned schemas
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA kernel           TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy_org      TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity_access  TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA customer_catalog TO app_user;

-- 4. Sequence USAGE (needed for any SERIAL / DEFAULT gen_random_uuid sequences)
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA kernel           TO app_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA tenancy_org      TO app_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA identity_access  TO app_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA customer_catalog TO app_user;

-- 5. Function EXECUTE (kernel utility functions, e.g. set_updated_at triggers)
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA kernel TO app_user;

-- 6. Future-proof: apply same grants to any tables/sequences added later
ALTER DEFAULT PRIVILEGES IN SCHEMA kernel           GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES    TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA tenancy_org      GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES    TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA identity_access  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES    TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA customer_catalog GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES    TO app_user;

ALTER DEFAULT PRIVILEGES IN SCHEMA kernel           GRANT USAGE, SELECT ON SEQUENCES TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA tenancy_org      GRANT USAGE, SELECT ON SEQUENCES TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA identity_access  GRANT USAGE, SELECT ON SEQUENCES TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA customer_catalog GRANT USAGE, SELECT ON SEQUENCES TO app_user;

-- 7. Allow app_user to SET the session vars used by RLS policies
-- (Required so the RlsConnectionInterceptor can call set_config)
-- No explicit grant needed — set_config() is available to all roles.

SELECT 'app_user_role.sql applied successfully.' AS result;
