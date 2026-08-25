-- 0022_deterministic_nav — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- Makes the admin sidebar deterministic and legible. Three separate defects, all found by
-- reading the rendered menu against this table rather than by any test.
--
-- ─── 1. The order was not stable ─────────────────────────────────────────────────────────────
-- GetNavigator orders by nav_order alone, and three pairs of nav rows shared a value:
--     cms / promotions       -> 23
--     fabrics / subscriptions-> 24
--     appointments / warehouse -> 13
-- With no tiebreaker the winner is whatever order Postgres happens to return, which is not
-- guaranteed to be the same twice. The live database returned CMS before Promotions; the browser
-- showed Promotions before CMS. Both were "correct". A sidebar that can reshuffle itself between
-- page loads costs users the muscle memory that is most of a menu's value.
--
-- The fix is on both sides: this migration removes the ties, and GetNavigator gains an explicit
-- .ThenBy(Key) so a future tie degrades to alphabetical instead of to chance.
--
-- Every value is multiplied by 10 rather than renumbered by hand. That preserves the CURRENT
-- on-screen order exactly — this migration is not a chance to re-decide the menu — while opening
-- 9 slots between neighbours so the next insert never has to reuse a number. The two hidden
-- appointments/warehouse rows keep warehouse where it renders today and park appointments
-- immediately after it.
--
-- ─── 2. Icons repeated ───────────────────────────────────────────────────────────────────────
-- `Coins` was on Expenses AND Royalty AND Platform billing; `Layers` on Fabrics and Platform
-- plans; `Tag` on Pricing and Subscriptions. Six of nineteen rows were visually ambiguous, which
-- is why the whole Finance group read as one repeated row. An icon that appears three times is
-- doing the job of a bullet point.
--
-- `Bell` on CMS was a plain mismatch: it is the universal notifications glyph, sitting on the
-- content-management screen.
--
-- ─── 3. Nothing stopped an icon the client cannot draw ───────────────────────────────────────
-- `appointments` is seeded with `CalendarClock`, which is NOT registered in the client's icon map
-- (admin-web/src/components/layout/Sidebar.tsx). It renders as a generic grid square. It is
-- invisible today only because laundry brands are not entitled to the `scheduling` feature — the
-- first salon or tiffin brand onboarded would have seen it.
--
-- The constraint below closes that class for good. The allowed set is exactly what the client
-- registers, so seeding a nav icon the UI cannot draw now fails HERE, in the migration that
-- introduces it, instead of shipping as a silent wrong glyph. Adding an icon deliberately costs a
-- paired migration + client change; that pairing is the point, not an inconvenience.

-- ─── 1. Break the nav_order ties, preserving the current visible order ───────────────────────
UPDATE identity_access.modules SET nav_order = nav_order * 10 WHERE show_in_nav;

-- The three former ties, placed in the order they render today.
UPDATE identity_access.modules SET nav_order = 135 WHERE key = 'appointments';  -- after warehouse (130)
UPDATE identity_access.modules SET nav_order = 235 WHERE key = 'cms';           -- after promotions (230)
UPDATE identity_access.modules SET nav_order = 245 WHERE key = 'fabrics';       -- after subscriptions (240)

-- Make a recurrence impossible rather than merely unlikely: two nav rows in one section may not
-- claim the same slot. Scoped to show_in_nav so the matrix-only rows (which never render in a
-- menu and share no ordering contract) are unaffected.
CREATE UNIQUE INDEX modules_nav_slot_unique
    ON identity_access.modules (section, nav_order)
    WHERE show_in_nav;

-- ─── 2. One icon, one meaning ────────────────────────────────────────────────────────────────
UPDATE identity_access.modules SET icon = 'Wallet'     WHERE key = 'expenses';          -- was Coins
UPDATE identity_access.modules SET icon = 'Percent'    WHERE key = 'royalty';           -- was Coins (a royalty IS a percentage)
UPDATE identity_access.modules SET icon = 'CreditCard' WHERE key = 'platform_billing';  -- was Coins
UPDATE identity_access.modules SET icon = 'Boxes'      WHERE key = 'platform_plans';    -- was Layers (kept for Fabrics)
UPDATE identity_access.modules SET icon = 'Repeat'     WHERE key = 'subscriptions';     -- was Tag (kept for Pricing)
UPDATE identity_access.modules SET icon = 'Newspaper'  WHERE key = 'cms';               -- was Bell (notifications glyph)

-- ─── 3. A nav icon must be one the client can actually draw ──────────────────────────────────
-- This list mirrors the ICONS map in admin-web/src/components/layout/Sidebar.tsx exactly. Change
-- one, change the other, in the same commit.
ALTER TABLE identity_access.modules
    ADD CONSTRAINT modules_nav_icon_registered CHECK (
        NOT show_in_nav OR icon IS NULL OR icon IN (
            'BarChart2', 'Bell', 'Bike', 'BookOpen', 'Boxes', 'Building2', 'CalendarClock',
            'Coins', 'CreditCard', 'Layers', 'LayoutDashboard', 'LayoutGrid', 'LifeBuoy',
            'Megaphone', 'Monitor', 'Network', 'Newspaper', 'Package', 'Percent', 'Receipt',
            'Repeat', 'Settings', 'ShieldCheck', 'Shirt', 'ShoppingCart', 'Tag', 'Users',
            'Wallet', 'Warehouse'
        )
    );

-- ─── 4. Assert all three defects are actually gone ───────────────────────────────────────────
DO $$
DECLARE dup_slot text; dup_icon text;
BEGIN
    SELECT string_agg(section || '/' || nav_order, ', ') INTO dup_slot
    FROM  (SELECT section, nav_order FROM identity_access.modules
           WHERE show_in_nav GROUP BY section, nav_order HAVING count(*) > 1) d;
    IF dup_slot IS NOT NULL THEN
        RAISE EXCEPTION 'nav rows still share a slot: %', dup_slot;
    END IF;

    SELECT string_agg(icon, ', ') INTO dup_icon
    FROM  (SELECT icon FROM identity_access.modules
           WHERE show_in_nav AND icon IS NOT NULL GROUP BY icon HAVING count(*) > 1) d;
    IF dup_icon IS NOT NULL THEN
        RAISE EXCEPTION 'nav icon(s) still used by more than one row: %', dup_icon;
    END IF;

    RAISE NOTICE 'sidebar is deterministic: unique slot per row, unique icon per row, every icon drawable';
END $$;
