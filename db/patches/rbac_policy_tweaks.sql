-- =============================================================================
-- rbac_policy_tweaks.sql  (least-privilege policy adjustments)
--   (a) Remove customer.delete from support_lead — a support/refunds role should
--       edit/anonymize customers, not hard-delete them.
--   (b) Re-rank regional_manager to priority 24 so its rank (oversight) sits ABOVE
--       operations_manager (25) but below brand_admin (20).
--   Idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/rbac_policy_tweaks.sql
-- =============================================================================

BEGIN;

-- (a) drop support_lead → customer.delete
DELETE FROM identity_access.role_permissions rp
USING identity_access.roles r, identity_access.permissions p
WHERE rp.role_id = r.id AND rp.permission_id = p.id
  AND r.code = 'support_lead' AND p.code = 'customer.delete';

-- (b) regional_manager outranks operations_manager
UPDATE identity_access.roles SET priority = 24
WHERE code = 'regional_manager' AND priority <> 24;

COMMIT;

-- Verify
SELECT code, priority FROM identity_access.roles
WHERE code IN ('brand_admin','regional_manager','operations_manager') ORDER BY priority;

SELECT 'support_lead has customer.delete?' AS check,
       EXISTS (SELECT 1 FROM identity_access.role_permissions rp
               JOIN identity_access.roles r ON r.id=rp.role_id
               JOIN identity_access.permissions p ON p.id=rp.permission_id
               WHERE r.code='support_lead' AND p.code='customer.delete') AS result;
