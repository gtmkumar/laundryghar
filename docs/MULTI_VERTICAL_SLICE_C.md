# Phase 1 · Slice C — Relocate laundry-fulfilment tables to `laundry_fulfillment` schema

> Follows Slice A (state-machine → strategies) and Slice C-prep (severed `Order.Garments`
> navigation, FK made unidirectional). This is XL blocker #2 from the blueprint
> (`MULTI_VERTICAL_BLUEPRINT.md` §7 Phase 1, critical path). Goal: make the laundry
> wash/QC tables a *private detail* of `LaundryProcessStrategy` by moving them out of the
> shared `order_lifecycle` schema (where they currently sit beside the vertical-neutral
> order spine) into a dedicated `laundry_fulfillment` schema.

## The 11 tables to move

Currently all in schema `order_lifecycle`. They split into 9 parent entities + 2 child tables:

| # | table | entity | notes |
|---|-------|--------|-------|
| 1 | `garments` | `Garment` | the root; `garments_tag_code_key` unique idx; composite FK→`orders(id, order_created_at)` |
| 2 | `garment_tags` | `GarmentTag` | |
| 3 | `garment_conditions` | `GarmentCondition` | brand-scoped lookup (`(brand_id, code)` unique) |
| 4 | `garment_inspections` | `GarmentInspection` | composite FK→`orders(order_id, order_created_at)` |
| 5 | `garment_inspection_photos` | `GarmentInspectionPhoto` | child of inspections + garments |
| 6 | `warehouse_processes` | `WarehouseProcess` | brand-scoped (`(brand_id, code)` unique) |
| 7 | `warehouse_batches` | `WarehouseBatch` | `warehouse_batches_batch_number_key`; `Garment.CurrentBatch` |
| 8 | `stock_reconciliations` | `StockReconciliation` | |
| 9 | `stock_reconciliation_items` | `StockReconciliationItem` | child of reconciliations; FK→garments |
| 10 | `process_logs` | `ProcessLog` | warehouse pipeline log |
| 11 | `quality_checks` | `QualityCheck` | laundry QC |

**Stays in `order_lifecycle` (the shared, vertical-neutral spine):** `orders`, `order_items`,
`order_addons`, `order_notes`, `order_status_history`, `invoices`, `pickup_requests`,
`delivery_assignments`, `delivery_slots`, `delivery_slot_bookings`.

## Why this is now safe (Slice C-prep paid the setup cost)

- `Order.Garments` inverse navigation removed → no EF model coupling forces co-location.
- The `garments.order_id` FK is unidirectional; all garment reads go through `_db.Garments` by `OrderId`.
- RLS policies are **per-table and schema-agnostic in their body** —
  `USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())`. They reference no
  schema name, so they ride along with the table.

## DB migration — `ALTER TABLE … SET SCHEMA` (non-destructive)

Postgres `SET SCHEMA` moves the table **with** its indexes, constraints, RLS policies, and
owned sequences, and **preserves all FK constraints by OID** (inbound and outbound) — no FK
breaks even though the qualified name changes. This is a metadata-only operation: fast, no
data copy, no downtime beyond the brief `ACCESS EXCLUSIVE` lock per table.

```sql
-- db/patches/phase1_slice_c_laundry_fulfillment.sql  (idempotent)
CREATE SCHEMA IF NOT EXISTS laundry_fulfillment;
-- grant usage to the same app roles that have order_lifecycle (mirror existing grants)

DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY[
    'garment_conditions','warehouse_processes','warehouse_batches',  -- lookups/parents first
    'garments','garment_tags','garment_inspections','garment_inspection_photos',
    'stock_reconciliations','stock_reconciliation_items','process_logs','quality_checks'
  ] LOOP
    IF EXISTS (SELECT 1 FROM information_schema.tables
              WHERE table_schema='order_lifecycle' AND table_name=t) THEN
      EXECUTE format('ALTER TABLE order_lifecycle.%I SET SCHEMA laundry_fulfillment;', t);
    END IF;
  END LOOP;
END $$;
```

Order within the array doesn't matter for `SET SCHEMA` (FKs survive regardless), but listing
parents first keeps it readable. RLS `ENABLE`/`FORCE` flags and policies move automatically —
verify with a post-migration assertion (policy count per table unchanged).

### Verification gate (in the patch)
- `SELECT count(*)` per table before/after = equal.
- `pg_policies` count per table unchanged after the move.
- `information_schema.table_constraints` FK count per table unchanged (inbound + outbound).

## EF / code changes

1. **11 `ToTable(...)` configs** in
   `laundryghar.SharedDataModel/Persistence/Configurations/OrderLifecycle/*` — change the
   second arg `"order_lifecycle"` → `"laundry_fulfillment"`. (Entities themselves can stay in
   the `OrderLifecycle/` folder for this slice; a folder/namespace move to `Laundry/` is
   cosmetic and can defer.)
2. **Boot-smoke**: EF model validates against the new schema (the model-vs-DB check the
   prior slices used).
3. **No repo/query changes** — DbSet names unchanged; LINQ is schema-transparent.

## Non-code references to update (housekeeping, non-blocking)

These are historical/idempotent scripts — update for future re-runs, but they don't gate the migration:
- `db/patches/seed_warehouse_garments.sql` — `order_lifecycle.garments` → `laundry_fulfillment.garments`
- `db/patches/wipe_demo_org.sql` — stock_reconciliation_items / stock_reconciliations / warehouse_batches
- `db/patches/fk_patch_04_order_lifecycle.sql` — references in DDL comments/ALTERs (already-applied; only matters if replayed on a fresh DB)

**No matviews touch these 11 tables** (the warehouse-throughput matview is a Phase-2
deliverable that doesn't exist yet) — so analytics is unaffected by this slice.

## Explicitly OUT of scope for Slice C (keep the slice mechanical + reversible)

- **`garments` → `fulfillment_unit` rename** (blueprint bullet #2). This is a *naming*
  change that touches the `Garment` entity, DbSet, and every repo reference — much higher
  blast radius than a schema move. Recommend a separate Slice (C-2) or fold into Phase 2's
  catalog generalization. Doing the schema move alone first keeps Slice C a pure, instantly
  reversible (`SET SCHEMA` back) relocation.
- OrderStatus widening (XL blocker #1) — its own slice ("B").
- `garment.*` → `fulfillment.*` permission rename — separate atomic grant migration (Risk #6).

## Gate to land Slice C
All three hosts build + boot; EF model validates; 20/20 parity suite still green;
row-count + policy-count + FK-count assertions pass; existing laundry behaviour byte-identical.
