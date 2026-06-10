-- ============================================================
-- Patch: pos_permissions.sql
-- Purpose:
--   1. Insert permission  customer.create  (Create a customer counter-side)
--   2. Insert permission  payment.record   (Record an offline POS payment)
--   3. Grant customer.create to: platform_admin, brand_admin, franchise_owner, store_admin
--   4. Grant payment.record   to: platform_admin, brand_admin, franchise_owner, store_admin
--
-- Idempotent: safe to re-run (WHERE NOT EXISTS / ON CONFLICT DO NOTHING).
-- Additive only: no drops or updates to existing rows.
-- ============================================================

-- ── 1. Insert customer.create permission ─────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'customer.create',
    'customer',
    'create',
    'Create customer (admin)',
    'Allows a counter or admin operator to create a new customer record directly without OTP signup.',
    true,
    true,
    'normal',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'customer.create'
);

-- ── 2. Insert payment.record permission ──────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'payment.record',
    'payment',
    'record',
    'Record offline payment',
    'Allows recording an offline (cash/UPI/card) payment against an order at the POS counter.',
    true,
    true,
    'normal',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'payment.record'
);

-- ── 3. Grant customer.create ──────────────────────────────────────────────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'customer.create'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner', 'store_admin')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 4. Grant payment.record ───────────────────────────────────────────────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'payment.record'
WHERE r.code IN ('platform_admin', 'brand_admin', 'franchise_owner', 'store_admin')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;
