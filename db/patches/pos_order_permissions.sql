-- ============================================================
-- Patch: pos_order_permissions.sql  (R3-SEC-2)
-- Purpose:
--   1. Insert permission  pos.order.create  (Create a POS order at the counter)
--   2. Insert permission  pos.order.read    (Read/list POS orders)
--   3. Grant pos.order.create to: platform_admin, brand_admin, franchise_owner,
--                                  store_admin, store_staff
--   4. Grant pos.order.read   to: platform_admin, brand_admin, franchise_owner,
--                                  store_admin, store_staff
--
-- Why a separate family?
--   The admin Orders module (orders.create / orders.list / orders.read) grants
--   access to the full back-office Orders screen.  POS counter staff should be
--   able to create and look up orders without being granted the wider admin
--   Orders panel.  The Orders service accepts EITHER family on the shared route
--   via AnyPermissionRequirement (pipe-syntax policy).
--
-- Idempotent: safe to re-run (WHERE NOT EXISTS / ON CONFLICT DO NOTHING).
-- Additive only.
-- ============================================================

-- ── 1. Insert pos.order.create permission ────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'pos.order.create',
    'pos',
    'order.create',
    'Create POS order',
    'Allows counter staff to create a new order at the POS terminal without access to the admin Orders module.',
    true,
    true,
    'normal',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'pos.order.create'
);

-- ── 2. Insert pos.order.read permission ──────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'pos.order.read',
    'pos',
    'order.read',
    'Read POS orders',
    'Allows counter staff to list and view orders from the POS terminal without access to the admin Orders module.',
    true,
    true,
    'low',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'pos.order.read'
);

-- ── 3. Grant pos.order.create ────────────────────────────────────────────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'pos.order.create'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner', 'store_admin', 'store_staff')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 4. Grant pos.order.read ──────────────────────────────────────────────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'pos.order.read'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner', 'store_admin', 'store_staff')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── Verification ──────────────────────────────────────────────────────────────
SELECT
    p.code,
    string_agg(r.code, ', ' ORDER BY r.code) AS granted_to
FROM identity_access.permissions p
JOIN identity_access.role_permissions rp ON rp.permission_id = p.id
JOIN identity_access.roles r ON r.id = rp.role_id
WHERE p.code IN ('pos.order.create', 'pos.order.read')
GROUP BY p.code
ORDER BY p.code;
