-- ============================================================================
-- seed_subscriptions_modules.sql  (idempotent)
-- Adds two entries to identity_access.modules so the subscription admin
-- screens appear in the admin sidebar.
--
--   1. 'subscriptions'  — Commerce plans + customer subscriptions.
--        Catalogue section, nav_order 24 (after CMS = 23).
--        Permission gate: subscription.read
--        (seeded by db/patches/subscriptions_module.sql).
--        Icon: Tag. Route: /subscriptions (registered in admin-web/src/App.tsx).
--
--   2. 'platform_plans' — SaaS platform plans + franchise subscriptions.
--        Finance section, nav_order 34 (after Royalty = 33).
--        Permission gate: saas.read (platform_admin only).
--        Icon: Layers. Route: /platform-plans (registered in App.tsx).
--
-- Mirrors db/patches/seed_royalty_module.sql.
-- ============================================================================

INSERT INTO identity_access.modules
    (key, label, icon, route, section,
     nav_order, matrix_order,
     show_in_nav, show_in_matrix,
     required_permission, permission_modules)
VALUES
    ('subscriptions', 'Subscriptions', 'Tag', '/subscriptions', 'Catalogue',
     24, 75,
     true, true,
     'subscription.read', '{subscription}'),
    ('platform_plans', 'Platform plans', 'Layers', '/platform-plans', 'Finance',
     34, 125,
     true, true,
     'saas.read', '{saas}')
ON CONFLICT (key) DO NOTHING;

SELECT key, label, section, nav_order, show_in_nav AS nav, required_permission
FROM identity_access.modules
WHERE key IN ('subscriptions', 'platform_plans')
ORDER BY nav_order;
