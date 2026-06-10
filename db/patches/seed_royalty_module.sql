-- ============================================================================
-- seed_royalty_module.sql  (idempotent)
-- Adds a 'royalty' entry to identity_access.modules so the Royalty page
-- appears in the admin sidebar under the Finance section.
--
-- Nav order 33: sits after Expenses (31) and Analytics (32) in Finance.
-- Permission gate: royalty.read (already seeded by IdentitySeeder.cs).
-- Icon: Coins (already in the admin-web ICONS map alongside the Finance items).
-- Route: /royalty (registered in admin-web/src/App.tsx).
-- ============================================================================

INSERT INTO identity_access.modules
    (key, label, icon, route, section,
     nav_order, matrix_order,
     show_in_nav, show_in_matrix,
     required_permission, permission_modules)
VALUES
    ('royalty', 'Royalty', 'Coins', '/royalty', 'Finance',
     33, 115,
     true, true,
     'royalty.read', '{royalty}')
ON CONFLICT (key) DO NOTHING;

SELECT key, label, section, nav_order, show_in_nav AS nav, required_permission
FROM identity_access.modules
WHERE key = 'royalty';
