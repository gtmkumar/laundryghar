-- =============================================================================
-- rbac_deny_rows.sql  (RBAC #12 — first-ever effect='deny' role_permissions rows)
--   Mirrors the Deny(...) calls added to
--   core.Infrastructure/Seeders/IdentitySeeder.cs. Deny wins (§7), so these are a
--   belt-and-braces layer on top of the (already read-only) auditor grant set.
--
--     1. auditor  → deny EVERY mutating permission (action verb ∈ the closed
--        write set), MINUS the *.view/*.list/*.read/*.export codes it is granted.
--     2. franchise_owner → deny royalty.override (§7 canonical example).
--
--   MUST run AFTER db/patches/permission_overrides.sql (which adds the
--   role_permissions.effect column). Additive + idempotent (ON CONFLICT DO
--   UPDATE SET effect='deny'). Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/rbac_deny_rows.sql
-- =============================================================================

BEGIN;

-- Safety: this patch is meaningless without the effect column. Fail loudly if the
-- prerequisite (permission_overrides.sql) has not been applied.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'identity_access'
          AND table_name   = 'role_permissions'
          AND column_name  = 'effect'
    ) THEN
        RAISE EXCEPTION 'role_permissions.effect is missing — run db/patches/permission_overrides.sql first';
    END IF;
END $$;

-- 1. auditor: deny every mutating permission (kept in lockstep with the seeder's
--    mutating-verb computation), excluding the codes auditor is explicitly granted.
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, effect, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, 'deny', now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'platforms.create','platforms.update','platforms.delete',
    'brands.create','brands.update','brands.delete',
    'territories.create','territories.update','territories.delete',
    'franchise_agreements.create','franchise_agreements.update','franchise_agreements.delete',
    'franchises.create','franchises.update','franchises.delete',
    'stores.create','stores.update','stores.delete',
    'warehouses.create','warehouses.update','warehouses.delete',
    'operating_hours.manage','holidays.manage','store_warehouse.manage',
    'users.create','users.update','users.deactivate',
    'roles.manage','permissions.assign',
    'orders.create','orders.update','orders.refund','orders.cancel',
    'catalog.category.create','catalog.category.update','catalog.category.delete',
    'catalog.service.create','catalog.service.update','catalog.service.delete',
    'catalog.fabric.manage','catalog.itemgroup.manage',
    'catalog.item.create','catalog.item.update','catalog.item.delete',
    'catalog.variant.manage','catalog.addon.manage',
    'pricing.pricelist.create','pricing.pricelist.update','pricing.pricelist.publish','pricing.item.manage',
    'customer.update','customer.delete',
    'orders.status.update','orders.notes.manage',
    'pickup.create','pickup.assign',
    'delivery.slot.manage','delivery.assign',
    'fulfillment.tag','fulfillment.inspect',
    'warehouse.batch.manage','warehouse.process.scan',
    'qc.perform','stockrecon.manage',
    'rider.manage','rider.assignment.manage','rider.capacity.manage','rider.tasks.update',
    'paymentmethod.manage','packages.manage','promotions.manage','coupons.manage','loyalty.manage',
    'payment.record','payment.refund','wallet.adjust',
    'subscription.manage','saas.manage',
    'pos.order.create',
    'cashbook.manage','expense.manage','expense.approve',
    'royalty.manage',
    'cms.template.manage','cms.banner.manage','cms.onboarding.manage','cms.appconfig.manage','cms.notification.manage',
    'customer.create','rider.verify','rider.settle',
    'settings.manage',
    'royalty.override','feature_flag.manage','support.manage'
]::text[])
WHERE r.code = 'auditor'
  AND r.deleted_at IS NULL
  AND NOT (p.code = ANY (ARRAY[
    -- auditor-allowed set: *.view/*.list/*.read/*.export it keeps as allow
    'brands.list','brands.read','franchises.list','franchises.read',
    'stores.list','stores.read','warehouses.list','orders.list',
    'users.list','roles.list','permissions.list',
    'catalog.read','pricing.read','customer.read',
    'orders.read','pickup.read','delivery.slot.read','fulfillment.read',
    'rider.read','rider.assignment.read','payment.read','wallet.read',
    'cashbook.read','expense.read','royalty.read','cms.notification.read','analytics.read',
    'orders.export','payment.export','cashbook.export','expense.export','royalty.export',
    'analytics.export','catalog.export','pricing.export','customer.export','rider.export','wallet.export',
    'audit.view','audit.export','report.view','report.export'
  ]::text[]))
ON CONFLICT (role_id, permission_id) DO UPDATE SET effect = 'deny';

-- 2. franchise_owner: deny royalty.override (§7 canonical deny-wins example).
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, effect, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, 'deny', now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'royalty.override'
WHERE r.code = 'franchise_owner' AND r.deleted_at IS NULL
ON CONFLICT (role_id, permission_id) DO UPDATE SET effect = 'deny';

COMMIT;

-- Verification: how many deny rows now exist per role.
SELECT r.code AS role, count(*) AS deny_rows
FROM identity_access.role_permissions rp
JOIN identity_access.roles r ON r.id = rp.role_id
WHERE rp.effect = 'deny'
GROUP BY r.code
ORDER BY r.code;
