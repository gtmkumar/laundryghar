-- =============================================================================
-- item_management.sql  (Items/Pricing redesign — dedicated "Manage laundry items")
--   1. Item operational fields used by the new Add/Edit-item drawer:
--        tat_hours, express_eligible, express_surcharge
--   2. Widen items.status to support the UI lifecycle Active / Draft / Archived
--      (was only active/disabled/seasonal). 'disabled' is kept = soft-delete state.
--   3. Add the "Items" navigator module (/items) so the split Items page gets its
--      own sidebar entry alongside Pricing (/catalog).
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/item_management.sql
-- =============================================================================

BEGIN;

-- 1. Operational fields on the item -------------------------------------------
ALTER TABLE customer_catalog.items
    ADD COLUMN IF NOT EXISTS tat_hours          integer,
    ADD COLUMN IF NOT EXISTS express_eligible   boolean       NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS express_surcharge  numeric(10,2);

-- 2. Lifecycle status: add draft + archived, keep existing values --------------
ALTER TABLE customer_catalog.items DROP CONSTRAINT IF EXISTS items_status_check;
ALTER TABLE customer_catalog.items ADD CONSTRAINT items_status_check
    CHECK (status::text = ANY (ARRAY['active','draft','archived','disabled','seasonal']::text[]));

-- 3. Items navigator module ---------------------------------------------------
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order,
     show_in_nav, show_in_matrix, required_permission, permission_modules, status, is_core)
VALUES
    ('items', 'Items', 'Shirt', '/items', 'Catalogue', 19, 49,
     true, true, 'catalog.read', ARRAY['catalog']::text[], 'active', false)
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
    status              = EXCLUDED.status,
    updated_at          = now();

COMMIT;

SELECT 'items cols' AS check,
       count(*) FILTER (WHERE column_name IN ('tat_hours','express_eligible','express_surcharge')) AS added
FROM information_schema.columns WHERE table_schema='customer_catalog' AND table_name='items';
SELECT key, route, nav_order FROM identity_access.modules WHERE key='items';
