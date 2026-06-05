-- ============================================================================
-- polymorphic_location_discriminators.sql
-- ----------------------------------------------------------------------------
-- Resolves the 5 unresolved polymorphic FKs from HANDOFF §13 item #3.
--
-- A garment's location/holder may be any of: store, warehouse, rider,
-- customer, in-transit, or other. Adding a single FK is impossible
-- because the target table differs per row. Instead, each polymorphic
-- id column gets a sibling `<col>_type` discriminator (text), plus:
--
--   1) a per-column CHECK that constrains the discriminator vocabulary
--   2) a per-pair CHECK that keeps the id and type in lock-step
--      ((id IS NULL) = (type IS NULL))
--
-- Idempotent: ADD COLUMN IF NOT EXISTS, DROP CONSTRAINT IF EXISTS / ADD.
-- App-level invariant: the app must verify the row referenced by
-- (type, id) actually exists in the right target table. The DB cannot
-- enforce that with this design — that's the explicit trade-off the
-- discriminator pattern accepts.
-- ============================================================================
SET client_min_messages = WARNING;

-- ---------------------------------------------------------------------------
-- garments.current_location_*
-- ---------------------------------------------------------------------------
ALTER TABLE order_lifecycle.garments
    ADD COLUMN IF NOT EXISTS current_location_type varchar(20);

ALTER TABLE order_lifecycle.garments
    DROP CONSTRAINT IF EXISTS garments_current_location_type_chk;
ALTER TABLE order_lifecycle.garments
    ADD  CONSTRAINT garments_current_location_type_chk
         CHECK (current_location_type IS NULL OR current_location_type IN
                ('store','warehouse','rider','customer','transit','other'));

ALTER TABLE order_lifecycle.garments
    DROP CONSTRAINT IF EXISTS garments_current_location_pair_chk;
ALTER TABLE order_lifecycle.garments
    ADD  CONSTRAINT garments_current_location_pair_chk
         CHECK ((current_location_id IS NULL) = (current_location_type IS NULL));

-- ---------------------------------------------------------------------------
-- garment_inspections.location_*
-- ---------------------------------------------------------------------------
ALTER TABLE order_lifecycle.garment_inspections
    ADD COLUMN IF NOT EXISTS location_type varchar(20);

ALTER TABLE order_lifecycle.garment_inspections
    DROP CONSTRAINT IF EXISTS garment_inspections_location_type_chk;
ALTER TABLE order_lifecycle.garment_inspections
    ADD  CONSTRAINT garment_inspections_location_type_chk
         CHECK (location_type IS NULL OR location_type IN
                ('store','warehouse','rider','customer','transit','other'));

ALTER TABLE order_lifecycle.garment_inspections
    DROP CONSTRAINT IF EXISTS garment_inspections_location_pair_chk;
ALTER TABLE order_lifecycle.garment_inspections
    ADD  CONSTRAINT garment_inspections_location_pair_chk
         CHECK ((location_id IS NULL) = (location_type IS NULL));

-- ---------------------------------------------------------------------------
-- stock_reconciliation_items.expected_location_*
-- ---------------------------------------------------------------------------
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD COLUMN IF NOT EXISTS expected_location_type varchar(20);

ALTER TABLE order_lifecycle.stock_reconciliation_items
    DROP CONSTRAINT IF EXISTS sri_expected_location_type_chk;
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD  CONSTRAINT sri_expected_location_type_chk
         CHECK (expected_location_type IS NULL OR expected_location_type IN
                ('store','warehouse','rider','customer','transit','other'));

ALTER TABLE order_lifecycle.stock_reconciliation_items
    DROP CONSTRAINT IF EXISTS sri_expected_location_pair_chk;
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD  CONSTRAINT sri_expected_location_pair_chk
         CHECK ((expected_location_id IS NULL) = (expected_location_type IS NULL));

-- ---------------------------------------------------------------------------
-- stock_reconciliation_items.found_location_*
-- ---------------------------------------------------------------------------
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD COLUMN IF NOT EXISTS found_location_type varchar(20);

ALTER TABLE order_lifecycle.stock_reconciliation_items
    DROP CONSTRAINT IF EXISTS sri_found_location_type_chk;
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD  CONSTRAINT sri_found_location_type_chk
         CHECK (found_location_type IS NULL OR found_location_type IN
                ('store','warehouse','rider','customer','transit','other'));

ALTER TABLE order_lifecycle.stock_reconciliation_items
    DROP CONSTRAINT IF EXISTS sri_found_location_pair_chk;
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD  CONSTRAINT sri_found_location_pair_chk
         CHECK ((found_location_id IS NULL) = (found_location_type IS NULL));

-- ---------------------------------------------------------------------------
-- stock_reconciliation_items.last_known_holder_*
-- ---------------------------------------------------------------------------
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD COLUMN IF NOT EXISTS last_known_holder_type varchar(20);

ALTER TABLE order_lifecycle.stock_reconciliation_items
    DROP CONSTRAINT IF EXISTS sri_last_known_holder_type_chk;
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD  CONSTRAINT sri_last_known_holder_type_chk
         CHECK (last_known_holder_type IS NULL OR last_known_holder_type IN
                ('store','warehouse','rider','customer','transit','other'));

ALTER TABLE order_lifecycle.stock_reconciliation_items
    DROP CONSTRAINT IF EXISTS sri_last_known_holder_pair_chk;
ALTER TABLE order_lifecycle.stock_reconciliation_items
    ADD  CONSTRAINT sri_last_known_holder_pair_chk
         CHECK ((last_known_holder_id IS NULL) = (last_known_holder_type IS NULL));
