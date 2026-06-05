---
name: project-orders-service
description: Orders microservice architecture decisions, DB quirks, and runtime lessons from BC-4 build
metadata:
  type: project
---

## Npgsql execution strategy + manual transactions

Npgsql's `NpgsqlRetryingExecutionStrategy` (enabled by `EnableRetryOnFailure`) does not support user-initiated `BeginTransactionAsync` directly. Wrap any explicit transaction in `_db.Database.CreateExecutionStrategy().ExecuteAsync(async () => { ... })`.

**Why:** CreateOrder needs order + order_items + order_addons + order_status_history + outbox_events in a single transaction. Direct `BeginTransactionAsync` throws `InvalidOperationException` at runtime.

**How to apply:** Any handler that opens an explicit DB transaction must use `CreateExecutionStrategy().ExecuteAsync(...)`.

---

## orders table: composite PK (id, created_at)

The `orders` table is range-partitioned by `created_at`. EF composite key is `(Id, CreatedAt)`. Cannot use `.FindAsync([id])` — use `.FirstOrDefaultAsync(o => o.Id == id ...)`. Child tables (order_items, order_status_history, order_notes, order_addons) carry `order_created_at` as partition-key column for the composite FK.

**How to apply:** Load orders by `.FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId)`. For child inserts, always populate `OrderCreatedAt = order.CreatedAt`.

---

## order_addons has NO brand_id column

`order_lifecycle.order_addons` has no `brand_id`. Brand isolation is via the parent order (which has brand_id). Never add a `BrandId` predicate directly on `OrderAddons` — always scope via the order join or parent order's brand.

---

## order_status_history: no CHECK on changed_by_type

The live DB has NO CHECK constraint on `changed_by_type` (checked via pg_constraint). Use `"user"`, `"customer"`, or `"system"` — semantically consistent with the codebase but not DB-enforced.

---

## Price resolution on order create

Orders service directly queries `PriceLists` + `PriceListItems` from shared DbContext using the same store→franchise→brand fallback as Catalog's `ResolvePriceQuery`. Express orders: use `ExpressPrice` if present, else `BasePrice`. Express surcharge (default 50%) applied at order level on top of item prices. CGST = SGST = TaxRate/2 each (GST split).

---

## Order number scheme

Format: `LG-{yyyy}-{storeCode}-{seq}` where seq is 0-padded to 6 digits, counted per store-year. Stored in `orders.order_number` (max 40 chars).

---

## State machine (production spec §4.1)

Key transitions seeded in `OrderStateMachine.cs`. Forward path: placed→pickup_scheduled→pickup_assigned→pickup_in_progress→picked_up→received→sorting→in_process→qc→ready→delivery_scheduled→delivery_assigned→out_for_delivery→delivered→closed. Branches: cancelled (from placed/pickup_scheduled/assigned/in_progress), returned, rewash, disputed. `placed` and `pickup_scheduled` are the only statuses from which customers can self-cancel.

---

## Atomic slot booking

Customer pickup: uses `ExecuteSqlAsync(FormattableString)` raw parameterized SQL to do atomic `UPDATE delivery_slots SET booked_count=booked_count+1 WHERE id=... AND booked_count<capacity AND is_active=true`. Returns 0 rows → `BusinessRuleException(422)` (slot full). This prevents race-condition overbooking without a distributed lock.

---

## Seeder creates franchise + store

Orders seeder seeds a franchise (LGF-MAIN) and store (LGS-MUM-001) under LG-MAIN brand because `orders.franchise_id` is NOT NULL. Identity seeder creates brand but not franchise/store. Orders seeder also seeds 28 delivery slots (4/day × 7 days: 2 pickup + 2 delivery slots per day).

---

## Orders service port: 5002
