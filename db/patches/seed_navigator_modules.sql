-- ============================================================================
-- seed_navigator_modules.sql  (idempotent)
-- The data-driven navigator/module registry. One table drives BOTH:
--   • the admin sidebar menu  (show_in_nav rows, ordered by nav_order, gated by
--     required_permission against the signed-in user's permissions), and
--   • the Roles & Permissions matrix rows (show_in_matrix rows, ordered by
--     matrix_order). permission_modules[] maps the raw permissions.module values
--     (analytics, fulfillment, qc, …) onto each UI module row.
-- Replaces the previously hardcoded module list in PermissionMatrix.cs.
-- ============================================================================

CREATE TABLE IF NOT EXISTS identity_access.modules (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    key                 varchar(64)  NOT NULL UNIQUE,
    label               varchar(128) NOT NULL,
    icon                varchar(64),
    route               varchar(160),
    section             varchar(64),
    nav_order           int          NOT NULL DEFAULT 100,
    matrix_order        int          NOT NULL DEFAULT 100,
    show_in_nav         boolean      NOT NULL DEFAULT false,
    show_in_matrix      boolean      NOT NULL DEFAULT true,
    required_permission varchar(128),
    permission_modules  text[]       NOT NULL DEFAULT '{}',
    status              varchar(32)  NOT NULL DEFAULT 'active',
    created_at          timestamptz  NOT NULL DEFAULT now(),
    updated_at          timestamptz  NOT NULL DEFAULT now()
);

GRANT SELECT ON identity_access.modules TO app_user;

-- key, label, icon, route, section, nav_order, matrix_order, show_in_nav,
-- show_in_matrix, required_permission, permission_modules
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order, show_in_nav, show_in_matrix, required_permission, permission_modules)
VALUES
    -- Operations
    ('dashboard',  'Dashboard',     'LayoutDashboard', '/',                         'Operations',     10,  10, true,  true,  NULL,             '{analytics}'),
    ('stores',     'Stores',        'Building2',       '/tenancy',                  'Operations',     11, 999, true,  false, 'stores.list',    '{stores}'),
    ('orders',     'Orders',        'ShoppingCart',    '/orders',                   'Operations',     12,  20, true,  true,  'orders.list',    '{orders}'),
    ('pos',        'POS',           'Monitor',         NULL,                        NULL,             999, 30, false, true,  NULL,             '{orders}'),
    ('warehouse',  'Warehouse',     'Warehouse',       '/warehouse/board',          'Operations',     13,  80, true,  true,  'fulfillment.read',   '{warehouse,fulfillment,qc,stockrecon,store_warehouse}'),
    ('customers',  'Customers',     'Users',           '/customers',                'Operations',     14,  40, true,  true,  'customer.read',  '{customer}'),
    ('riders',     'Riders',        'Bike',            '/riders',                   'Operations',     15,  90, true,  true,  'rider.read',     '{rider,delivery,pickup}'),
    -- Catalogue
    ('pricing',    'Pricing',       'Tag',             '/catalog',                  'Catalogue',      20,  50, true,  true,  'pricing.read',   '{pricing,catalog}'),
    ('packages',   'Packages',      'Package',         '/packages',                 'Catalogue',      21,  60, true,  true,  'packages.manage','{packages}'),
    ('coupons',    'Coupons',       'Receipt',         '/coupons',                  'Catalogue',      22,  70, true,  true,  'coupons.manage', '{coupons,promotions}'),
    ('cms',        'CMS',           'Bell',            '/cms',                      'Catalogue',      23, 999, true,  false, NULL,             '{cms}'),
    -- Finance
    ('cashbook',   'Cash book',     'BookOpen',        '/cashbook',                 'Finance',        30, 100, true,  true,  'cashbook.read',  '{cashbook}'),
    ('expenses',   'Expenses',      'Coins',           '/expenses',                 'Finance',        31, 110, true,  true,  'expense.read',   '{expense}'),
    ('analytics',  'Analytics',     'BarChart2',       '/analytics',                'Finance',        32, 120, true,  true,  'analytics.read', '{analytics}'),
    -- Administration
    ('users',      'Users & Roles', 'ShieldCheck',     '/access-control',           'Administration', 40, 130, true,  true,  'users.list',     '{users,roles,permissions,memberships}'),
    ('franchises', 'Franchises',    'Network',         '/access-control?tab=franchises', 'Administration', 41, 140, true, true, 'franchises.list','{franchises,franchise_agreements}'),
    -- Matrix-only catch-all
    ('settings',   'Settings',      'Settings',        NULL,                        NULL,             999, 150, false, true,  NULL,             '{brands,platforms,territories,stores,warehouses,cms,loyalty,wallet,holidays,operating_hours,paymentmethod,payment,royalty}')
ON CONFLICT (key) DO NOTHING;

SELECT key, label, section, nav_order, matrix_order, show_in_nav AS nav, show_in_matrix AS mtx, required_permission
FROM identity_access.modules ORDER BY show_in_nav DESC, nav_order, matrix_order;
