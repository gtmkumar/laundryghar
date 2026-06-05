-- ============================================================================
-- rls_enable_order_lifecycle.sql
-- ----------------------------------------------------------------------------
-- BC-4 (order_lifecycle) RLS gap-close. Enables brand-scoped Row-Level
-- Security on the brand_id-bearing order_lifecycle tables and (re)creates a
-- uniform `rls_brand` policy on each, mirroring exactly the pattern and kernel
-- helpers from rls_proposal.sql:
--
--   USING/WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
--
-- where:
--   kernel.current_brand_id() = NULLIF(current_setting('app.current_brand_id',true),'')::uuid
--                               (NULLIF-safe: empty string -> NULL, never throws)
--   kernel.rls_bypass()       = COALESCE(current_setting('app.bypass_rls',true),'off')='on'
--
-- SCOPE (19 brand_id-bearing tables, verified via pg_attribute):
--   delivery_assignments, delivery_slot_bookings, delivery_slots,
--   garment_conditions, garment_inspection_photos, garment_inspections,
--   garment_tags, garments, order_items, order_notes, order_status_history,
--   orders (partitioned parent), pickup_requests, process_logs (partitioned
--   parent), quality_checks, stock_reconciliation_items, stock_reconciliations,
--   warehouse_batches, warehouse_processes
--
-- PARTITIONED PARENTS: `orders` and `process_logs` are pg_partman-managed. We
-- ENABLE ROW LEVEL SECURITY on the PARENT only — PostgreSQL applies the parent's
-- RLS to every existing and future partition child automatically. We never
-- enable per-child.
--
-- OUT OF SCOPE — order_addons: it has NO brand_id column (brand scoping is
-- transitive via order_id -> orders.brand_id, it is an aggregate child of
-- orders). A brand-only rls_brand policy is impossible (no column to filter).
-- RLS left OFF on order_addons; handled/noted in
-- fix_legacy_order_lifecycle_rls_policies.sql header. See report for rationale.
--
-- Idempotent: DROP POLICY IF EXISTS before CREATE; ENABLE ROW LEVEL SECURITY is
-- a no-op when already enabled. `garments` already had rls_brand + RLS on; this
-- script re-asserts it harmlessly.
-- ============================================================================

SET client_min_messages = WARNING;

DO $enable$
DECLARE
    tbl text;
BEGIN
    FOREACH tbl IN ARRAY ARRAY[
        'delivery_assignments',
        'delivery_slot_bookings',
        'delivery_slots',
        'garment_conditions',
        'garment_inspection_photos',
        'garment_inspections',
        'garment_tags',
        'garments',                    -- already enabled; re-assert for uniformity
        'order_items',
        'order_notes',
        'order_status_history',
        'orders',                      -- partitioned parent (propagates to children)
        'pickup_requests',
        'process_logs',                -- partitioned parent (propagates to children)
        'quality_checks',
        'stock_reconciliation_items',
        'stock_reconciliations',
        'warehouse_batches',
        'warehouse_processes'
    ]
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS rls_brand ON order_lifecycle.%I', tbl);
        EXECUTE format(
            'CREATE POLICY rls_brand ON order_lifecycle.%I FOR ALL TO app_user '
            'USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id()) '
            'WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())',
            tbl);
        EXECUTE format('ALTER TABLE order_lifecycle.%I ENABLE ROW LEVEL SECURITY', tbl);
        RAISE NOTICE 'RLS enabled + rls_brand (re)created on order_lifecycle.%', tbl;
    END LOOP;
END
$enable$;

-- Post-condition: rowsecurity for all 20 logical order_lifecycle tables.
SELECT c.relname AS tbl, c.relrowsecurity AS rls_on
FROM   pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE  n.nspname='order_lifecycle' AND c.relkind IN ('r','p') AND c.relispartition=false
ORDER  BY c.relname;
