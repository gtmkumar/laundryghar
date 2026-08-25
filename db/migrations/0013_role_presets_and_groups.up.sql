-- 0013_role_presets_and_groups — forward migration
--
-- PLATFORM_STRATEGY.md §6: "max 8 roles, 10 permission groups (not 300 permissions), plain do/don't
-- language". §6.3 is explicit about HOW: "Presets over the engine. These 8 roles are presets on the
-- scoped-RBAC engine already specced in RBAC.md. The engine's power stays; the surface is 8 named
-- roles and a 10-row matrix."
--
-- ─── That settles the open question (OQ-5) ───────────────────────────────────────────────────
-- The question was whether the 8 roles REPLACE the 17 shipped system roles or sit OVER them. §6.3's
-- word is "presets ON the engine… the engine's power stays" — additive. So this migration adds a
-- presentation layer and changes not one grant:
--   * no role is renamed, deleted or re-scoped
--   * no role_permission row is touched
--   * every existing membership keeps working
-- A replacement would have meant a destructive grant migration across every brand — exactly the
-- lockout risk MULTI_VERTICAL_BLUEPRINT §8 Risk #6 warns about — to buy a nicer noun.

-- ─── 1. The 8 named roles §6.1 lists ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.role_presets (
    key             VARCHAR(32) PRIMARY KEY,
    name            VARCHAR(64) NOT NULL,
    -- §6's "plain do/don't language", verbatim from the §6.1 table. This is the whole point of the
    -- preset layer: an owner picking a role reads a sentence, not a permission matrix.
    does            TEXT NOT NULL,
    does_not        TEXT NOT NULL,
    -- The engine role this preset actually grants. NULL for `customer`, which is not part of the
    -- staff RBAC graph at all (docs/rbac.md §2) and is listed only to complete the §6 picture.
    role_code       VARCHAR(50) NULL,
    -- Roles follow features (§5): a preset whose feature the brand has not bought is not offered.
    -- Mirrors roles.feature_key from migration 0006.
    requires_feature VARCHAR(64) NULL REFERENCES identity_access.features(key) ON DELETE SET NULL,
    is_platform     BOOLEAN NOT NULL DEFAULT false,   -- our side of §8's line, not the provider's
    sort_order      INTEGER NOT NULL DEFAULT 100
);

GRANT SELECT ON identity_access.role_presets TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.role_presets TO app_admin;

INSERT INTO identity_access.role_presets
    (key, name, does, does_not, role_code, requires_feature, is_platform, sort_order) VALUES
    ('platform_admin', 'Platform Admin',
     'Everything, all providers: plans, features, domains, suspend/reactivate, break-glass support.',
     'Day-to-day work inside a provider''s business.',
     'platform_admin', NULL, true, 10),

    ('platform_support', 'Platform Support',
     'View any provider''s data to help; add notes; guide.',
     'Change money, plans, branding, or any provider config; delete anything.',
     'support', NULL, true, 20),

    ('owner', 'Owner',
     'Everything in their business: locations, staff, pricing, refunds, reports, branding, domain, and pays the subscription.',
     'See any other provider; platform config.',
     'brand_admin', NULL, false, 30),

    ('manager', 'Manager',
     'Run operations: bookings, dispatch, pricing edits, staff and rider management, location reports, refunds up to a cap.',
     'Branding/domain, subscription and billing, add or remove an Owner, refunds above cap.',
     'franchise_owner', NULL, false, 40),

    ('staff', 'Staff',
     'Daily counter work: create and update bookings, collect payment, handle customers, print labels.',
     'Change pricing, manage staff, see reports, refunds, settings.',
     'store_staff', NULL, false, 50),

    ('rider', 'Rider',
     'See and run assigned jobs: accept, navigate, OTP, photos, COD handover, own earnings.',
     'See other riders'' jobs, customers beyond the job, pricing, reports.',
     'rider', 'fleet', false, 60),

    ('facility_staff', 'Facility Staff',
     'Receive, process, QC and scan items at the facility.',
     'Bookings, customers, money, dispatch.',
     'warehouse_staff', 'processing_facility', false, 70),

    ('customer', 'Customer',
     'Book, track, pay and rate — their own data only.',
     'Anything beyond self.',
     NULL, NULL, false, 80)
ON CONFLICT (key) DO UPDATE
    SET name = EXCLUDED.name, does = EXCLUDED.does, does_not = EXCLUDED.does_not,
        role_code = EXCLUDED.role_code, requires_feature = EXCLUDED.requires_feature,
        is_platform = EXCLUDED.is_platform, sort_order = EXCLUDED.sort_order;

-- ─── 2. The 10 permission groups §6.2 lists ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity_access.permission_groups (
    key                VARCHAR(48) PRIMARY KEY,
    name               VARCHAR(64) NOT NULL,
    -- The raw permissions.module values this group covers. Reuses the same reverse-mapping shape as
    -- modules.permission_modules, so one permission catalogue serves both the detailed matrix and
    -- this 10-row summary — they cannot drift into disagreeing.
    permission_modules TEXT[] NOT NULL DEFAULT '{}',
    sort_order         INTEGER NOT NULL DEFAULT 100
);

GRANT SELECT ON identity_access.permission_groups TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.permission_groups TO app_admin;

INSERT INTO identity_access.permission_groups (key, name, permission_modules, sort_order) VALUES
    ('bookings',     'Bookings',                    ARRAY['order','orders','pickup','booking','appointment','pos'], 10),
    ('dispatch',     'Dispatch',                    ARRAY['delivery','dispatch','rider_assignment'],                20),
    ('customers',    'Customers',                   ARRAY['customer','customers','support'],                        30),
    ('catalog',      'Catalog & Pricing',           ARRAY['catalog','pricing','items','item','service','package','packages','fabric','fabrics'], 40),
    ('staff',        'Staff & Riders',              ARRAY['user','users','role','roles','rider','riders'],           50),
    ('money',        'Money',                       ARRAY['payment','payments','refund','wallet','cashbook','expense','expenses'], 60),
    ('processing',   'Item tracking & Processing',  ARRAY['garment','fulfillment','warehouse','warehouse_ops','quality'], 70),
    ('reports',      'Reports',                     ARRAY['report','reports','analytics','audit'],                   80),
    ('settings',     'Branding, Domain & Settings', ARRAY['brand','brands','settings','feature_flag','cms'],         90),
    ('billing',      'Subscription & Billing',      ARRAY['saas','subscription','royalty','platform_plans'],        100)
ON CONFLICT (key) DO UPDATE
    SET name = EXCLUDED.name, permission_modules = EXCLUDED.permission_modules,
        sort_order = EXCLUDED.sort_order;

-- §6 says max 8 roles and 10 groups. The numbers are the design constraint — §12's last risk is
-- "role sprawl: resist adding role #9" — so they are asserted rather than trusted.
DO $$
DECLARE presets int; groups int; dangling text;
BEGIN
    SELECT count(*) INTO presets FROM identity_access.role_presets;
    SELECT count(*) INTO groups  FROM identity_access.permission_groups;

    IF presets > 8 THEN
        RAISE EXCEPTION 'role sprawl: % presets, §6 allows at most 8', presets;
    END IF;
    IF groups > 10 THEN
        RAISE EXCEPTION '% permission groups, §6 allows at most 10', groups;
    END IF;

    -- A preset pointing at a role that does not exist would offer a role nobody can actually be given.
    SELECT string_agg(p.key || ' -> ' || p.role_code, ', ') INTO dangling
    FROM   identity_access.role_presets p
    WHERE  p.role_code IS NOT NULL
      AND  NOT EXISTS (SELECT 1 FROM identity_access.roles r WHERE r.code = p.role_code);

    IF dangling IS NOT NULL THEN
        RAISE WARNING 'presets referencing a missing role (harmless here, fatal in a seeded env): %', dangling;
    END IF;

    RAISE NOTICE '§6 surface: % role presets, % permission groups', presets, groups;
END $$;
