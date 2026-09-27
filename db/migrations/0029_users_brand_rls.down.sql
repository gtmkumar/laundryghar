-- Down for 0029. Restores identity_access.users to its pre-migration state: row security off,
-- carrying the inert rls_admin_only policy it had before.

ALTER TABLE identity_access.users DISABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_users_select ON identity_access.users;
DROP POLICY IF EXISTS rls_users_insert ON identity_access.users;
DROP POLICY IF EXISTS rls_users_update ON identity_access.users;
DROP POLICY IF EXISTS rls_users_delete ON identity_access.users;

-- Recreated exactly as it was, including the fact that it never ran.
DROP POLICY IF EXISTS rls_admin_only ON identity_access.users;
CREATE POLICY rls_admin_only ON identity_access.users FOR ALL TO app_user
    USING (kernel.rls_bypass())
    WITH CHECK (kernel.rls_bypass());

DROP FUNCTION IF EXISTS identity_access.user_in_brand(uuid, uuid);
