-- =============================================================================
-- user_perm_version.sql  (live permission revocation — perm_version)
--   Adds identity_access.users.perm_version (int, default 0). Stamped into the JWT
--   at issuance; bumped whenever a user's effective permissions change (role grant/
--   revoke, role-cell edit, brand entitlement change). A request bearing a token
--   with a stale perm_version is rejected (→ silent refresh → fresh token), giving
--   near-real-time revocation without waiting for the access-token TTL to lapse.
--   Enforcement is gated by config Auth:EnforceTokenVersion (default off).
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/user_perm_version.sql
-- =============================================================================

BEGIN;

ALTER TABLE identity_access.users
    ADD COLUMN IF NOT EXISTS perm_version integer NOT NULL DEFAULT 0;

-- users is RLS 'admin_only' (readable only under rls_bypass), so a normal request
-- can't SELECT it. Expose just perm_version via a SECURITY DEFINER function (runs as
-- the owner, bypassing RLS) so the per-request token-version guard can read it.
CREATE OR REPLACE FUNCTION kernel.user_perm_version(p_user uuid)
RETURNS integer
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = identity_access, pg_temp
AS $function$
    SELECT perm_version FROM identity_access.users WHERE id = p_user
$function$;

GRANT EXECUTE ON FUNCTION kernel.user_perm_version(uuid) TO app_user, app_admin;

COMMIT;

SELECT 'users.perm_version' AS column,
       (SELECT count(*) FROM information_schema.columns
        WHERE table_schema='identity_access' AND table_name='users' AND column_name='perm_version') AS present;
