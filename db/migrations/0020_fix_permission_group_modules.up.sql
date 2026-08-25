-- 0020_fix_permission_group_modules — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- Corrects four misclassifications in the §6.2 permission groups seeded by migration 0013.
--
-- ─── How they were found ─────────────────────────────────────────────────────────────────────
-- By diffing the LIVE role surface, cell by cell, against §6.2's table in PLATFORM_STRATEGY.md.
-- Twenty-two of the seventy-five gradeable cells disagreed. Six looked like over-grants — a role
-- holding MORE than the strategy says it should — which is the alarming direction.
--
-- Four of those six were not over-grants at all. They were this table putting a permission in the
-- wrong group, so the matrix described authority nobody had. That is worse than a missing matrix:
-- 0013's own commit message argued that "a role matrix that lies is worse than none, because people
-- delegate on it", and then this table made it lie in the most frightening possible direction —
-- someone reading it would have gone hunting for grants that do not exist.
--
-- ─── The four ───────────────────────────────────────────────────────────────────────────────
--
-- 1. `billing` claimed `royalty` and `subscription`.
--    §6.2 group 10 is "Subscription & Billing (**pays us**)" — the platform subscription. `royalty`
--    is the FRANCHISEE's royalty to their brand, and `subscription` is a CUSTOMER's recurring plan
--    (§5's `customer_subscriptions`). Three different money flows, only one of which is ours.
--    Effect: `franchise_owner` — the engine role behind the §6 "Manager" preset — appeared to hold
--    FULL control of the platform subscription, reading as a flat violation of **Law 1**
--    ("the subscription belongs to the Owner only"). It holds `royalty.*` and `subscription.*`, and
--    nothing whatsoever on `saas` or `platform_plans`. Law 1 was never broken.
--
-- 2. `settings` claimed `feature_flag`.
--    §6.2 group 9 is "Branding, Domain & Settings". Which features are switched on is not branding —
--    it is what the business has bought, which is group 10.
--
-- 3. `staff` claimed the `rider` module.
--    Group 5 is "Staff & Riders", meaning MANAGING them — that is the `riders` module. The bare
--    `rider` module is `rider.tasks.*`: a rider reading and updating their OWN assigned jobs. Filing
--    it under staff management made a Rider look like a people manager, and simultaneously left the
--    Rider row empty on Bookings and Dispatch where §6.2 says `O` (own) — one mistake causing two
--    opposite-looking errors.
--
-- 4. `bookings` did not claim `subscription`.
--    Follows from 1: a customer's recurring plan is a repeating BOOKING, which is where §6.2 would
--    put it.
--
-- ─── What is NOT changed here ────────────────────────────────────────────────────────────────
-- The two REAL divergences the diff surfaced are left exactly as they are, because both are grant
-- decisions and not labelling ones — and quietly re-granting to make a table agree with a document
-- is the same class of mistake in the opposite direction. They are recorded as OQ-9 and OQ-16.

-- Every group is written as a COMPLETE array, never appended to. An `||` append is not idempotent:
-- re-applying after a partial rollback duplicated modules inside a single group, which the
-- duplicate assertion below then reported as "claimed by two groups". Complete arrays make the
-- migration re-runnable and make each group's contents readable in one place.

UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['order', 'orders', 'pickup', 'booking', 'appointment', 'pos',
          -- a customer's recurring plan is a repeating BOOKING (§5 `customer_subscriptions`)
          'subscription', 'partner_booking']
 WHERE key = 'bookings';

UPDATE identity_access.permission_groups SET permission_modules =
    -- `rider` (rider.tasks.*) is a rider's OWN jobs — dispatch-side, not people management.
    ARRAY['delivery', 'dispatch', 'rider_assignment', 'rider']
 WHERE key = 'dispatch';

UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['customer', 'customers', 'support']
 WHERE key = 'customers';

UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['catalog', 'pricing', 'items', 'item', 'service', 'package', 'packages',
          'fabric', 'fabrics',
          -- coupons, loyalty and promotions decide what a customer is charged
          'coupons', 'loyalty', 'promotions']
 WHERE key = 'catalog';

UPDATE identity_access.permission_groups SET permission_modules =
    -- `riders` = managing them. memberships = who holds which role; permissions = the catalogue.
    ARRAY['user', 'users', 'role', 'roles', 'riders', 'memberships', 'permissions']
 WHERE key = 'staff';

UPDATE identity_access.permission_groups SET permission_modules =
    -- `royalty` is money the FRANCHISEE pays their brand — a provider-side money flow, not ours.
    ARRAY['payment', 'payments', 'refund', 'wallet', 'cashbook', 'expense', 'expenses',
          'royalty', 'paymentmethod']
 WHERE key = 'money';

UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['garment', 'fulfillment', 'warehouse', 'warehouse_ops', 'quality',
          'qc', 'stockrecon', 'warehouses', 'store_warehouse']
 WHERE key = 'processing';

UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['report', 'reports', 'analytics', 'audit']
 WHERE key = 'reports';

UPDATE identity_access.permission_groups SET permission_modules =
    -- §6.2 group 9 by name: "Branding, Domain & Settings". `domains` and `white_label` belong.
    ARRAY['brand', 'brands', 'settings', 'cms', 'domains', 'white_label']
 WHERE key = 'settings';

UPDATE identity_access.permission_groups SET permission_modules =
    -- §6.2 group 10 is "Subscription & Billing (**pays us**)" — ours, and only ours.
    ARRAY['saas', 'platform_plans', 'feature_flag']
 WHERE key = 'billing';

-- ─── Assertions ──────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    n_groups   int;
    duplicated text;
    unclaimed  text;
BEGIN
    SELECT count(*) INTO n_groups FROM identity_access.permission_groups;
    IF n_groups <> 10 THEN
        RAISE EXCEPTION '§6 allows ten permission groups, found %', n_groups;
    END IF;

    -- A module in two groups means one permission counted twice, and two cells that can never both
    -- be right. This is the check that would have caught the original mistake.
    SELECT string_agg(m || ' (' || cnt || ' groups)', ', ')
    INTO   duplicated
    FROM (
        SELECT unnest(permission_modules) AS m, count(*) AS cnt
        FROM   identity_access.permission_groups
        GROUP  BY 1 HAVING count(*) > 1
    ) d;

    IF duplicated IS NOT NULL THEN
        RAISE EXCEPTION 'module(s) claimed by more than one permission group: %', duplicated;
    END IF;

    -- A permission whose module no group claims is invisible in the matrix — authority that exists
    -- and is never shown. A WARNING, not an error: the platform has modules §6.2's ten groups
    -- legitimately do not cover, and failing the migration for those would teach people to ignore
    -- assertions.
    SELECT string_agg(DISTINCT p.module, ', ')
    INTO   unclaimed
    FROM   identity_access.permissions p
    WHERE  p.status = 'active'
      AND  NOT EXISTS (SELECT 1 FROM identity_access.permission_groups g
                       WHERE p.module = ANY (g.permission_modules));

    IF unclaimed IS NOT NULL THEN
        RAISE WARNING 'permission module(s) in no §6.2 group — invisible in the role matrix: %', unclaimed;
    ELSE
        RAISE NOTICE 'every active permission module is claimed by exactly one group';
    END IF;
END $$;
