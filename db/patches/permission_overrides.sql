-- =============================================================================
-- permission_overrides.sql  (allow/deny effect + per-user overrides — gap #4)
--   1. identity_access.role_permissions.effect  — 'allow' (default) | 'deny'.
--      Deny wins, so a broad role can have precise exceptions without a new role.
--   2. identity_access.user_permission_override  — per-user allow/deny on a single
--      permission, layered on top of role grants (deny always wins).
--   Effective = (role-allowed − role-denied ∪ user-allow) − user-deny.
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/permission_overrides.sql
-- =============================================================================

BEGIN;

ALTER TABLE identity_access.role_permissions
    ADD COLUMN IF NOT EXISTS effect varchar NOT NULL DEFAULT 'allow'
        CHECK (effect IN ('allow', 'deny'));

CREATE TABLE IF NOT EXISTS identity_access.user_permission_override (
    user_id       uuid    NOT NULL REFERENCES identity_access.users(id) ON DELETE CASCADE,
    permission_id uuid    NOT NULL REFERENCES identity_access.permissions(id) ON DELETE CASCADE,
    effect        varchar NOT NULL DEFAULT 'allow' CHECK (effect IN ('allow', 'deny')),
    granted_at    timestamptz NOT NULL DEFAULT now(),
    granted_by    uuid NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, permission_id)
);
CREATE INDEX IF NOT EXISTS ix_user_perm_override_user ON identity_access.user_permission_override (user_id);

-- RLS: a user may read their own overrides; admin operations run under rls_bypass.
ALTER TABLE identity_access.user_permission_override ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_user_self ON identity_access.user_permission_override;
CREATE POLICY rls_user_self ON identity_access.user_permission_override
    USING (kernel.rls_bypass() OR (user_id = kernel.current_user_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.user_permission_override TO app_user, app_admin;

COMMIT;

SELECT 'role_permissions.effect' AS what,
       (SELECT count(*) FROM information_schema.columns
        WHERE table_schema='identity_access' AND table_name='role_permissions' AND column_name='effect') AS present
UNION ALL
SELECT 'user_permission_override table',
       (SELECT count(*) FROM information_schema.tables
        WHERE table_schema='identity_access' AND table_name='user_permission_override');
