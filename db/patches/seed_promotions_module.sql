-- ============================================================================
-- seed_promotions_module.sql  (idempotent)  — Wave 6
-- Registers the Promotions admin page as a data-driven navigator/sidebar entry
-- in identity_access.modules, mirroring seed_navigator_modules.sql /
-- seed_support_module.sql. The admin-web sidebar is rendered from /navigator
-- (this table), so the Promotions page is only visible once it has a row here
-- gated by required_permission = promotions.manage.
--
-- The promotions.manage permission itself is already seeded by
-- seed_access_control.sql (module = 'promotions'). The admin Promotions CRUD API
-- lives in laundryghar.Commerce at /api/v1/admin/promotions.
--
-- Apply with:
--   psql "$LAUNDRYGHAR_DB" -v ON_ERROR_STOP=1 -f db/patches/seed_promotions_module.sql
-- ============================================================================

-- key, label, icon, route, section, nav_order, matrix_order, show_in_nav,
-- show_in_matrix, required_permission, permission_modules
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order, show_in_nav, show_in_matrix, required_permission, permission_modules)
VALUES
    ('promotions', 'Promotions', 'Megaphone', '/promotions', 'Catalogue', 23, 75, true, true, 'promotions.manage', '{promotions}')
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
