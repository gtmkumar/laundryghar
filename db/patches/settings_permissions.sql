-- ============================================================
-- Patch: settings_permissions.sql  (R3-SEC-3)
-- Purpose:
--   1. Insert permission  settings.read   (Read admin settings)
--   2. Insert permission  settings.manage (Write admin settings — email, WhatsApp, SMS,
--                                          payment gateway, payout, maps, provisioning)
--   3. Grant settings.read  to: platform_admin, brand_admin
--   4. Grant settings.manage to: platform_admin, brand_admin
--   5. Update identity_access.modules row for 'settings' to set required_permission
--      = 'settings.read' so the sidebar navigator gates the Settings link correctly
--      (was NULL, meaning visible to every authenticated admin user).
--
-- Idempotent: safe to re-run (WHERE NOT EXISTS / ON CONFLICT DO NOTHING).
-- Additive only: no drops or updates to existing permission/role rows.
-- ============================================================

-- ── 1. Insert settings.read permission ──────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'settings.read',
    'settings',
    'read',
    'Read admin settings',
    'View platform/brand configuration including email, messaging, payment gateway, and payout settings.',
    true,
    true,
    'low',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'settings.read'
);

-- ── 2. Insert settings.manage permission ─────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'settings.manage',
    'settings',
    'manage',
    'Manage admin settings',
    'Create or update platform/brand configuration: email transport, WhatsApp, SMS, payment gateway credentials, payout rules, maps keys, and user-provisioning mode.',
    true,
    true,
    'high',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'settings.manage'
);

-- ── 3. Grant settings.read ────────────────────────────────────────────────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'settings.read'
WHERE r.code IN ('platform_admin', 'brand_admin')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 4. Grant settings.manage ──────────────────────────────────────────────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'settings.manage'
WHERE r.code IN ('platform_admin', 'brand_admin')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 5. Gate the 'settings' navigator module on settings.read ─────────────────
-- required_permission was NULL (visible to all authenticated admins).
-- After this patch it gates the sidebar entry on settings.read, which matches
-- the GetNavigatorHandler's permission check logic.
UPDATE identity_access.modules
SET    required_permission = 'settings.read',
       updated_at          = now()
WHERE  key = 'settings'
  AND  (required_permission IS NULL OR required_permission <> 'settings.read');

-- ── Verification ──────────────────────────────────────────────────────────────
SELECT
    p.code,
    string_agg(r.code, ', ' ORDER BY r.code) AS granted_to
FROM identity_access.permissions p
JOIN identity_access.role_permissions rp ON rp.permission_id = p.id
JOIN identity_access.roles r ON r.id = rp.role_id
WHERE p.code IN ('settings.read', 'settings.manage')
GROUP BY p.code
ORDER BY p.code;

SELECT key, required_permission
FROM identity_access.modules
WHERE key = 'settings';
