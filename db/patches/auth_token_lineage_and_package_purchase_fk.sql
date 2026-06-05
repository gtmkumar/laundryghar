-- ============================================================================
-- auth_token_lineage_and_package_purchase_fk.sql
-- ----------------------------------------------------------------------------
-- Resolves HANDOFF §13 items #4 and #5 — both flagged as "unresolved" in
-- the original 138-FK analysis but in fact have natural targets in the DB.
--
-- #4  identity_access.refresh_tokens.family_id  →  refresh_tokens(id)
--     The standard refresh-token rotation pattern: a "family" is a chain of
--     rotated tokens sharing one common root. family_id points at the root
--     token of the family. Same self-FK shape as the already-present
--     parent_token_id FK. NO ACTION on delete to match parent_token_id.
--
-- #5  commerce.customer_packages.purchase_order_id +
--     commerce.customer_packages.purchase_order_created_at
--                                  →  order_lifecycle.orders(id, created_at)
--     The column was reported as "orphan" only because the FK generator's
--     name map didn't know `purchase_order_id` aliases `order_id`. The
--     companion `purchase_order_created_at` column proves the author
--     intended the composite-FK-to-partitioned-orders pattern used
--     everywhere else. ON DELETE RESTRICT — a customer package outlives
--     the order that created it (refunds, audit).
--
-- Idempotent: wrapped in DO/EXCEPTION blocks. Re-runnable.
-- ============================================================================
SET client_min_messages = WARNING;

-- #4 ------------------------------------------------------------------------
DO $$
BEGIN
    ALTER TABLE identity_access.refresh_tokens
        ADD CONSTRAINT refresh_tokens_family_id_fkey
        FOREIGN KEY (family_id)
        REFERENCES identity_access.refresh_tokens(id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_family_id_fk
    ON identity_access.refresh_tokens(family_id);

-- #5 ------------------------------------------------------------------------
DO $$
BEGIN
    ALTER TABLE commerce.customer_packages
        ADD CONSTRAINT customer_packages_purchase_order_id_fkey
        FOREIGN KEY (purchase_order_id, purchase_order_created_at)
        REFERENCES order_lifecycle.orders(id, created_at)
        ON DELETE RESTRICT;
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

CREATE INDEX IF NOT EXISTS idx_customer_packages_purchase_order_id_fk
    ON commerce.customer_packages(purchase_order_id);
