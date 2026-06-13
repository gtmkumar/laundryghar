-- ============================================================================
-- seed_support_module.sql  (idempotent)  — Wave 5
-- Registers the Support inbox as a data-driven navigator/sidebar entry in
-- identity_access.modules, mirroring seed_navigator_modules.sql. The admin-web
-- sidebar is rendered from /navigator (this table), so a new top-level page is
-- only visible once it has a row here gated by required_permission = support.read.
--
-- The support.read / support.manage permissions themselves are seeded by
-- support_permissions.sql (run that first). The admin Support inbox API lives in
-- laundryghar.Operations (Logistics admin group) at /api/v1/admin/support/tickets.
--
-- Apply with:
--   psql "$LAUNDRYGHAR_DB" -v ON_ERROR_STOP=1 -f db/patches/seed_support_module.sql
-- ============================================================================

-- key, label, icon, route, section, nav_order, matrix_order, show_in_nav,
-- show_in_matrix, required_permission, permission_modules
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order, show_in_nav, show_in_matrix, required_permission, permission_modules)
VALUES
    ('support', 'Support', 'LifeBuoy', '/support', 'Operations', 16, 45, true, true, 'support.read', '{support}')
ON CONFLICT (key) DO UPDATE SET
    label               = EXCLUDED.label,
    icon                = EXCLUDED.icon,
    route               = EXCLUDED.route,
    section             = EXCLUDED.section,
    nav_order           = EXCLUDED.nav_order,
    matrix_order        = EXCLUDED.matrix_order,
    show_in_nav         = EXCLUDED.show_in_nav,
    show_in_matrix      = EXCLUDED.show_in_matrix,
    required_permission = EXCLUDED.required_permission,
    permission_modules  = EXCLUDED.permission_modules,
    updated_at          = now();
