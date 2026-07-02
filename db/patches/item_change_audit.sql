-- =============================================================================
-- item_change_audit.sql  (GH #24 — catalog item change audit)
--   Item create/update/delete (and status/code changes) are recorded in the
--   pricing change log so they appear in the Change history tab and an UPDATE can
--   be reverted. This only widens the target_kind CHECK to admit 'item'; the log
--   rows themselves are written by the item command handlers (PricingChangeLogger).
--
--   The before/after payload for an 'item' entry is an envelope:
--     { "op": "create|update|delete", "state": { name, code, itemGroupId, status,
--       tatHours, expressEligible, expressSurcharge, pricingMode } | null }
--   Revert restores the before-state of an UPDATE; create/delete are not revertible.
--
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/item_change_audit.sql
-- =============================================================================

BEGIN;

-- Extend pricing_change_log.target_kind CHECK with 'item' (drop + re-add so the
-- new set is authoritative regardless of which earlier patch last defined it).
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'pricing_change_log_target_kind_check'
          AND conrelid = 'customer_catalog.pricing_change_log'::regclass
    ) THEN
        ALTER TABLE customer_catalog.pricing_change_log
            DROP CONSTRAINT pricing_change_log_target_kind_check;
    END IF;
    ALTER TABLE customer_catalog.pricing_change_log
        ADD CONSTRAINT pricing_change_log_target_kind_check
        CHECK (target_kind IN ('fabric_type','price_list_item','add_on','value_price_slab','item'));
END $$;

COMMIT;

-- Verification.
SELECT 'pricing_change_log target_kind admits item' AS check,
       pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conname = 'pricing_change_log_target_kind_check'
  AND conrelid = 'customer_catalog.pricing_change_log'::regclass;
