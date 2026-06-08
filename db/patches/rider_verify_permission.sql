-- ============================================================
-- Patch: rider_verify_permission.sql
-- Purpose:
--   1. Insert permission  rider.verify  (Verify rider KYC)
--   2. Grant rider.verify to: platform_admin, brand_admin,
--      operations_manager, franchise_owner
--   3. Grant rider.manage to: franchise_owner (if not already present)
--
-- Idempotent: safe to re-run (ON CONFLICT DO NOTHING / WHERE NOT EXISTS).
-- Additive only: no drops or updates to existing rows.
-- ============================================================

-- ── 1. Insert rider.verify permission ───────────────────────────────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status,
     created_at, updated_at)
SELECT
    gen_random_uuid(),
    'rider.verify',
    'rider',
    'verify',
    'Verify rider KYC',
    'Approve or reject a rider KYC/document submission.',
    true,   -- is_system
    true,   -- requires_scope
    'high', -- matches rider.manage risk level
    'active',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.permissions WHERE code = 'rider.verify'
);

-- ── 2. Grant rider.verify to the four target roles ───────────────────────────
-- Uses a JOIN so UUIDs are not hardcoded; ON CONFLICT guards re-runs.
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'rider.verify'
WHERE r.code IN ('platform_admin', 'brand_admin', 'operations_manager', 'franchise_owner')
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 3. Grant rider.manage to franchise_owner (if not already present) ────────
INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    now(),
    now()
FROM identity_access.roles       r
JOIN identity_access.permissions  p ON p.code = 'rider.manage'
WHERE r.code = 'franchise_owner'
  AND r.deleted_at IS NULL
  AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;
