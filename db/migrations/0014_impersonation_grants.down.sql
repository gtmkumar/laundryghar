-- 0014_impersonation_grants — rollback
--
-- Drops the consent table and its permissions. The audit COLUMN is dropped too: it is only ever
-- populated by the middleware this migration enables, so on rollback it is uniformly NULL.

DELETE FROM identity_access.role_permissions rp
 USING identity_access.permissions p
 WHERE p.id = rp.permission_id
   AND p.code IN ('impersonation.request', 'impersonation.approve');

DELETE FROM identity_access.permissions
 WHERE code IN ('impersonation.request', 'impersonation.approve');

DROP FUNCTION IF EXISTS kernel.request_impersonation(uuid, uuid, text, text);
DROP FUNCTION IF EXISTS kernel.impersonation_grant_state(uuid);

DROP INDEX IF EXISTS identity_access.idx_audit_logs_impersonation;
ALTER TABLE identity_access.audit_logs DROP COLUMN IF EXISTS impersonation_grant_id;

DROP TABLE IF EXISTS identity_access.impersonation_grants;
