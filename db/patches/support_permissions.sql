-- ============================================================
-- Patch: support_permissions.sql  (Laundry + Logistics — Wave 5)
-- Adds support.read (view the support inbox) and support.manage (reply / change
-- status / assign). Granted to platform_admin, brand_admin, operations_manager,
-- franchise_owner. Idempotent, additive.
-- ============================================================

INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), v.code, 'support', v.action, v.name, v.descr, true, false, 'normal', 'active', now(), now()
FROM (VALUES
    ('support.read',   'read',   'View support inbox',  'View customer/rider support tickets.'),
    ('support.manage', 'manage', 'Manage support',      'Reply to, assign, and change the status of support tickets.')
) AS v(code, action, name, descr)
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions p WHERE p.code = v.code);

INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code IN ('support.read','support.manage')
WHERE r.code IN ('platform_admin','brand_admin','operations_manager','franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;
