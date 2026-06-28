-- =============================================================================
-- db/patches/phase4_platform_billing_nav.sql
--
-- Adds the "Platform billing" navigator module (the operator MRR/SaaS-revenue view at
-- /platform-billing) as a sibling of "Platform plans" in the Finance section. Gated by saas.read,
-- so only platform admins see it. show_in_matrix=false — the saas permission already has a matrix
-- entry via platform_plans.
--
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_platform_billing_nav.sql
-- =============================================================================

BEGIN;

INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order,
     show_in_nav, show_in_matrix, required_permission, permission_modules, is_core, vertical_key, status)
VALUES
    ('platform_billing', 'Platform billing', 'Coins', '/platform-billing', 'Finance', 35, 999,
     true, false, 'saas.read', '{saas}', false, NULL, 'active')
ON CONFLICT (key) DO UPDATE
    SET label               = EXCLUDED.label,
        icon                = EXCLUDED.icon,
        route               = EXCLUDED.route,
        section             = EXCLUDED.section,
        nav_order           = EXCLUDED.nav_order,
        show_in_nav         = EXCLUDED.show_in_nav,
        required_permission = EXCLUDED.required_permission,
        updated_at          = now();

DO $verify$
DECLARE r text;
BEGIN
    SELECT required_permission INTO r FROM identity_access.modules WHERE key = 'platform_billing';
    IF r IS DISTINCT FROM 'saas.read' THEN
        RAISE EXCEPTION 'platform_billing_nav: module missing or not gated by saas.read (got %)', r;
    END IF;
    RAISE NOTICE 'platform_billing_nav verification passed: Platform billing nav module ready (Finance, saas.read).';
END
$verify$;

COMMIT;

SELECT 'phase4_platform_billing_nav.sql applied successfully.' AS result;
