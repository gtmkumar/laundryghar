-- 0008_backfill_entitlements_from_plans — rollback: must exactly undo the .up.sql
--
-- Removes the rows this backfill created, returning each brand to whatever entitlements it held
-- before. It cannot distinguish a row IT created from one an operator has since added by hand, so it
-- removes only rows whose timestamps still match their creation — an operator edit updates
-- updated_at via SetBrandFeature and is therefore preserved.
--
-- IMPORTANT: roll this back only with `Entitlement:Enforced` set back to false. Undoing the backfill
-- while enforcement is on is precisely the outage the backfill exists to prevent — LG-MAIN loses its
-- catalogue (`items`) the moment its users' tokens are re-minted.

DELETE FROM identity_access.brand_feature
 WHERE created_at = updated_at            -- untouched since the backfill wrote it
   AND created_by IS NULL                 -- and not attributable to a human action
   AND updated_by IS NULL;
