-- ============================================================
-- Patch: dispatch_permissions.sql  (Laundry + Logistics — Wave 1.4)
-- Purpose:
--   1. Insert permission  dispatch.mode.manage  (Manage dispatch mode)
--   2. Grant it to platform_admin ONLY — enabling 'offer_accept' dispatch is a
--      platform-level decision. Franchise admins may only narrow to push-assign,
--      which needs no special permission.
--
-- Idempotent: safe to re-run. Additive only.
-- ============================================================

INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'dispatch.mode.manage',
    'dispatch',
    'manage',
    'Manage dispatch mode',
    'Enable or disable offer→accept dispatch at the platform level.',
    true,    -- is_system
    false,   -- platform-wide, not scoped
    'high',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'dispatch.mode.manage'
);

-- Grant to platform_admin only.
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'dispatch.mode.manage'
WHERE r.code = 'platform_admin'
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;
