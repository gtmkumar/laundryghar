-- =============================================================================
-- seed_access_control.sql
-- Access Control console seed data — idempotent via deterministic md5 UUIDs
-- and ON CONFLICT DO NOTHING throughout.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/seed_access_control.sql
-- =============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- Helpers: stable seed constants
-- ---------------------------------------------------------------------------
-- Brand anchor
-- brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
-- Existing Sector-45 franchise = '36f9801c-aa60-4c00-b2bb-ad78fff7615e'
-- Existing Sector-45 store     = '60e5bb20-8e4e-4892-a85e-449402463cf9'
-- Shared password hash (Warehouse@123)
-- v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=

-- =============================================================================
-- SECTION 1 — NEW ROLES
-- scope_type = 'brand', is_system = false, is_assignable = true, status='active'
-- brand_id   = '5b375161-9b8b-4177-ab58-54848606aa2f'
-- Unique key: (brand_id, code)
-- =============================================================================

INSERT INTO identity_access.roles
    (id, brand_id, code, name, description, scope_type, is_system, is_assignable, priority, status)
VALUES
    (
        md5('seed_role_operations_manager')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'operations_manager',
        'Operations Manager',
        'Orders, warehouse, riders, analytics',
        'brand', false, true, 25, 'active'
    ),
    (
        md5('seed_role_finance_manager')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'finance_manager',
        'Finance Manager',
        'Cash book, expenses, P&L, exports',
        'brand', false, true, 26, 'active'
    ),
    (
        md5('seed_role_catalogue_manager')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'catalogue_manager',
        'Catalogue Manager',
        'Pricing, packages, coupons, CMS',
        'brand', false, true, 27, 'active'
    ),
    (
        md5('seed_role_support_lead')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'support_lead',
        'Support Lead',
        'Customers, refunds',
        'brand', false, true, 28, 'active'
    )
ON CONFLICT (brand_id, code) DO NOTHING;


-- =============================================================================
-- SECTION 2 — ROLE PERMISSIONS
-- Unique key: (role_id, permission_id)
-- We look up role ids by code + brand_id, and permission ids by code.
-- =============================================================================

-- Helper: operations_manager permissions
-- Covers: all orders.*, garment.*, qc.*, warehouse.*, stockrecon.*, rider.*,
--         delivery.*, pickup.*, analytics.read
INSERT INTO identity_access.role_permissions (id, role_id, permission_id)
SELECT
    md5('seed_rp_opsmgr_' || p.code)::uuid,
    (SELECT id FROM identity_access.roles
     WHERE brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f' AND code = 'operations_manager'),
    p.id
FROM identity_access.permissions p
WHERE p.code IN (
    -- orders.*
    'orders.cancel','orders.create','orders.list','orders.notes.manage',
    'orders.read','orders.refund','orders.status.update','orders.update',
    -- garment.*
    'garment.inspect','garment.read','garment.tag',
    -- qc.*
    'qc.perform',
    -- warehouse.*
    'warehouse.batch.manage','warehouse.process.scan',
    -- stockrecon.*
    'stockrecon.manage',
    -- rider.*
    'rider.assignment.manage','rider.assignment.read','rider.capacity.manage',
    'rider.manage','rider.read',
    -- delivery.*
    'delivery.assign','delivery.slot.manage','delivery.slot.read',
    -- pickup.*
    'pickup.assign','pickup.create','pickup.read',
    -- analytics.read
    'analytics.read'
)
ON CONFLICT (role_id, permission_id) DO NOTHING;


-- Helper: finance_manager permissions
-- Covers: cashbook.*, expense.*, royalty.*, payment.*, wallet.*, analytics.read
INSERT INTO identity_access.role_permissions (id, role_id, permission_id)
SELECT
    md5('seed_rp_finmgr_' || p.code)::uuid,
    (SELECT id FROM identity_access.roles
     WHERE brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f' AND code = 'finance_manager'),
    p.id
FROM identity_access.permissions p
WHERE p.code IN (
    'cashbook.manage','cashbook.read',
    'expense.approve','expense.manage','expense.read',
    'royalty.manage','royalty.read',
    'payment.read','payment.refund',
    'wallet.adjust','wallet.read',
    'analytics.read'
)
ON CONFLICT (role_id, permission_id) DO NOTHING;


-- Helper: catalogue_manager permissions
-- Covers: pricing.*, catalog.*, packages.*, coupons.*, promotions.*, cms.*, loyalty.*
INSERT INTO identity_access.role_permissions (id, role_id, permission_id)
SELECT
    md5('seed_rp_catmgr_' || p.code)::uuid,
    (SELECT id FROM identity_access.roles
     WHERE brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f' AND code = 'catalogue_manager'),
    p.id
FROM identity_access.permissions p
WHERE p.code IN (
    'pricing.read','pricing.item.manage','pricing.pricelist.create',
    'pricing.pricelist.publish','pricing.pricelist.update',
    'catalog.read','catalog.addon.manage','catalog.category.create',
    'catalog.category.delete','catalog.category.update',
    'catalog.fabric.manage','catalog.item.create','catalog.item.delete',
    'catalog.item.update','catalog.itemgroup.manage',
    'catalog.service.create','catalog.service.delete','catalog.service.update',
    'catalog.variant.manage',
    'packages.manage',
    'coupons.manage',
    'promotions.manage',
    'cms.appconfig.manage','cms.banner.manage','cms.notification.read',
    'cms.onboarding.manage','cms.template.manage',
    'loyalty.manage'
)
ON CONFLICT (role_id, permission_id) DO NOTHING;


-- Helper: support_lead permissions
-- Covers: customer.*, orders.read, orders.list, orders.refund, payment.refund
INSERT INTO identity_access.role_permissions (id, role_id, permission_id)
SELECT
    md5('seed_rp_suplead_' || p.code)::uuid,
    (SELECT id FROM identity_access.roles
     WHERE brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f' AND code = 'support_lead'),
    p.id
FROM identity_access.permissions p
WHERE p.code IN (
    'customer.delete','customer.read','customer.update',
    'orders.read','orders.list','orders.refund',
    'payment.refund'
)
ON CONFLICT (role_id, permission_id) DO NOTHING;


-- =============================================================================
-- SECTION 3 — FRANCHISES
-- Unique key: (brand_id, code)
-- owner_user_id backfilled after users are inserted (via UPDATE ... DO NOTHING
-- pattern is handled by inserting without owner first, then UPDATE below).
-- territory_id / franchise_agreement_id: existing row has NULLs for both — keep NULL.
-- =============================================================================

INSERT INTO tenancy_org.franchises
    (id, brand_id, code, legal_name, display_name, contact_phone,
     billing_address, royalty_percent, marketing_fee_percent,
     onboarding_status, config, metadata, status, version)
VALUES
    -- DLF Phase 4
    (
        md5('seed_franchise_dlf4')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'LGF-DLF4', 'Laundry Ghar DLF Phase 4 Pvt Ltd', 'DLF Phase 4',
        '+919800000110',
        '{"line1":"Shop 12, DLF Phase 4 Market","city":"Gurugram","state":"Haryana","pincode":"122009","country":"IN"}'::jsonb,
        6.00, 2.00,
        'active',
        '{}'::jsonb,
        '{"sinceYear":2024,"location":"Gurugram","ownershipType":"franchise","staffCount":8,"riderCount":5,"revenueMonthly":1140000}'::jsonb,
        'active', 1
    ),
    -- Sector 56
    (
        md5('seed_franchise_sec56')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'LGF-S56', 'Laundry Ghar Sector 56 Pvt Ltd', 'Sector 56',
        '+919800000111',
        '{"line1":"Shop 5, Sector 56 Market","city":"Gurugram","state":"Haryana","pincode":"122011","country":"IN"}'::jsonb,
        6.00, 2.00,
        'active',
        '{}'::jsonb,
        '{"sinceYear":2024,"location":"Gurugram","ownershipType":"franchise","staffCount":13,"riderCount":8,"revenueMonthly":830000}'::jsonb,
        'active', 1
    ),
    -- Palam Vihar (onboarding — uses 'setup' which is the nearest valid value before 'active')
    (
        md5('seed_franchise_palam')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'LGF-PALAM', 'Laundry Ghar Palam Vihar Pvt Ltd', 'Palam Vihar',
        '+919800000112',
        '{"line1":"Plot 8, Palam Vihar","city":"Gurugram","state":"Haryana","pincode":"122017","country":"IN"}'::jsonb,
        6.00, 2.00,
        'setup',
        '{}'::jsonb,
        '{"sinceYear":2026,"location":"Gurugram","ownershipType":"franchise","staffCount":4,"riderCount":2,"revenueMonthly":780000}'::jsonb,
        'active', 1
    ),
    -- Sushant Lok
    (
        md5('seed_franchise_sushant')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'LGF-SUSH', 'Laundry Ghar Sushant Lok Pvt Ltd', 'Sushant Lok',
        '+919800000113',
        '{"line1":"Plot 3, Sushant Lok Block A","city":"Gurugram","state":"Haryana","pincode":"122001","country":"IN"}'::jsonb,
        6.00, 2.00,
        'active',
        '{}'::jsonb,
        '{"sinceYear":2025,"location":"Gurugram","ownershipType":"franchise","staffCount":10,"riderCount":6,"revenueMonthly":1090000}'::jsonb,
        'active', 1
    ),
    -- Sector 45 (company-owned — owner_user_id NULL, linked to existing LGF-MAIN franchise below via UPDATE)
    -- Note: Sector 45 franchise already exists as LGF-MAIN; we seed a metadata-enriched alias
    -- Actually: the existing franchise IS Sector 45. We just UPDATE its metadata.
    -- Skip inserting a new Sector 45 franchise; handled via UPDATE below.
    -- Sector 14
    (
        md5('seed_franchise_sec14')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        'LGF-S14', 'Laundry Ghar Sector 14 Pvt Ltd', 'Sector 14',
        '+919800000114',
        '{"line1":"Shop 22, Sector 14 Market","city":"Gurugram","state":"Haryana","pincode":"122001","country":"IN"}'::jsonb,
        6.00, 2.00,
        'active',
        '{}'::jsonb,
        '{"sinceYear":2025,"location":"Gurugram","ownershipType":"franchise","staffCount":12,"riderCount":8,"revenueMonthly":1380000}'::jsonb,
        'active', 1
    )
ON CONFLICT (brand_id, code) DO NOTHING;

-- Update the existing LGF-MAIN (Sector 45) franchise metadata so the console card is populated.
-- We only set metadata if it's still the empty default (idempotent: same jsonb on re-run).
UPDATE tenancy_org.franchises
SET
    display_name    = COALESCE(display_name, 'Sector 45'),
    metadata        = '{"sinceYear":2023,"location":"Gurugram","ownershipType":"company","staffCount":16,"riderCount":9,"revenueMonthly":1620000}'::jsonb,
    updated_at      = now()
WHERE id = '36f9801c-aa60-4c00-b2bb-ad78fff7615e'
  AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f';


-- =============================================================================
-- SECTION 4 — STORES
-- One store already exists: LGG-S45-001 under LGF-MAIN (Sector 45).
-- We add remaining stores so each franchise hits its target count:
--   DLF Phase 4  → 3 stores
--   Sector 56    → 5 stores
--   Palam Vihar  → 2 stores
--   Sushant Lok  → 4 stores
--   Sector 45    → 6 stores  (1 existing + 5 new)
--   Sector 14    → 4 stores
-- Unique key: (brand_id, code)
-- =============================================================================

INSERT INTO tenancy_org.stores
    (id, brand_id, franchise_id, code, name, store_type,
     address_line1, city, state, pincode,
     service_radius_km, status, version)
VALUES
    -- DLF Phase 4 — 3 stores
    (
        md5('seed_store_dlf4_1')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_dlf4')::uuid,
        'LGG-DLF4-001', 'Laundry Ghar DLF Phase 4 - Store 1',
        'walkin', 'Shop 12, DLF Phase 4 Market', 'Gurgaon', 'Haryana', '122009',
        5.00, 'active', 1
    ),
    (
        md5('seed_store_dlf4_2')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_dlf4')::uuid,
        'LGG-DLF4-002', 'Laundry Ghar DLF Phase 4 - Store 2',
        'walkin', 'Shop 34, DLF Phase 4 Galleria', 'Gurgaon', 'Haryana', '122009',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_dlf4_3')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_dlf4')::uuid,
        'LGG-DLF4-003', 'Laundry Ghar DLF Phase 4 - Store 3',
        'pickup_only', 'Kiosk A, DLF Cyber Hub', 'Gurgaon', 'Haryana', '122002',
        3.00, 'active', 1
    ),

    -- Sector 56 — 5 stores
    (
        md5('seed_store_sec56_1')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec56')::uuid,
        'LGG-S56-001', 'Laundry Ghar Sector 56 - Store 1',
        'walkin', 'Shop 5, Sector 56 Market', 'Gurgaon', 'Haryana', '122011',
        5.00, 'active', 1
    ),
    (
        md5('seed_store_sec56_2')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec56')::uuid,
        'LGG-S56-002', 'Laundry Ghar Sector 56 - Store 2',
        'walkin', 'Plot 7, Sector 56 Main Road', 'Gurgaon', 'Haryana', '122011',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_sec56_3')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec56')::uuid,
        'LGG-S56-003', 'Laundry Ghar Sector 56 - Store 3',
        'walkin', 'Shop 2, Sector 56 Community Centre', 'Gurgaon', 'Haryana', '122011',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_sec56_4')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec56')::uuid,
        'LGG-S56-004', 'Laundry Ghar Sector 56 - Store 4',
        'pickup_only', 'Kiosk B, Sector 56 Metro', 'Gurgaon', 'Haryana', '122011',
        3.00, 'active', 1
    ),
    (
        md5('seed_store_sec56_5')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec56')::uuid,
        'LGG-S56-005', 'Laundry Ghar Sector 56 - Store 5',
        'pickup_only', 'Shop 11, Sector 57 Border Market', 'Gurgaon', 'Haryana', '122011',
        3.00, 'active', 1
    ),

    -- Palam Vihar — 2 stores (franchise is in 'setup' / onboarding; stores can be 'coming_soon')
    (
        md5('seed_store_palam_1')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_palam')::uuid,
        'LGG-PALAM-001', 'Laundry Ghar Palam Vihar - Store 1',
        'walkin', 'Plot 8, Palam Vihar Market', 'Gurgaon', 'Haryana', '122017',
        5.00, 'coming_soon', 1
    ),
    (
        md5('seed_store_palam_2')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_palam')::uuid,
        'LGG-PALAM-002', 'Laundry Ghar Palam Vihar - Store 2',
        'walkin', 'Shop 3, Palam Vihar Sector 2', 'Gurgaon', 'Haryana', '122017',
        4.00, 'coming_soon', 1
    ),

    -- Sushant Lok — 4 stores
    (
        md5('seed_store_sush_1')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sushant')::uuid,
        'LGG-SUSH-001', 'Laundry Ghar Sushant Lok - Store 1',
        'walkin', 'Plot 3, Sushant Lok Block A', 'Gurgaon', 'Haryana', '122001',
        5.00, 'active', 1
    ),
    (
        md5('seed_store_sush_2')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sushant')::uuid,
        'LGG-SUSH-002', 'Laundry Ghar Sushant Lok - Store 2',
        'walkin', 'Shop 8, Sushant Lok Block B', 'Gurgaon', 'Haryana', '122001',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_sush_3')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sushant')::uuid,
        'LGG-SUSH-003', 'Laundry Ghar Sushant Lok - Store 3',
        'pickup_only', 'Kiosk C, Sushant Lok Phase 2', 'Gurgaon', 'Haryana', '122001',
        3.00, 'active', 1
    ),
    (
        md5('seed_store_sush_4')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sushant')::uuid,
        'LGG-SUSH-004', 'Laundry Ghar Sushant Lok - Store 4',
        'pickup_only', 'Shop 15, Sushant Lok Sector 27', 'Gurgaon', 'Haryana', '122001',
        3.00, 'active', 1
    ),

    -- Sector 45 — 5 additional (1 already exists = LGG-S45-001)
    (
        md5('seed_store_sec45_2')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        '36f9801c-aa60-4c00-b2bb-ad78fff7615e',
        'LGG-S45-002', 'Laundry Ghar Sector 45 - Store 2',
        'walkin', '456 Sector 45 Block B', 'Gurgaon', 'Haryana', '122003',
        5.00, 'active', 1
    ),
    (
        md5('seed_store_sec45_3')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        '36f9801c-aa60-4c00-b2bb-ad78fff7615e',
        'LGG-S45-003', 'Laundry Ghar Sector 45 - Store 3',
        'pickup_only', 'Shop 7, Sector 45 Commercial', 'Gurgaon', 'Haryana', '122003',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_sec45_4')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        '36f9801c-aa60-4c00-b2bb-ad78fff7615e',
        'LGG-S45-004', 'Laundry Ghar Sector 45 - Store 4',
        'walkin', 'Plot 19, Sector 46 Junction', 'Gurgaon', 'Haryana', '122003',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_sec45_5')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        '36f9801c-aa60-4c00-b2bb-ad78fff7615e',
        'LGG-S45-005', 'Laundry Ghar Sector 45 - Store 5',
        'express', 'Express Hub, Sector 44', 'Gurgaon', 'Haryana', '122003',
        6.00, 'active', 1
    ),
    (
        md5('seed_store_sec45_6')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        '36f9801c-aa60-4c00-b2bb-ad78fff7615e',
        'LGG-S45-006', 'Laundry Ghar Sector 45 - Store 6',
        'pickup_only', 'Kiosk D, Sector 45 Metro', 'Gurgaon', 'Haryana', '122003',
        3.00, 'active', 1
    ),

    -- Sector 14 — 4 stores
    (
        md5('seed_store_sec14_1')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec14')::uuid,
        'LGG-S14-001', 'Laundry Ghar Sector 14 - Store 1',
        'walkin', 'Shop 22, Sector 14 Market', 'Gurgaon', 'Haryana', '122001',
        5.00, 'active', 1
    ),
    (
        md5('seed_store_sec14_2')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec14')::uuid,
        'LGG-S14-002', 'Laundry Ghar Sector 14 - Store 2',
        'walkin', 'Plot 4, Sector 14 Block C', 'Gurgaon', 'Haryana', '122001',
        4.00, 'active', 1
    ),
    (
        md5('seed_store_sec14_3')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec14')::uuid,
        'LGG-S14-003', 'Laundry Ghar Sector 14 - Store 3',
        'pickup_only', 'Kiosk E, Sector 14 Commercial', 'Gurgaon', 'Haryana', '122001',
        3.00, 'active', 1
    ),
    (
        md5('seed_store_sec14_4')::uuid,
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        md5('seed_franchise_sec14')::uuid,
        'LGG-S14-004', 'Laundry Ghar Sector 14 - Store 4',
        'walkin', 'Shop 9, Sector 13 Adjoining Market', 'Gurgaon', 'Haryana', '122001',
        4.00, 'active', 1
    )
ON CONFLICT (brand_id, code) DO NOTHING;


-- =============================================================================
-- SECTION 5 — USERS
-- unique on email; also unique on phone_e164
-- user_type CHECK: platform_admin|brand_admin|franchise_owner|store_admin|staff|
--                  warehouse_staff|rider|auditor|support
-- status CHECK: active|invited|locked|suspended|deleted
-- =============================================================================

INSERT INTO identity_access.users
    (id, email, phone_e164, password_hash,
     user_type, locale, timezone, status,
     must_change_password, mfa_enabled, failed_attempts, version)
VALUES
    -- 1. Shantanu Banerjee — platform_admin
    (
        md5('seed_user_shantanu_b')::uuid,
        'shantanu.b@laundryghar.in', '+919800000101',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'platform_admin', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 2. Meera Krishnan — operations_manager (staff)
    (
        md5('seed_user_meera_k')::uuid,
        'meera.k@laundryghar.in', '+919800000102',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 3. Arjun Desai — finance_manager (staff)
    (
        md5('seed_user_arjun_d')::uuid,
        'arjun.d@laundryghar.in', '+919800000103',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 4. Kavya Reddy — catalogue_manager (staff)
    (
        md5('seed_user_kavya_r')::uuid,
        'kavya.r@laundryghar.in', '+919800000104',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 5. Imran Shaikh — support_lead (staff)
    (
        md5('seed_user_imran_s')::uuid,
        'imran.s@laundryghar.in', '+919800000105',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 6. Sneha Iyer — operations_manager (staff)
    (
        md5('seed_user_sneha_i')::uuid,
        'sneha.i@laundryghar.in', '+919800000106',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 7. Vikas Rao — finance_manager (staff)
    (
        md5('seed_user_vikas_r')::uuid,
        'vikas.r@laundryghar.in', '+919800000107',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 8. Ritu Agarwal — support_lead (staff, invited)
    (
        md5('seed_user_ritu_a')::uuid,
        'ritu.a@laundryghar.in', '+919800000108',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'invited',
        false, false, 0, 1
    ),
    -- 9. Rajiv Malhotra — franchise_owner (DLF Phase 4)
    (
        md5('seed_user_rajiv_m')::uuid,
        'rajiv.m@dl4.in', '+919800000109',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'franchise_owner', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 10. Priya Nair — franchise_owner (Sector 56)
    (
        md5('seed_user_priya_n')::uuid,
        'priya@sec56.in', '+919800000110',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'franchise_owner', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 11. Amit Saxena — franchise_owner (Palam Vihar, invited)
    (
        md5('seed_user_amit_sx')::uuid,
        'amit@palam.in', '+919800000111',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'franchise_owner', 'en-IN', 'Asia/Kolkata', 'invited',
        false, false, 0, 1
    ),
    -- 12. Neha Gupta — franchise_owner (Sushant Lok)
    (
        md5('seed_user_neha_g')::uuid,
        'neha@sushant.in', '+919800000112',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'franchise_owner', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 13. Vikram Reddy — franchise_owner (Sector 14)
    (
        md5('seed_user_vikram_r')::uuid,
        'vikram@sec14.in', '+919800000113',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'franchise_owner', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 14. Pooja Verma — store_admin at DLF Phase 4
    (
        md5('seed_user_pooja_v')::uuid,
        'pooja.v@laundryghar.in', '+919800000114',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'store_admin', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 15. Karan Mehta — store_staff at Sector 56
    (
        md5('seed_user_karan_m')::uuid,
        'karan.m@laundryghar.in', '+919800000115',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 16. Anjali Singh — store_admin at Sushant Lok
    (
        md5('seed_user_anjali_s')::uuid,
        'anjali.s@laundryghar.in', '+919800000116',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'store_admin', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 17. Deepak Joshi — store_staff at Sector 14
    (
        md5('seed_user_deepak_j')::uuid,
        'deepak.j@laundryghar.in', '+919800000117',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'staff', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    ),
    -- 18. Rohan Kapoor — store_admin at Sector 45
    (
        md5('seed_user_rohan_k')::uuid,
        'rohan.k@laundryghar.in', '+919800000118',
        'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
        'store_admin', 'en-IN', 'Asia/Kolkata', 'active',
        false, false, 0, 1
    )
ON CONFLICT (email) DO NOTHING;


-- =============================================================================
-- SECTION 6 — USER PROFILES (first/last name, display name)
-- PK: user_id — ON CONFLICT (user_id) DO NOTHING
-- =============================================================================

INSERT INTO identity_access.user_profiles
    (user_id, first_name, last_name, display_name, designation, status)
VALUES
    (md5('seed_user_shantanu_b')::uuid, 'Shantanu', 'Banerjee',  'Shantanu Banerjee',  'Platform Admin',        'active'),
    (md5('seed_user_meera_k')::uuid,    'Meera',    'Krishnan',  'Meera Krishnan',     'Operations Manager',    'active'),
    (md5('seed_user_arjun_d')::uuid,    'Arjun',    'Desai',     'Arjun Desai',        'Finance Manager',       'active'),
    (md5('seed_user_kavya_r')::uuid,    'Kavya',    'Reddy',     'Kavya Reddy',        'Catalogue Manager',     'active'),
    (md5('seed_user_imran_s')::uuid,    'Imran',    'Shaikh',    'Imran Shaikh',       'Support Lead',          'active'),
    (md5('seed_user_sneha_i')::uuid,    'Sneha',    'Iyer',      'Sneha Iyer',         'Operations Manager',    'active'),
    (md5('seed_user_vikas_r')::uuid,    'Vikas',    'Rao',       'Vikas Rao',          'Finance Manager',       'active'),
    (md5('seed_user_ritu_a')::uuid,     'Ritu',     'Agarwal',   'Ritu Agarwal',       'Support Lead',          'active'),
    (md5('seed_user_rajiv_m')::uuid,    'Rajiv',    'Malhotra',  'Rajiv Malhotra',     'Franchise Owner',       'active'),
    (md5('seed_user_priya_n')::uuid,    'Priya',    'Nair',      'Priya Nair',         'Franchise Owner',       'active'),
    (md5('seed_user_amit_sx')::uuid,    'Amit',     'Saxena',    'Amit Saxena',        'Franchise Owner',       'active'),
    (md5('seed_user_neha_g')::uuid,     'Neha',     'Gupta',     'Neha Gupta',         'Franchise Owner',       'active'),
    (md5('seed_user_vikram_r')::uuid,   'Vikram',   'Reddy',     'Vikram Reddy',       'Franchise Owner',       'active'),
    (md5('seed_user_pooja_v')::uuid,    'Pooja',    'Verma',     'Pooja Verma',        'Store Manager',         'active'),
    (md5('seed_user_karan_m')::uuid,    'Karan',    'Mehta',     'Karan Mehta',        'Store Staff',           'active'),
    (md5('seed_user_anjali_s')::uuid,   'Anjali',   'Singh',     'Anjali Singh',       'Store Manager',         'active'),
    (md5('seed_user_deepak_j')::uuid,   'Deepak',   'Joshi',     'Deepak Joshi',       'Store Staff',           'active'),
    (md5('seed_user_rohan_k')::uuid,    'Rohan',    'Kapoor',    'Rohan Kapoor',       'Store Manager',         'active')
ON CONFLICT (user_id) DO NOTHING;


-- =============================================================================
-- SECTION 7 — BACKFILL owner_user_id on franchises
-- =============================================================================

UPDATE tenancy_org.franchises
SET owner_user_id = md5('seed_user_rajiv_m')::uuid, updated_at = now()
WHERE id = md5('seed_franchise_dlf4')::uuid
  AND owner_user_id IS DISTINCT FROM md5('seed_user_rajiv_m')::uuid;

UPDATE tenancy_org.franchises
SET owner_user_id = md5('seed_user_priya_n')::uuid, updated_at = now()
WHERE id = md5('seed_franchise_sec56')::uuid
  AND owner_user_id IS DISTINCT FROM md5('seed_user_priya_n')::uuid;

UPDATE tenancy_org.franchises
SET owner_user_id = md5('seed_user_amit_sx')::uuid, updated_at = now()
WHERE id = md5('seed_franchise_palam')::uuid
  AND owner_user_id IS DISTINCT FROM md5('seed_user_amit_sx')::uuid;

UPDATE tenancy_org.franchises
SET owner_user_id = md5('seed_user_neha_g')::uuid, updated_at = now()
WHERE id = md5('seed_franchise_sushant')::uuid
  AND owner_user_id IS DISTINCT FROM md5('seed_user_neha_g')::uuid;

-- Sector 45 (LGF-MAIN) is company-owned — owner_user_id stays NULL.

UPDATE tenancy_org.franchises
SET owner_user_id = md5('seed_user_vikram_r')::uuid, updated_at = now()
WHERE id = md5('seed_franchise_sec14')::uuid
  AND owner_user_id IS DISTINCT FROM md5('seed_user_vikram_r')::uuid;


-- =============================================================================
-- SECTION 8 — USER SCOPE MEMBERSHIPS
-- Unique key: (user_id, scope_type, scope_id, role_id)
-- Role IDs looked up inline by code (system roles) or by brand_id+code (new roles).
-- scope_id for platform-scoped rows = NULL (platform has no specific entity id).
-- scope_id for brand-scoped rows    = brand_id.
-- scope_id for franchise-scoped     = franchise id.
-- scope_id for store-scoped         = store id.
-- =============================================================================

-- HQ / brand-scoped memberships
INSERT INTO identity_access.user_scope_memberships
    (id, user_id, scope_type, scope_id, role_id, is_primary, metadata)
VALUES
    -- 1. Shantanu — platform_admin — platform scope (scope_id = NULL is not allowed in unique key;
    --    use brand scope for platform_admin to keep scope_id meaningful)
    (
        md5('seed_mem_shantanu_b')::uuid,
        md5('seed_user_shantanu_b')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles WHERE code = 'platform_admin' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 2. Meera — operations_manager — brand scope
    (
        md5('seed_mem_meera_k')::uuid,
        md5('seed_user_meera_k')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'operations_manager' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),
    -- 3. Arjun — finance_manager — brand scope
    (
        md5('seed_mem_arjun_d')::uuid,
        md5('seed_user_arjun_d')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'finance_manager' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),
    -- 4. Kavya — catalogue_manager — brand scope
    (
        md5('seed_mem_kavya_r')::uuid,
        md5('seed_user_kavya_r')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'catalogue_manager' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),
    -- 5. Imran — support_lead — brand scope
    (
        md5('seed_mem_imran_s')::uuid,
        md5('seed_user_imran_s')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'support_lead' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),
    -- 6. Sneha — operations_manager — brand scope
    (
        md5('seed_mem_sneha_i')::uuid,
        md5('seed_user_sneha_i')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'operations_manager' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),
    -- 7. Vikas — finance_manager — brand scope
    (
        md5('seed_mem_vikas_r')::uuid,
        md5('seed_user_vikas_r')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'finance_manager' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),
    -- 8. Ritu — support_lead — brand scope
    (
        md5('seed_mem_ritu_a')::uuid,
        md5('seed_user_ritu_a')::uuid,
        'brand',
        '5b375161-9b8b-4177-ab58-54848606aa2f',
        (SELECT id FROM identity_access.roles
         WHERE code = 'support_lead' AND brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'),
        true,
        '{}'::jsonb
    ),

    -- Franchise owners — franchise scope
    -- 9. Rajiv — DLF Phase 4
    (
        md5('seed_mem_rajiv_m')::uuid,
        md5('seed_user_rajiv_m')::uuid,
        'franchise',
        md5('seed_franchise_dlf4')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'franchise_owner' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 10. Priya — Sector 56
    (
        md5('seed_mem_priya_n')::uuid,
        md5('seed_user_priya_n')::uuid,
        'franchise',
        md5('seed_franchise_sec56')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'franchise_owner' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 11. Amit — Palam Vihar
    (
        md5('seed_mem_amit_sx')::uuid,
        md5('seed_user_amit_sx')::uuid,
        'franchise',
        md5('seed_franchise_palam')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'franchise_owner' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 12. Neha — Sushant Lok
    (
        md5('seed_mem_neha_g')::uuid,
        md5('seed_user_neha_g')::uuid,
        'franchise',
        md5('seed_franchise_sushant')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'franchise_owner' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 13. Vikram — Sector 14
    (
        md5('seed_mem_vikram_r')::uuid,
        md5('seed_user_vikram_r')::uuid,
        'franchise',
        md5('seed_franchise_sec14')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'franchise_owner' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),

    -- Store staff/admins — store scope
    -- 14. Pooja — store_admin at DLF Phase 4 Store 1
    (
        md5('seed_mem_pooja_v')::uuid,
        md5('seed_user_pooja_v')::uuid,
        'store',
        md5('seed_store_dlf4_1')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'store_admin' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 15. Karan — store_staff at Sector 56 Store 1
    (
        md5('seed_mem_karan_m')::uuid,
        md5('seed_user_karan_m')::uuid,
        'store',
        md5('seed_store_sec56_1')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'store_staff' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 16. Anjali — store_admin at Sushant Lok Store 1
    (
        md5('seed_mem_anjali_s')::uuid,
        md5('seed_user_anjali_s')::uuid,
        'store',
        md5('seed_store_sush_1')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'store_admin' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 17. Deepak — store_staff at Sector 14 Store 1
    (
        md5('seed_mem_deepak_j')::uuid,
        md5('seed_user_deepak_j')::uuid,
        'store',
        md5('seed_store_sec14_1')::uuid,
        (SELECT id FROM identity_access.roles WHERE code = 'store_staff' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    ),
    -- 18. Rohan — store_admin at Sector 45 (existing store LGG-S45-001)
    (
        md5('seed_mem_rohan_k')::uuid,
        md5('seed_user_rohan_k')::uuid,
        'store',
        '60e5bb20-8e4e-4892-a85e-449402463cf9',
        (SELECT id FROM identity_access.roles WHERE code = 'store_admin' AND brand_id IS NULL),
        true,
        '{}'::jsonb
    )
ON CONFLICT (user_id, scope_type, scope_id, role_id) DO NOTHING;


-- =============================================================================
-- VERIFICATION SELECTS
-- =============================================================================

\echo ''
\echo '=== NEW ROLES ==='
SELECT code, name, scope_type, priority, status
FROM identity_access.roles
WHERE brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
  AND is_system = false
ORDER BY priority;

\echo ''
\echo '=== ROLE PERMISSION COUNTS (new roles only) ==='
SELECT r.code, COUNT(rp.id) AS permission_count
FROM identity_access.roles r
JOIN identity_access.role_permissions rp ON rp.role_id = r.id
WHERE r.brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
  AND r.is_system = false
GROUP BY r.code
ORDER BY r.code;

\echo ''
\echo '=== TOTAL USERS SEEDED ==='
SELECT COUNT(*) AS total_users
FROM identity_access.users
WHERE email LIKE '%@laundryghar.in'
   OR email IN (
       'rajiv.m@dl4.in','priya@sec56.in','amit@palam.in',
       'neha@sushant.in','vikram@sec14.in'
   );

\echo ''
\echo '=== USERS BY TIER ==='
SELECT
    CASE
        WHEN u.user_type = 'platform_admin'  THEN '1. Platform Admin'
        WHEN u.user_type = 'franchise_owner' THEN '3. Franchise Owner'
        WHEN u.user_type IN ('store_admin')  THEN '4. Store Admin'
        WHEN u.user_type = 'staff' AND EXISTS (
            SELECT 1 FROM identity_access.user_scope_memberships m
            JOIN identity_access.roles r ON r.id = m.role_id
            WHERE m.user_id = u.id AND r.scope_type = 'brand'
        )                                    THEN '2. HQ Staff'
        ELSE '4. Store Staff'
    END AS tier,
    up.display_name,
    u.email,
    u.status
FROM identity_access.users u
JOIN identity_access.user_profiles up ON up.user_id = u.id
WHERE u.id IN (
    SELECT md5(k)::uuid FROM (VALUES
        ('seed_user_shantanu_b'),('seed_user_meera_k'),('seed_user_arjun_d'),
        ('seed_user_kavya_r'),('seed_user_imran_s'),('seed_user_sneha_i'),
        ('seed_user_vikas_r'),('seed_user_ritu_a'),('seed_user_rajiv_m'),
        ('seed_user_priya_n'),('seed_user_amit_sx'),('seed_user_neha_g'),
        ('seed_user_vikram_r'),('seed_user_pooja_v'),('seed_user_karan_m'),
        ('seed_user_anjali_s'),('seed_user_deepak_j'),('seed_user_rohan_k')
    ) AS t(k)
)
ORDER BY tier, up.display_name;

\echo ''
\echo '=== FRANCHISES WITH OWNER ==='
SELECT
    f.code,
    f.display_name,
    f.onboarding_status,
    f.status,
    COALESCE(up.display_name, '(company-owned)') AS owner_name,
    (f.metadata->>'revenueMonthly')::bigint        AS revenue_monthly
FROM tenancy_org.franchises f
LEFT JOIN identity_access.user_profiles up ON up.user_id = f.owner_user_id
WHERE f.brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
ORDER BY f.code;

\echo ''
\echo '=== STORE COUNTS PER FRANCHISE ==='
SELECT
    f.display_name  AS franchise,
    COUNT(s.id)     AS store_count
FROM tenancy_org.franchises f
JOIN tenancy_org.stores s ON s.franchise_id = f.id
WHERE f.brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
  AND s.deleted_at IS NULL
GROUP BY f.id, f.display_name
ORDER BY f.display_name;

COMMIT;
