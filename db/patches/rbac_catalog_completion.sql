-- =============================================================================
-- rbac_catalog_completion.sql  (RBAC #12 — permission/role catalog completion)
--   Mirrors the add-only changes made to
--   core.Infrastructure/Seeders/IdentitySeeder.cs so a live DB matches a fresh
--   seeder-built one. Everything here is additive + idempotent; no existing row
--   is mutated or removed.
--
--   Adds:
--     1. new permissions (royalty.override, audit.*, report.*, feature_flag.*,
--        support.*, territories.read, and the *.export set) — ON CONFLICT (code).
--     2. the `support` system role (scope_type=brand) — guarded WHERE NOT EXISTS
--        (roles' UNIQUE is (brand_id, code) and brand_id is NULL for system roles,
--        so ON CONFLICT (code) has no matching arbiter).
--     3. role_permissions grants for platform_admin (all new codes, mirrors the
--        wildcard grant), support, auditor, brand_admin, franchise_owner,
--        store_admin — ON CONFLICT (role_id, permission_id) DO NOTHING.
--     4. `audit` + `report` navigator/matrix module rows so
--        AssignPermissionModuleKeysAsync auto-owns the new codes.
--
--   Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/rbac_catalog_completion.sql
-- =============================================================================

BEGIN;

-- ── 1. New permissions (risk_level mirrors the seeder's PermissionDefs) ───────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), v.code, v.module, v.action, v.name, v.name, true, true, v.risk, 'active', now(), now()
FROM (VALUES
    ('royalty.override',    'royalty',      'override', 'Override royalty invoice', 'critical'),
    ('audit.view',          'audit',        'view',     'View audit logs',          'low'),
    ('audit.export',        'audit',        'export',   'Export audit logs',        'normal'),
    ('report.view',         'report',       'view',     'View reports',             'low'),
    ('report.export',       'report',       'export',   'Export reports',           'normal'),
    ('feature_flag.view',   'feature_flag', 'view',     'View feature flags',       'low'),
    ('feature_flag.manage', 'feature_flag', 'manage',   'Manage feature flags',     'high'),
    ('support.read',        'support',      'read',     'View support inbox',       'low'),
    ('support.manage',      'support',      'manage',   'Manage support tickets',   'normal'),
    ('territories.read',    'territories',  'read',     'Read territory',           'low'),
    ('orders.export',       'orders',       'export',   'Export orders',            'normal'),
    ('payment.export',      'payment',      'export',   'Export payments',          'normal'),
    ('cashbook.export',     'cashbook',     'export',   'Export cash books',        'normal'),
    ('expense.export',      'expense',      'export',   'Export expenses',          'normal'),
    ('royalty.export',      'royalty',      'export',   'Export royalty invoices',  'normal'),
    ('analytics.export',    'analytics',    'export',   'Export analytics',         'normal'),
    ('catalog.export',      'catalog',      'export',   'Export catalog',           'normal'),
    ('pricing.export',      'pricing',      'export',   'Export price lists',       'normal'),
    ('customer.export',     'customer',     'export',   'Export customers',         'normal'),
    ('rider.export',        'rider',        'export',   'Export riders',            'normal'),
    ('wallet.export',       'wallet',       'export',   'Export wallets',           'normal')
) AS v(code, module, action, name, risk)
ON CONFLICT (code) DO NOTHING;

-- ── 2. `support` system role (brand scope). UNIQUE is (brand_id, code); brand_id
--       is NULL for system roles, so guard with NOT EXISTS instead of ON CONFLICT. ──
INSERT INTO identity_access.roles
    (id, brand_id, code, name, scope_type, is_system, is_assignable, priority, status, created_at, updated_at)
SELECT gen_random_uuid(), NULL, 'support', 'Support', 'brand', true, true, 110, 'active', now(), now()
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.roles r
    WHERE r.code = 'support' AND r.brand_id IS NULL AND r.deleted_at IS NULL
);

-- ── 3. Grants (join by code; deny rows are seeded separately in rbac_deny_rows.sql) ──

-- platform_admin → every new permission (mirrors the wildcard Grant in the seeder)
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'royalty.override','audit.view','audit.export','report.view','report.export',
    'feature_flag.view','feature_flag.manage','support.read','support.manage','territories.read',
    'orders.export','payment.export','cashbook.export','expense.export','royalty.export',
    'analytics.export','catalog.export','pricing.export','customer.export','rider.export','wallet.export'
]::text[])
WHERE r.code = 'platform_admin' AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- support → order view+notes, customer read, pickup/delivery view, subscription view,
--           refund within cap (endpoint-enforced), support inbox.
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'orders.list','orders.read','orders.notes.manage',
    'customer.read','pickup.read','delivery.slot.read',
    'subscription.read','payment.refund','support.read','support.manage'
]::text[])
WHERE r.code = 'support' AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- auditor → audit + report (view/export) and the *.export set (§9: *.view + *.export).
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'audit.view','audit.export','report.view','report.export',
    'orders.export','payment.export','cashbook.export','expense.export','royalty.export',
    'analytics.export','catalog.export','pricing.export','customer.export','rider.export','wallet.export'
]::text[])
WHERE r.code = 'auditor' AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- brand_admin (enumerated) → feature_flag (manage), audit + report, and the *.export set.
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'feature_flag.view','feature_flag.manage',
    'audit.view','audit.export','report.view','report.export',
    'orders.export','payment.export','cashbook.export','expense.export','royalty.export',
    'analytics.export','catalog.export','pricing.export','customer.export','rider.export','wallet.export'
]::text[])
WHERE r.code = 'brand_admin' AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- franchise_owner + store_admin → feature_flag.view (§10 settings/feature_flag → V).
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'feature_flag.view'
WHERE r.code IN ('franchise_owner','store_admin') AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- ── 4. Navigator/matrix module rows so AssignPermissionModuleKeysAsync owns the
--       new module.action codes (module_key). Matrix-only (no route yet), mirroring
--       the 'pos'/'settings' rows in seed_navigator_modules.sql. ──
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order, show_in_nav, show_in_matrix, required_permission, permission_modules)
VALUES
    ('audit',  'Audit Log', 'ScrollText', NULL, 'Administration', 999, 160, false, true, 'audit.view',  '{audit}'),
    ('report', 'Reports',   'FileText',   NULL, 'Finance',        999, 125, false, true, 'report.view', '{report,analytics}')
ON CONFLICT (key) DO NOTHING;

COMMIT;

-- Verification.
SELECT 'new_permissions' AS what, count(*) AS n
FROM identity_access.permissions
WHERE code IN (
    'royalty.override','audit.view','audit.export','report.view','report.export',
    'feature_flag.view','feature_flag.manage','support.read','support.manage','territories.read',
    'orders.export','payment.export','cashbook.export','expense.export','royalty.export',
    'analytics.export','catalog.export','pricing.export','customer.export','rider.export','wallet.export')
UNION ALL
SELECT 'support_role', count(*) FROM identity_access.roles WHERE code = 'support' AND brand_id IS NULL
UNION ALL
SELECT 'audit_report_modules', count(*) FROM identity_access.modules WHERE key IN ('audit','report');
