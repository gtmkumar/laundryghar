-- ============================================================
-- Patch: seeder_parity_r3sec1.sql  (R3-SEC-1)
-- Purpose:
--   Ensure the LIVE database contains all permission codes that were previously
--   defined only in individual SQL patches (pos_permissions.sql,
--   rider_verify_permission.sql, rider_cod_settlement.sql,
--   subscriptions_module.sql) and are now also folded into IdentitySeeder.
--
--   These codes are already present on any DB that applied those patches.
--   This patch is therefore a no-op on such DBs (all guards use WHERE NOT EXISTS /
--   ON CONFLICT DO NOTHING).  It is the safety net for fresh CI/staging/DR
--   environments that run the seeder but skip individual application patches.
--
-- Codes covered:
--   customer.create   payment.record   rider.verify   rider.settle
--   subscription.manage  subscription.read  saas.manage  saas.read
--
-- Idempotent. Additive only. No drops or updates.
-- ============================================================

-- ── customer.create ──────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'customer.create', 'customer', 'create',
    'Create customer (admin)',
    'Allows a counter or admin operator to create a new customer record directly without OTP signup.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'customer.create');

-- ── payment.record ───────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'payment.record', 'payment', 'record',
    'Record offline payment',
    'Allows recording an offline (cash/UPI/card) payment against an order at the POS counter.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'payment.record');

-- ── rider.verify ─────────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'rider.verify', 'rider', 'verify',
    'Verify rider KYC', 'Approve or reject a rider KYC/document submission.',
    true, true, 'high', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'rider.verify');

-- ── rider.settle ─────────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'rider.settle', 'rider', 'settle',
    'Settle rider COD cash',
    'Record a rider''s COD cash handover (settlement) and clear outstanding collections.',
    true, true, 'high', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'rider.settle');

-- ── subscription.manage ──────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'subscription.manage', 'subscription', 'manage',
    'Manage subscription plans', 'Create, update, delete subscription plan definitions.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'subscription.manage');

-- ── subscription.read ────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'subscription.read', 'subscription', 'read',
    'Read subscription data', 'View customer subscriptions and invoices.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'subscription.read');

-- ── saas.manage ──────────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'saas.manage', 'saas', 'manage',
    'Manage SaaS plans',
    'Create/update platform SaaS plans and manage franchise subscriptions.',
    true, true, 'high', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'saas.manage');

-- ── saas.read ────────────────────────────────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'saas.read', 'saas', 'read',
    'Read SaaS subscription data', 'View franchise SaaS subscription details and invoices.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'saas.read');

-- ── Role grants ───────────────────────────────────────────────────────────────
-- customer.create → platform_admin, brand_admin, franchise_owner, store_admin, store_staff
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'customer.create'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner', 'store_admin', 'store_staff')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- payment.record → platform_admin, brand_admin, franchise_owner, store_admin, store_staff
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'payment.record'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner', 'store_admin', 'store_staff')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- rider.verify → platform_admin, brand_admin, franchise_owner
-- (Note: pos_permissions.sql/rider_verify_permission.sql also listed 'operations_manager'
--  which is not a seeded system role; omitted here.)
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'rider.verify'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- rider.settle → platform_admin, brand_admin, franchise_owner
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'rider.settle'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- subscription.manage → platform_admin, brand_admin, franchise_owner
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'subscription.manage'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- subscription.read → platform_admin, brand_admin, franchise_owner
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'subscription.read'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- saas.manage → platform_admin only (SaaS billing is platform-scope)
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'saas.manage'
WHERE r.code = 'platform_admin'
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- saas.read → platform_admin only
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'saas.read'
WHERE r.code = 'platform_admin'
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── Verification ──────────────────────────────────────────────────────────────
SELECT
    p.code,
    string_agg(r.code, ', ' ORDER BY r.code) AS granted_to
FROM identity_access.permissions p
LEFT JOIN identity_access.role_permissions rp ON rp.permission_id = p.id
LEFT JOIN identity_access.roles r ON r.id = rp.role_id
WHERE p.code IN (
    'customer.create', 'payment.record',
    'rider.verify', 'rider.settle',
    'subscription.manage', 'subscription.read',
    'saas.manage', 'saas.read'
)
GROUP BY p.code
ORDER BY p.code;
