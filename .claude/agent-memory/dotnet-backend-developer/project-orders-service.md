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

---

## pickup_requests: requested_items + payment_preference columns (added 2026-06-10)

Two new columns added via `db/patches/pickup_requested_items.sql` (idempotent):
- `requested_items jsonb NOT NULL DEFAULT '[]'` — estimated cart lines from the customer booking flow. Serialised as `RequestedCartItemDto[]` (System.Text.Json source-generated context `PickupJsonContext`). These are ESTIMATES; the real order is created after weighing.
- `payment_preference varchar(20) NOT NULL DEFAULT 'cod'` — customer intent: `wallet | cod | upi-deferred`. UPI/card selections normalised to `upi-deferred` in the command handler.

**Why:** The customer booking flow in customer-mobile needed to persist cart items and payment intent with the pickup request so ops can prepare the collection method.

**How to apply:** When creating a pickup request via `CreatePickup()` static helper, the new fields are computed from `CreatePickupRequestRequest.CartItems` and `PaymentPreference`. `ToDto()` deserialises `RequestedItems` JSON back to `RequestedCartItemDto[]`. Both fields default gracefully (empty array / cod) for admin-created requests that don't supply them.

---

## Customer self-service pickup endpoints (added 2026-06-10)

`GET /api/v1/customer/pickup-requests` and `GET /api/v1/customer/pickup-requests/{id}` added to `CustomerOrderEndpoints`, backed by `GetMyPickupRequestsQuery` / `GetMyPickupRequestByIdQuery`. Both self-filter by `customerId` from JWT `sub` — IDOR guard, never trusts URL params.

---

## LocalStorageOptions CS0246 in solution-wide build

Building the full `laundryghar.slnx` fails with a pre-existing CS0246 in `laundryghar.ServiceDefaults` (not Orders). Building `laundryghar.Orders.csproj` directly succeeds with 0 errors. This is a pre-existing issue in the solution file — not introduced by any Orders changes.

---

## Pickup reject flow (Task #32, 2026-06-10)

`POST /api/v1/admin/pickup-requests/{id}/reject` — permission `pickup.assign` (no separate reject code in DB; same role owns both disposition actions).

Status mapping: `pickup_requests_status_check` CHECK does NOT include 'rejected' — only `cancelled` is the correct terminal status. Distinguish admin-reject by storing `cancelled_by_type='admin'` + `cancellation_reason` (existing column — no schema change needed).

Slot release: inverse `ExecuteSqlAsync` decrement (`booked_count - 1 WHERE booked_count > 0`) plus mark `delivery_slot_bookings.status = 'cancelled'`. Only fires when `PickupSlotId IS NOT NULL`.

Outbox event: `pickup.rejected` with `aggregate_type='pickup_request'`. Worker resolves customer via `PickupRequests` table (quaternary lookup path in `ResolveCustomerAsync`). Template base code `PICKUP_REJECTED`, seeded via `db/patches/seed_pickup_rejected_notification_templates.sql` (3 channels × 1 base = 3 templates).

Perf fix: Both admin + customer list queries in `PickupQueries.cs` called `ToDto` (which does `JsonSerializer.Deserialize`) inside EF `.Select()` causing client-eval. Fixed by materializing the page as `PaginatedList<PickupRequest>` first, then calling `.Map(ToDto)` (already existed in `PaginatedList`).

Pre-existing test failures in `CouponMathTests` and `OrderValidatorTests` (a missing using in an untracked file that was added by another agent) — NOT caused by Task #32 changes.

## TAT engine + promised_delivery_at (Task #19, 2026-06-10)

`promised_delivery_at` column already exists in `04_bc4_order_lifecycle.sql` (line 100) as `promised_delivery_at TIMESTAMPTZ`. EF mapping is `PromisedDeliveryAt` in `OrderConfiguration`. No migration needed.

TAT rule: MAX(service.BaseTatHours or ExpressTatHours) across all unique services on the order. Falls back to `Orders:DefaultTatHours` (48) or `Orders:ExpressTatHours` (24) config when services are missing/zero. Computed by `TatCalculator.Compute()` in `CreateOrderCommand`. Set on `order.PromisedDeliveryAt` at creation.

`tatBreached` flag added to `order.status_changed` outbox payload: true when `now > promised_delivery_at` and new status is non-terminal. Pure additive — does not affect existing handlers.

New `OrdersSettings` keys: `DefaultTatHours`, `ExpressTatHours`, `StuckThresholdHours` (24).

Ops queues endpoint: `GET /api/v1/admin/orders/ops-queues` — returns `{dueToday, overdue, stuck}` buckets with count+list each. Permission: `orders.read`. Stuck = latest status_history entry older than `StuckThresholdHours`.

`WarehouseBoardPage.tsx` build errors are pre-existing (another agent's unfinished work) — not introduced here. `npx tsc --noEmit` = 0 errors for admin-web.
