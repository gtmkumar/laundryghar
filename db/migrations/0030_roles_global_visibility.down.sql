-- Down for 0030. Restores the single generic rls_brand template on identity_access.roles exactly
-- as db/patches/rls_proposal.sql wrote it — including the NULL-brand blind spot, which is the
-- state this migration exists to leave.

SET client_min_messages = WARNING;

DROP POLICY IF EXISTS rls_roles_select ON identity_access.roles;
DROP POLICY IF EXISTS rls_roles_insert ON identity_access.roles;
DROP POLICY IF EXISTS rls_roles_update ON identity_access.roles;
DROP POLICY IF EXISTS rls_roles_delete ON identity_access.roles;

DROP POLICY IF EXISTS rls_brand ON identity_access.roles;
CREATE POLICY rls_brand ON identity_access.roles FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
