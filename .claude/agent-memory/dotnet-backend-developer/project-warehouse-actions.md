---
name: project-warehouse-actions
description: Task #20 warehouse board actions, daily recon worker, lost-garment flow — key domain constraints and design choices
metadata:
  type: project
---

## Warehouse board actions + daily recon + lost flow (Task #20)

**garments.status CHECK only allows `active|inactive|archived`** — there is no `lost` status value. Lost state is recorded exclusively via `current_stage = 'lost'` (which IS in the current_stage CHECK constraint). LostGarmentProcessor sets `CurrentStage = GarmentStage.Lost` and leaves `Status = 'active'` for traceability.

**Why:** DB schema was created without a lost/damaged status dimension separate from stage. Changing the CHECK constraint requires a migration patch; decision was to re-use the stage column.

**How to apply:** Never set `garment.Status = "lost"` — the column won't accept it. Use `garment.CurrentStage = GarmentStage.Lost`.

---

**stock_reconciliation_items.status** allows: `matched|missing|unexpected|damaged|resolved|escalated`. Auto-flagged stale garments use `status='missing'` — these are candidates, not confirmed lost. Confirmed-lost only happens when a recon is CLOSED with unresolved missing items.

**DailyReconService** polls every 5 minutes, fires when `nowIst.Hour == DailyReconHourLocal` (default 21). Uses IST (UTC+5:30) hardcoded because warehouse timezone is stored as a string in `Warehouse.Timezone`, not machine-readable enough to query per-warehouse. Per-warehouse timezone column is a future improvement.

**LostGarmentProcessor** is called from within `CloseStockReconHandler` before `SaveChangesAsync` — atomic with the close operation. It adds outbox events inside the same unit of work.

**NotificationMappingService** now handles `garment.lost` events via a new case in `ResolveTemplate`. Customer is resolved via `garment.AggregateId → garments.customer_id → customers`. Added `garment` aggregate type to `ResolveCustomerAsync`.

**Wallet compensation** for confirmed-lost garments is OUT OF SCOPE (needs policy — credit amount, caps, approval flow). The `garment.lost` outbox event carries `GarmentId, CustomerId, OrderId, OrderItemId` — sufficient for a future compensation handler.

**Frontend +Add Card scoping:** `CreateGarmentCommand` requires a valid `OrderItemId` + available `TagCode`. The drawer collects both from the operator. There's no way to create a garment without an order-item (by design — billing and SLA traceability). If the operator doesn't have it they must use the orders screen.

**CloseStockReconHandler** now requires `ILogger<CloseStockReconHandler>` in its constructor (added for `LostGarmentProcessor` call). DI wires it automatically.

**Sibling collision note:** `OpsQueuesTab.tsx` had an unused `Card` import that blocked `npm run build`. Fixed the unused import (harmless — sibling's file, not touching logic).

**Worker:DailyReconEnabled defaults to false** — must be explicitly enabled. Same pattern as `AutoDispatchEnabled` and `RoyaltyGenerationEnabled`.
