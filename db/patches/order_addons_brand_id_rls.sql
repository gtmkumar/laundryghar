-- =============================================================================
-- db/patches/order_addons_brand_id_rls.sql
--
-- Closes the last brand-scoping gap: order_lifecycle.order_addons had no brand_id
-- and was therefore excluded from Row-Level Security (every other brand-scoped
-- table in order_lifecycle is RLS-protected). Mirrors order_items exactly:
--   brand_id uuid NOT NULL, FK -> tenancy_org.brands(id) ON DELETE RESTRICT,
--   btree index, RLS policy (kernel.rls_bypass() OR brand_id = current_brand_id()).
--
-- order_addons currently has 0 rows, so brand_id is added NOT NULL directly with
-- no backfill. (If rows existed, backfill from the parent order first.)
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/order_addons_brand_id_rls.sql
-- =============================================================================

-- 1. Column (nullable first for idempotent re-run safety) -----------------------
ALTER TABLE order_lifecycle.order_addons
    ADD COLUMN IF NOT EXISTS brand_id uuid;

-- Backfill any pre-existing rows from the parent order (no-op when empty).
UPDATE order_lifecycle.order_addons a
SET    brand_id = o.brand_id
FROM   order_lifecycle.orders o
WHERE  a.order_id = o.id
  AND  a.order_created_at = o.created_at
  AND  a.brand_id IS NULL;

-- Enforce NOT NULL now that every row has a value.
ALTER TABLE order_lifecycle.order_addons
    ALTER COLUMN brand_id SET NOT NULL;

-- 2. FK -> brands (mirror order_items_brand_id_fkey) ----------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'order_addons_brand_id_fkey'
          AND conrelid = 'order_lifecycle.order_addons'::regclass
    ) THEN
        ALTER TABLE order_lifecycle.order_addons
            ADD CONSTRAINT order_addons_brand_id_fkey
            FOREIGN KEY (brand_id) REFERENCES tenancy_org.brands(id) ON DELETE RESTRICT;
    END IF;
END$$;

-- 3. Index for the RLS predicate / FK ------------------------------------------
CREATE INDEX IF NOT EXISTS idx_order_addons_brand_id_fk
    ON order_lifecycle.order_addons USING btree (brand_id);

-- 4. Enable RLS + brand policy (mirror order_items rls_brand) -------------------
ALTER TABLE order_lifecycle.order_addons ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON order_lifecycle.order_addons;
CREATE POLICY rls_brand ON order_lifecycle.order_addons
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()))
    WITH CHECK (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));

-- Drop the legacy bypass-only policy left over from when order_addons had no
-- brand_id; rls_brand (above) is a strict superset, so it is now redundant.
DROP POLICY IF EXISTS rls_admin_only ON order_lifecycle.order_addons;

-- 5. Grants already cover order_lifecycle for app_user (harden_app_user...sql),
--    but re-assert defensively for the new column/policy context.
GRANT SELECT, INSERT, UPDATE, DELETE ON order_lifecycle.order_addons TO app_user;

SELECT 'order_addons_brand_id_rls.sql applied successfully.' AS result;
