-- ============================================================
-- Patch: pii_read_financial_permission.sql
-- Purpose:
--   1. Insert permission  users.read_financial  (Read user financial PII unmasked)
--   2. Grant users.read_financial to: platform_admin, brand_admin
--
-- Idempotent: safe to re-run (WHERE NOT EXISTS / ON CONFLICT DO NOTHING).
-- Additive only: no drops or updates to existing rows.
-- ============================================================

-- ── 1. Insert users.read_financial permission ────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'users.read_financial',
    'users',
    'read_financial',
    'Read user financial PII (unmasked)',
    'Grants access to unmasked PAN, bank account, and UPI ID values in user and rider profiles.',
    true,   -- is_system
    true,   -- requires_scope
    'high',
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'users.read_financial'
);

-- ── 2. Grant users.read_financial to platform_admin and brand_admin ──────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'users.read_financial'
WHERE r.code IN ('platform_admin', 'brand_admin')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;
