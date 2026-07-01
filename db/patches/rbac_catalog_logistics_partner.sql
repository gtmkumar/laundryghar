-- =============================================================================
-- rbac_catalog_logistics_partner.sql  (RaaS FULL-12 — partner catalog completion)
--   Mirrors the add-only changes made to
--   core.Infrastructure/Seeders/IdentitySeeder.cs so a live DB matches a fresh
--   seeder-built one. Everything here is additive + idempotent; no existing row
--   is mutated or removed.
--
--   SELF-SUFFICIENT ORDERING: this patch FOLDS the roles.scope_type CHECK relax
--   (allow 'logistics_partner') into step 0 below, BEFORE the role INSERTs. A
--   lexicographic/glob patch runner applies this file ('c'atalog) before its old
--   companion rbac_roles_scope_logistics_partner.sql ('r'oles) — folding the relax
--   here makes the apply order-independent. The companion patch remains a harmless
--   idempotent duplicate (it drops/re-adds the same superset CHECK); running either
--   order, or re-running, converges on the same constraint.
--
--   Adds:
--     1. 8 partner_booking.* permissions (module 'partner_booking') — ON CONFLICT (code).
--     2. partner_admin + partner_operator system roles (scope_type=logistics_partner,
--        vertical=logistics) — guarded WHERE NOT EXISTS (roles' UNIQUE is (brand_id, code)
--        and brand_id is NULL for system roles, so ON CONFLICT (code) has no arbiter).
--     3. role_permissions grants: platform_admin (all 8, mirrors the wildcard grant),
--        partner_admin (all 8), partner_operator (booking read/create/track/cancel +
--        partner_wallet.read only — §13 "operator sees but can't top up") —
--        ON CONFLICT (role_id, permission_id) DO NOTHING.
--     4. a `partner_booking` navigator/matrix module row so
--        AssignPermissionModuleKeysAsync auto-owns the 8 codes (module_key).
--
--   NOTE: partner authz in the MVP is enforced by a partner_role JWT claim + partner_id
--   RLS, NOT these role_permissions rows / ScopeResolver. This catalog is UI/matrix truth
--   only (docs/rbac.md §4/§10). Partners get NO user_scope_memberships rows.
--
--   Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/rbac_catalog_logistics_partner.sql
-- =============================================================================

BEGIN;

-- ── 0. Relax roles.scope_type CHECK to allow 'logistics_partner' (folded from
--       rbac_roles_scope_logistics_partner.sql so this patch is self-sufficient and
--       order-independent). MUST run before the partner_admin/partner_operator INSERTs
--       in step 2, which set scope_type='logistics_partner'. Idempotent: drop-if-exists
--       then re-add the SUPERSET set (safe whether or not the territory patch has run). ──
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'roles_scope_type_check'
          AND conrelid = 'identity_access.roles'::regclass
    ) THEN
        ALTER TABLE identity_access.roles DROP CONSTRAINT roles_scope_type_check;
    END IF;

    ALTER TABLE identity_access.roles
        ADD CONSTRAINT roles_scope_type_check
        CHECK (scope_type IN ('platform','brand','territory','franchise','store','warehouse','logistics_partner'));
END $$;

-- ── 1. New permissions (module 'partner_booking'; risk mirrors PermissionDefs) ────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), v.code, v.module, v.action, v.name, v.name, true, true, v.risk, 'active', now(), now()
FROM (VALUES
    ('partner_booking.read',   'partner_booking', 'booking.read',   'View partner bookings',   'low'),
    ('partner_booking.create', 'partner_booking', 'booking.create', 'Create partner booking',  'normal'),
    ('partner_booking.track',  'partner_booking', 'booking.track',  'Track partner booking',   'low'),
    ('partner_booking.cancel', 'partner_booking', 'booking.cancel', 'Cancel partner booking',  'normal'),
    ('partner_wallet.read',    'partner_booking', 'wallet.read',    'View partner wallet',     'low'),
    ('partner_wallet.topup',   'partner_booking', 'wallet.topup',   'Top up partner wallet',   'high'),
    ('partner_invoice.read',   'partner_booking', 'invoice.read',   'View partner invoices',   'low'),
    ('partner_invoice.export', 'partner_booking', 'invoice.export', 'Export partner invoices', 'normal')
) AS v(code, module, action, name, risk)
ON CONFLICT (code) DO NOTHING;

-- ── 2. partner_admin + partner_operator system roles (logistics_partner scope).
--       UNIQUE is (brand_id, code); brand_id is NULL for system roles, so guard with
--       NOT EXISTS instead of ON CONFLICT (code). ──
INSERT INTO identity_access.roles
    (id, brand_id, code, name, scope_type, vertical_key, is_system, is_assignable, priority, status, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.name, 'logistics_partner', 'logistics', true, true, v.priority, 'active', now(), now()
FROM (VALUES
    ('partner_admin',    'Partner Admin',    120::smallint),
    ('partner_operator', 'Partner Operator', 130::smallint)
) AS v(code, name, priority)
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.roles r
    WHERE r.code = v.code AND r.brand_id IS NULL AND r.deleted_at IS NULL
);

-- ── 3. Grants (join by code; guard deleted_at IS NULL + active permission). ──

-- platform_admin → every new partner permission (mirrors the wildcard Grant in the seeder).
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'partner_booking.read','partner_booking.create','partner_booking.track','partner_booking.cancel',
    'partner_wallet.read','partner_wallet.topup','partner_invoice.read','partner_invoice.export'
]::text[])
WHERE r.code = 'platform_admin' AND r.brand_id IS NULL AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- partner_admin → full account control (bookings + wallet incl. top-up + invoices).
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'partner_booking.read','partner_booking.create','partner_booking.track','partner_booking.cancel',
    'partner_wallet.read','partner_wallet.topup','partner_invoice.read','partner_invoice.export'
]::text[])
WHERE r.code = 'partner_admin' AND r.brand_id IS NULL AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- partner_operator → books/tracks/cancels + sees wallet balance; NO top-up, NO invoices
--                    (§13 "operator sees but can't top up").
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'partner_booking.read','partner_booking.create','partner_booking.track','partner_booking.cancel',
    'partner_wallet.read'
]::text[])
WHERE r.code = 'partner_operator' AND r.brand_id IS NULL AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 4. Navigator/matrix module row so AssignPermissionModuleKeysAsync owns the 8
--       partner_booking.* codes (module_key). Matrix-only (no route yet), mirroring the
--       'pos'/'settings'/'audit'/'report' rows. permission_modules '{partner_booking}'
--       matches every partner permission's stored `module` value. ──
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order, show_in_nav, show_in_matrix, required_permission, permission_modules)
VALUES
    ('partner_booking', 'Partners (RaaS)', 'Handshake', NULL, NULL, 999, 155, false, true, 'partner_booking.read', '{partner_booking}')
ON CONFLICT (key) DO NOTHING;

COMMIT;

-- Verification.
SELECT 'new_permissions' AS what, count(*) AS n
FROM identity_access.permissions
WHERE code IN (
    'partner_booking.read','partner_booking.create','partner_booking.track','partner_booking.cancel',
    'partner_wallet.read','partner_wallet.topup','partner_invoice.read','partner_invoice.export')
UNION ALL
SELECT 'partner_roles', count(*) FROM identity_access.roles
    WHERE code IN ('partner_admin','partner_operator') AND brand_id IS NULL
UNION ALL
SELECT 'partner_admin_grants', count(*) FROM identity_access.role_permissions rp
    JOIN identity_access.roles r ON r.id = rp.role_id
    WHERE r.code = 'partner_admin' AND r.brand_id IS NULL
UNION ALL
SELECT 'partner_operator_grants', count(*) FROM identity_access.role_permissions rp
    JOIN identity_access.roles r ON r.id = rp.role_id
    WHERE r.code = 'partner_operator' AND r.brand_id IS NULL
UNION ALL
SELECT 'partner_booking_module', count(*) FROM identity_access.modules WHERE key = 'partner_booking';
