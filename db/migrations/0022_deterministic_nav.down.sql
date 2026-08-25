-- 0022_deterministic_nav — rollback
--
-- Restores the ambiguous ordering and the repeated icons. The guards come off FIRST: both the
-- unique index and the icon CHECK would reject the state we are rolling back to, which is the
-- whole reason they exist.

ALTER TABLE identity_access.modules DROP CONSTRAINT IF EXISTS modules_nav_icon_registered;
DROP INDEX IF EXISTS identity_access.modules_nav_slot_unique;

UPDATE identity_access.modules SET icon = 'Coins'  WHERE key = 'expenses';
UPDATE identity_access.modules SET icon = 'Coins'  WHERE key = 'royalty';
UPDATE identity_access.modules SET icon = 'Coins'  WHERE key = 'platform_billing';
UPDATE identity_access.modules SET icon = 'Layers' WHERE key = 'platform_plans';
UPDATE identity_access.modules SET icon = 'Tag'    WHERE key = 'subscriptions';
UPDATE identity_access.modules SET icon = 'Bell'   WHERE key = 'cms';

-- Put the three off-grid rows back onto the grid before dividing, so each lands on its original
-- (tied) value: appointments 135 -> 130 -> 13, cms 235 -> 230 -> 23, fabrics 245 -> 240 -> 24.
UPDATE identity_access.modules SET nav_order = 130 WHERE key = 'appointments';
UPDATE identity_access.modules SET nav_order = 230 WHERE key = 'cms';
UPDATE identity_access.modules SET nav_order = 240 WHERE key = 'fabrics';

UPDATE identity_access.modules SET nav_order = nav_order / 10 WHERE show_in_nav;
