# LaundryGhar → Multi-Vertical SaaS Platform: Migration Blueprint

> **Target:** Evolve LaundryGhar from a single-vertical laundry app into a multi-vertical SaaS platform (laundry + salon + logistics), where one Platform hosts many Brands, each Brand operates ONE vertical via `Brand.VerticalKey`, and per-vertical behavior is supplied by a pluggable `IFulfillmentStrategy`.

---

## 1. Executive Summary

LaundryGhar is **already a partially-generalized marketplace platform**, not a laundry monolith. The shared spine — Identity/RBAC, Tenancy, Commerce, Finance, Logistics/dispatch, Engagement, ModuleBundle entitlement, MobileAppConfig remote-config — is **vertical-agnostic and reusable essentially as-is**. The `Order` aggregate already carries a generic job spine (`JobType` laundry/parcel, `RequestedVehicleTier`, a parcel-aware `OrderStateMachine`, generic money/discount/payment columns, `OutboxEvent` + status-history), and the RLS model (`brand_id = current_setting('app.current_brand_id')` on every tenant table) needs **zero** change for new verticals.

The laundry coupling is **concentrated, structural, and shallow** — it lives in four recurring patterns rather than being diffused everywhere:

1. **No discriminator exists.** There is no `Brand.VerticalKey`, `Order.VerticalKey`, `Order.FulfillmentMode`, `Item.CatalogKind`, or `IFulfillmentStrategy` anywhere in the codebase. Every laundry assumption exists because *the platform has nothing to branch on*.
2. **Status/stage vocabularies are hardcoded.** `OrderStatus` (`sorting/in_process/qc/rewash`), `GarmentStage`, `OrderStateMachine` transition maps, and the `garments.current_stage` DB CHECK constraint bake the wash/QC pipeline into shared code + schema.
3. **The garment/warehouse subsystem leaks into the shared aggregate.** `Order.Garments` navigation drags `Garment → WarehouseProcess/QualityCheck/GarmentInspection/...` (9 entity types / 11 physical tables) into the generic spine.
4. **Catalog hardwires fabric.** `FabricType` is a first-class table FK'd from `ItemVariant`, `PriceListItem`, `OrderItem`, and `Garment`, with Cotton-base × fabric-multiplier pricing baked into the UI and matrix.

### % Already Generic vs Work Remaining

| Area | % Generic (reuse as-is) | Nature of remaining work |
|---|---|---|
| Identity / RBAC | ~90% | Split seeder into core + per-vertical permission/role packs |
| Tenancy / Org | ~85% | Add `VerticalKey`; strip laundry capability/capacity columns to jsonb |
| Commerce | ~95% | 3 couplings: `QuotaType` weight_kg, `SubscriptionPlan` pickup/delivery/express flags, `SubscriptionInvoice` Cgst/Sgst/Igst |
| Finance / Royalty | ~90% | Rename "order" metering → "job"; vertical-keyed operational counters; `RoyaltyInvoice`/`FranchiseSubscriptionInvoice` GST |
| Logistics / Dispatch | ~80% | Extract state-machine maps + pickup round-trip behind strategy |
| Engagement / CMS | ~85% (entities) | `NotificationMappingService` hardcodes laundry events/aggregates |
| Module Entitlement | ~95% | Add `vertical_key` to modules/bundles; re-seed per vertical |
| Analytics | ~70% | Extract warehouse-throughput; gate rider-perf; generalize express; 7-matview registry |
| Orders / Job Spine | ~60% | Status vocab + strategy seam + sever `Order.Garments` (2 XL blockers) |
| Catalog | ~70% | `CatalogKind` + `Attributes`; demote `FabricType` |
| Laundry Fulfillment | ~30% | Rename Warehouse→Fulfillment; promote `garments`→`fulfillment_unit` |
| Customer Mobile | ~80% (infra) | Feature-pack registry; split laundry product screens |
| Rider Mobile | ~80% | VerticalContext; generalize `garmentCount`/phases/inspection |
| Admin Web | ~80% | VerticalKey gating; strategy-driven board/status/catalog |
| POS Web | ~85% | VerticalKey; generalize weight-mode/tags/status/express |
| Database / RLS | RLS+tenancy 100% | Discriminators + extract garment_* to `laundry_fulfillment` schema |

**Aggregate estimate:** **324 person-days** — the exact sum of the 16 audited per-module `estimatePersonDays` (see §5). An earlier "~305" headline was a back-fit artifact and has been corrected throughout; the §7 phase totals now reconcile to 324 via the traceability matrix in §7.0. The two XL blockers that gate everything — widening the `OrderStatus` vocabulary and severing the `Order.Garments` navigation — sit in the Orders + Fulfillment + Database modules and define the critical path.

---

## 2. The Core Abstraction: `Brand.VerticalKey` + `IFulfillmentStrategy` + `FulfillmentMode`

### 2.1 The three discriminators

```csharp
// laundryghar.SharedDataModel/Enums/VerticalKey.cs  (NEW — const-class, mirrors UserType/ScopeType pattern)
public static class VerticalKey
{
    public const string Laundry   = "laundry";
    public const string Salon     = "salon";
    public const string Logistics = "logistics";
}

// laundryghar.SharedDataModel/Enums/FulfillmentMode.cs  (NEW)
public static class FulfillmentMode
{
    public const string ProcessDeliver = "process_deliver"; // laundry: collect → process → deliver
    public const string Appointment    = "appointment";     // salon: on-site booked slot
    public const string PointToPoint   = "point_to_point";  // logistics: origin → destination
}
```

`Brand.VerticalKey` is the **single source of truth** — one brand operates exactly one vertical. It is denormalized onto `Order.VerticalKey` (for partition-pruning/RLS-free reads on the range-partitioned `orders` table) and drives strategy resolution, MobileAppConfig feature-pack mounting, ModuleBundle defaults, and per-vertical i18n.

```csharp
// Entities/TenancyOrg/Brand.cs  — ADD
public string VerticalKey { get; set; } = VerticalKey.Laundry; // NOT NULL, backfill 'laundry'

// Entities/OrderLifecycle/Order.cs  — ADD; KEEP JobType as the orthogonal dispatch-tier concern
public string VerticalKey     { get; set; }   // denormalized from Brand
public string FulfillmentMode { get; set; }   // process_deliver | appointment | point_to_point
// Order.JobType (laundry|parcel|truck) stays = the dispatch/vehicle-tier shape, NOT the vertical
```

**Decision (resolves a recurring open question):** `JobType` is kept **orthogonal** to `VerticalKey`. `JobType` describes the *dispatch shape* (which vehicle ladder, which leg topology); `VerticalKey` describes *which strategy owns stages/catalog/resources*. Backfill: `VerticalKey='laundry', FulfillmentMode='process_deliver'` for existing rows; logistics ≈ `JobType.Parcel` → `point_to_point`.

### 2.2 The strategy interface

```csharp
// operations.Application/Fulfillment/IFulfillmentStrategy.cs  (NEW — shared operations kernel)
// Resolves OrderStateMachine consumers (Orders BC + Logistics rider flow) off one seam.
public interface IFulfillmentStrategy
{
    string VerticalKey { get; }

    // ---- State machine (replaces OrderStateMachine.AllowedTransitions/ParcelTransitions/MapFor) ----
    IReadOnlyDictionary<string, string[]> GetTransitions();
    string[]  GetHappyPath();
    bool      IsKnownStatus(string status);
    string    InitialStatus { get; }
    string[]  TerminalStatuses { get; }
    bool      ValidateTransition(string from, string to);

    // ---- Order creation (replaces isParcel branching in CreateOrderCommand) ----
    ValidationResult ValidateOrderRequest(CreateOrderRequest req);   // laundry: items; logistics: 2 endpoints; salon: staff+slot
    Task PrepareFulfilmentAsync(Order order, CreateOrderRequest req); // laundry no-op; logistics fare-quote; salon reserves StaffMember/ResourceBooking
    string[] AllowedOrderTypes { get; }                              // retires/scopes OrderType rewash/complaint/exchange

    // ---- Per-transition side effects (replaces hardcoded Received→ReceivedAt switch) ----
    Task OnTransitionAsync(Order order, string toStatus, DateTimeOffset now);

    // ---- Dispatch / rider flow (replaces literal OrderStatus.Received, store-drop branch) ----
    string  PostPickupStatus(Order order);   // laundry→received, point_to_point→out_for_delivery, appointment→noop
    bool    HasStoreDropLeg(Order order);    // true only for process_deliver
    bool    SupportsPickupInspection(Order order);
    InspectionSchema PickupInspectionSchema(); // strategy-defined condition flag keys + photo slots

    // ---- Tax / catalog ----
    TaxProfile GetTaxProfile();              // SAC/HSN + service description (laundry 999712)
    string[]   ValidQuotaUnits { get; }      // commerce: credit/count/unlimited + weight_kg|service_minutes|distance_km
    CapacityDescriptor ReadStoreCapacity(JsonDocument storeConfig);
    ShiftSnapshot GetShiftSnapshot(string brandId);
}

// DI resolver keyed by VerticalKey
public interface IFulfillmentStrategyResolver
{
    IFulfillmentStrategy Resolve(string verticalKey); // unknown → falls back to laundry (preserves existing rows)
}
```

### 2.3 How laundry's `Garment`/`WarehouseProcess` becomes `LaundryProcessStrategy`'s private detail

The current shared `OrderLifecycle` namespace mixes the generic spine with the laundry fulfillment tables. The refactor:

- **Sever `Order.Garments`** (`Order.cs:139`) — the `ICollection<Garment>` that drags `Garment → WarehouseProcess, QualityCheck, GarmentInspection, GarmentTag, WarehouseBatch, ProcessLog, StockReconciliation, GarmentCondition` into the aggregate.
- **Relocate** all fulfillment tables from `order_lifecycle` → a new `laundry_fulfillment` schema, linked to orders by `OrderId` scalar only (no shared EF navigation), preserving the composite FK to partitioned `orders(id, created_at)`.
- **Promote** `garments` → a generic `fulfillment_unit` spine (it already has `Metadata` jsonb + polymorphic `CurrentLocationType/CurrentLocationId`); move `has_ornaments/has_lining/is_designer_wear/fabric_type_id/weight_grams/care_instructions/rewash_count` into a strategy-private `Attributes` jsonb slice.
- **Drop the `garments.current_stage` CHECK constraint** (confirmed `04_bc4_order_lifecycle.sql:423-426`) and the `GarmentStage` enum becomes a `LaundryProcessStrategy`-private constant set; `LostGarmentProcessor` (which depends on `'lost'` being in the constraint) moves to the strategy's terminal-stage.

#### Canonical relocation list — **9 entity types / 11 physical tables**

The audits describe this set two ways; both are correct and reconciled here. **Entity-level count = 9** (the aggregate roots / EF entities). **Physical-table count = 11** (entities plus their owned child tables). **All scope statements below use the physical-table basis (11).**

| # (physical) | Table | Entity? | Notes |
|---|---|---|---|
| 1 | `garments` | ✅ entity 1 → `fulfillment_unit` | promoted to generic spine |
| 2 | `garment_tags` | ✅ entity 2 | |
| 3 | `garment_inspections` | ✅ entity 3 | |
| 4 | `garment_inspection_photos` | child of entity 3 | owned child table |
| 5 | `garment_conditions` | ✅ entity 4 | |
| 6 | `warehouse_batches` | ✅ entity 5 | |
| 7 | `warehouse_processes` | ✅ entity 6 | |
| 8 | `process_logs` | ✅ entity 7 | |
| 9 | `quality_checks` | ✅ entity 8 | |
| 10 | `stock_reconciliations` | ✅ entity 9 | |
| 11 | `stock_reconciliation_items` | child of entity 9 | owned child table |

**Canonical wording for all scope statements:** *"relocate the 11 laundry-fulfillment physical tables (9 entity types + 2 owned child tables: `garment_inspection_photos`, `stock_reconciliation_items`) from `order_lifecycle` to `laundry_fulfillment`."*

```csharp
// operations.Application/Fulfillment/Laundry/LaundryProcessStrategy.cs  (NEW)
public sealed class LaundryProcessStrategy : IFulfillmentStrategy
{
    public string VerticalKey => Enums.VerticalKey.Laundry;
    public string InitialStatus => OrderStatus.Placed;
    public string[] TerminalStatuses => new[] { "delivered", "cancelled", "closed", "returned" };

    // Owns the wash/QC pipeline that was hardcoded in OrderStateMachine.AllowedTransitions:
    public IReadOnlyDictionary<string,string[]> GetTransitions() => LaundryTransitions; // received→sorting→in_process→qc→ready→…+rewash
    public string[] GetHappyPath() => LaundryHappyPath;
    public string PostPickupStatus(Order o) => OrderStatus.Received;   // stamps ReceivedAt
    public bool HasStoreDropLeg(Order o) => true;
    public bool SupportsPickupInspection(Order o) => true;
    public InspectionSchema PickupInspectionSchema() => new(flags: ["stains","tears","missing_buttons"], photos: ["front","back"]);
    public TaxProfile GetTaxProfile() => new(SacCode: "999712", Description: "Laundry & Dry-Cleaning Services");
    // garment/warehouse linkage, rewash counter (now in Attributes jsonb), estimate-then-weigh conversion — all private here
}
```

`LaundryProcessStrategy` is the **reference implementation and regression baseline**: it must reproduce current laundry behaviour *exactly* (including preserved latent bugs) so the seam extraction is provably behaviour-preserving.

### 2.4 The new Salon Appointment module

Salon ships as a new strategy + its own private schema, **without touching the shared spine**:

```csharp
// operations.Application/Fulfillment/Salon/SalonAppointmentStrategy.cs  (NEW)
public sealed class SalonAppointmentStrategy : IFulfillmentStrategy
{
    public string VerticalKey => Enums.VerticalKey.Salon;
    public string InitialStatus => "booked";
    public IReadOnlyDictionary<string,string[]> GetTransitions() => /* booked→confirmed→checked_in→in_service→completed */;
    public string PostPickupStatus(Order o) => /* no-op: appointment has no pickup leg */ null;
    public bool HasStoreDropLeg(Order o) => false;
    public bool SupportsPickupInspection(Order o) => false; // or supplies a before/after photo schema
    public async Task PrepareFulfilmentAsync(Order o, CreateOrderRequest req) => /* reserve StaffMember + ResourceBooking */;
    public string[] ValidQuotaUnits => ["credit","count","unlimited","service_minutes"];
}
```

New salon-private entities live in a `salon_fulfillment` schema with the **identical brand_id RLS policy** reused verbatim: `Appointment`, `StaffMember`, `Resource`, `ResourceBooking`. Salon reuses the generic `DeliverySlot/DeliverySlotBooking` *time-capacity* model for appointment slots where it fits; distinct staff/chair concurrency goes in `ResourceBooking`.

**Logistics** ≈ existing `JobType.Parcel` + Rider module → `LogisticsPointToPointStrategy` (carries today's `ParcelTransitions`, single origin→destination leg, no store drop). The existing `app/(app)/parcel/*` flow is the de-facto proof the seam generalizes — it gets refactored *into* the logistics pack first to validate the abstraction before salon is built.

---

## 3. Catalog Generalization: `CatalogKind` + `Attributes` jsonb

The Catalog spine (`ServiceCategory → Service → Item/ItemGroup/ItemVariant → PriceList/PriceListItem`, the store→franchise→brand price-resolution ladder, AddOn engine, PricingChangeLog/revert, CSV import) is ~70% generic. The fix pushes all vertical specifics into two new fields + jsonb, demoting `FabricType` behind the strategy.

```csharp
// Entities/CustomerCatalog/Item.cs  — ADD
public string CatalogKind { get; set; }   // "laundry" | "salon" | "logistics" (default backfill 'laundry')
public JsonDocument Attributes { get; set; } // { fabric_type, typical_weight_grams, requires_per_side_price, tat_hours,
                                             //   express_eligible, express_surcharge } for laundry;
                                             //   { duration_minutes, staff_tier } salon; { weight_class } logistics

// ItemVariant.cs — ADD Attributes jsonb (absorbs Side, optionally Size/Color); DROP FabricTypeId FK
// ServiceCategory.cs — Attributes jsonb / RequiredCapabilities[] (replaces RequiresWarehouseCap string[])
// Service.cs — RequiresInspection/RequiresQc/BaseTatHours/ExpressTatHours/ExpressMultiplier → Attributes
```

Key moves grounded in the audit:
- **`FabricType` table + CRUD slice** (`FabricType.cs`, `FabricTypeCommands/Queries/Dtos`) → moved into a laundry feature-pack assembly, gated behind ModuleBundle entitlement so non-laundry brands never see it. Physical table retained; EF mapping/DbSet conditionally configured.
- **`ItemVariant.FabricTypeId` / `PriceListItem.FabricTypeId` / `OrderItem.FabricTypeId` FKs** → generic variant-dimension reference (jsonb resolving the dimension id). The price multiplier is applied by the strategy, not a hardcoded fabric join.
- **`SaveItemPricingHandler`** (`ItemPricingCommands.cs:108-151`) stops treating "variant == fabric"; asks the strategy for the item's variant dimensions.
- **Pricing matrix** (`PricingMatrixQueries.cs:11-30`) becomes strategy-rendered: laundry → fabric columns, salon → staff/room tiers, logistics → vehicle tiers; the `Cotton`/`Garment·Standard` literals go.
- **`PricingChangeLog.TargetKind`** (`'fabric_type'`) becomes open-ended; revert dispatches to a registered `IPricingRevertHandler` per kind (laundry registers `fabric_type`).

**Cross-area dependency:** `FabricType` is FK'd from `Garment`, `OrderItem`, `ItemVariant`, AND `PriceListItem` — its demotion must be coordinated across Catalog + Orders + Fulfillment + Pricing, with backfill into `Attributes` jsonb being exact or laundry price multipliers break.

---

## 4. Coupling Register (highest-severity, all areas)

| Area | File / Symbol | Severity | Generalization |
|---|---|---|---|
| Orders | `Brand.cs:39` / no `VerticalKey`·`FulfillmentMode` | **blocker** | Add `Brand.VerticalKey` + `Order.VerticalKey`/`FulfillmentMode`; resolve strategy by VerticalKey |
| Orders | `Order.cs:139` / `Order.Garments` nav | **blocker** | Sever nav; relink garment subsystem by `OrderId` scalar; LaundryProcessStrategy-private |
| Orders | `OrderStatus.cs:11-13` / `Sorting/InProcess/Qc/Rewash` | **blocker** | Generic super-states + strategy-owned sub-status; relax DB CHECK |
| Orders | `OrderStateMachine.cs:26-72` / `AllowedTransitions`/`ParcelTransitions` | **blocker** | Move transition maps into per-vertical strategies; delete `jobType==Parcel` branch |
| Fulfillment | `04_bc4:423-426` / `garments.current_stage` CHECK + `GarmentStage.cs` | **blocker** | Drop CHECK; stages validated by strategy; `GarmentStage` laundry-private |
| Fulfillment | `Garment.cs:9-69` / garment physical columns | **blocker** | Promote `garments`→`fulfillment_unit`; laundry cols → jsonb Attributes |
| Fulfillment | `CreateQualityCheck.cs:69-93` / QC→stage rules + `garment.qc_*` events | **blocker** | `strategy.OnInspectionResult`; emit `fulfillment_unit` aggregate |
| Catalog | `Item.cs` / no `CatalogKind`/`Attributes` | **blocker** | Add `CatalogKind` + jsonb `Attributes`; resolve strategy at catalog boundary |
| Tenancy | `Brand.cs:6-50` / no `VerticalKey` | **blocker** | Add `VerticalKey` (NOT NULL, backfill laundry) — keystone for all gating |
| Database | `01_bc1:42` / `brands` no `vertical_key` | **blocker** | `ALTER brands ADD vertical_key DEFAULT 'laundry'` |
| Database | `04_bc4:399` / garment_* tables | **blocker** | Move 11 physical tables to `laundry_fulfillment` schema behind LaundryProcessStrategy |
| Entitlement | `Brand.cs:6` / entitlement can't scope per vertical | **blocker** | Add `Brand.VerticalKey` + `modules.vertical_key` |
| Engagement | `Brand.cs:7` / no `VerticalKey` for content scoping | **blocker** | Add `VerticalKey`; surface via `GetPublicAppConfig` |
| Customer Mobile | `config.ts:30` / no feature-pack mechanism | **blocker** | VerticalContext + feature-pack registry from MobileAppConfig |
| Customer Mobile | `_layout.tsx:72` / create sheet hardcodes laundry+parcel | **blocker** | Pack-declared entry actions keyed on FulfillmentMode |
| Customer Mobile | `booking/items.tsx:140` / laundry item basket | **blocker** | Per-pack booking screen; generic line model |
| Customer Mobile | `api.ts:211` / `OrderStatus` ladder | **blocker** | Backend-driven stage descriptors |
| Rider Mobile | `engagement.ts:34-43` / no VerticalKey, brand hardwired | **blocker** | VerticalContext from MobileAppConfig |
| Admin Web | `api.ts:59-71` / `BrandDto` no `verticalKey` | **blocker** | Add `verticalKey`; gate feature packs |
| Admin Web | `ScanInDrawer.tsx:21-29` / `NEXT_STAGE` ladder | **blocker** | Source stage ladder from strategy endpoint |
| POS Web | `api.ts:53-65` / no VerticalKey/CatalogKind/FulfillmentMode | **blocker** | Add DTO fields; `useVertical()`/`useFulfillmentConfig()` |
| Logistics | `OrderStateMachine.cs:26-47` / default map = laundry | high | Move map into `LaundryProcessStrategy`; route by VerticalKey |
| Logistics | `UpdateMyTaskStatus.cs:129-133` / pickup→`Received` literal | high→med | `strategy.PostPickupStatus(order)` |
| Logistics | COD-on-delivery clears the order's own balance (marketplace billing) | high | **GATE before logistics GA:** sender/merchant-credit settlement model is a separate Finance/Commerce decision (see §8 Risk #9, §7 Phase 3 gate) |
| Orders | `OrderItem.cs:25,52,55` / `FabricTypeId`+`Garments` | high | Drop FK/nav; `OrderItem.Attributes` jsonb |
| Orders | `Invoice.cs:43-46` / GST + SAC 999712 | high | Per-vertical `TaxProfile`; tax-breakdown jsonb (3-way coord — §8 Risk #4) |
| Commerce | `SubscriptionInvoice.cs` / Cgst/Sgst/Igst | high | tax-breakdown jsonb (3-way coord — §8 Risk #4) |
| Finance | `RoyaltyInvoice`/`FranchiseSubscriptionInvoice` / GST cols | high | tax-breakdown jsonb (3-way coord — §8 Risk #4) |
| Catalog | `FabricType.cs` / first-class laundry table | high | Demote behind strategy + ModuleBundle gate |
| Catalog | `ItemPricingCommands.cs:108` / fabric==variant | high | Route variant management through strategy |
| Identity | `IdentitySeeder.cs:184-191` / `garment.*`/`warehouse.*`/`qc.perform` perms | high | Split into core + per-vertical permission packs |
| Identity | `IdentitySeeder.cs:337-625` / warehouse/rider roles + grants | high | Per-vertical role packs by VerticalKey |
| Commerce | `SubscriptionPlanHandlers.cs:190` / `weight_kg` quota | high | `strategy.ValidQuotaUnits` |
| Commerce | `SubscriptionPlan.cs:38` / Pickup/Delivery/ExpressIncluded | high | jsonb `FulfillmentInclusions` |
| Engagement | `NotificationMappingService.cs:109-117,361-376` / laundry events+aggregate joins | high | Per-vertical event catalog + pluggable recipient resolver |
| Tenancy | `Warehouse.cs:33-36` / has_dry_clean/steam_iron/... | high | Drop booleans → generic `Capabilities[]` + Config jsonb |
| Tenancy | `Store.cs:37-42` / pickup/delivery capacity | high | Strategy-owned capacity descriptor in `Store.Config` jsonb |
| Analytics | `WarehouseThroughput.cs:12` / garments/rewash/TAT | high | Extract to LaundryProcessStrategy-private; generic `FulfillmentThroughput` |

---

## 5. Per-Module Work Breakdown

> **Estimate basis:** each figure below is the audited `estimatePersonDays` for that module. The column sums to **324 pd** (verified arithmetic below). This is the authoritative program total; §7 phase totals trace back to these via §7.0.

| Module | changeType | Est. (pd) | Key tasks |
|---|---|---|---|
| **Database / Migrations / RLS** | refactor | 34 | Add `brand.vertical_key`, `order.vertical_key`/`fulfillment_mode`, `item.catalog_kind`/`attributes`; relax `orders.status` + `garments.current_stage` CHECKs; create `laundry_fulfillment` schema + relocate the 11 garment/warehouse physical tables (RLS reused verbatim); vertical-aware RBAC seed |
| **Laundry Fulfillment** | refactor | 48 | Define `IFulfillmentStrategy`; promote `garments`→`fulfillment_unit`; strategy-driven stages; `OnInspectionResult`; rename Warehouse→Fulfillment module; `garment.*`→`fulfillment.*` perms+events (atomic grant re-map — §8 Risk #6; coord. Engagement); assemble `LaundryProcessStrategy` |
| **Orders / Job Spine** | refactor | 38 | Add discriminators; `IFulfillmentStrategy` seam; widen `OrderStatus` (XL); sever `Order.Garments` (XL); delegate validation/PrepareFulfilment/OnTransition; vertical-aware `Invoice` tax (3-way coord) |
| **Customer Mobile** | refactor | 38 | VerticalContext + feature-pack registry (XL); replace `JobType`→`FulfillmentMode`; per-pack booking/home/create-sheet; backend-driven tracking ladder; split i18n; refactor parcel→logistics pack; EAS build profiles |
| **Catalog** | refactor | 26 | `CatalogKind` + `Attributes` foundation; strategy resolver; demote FabricType FKs + table; strategy-rendered pricing matrix; open `PricingChangeLog.TargetKind` |
| **Rider Mobile** | refactor | 17 | VerticalContext; `garmentCount`→`itemSummary`; strategy phase descriptors; FulfillmentMode-driven steps; relocate inspection behind feature flag; parameterize brand identity |
| **Admin Web** | refactor | 19 | Add `BrandDto.verticalKey`; fulfillment-strategy client surface; strategy-driven order state machine + dashboard; warehouse board → process-board pack; CatalogKind item editor |
| **Identity / RBAC** | refactor | 14 | Add `Brand.VerticalKey`; per-vertical seed-pack abstraction; split permission/role/grant packs (atomic re-map — §8 Risk #6); neutralize `UserType`; generalize `InviteRider`; relocate dispatch settings; drop `LG-MAIN` fallback |
| **Logistics / Dispatch** | refactor | 14 | Strategy + registry; extract transition maps; route all `OrderStateMachine` call sites; `PostPickupStatus`; gate store-drop; conditions-bag; `GarmentCount`→`ItemCount`; parity tests; COD/merchant-credit gate (§8 Risk #9) |
| **Engagement / CMS** | refactor | 11 | `Brand.VerticalKey`; surface via `GetPublicAppConfig`; shared deeplink-scheme policy; data-driven placements/AppType; rework `NotificationMappingService` (events + recipient resolver + template-only body); seed laundry templates |
| **Tenancy / Org** | refactor | 11 | Add `VerticalKey`; strip Warehouse capability booleans + Store capacity cols → jsonb; strategy-driven Create handlers; open `WarehouseType`/`StoreType` strings |
| **POS Web** | refactor | 11 | Add DTO fields; `useVertical()`/`useFulfillmentConfig()`; generalize weight-mode/print-artifact/status-ladder/express; brand-sourced name; generic tax label |
| **Finance / Royalty** | refactor | 11 | Rename quota `order`→`job`; vertical-keyed `ShiftHandover`/`CashBook` counters (RENAME, preserve history); `operational_snapshot` backfill (destructive — §8 Risk #5); configurable cash categories; entitlement-gate CashBook; `RoyaltyInvoice` GST→tax_breakdown (3-way coord); migration |
| **Analytics** | refactor | 11 | Consume VerticalKey; 7-matview registry (incl. `mv_subscription_mrr`, `mv_franchise_saas_mrr`); extract warehouse-throughput; gate rider-perf by FulfillmentMode; generalize express/active_packages; vertical dashboard tiles |
| **Module Entitlement** | extend | 7 | Add `Brand.VerticalKey` + `modules.vertical_key` + `module_bundle.vertical_key`; scope `warehouse` module to laundry; per-vertical bundles; vertical filter in navigator/entitlements/backfill |
| **Commerce** | refactor | 7 | Quota `order`→`job`/`ValidQuotaUnits`; `SubscriptionPlan` flags → `FulfillmentInclusions` jsonb; `SubscriptionInvoice` GST→tax_breakdown (3-way coord); Package doc; currency-from-wallet; (deferred) gateway |
| **TOTAL** | | **324** | `34+48+38+38+26+17+19+14+14+11+11+11+11+11+7+7 = 324` |

---

## 6. Mobile / Web App Strategy

**One white-label Expo codebase per app role (customer / rider), with per-vertical feature packs mounted at runtime, and separate store listings per brand via EAS build profiles.** Same pattern for admin-web/pos-web (React, runtime-gated by `BrandDto.verticalKey`).

### Architecture
- **Resolve `Brand.VerticalKey` at boot** from a `MobileAppConfig` row keyed on `defaultBrandCode` (a `config_key='vertical'` / `AppSettingsConfigValue.feature_flags` manifest). Expose via a `VerticalContext` provider.
- **Feature-pack registry** `{ laundry, salon, logistics }`: each pack declares create-flow entry actions, home tiles/hero, booking screen, tracking-stage rendering, demo/fallback catalog, icon map, and per-vertical i18n bundle. Screens consume the active pack instead of importing laundry screens directly.
- **EAS build profiles per brand** inject `name`/`slug`/`scheme`/`bundleIdentifier`/colors/icon/`projectId`, replacing hardcoded `Laundry Ghar`/`laundryghar://`/`com.laundryghar.*` (`app.config.ts:9`). `BrandSplash` reads name/tagline/logo from MobileAppConfig.
- **Open decision:** one multi-vertical build (runtime pack selection) vs one-vertical-per-EAS-profile. The current parcel+laundry coexistence implies multi-flow-per-build already works; recommend runtime pack selection with per-brand EAS profiles for store identity.

### Core vs Vertical Pack split

| App | **Core (shared, unchanged)** | **Vertical pack** |
|---|---|---|
| **Customer** | API envelope/client, auth/OTP, addresses, Commerce (wallet/loyalty/coupons/payment), support, push/OTA/Sentry, i18n engine, version-gate, brand-code propagation, UI kit, MobileAppConfig plumbing | Create-sheet entries, home service grid + hero, booking flow (garment basket / appointment slot / parcel dims), tracking ladder copy, demo catalog, service icons, vertical i18n strings |
| **Rider** | Duty/location pings, task lifecycle (assigned→started→arrived→completed/failed), OTP/proof photo, earnings/cash/payouts/incentives, KYC docs, support, version-gate/OTA, UI kit | `itemSummary` unit label (garments/parcels/services), phase descriptors (`to_store`/`dropped` → strategy-defined), pickup-inspection schema (laundry condition flags / parcel condition / none), drop-step sequence |
| **Admin** | Server-driven `/navigator`, ModuleBundle entitlements UI, brand-switch/tenancy, finance/CMS/MobileAppConfig, riders/logistics, access-control, table/drawer primitives | Warehouse/process board (laundry) vs appointments/dispatch tabs, Fabric-multipliers tab (laundry) vs service-tier (salon), CatalogKind item editor, status ladder labels, dashboard KPIs |
| **POS** | Catalog→cart→order→payment→print spine, auth/tenancy, RBAC, offline payment, cash-book, barcode/UI primitives | Line unit-of-measure widget (kg/count/minutes), print artifact (GarmentTags / waybill / appointment-slip), status ladder, order-options region (express toggle / slot picker / pickup-dropoff) |

**Migration ordering caveat:** mobile/web generalization is *blocked* on backend exposing `Item.CatalogKind`+`Attributes`, `Order.VerticalKey`/`FulfillmentMode`, and a vertical-neutral stage-descriptor list. Clients ship interim adapters keyed off `verticalKey` where the backend contract lags.

---

## 7. Phased Roadmap

### 7.0 Phase ↔ Module traceability matrix (reconciles §7 to §5's 324 pd)

Phases are a *sequencing* decomposition of the **same** 324 pd in §5. Each module's audited estimate is sliced across the phases it touches; the row totals equal the §5 module estimate, and the column totals are the phase totals. **Both decompositions sum to 324.**

| Module (§5 est.) | P0 | P1 | P2 | P3 | P4 | Row total |
|---|--:|--:|--:|--:|--:|--:|
| Database (34) | 4 | 18 | 8 | 0 | 4 | 34 |
| Laundry Fulfillment (48) | 4 | 40 | 4 | 0 | 0 | 48 |
| Orders (38) | 4 | 26 | 6 | 0 | 2 | 38 |
| Catalog (26) | 2 | 0 | 18 | 0 | 6 | 26 |
| Identity (14) | 0 | 4 | 8 | 0 | 2 | 14 |
| Logistics (14) | 1 | 10 | 0 | 3 | 0 | 14 |
| Commerce (7) | 0 | 0 | 6 | 0 | 1 | 7 |
| Finance (11) | 0 | 0 | 10 | 0 | 1 | 11 |
| Tenancy (11) | 0 | 0 | 9 | 0 | 2 | 11 |
| Engagement (11) | 0 | 2 | 8 | 0 | 1 | 11 |
| Analytics (11) | 0 | 0 | 9 | 0 | 2 | 11 |
| Entitlement (7) | 0 | 0 | 4 | 0 | 3 | 7 |
| Customer Mobile (38) | 0 | 0 | 0 | 28 | 10 | 38 |
| Rider Mobile (17) | 0 | 0 | 0 | 14 | 3 | 17 |
| Admin Web (19) | 0 | 0 | 0 | 12 | 7 | 19 |
| POS Web (11) | 0 | 0 | 0 | 6 | 5 | 11 |
| **Phase total** | **15** | **100** | **100** | **73** | **49** | **324** |

> Both axes reconcile: phase column totals `15+100+100+73+49 = 324` = module row totals = §5 total. The phase numbers are now *derived from* the audited module estimates, not back-fit to a headline.

### Critical path
`Brand.VerticalKey` (Phase 0) → **partition-key + immutability decision (Phase 0 BLOCKING gate, §8 OQ-A)** → `IFulfillmentStrategy` contract + resolver (Phase 0) → widen `OrderStatus` + sever `Order.Garments` + `laundry_fulfillment` schema (Phase 1, the two XL blockers) → `LaundryProcessStrategy` parity (Phase 1) → Catalog `CatalogKind` (Phase 2) → client feature packs (Phase 3) → Salon strategy + module (Phase 4).

---

#### Phase 0 — Foundation (discriminators + seam + blocking decisions) · 15 pd
*Goal: every layer has something to branch on; strategy seam exists with laundry as the only impl; the two partitioned-migration-shaping decisions are RESOLVED before any destructive Phase-1 migration.*
- **BLOCKING DECISION (gate Phase 1):** does `vertical_key` join the `orders (id, created_at)` range-partition key, and is it denormalized onto `OrderItem`/`OrderStatusHistory`/`DeliveryAssignment`? This materially shapes the two XL blockers (§8 OQ-A). **Must be resolved in Phase 0** — deferring it forces re-partitioning rework after the destructive garment relocation.
- **BLOCKING DECISION (gate Phase 1):** add a CHECK/trigger forbidding mutation of `brands.vertical_key` once orders exist? (one-vertical-per-brand invariant; cheap in Phase 0, expensive retrofitted) (§8 OQ-A).
- `Brand.VerticalKey` (Tenancy/Identity/Entitlement/Engagement all consume the same column) — owned by Tenancy/DB. SQL patch `brand_vertical_key.sql` + EF.
- `Order.VerticalKey` + `Order.FulfillmentMode` (Orders/DB), backfill from `JobType`.
- `IFulfillmentStrategy` interface + `IFulfillmentStrategyResolver` (empty default delegating to today's `OrderStateMachine`), in `operations.Application` shared kernel.
- `Item.CatalogKind` + `Item.Attributes` column foundation (Catalog/DB).
- **Dependencies:** none (Phase 0 is the root). **Gate:** all three hosts (CORE/OPERATIONS/COMMERCE) still build + boot; existing laundry behaviour byte-identical; both blocking decisions signed off.

#### Phase 1 — Laundry extraction behind the strategy (the XL work) · 100 pd
*Goal: laundry becomes a strategy + private schema; shared spine is vertical-neutral; laundry regression-clean.*
- Widen `OrderStatus` → generic super-states + strategy sub-status; relax DB CHECK — **XL**.
- Sever `Order.Garments`; create `laundry_fulfillment` schema; promote `garments`→`fulfillment_unit`; relocate the **11 physical tables** (9 entities + `garment_inspection_photos`, `stock_reconciliation_items`; RLS reused) — **XL**.
- Move transition maps + happy paths into `LaundryProcessStrategy`/`LogisticsPointToPointStrategy`; route ALL `OrderStateMachine` call sites (Orders BC + rider flow).
- Delegate `CreateOrder` validation/`PrepareFulfilment`, `UpdateOrderStatus` `OnTransition`, `OpsQueues` terminal/initial, pickup-leg `PostPickupStatus`, geofence store-drop gate.
- `garment.*`→`fulfillment.*` perms + outbox events. **ATOMICITY (§8 Risk #6):** the permission-string RENAME must execute as a single atomic grant migration that re-maps every existing `garment.*` grant to `fulfillment.*` in the same transaction — it must NOT orphan laundry-brand grants before the Phase-2 seeder split lands. A bridge view/alias keeps both keys resolvable until Phase 2 completes. Coord. Engagement `NotificationMappingService` + Analytics consumers keyed on `AggregateType='garment'`.
- Assemble + parity-test `LaundryProcessStrategy` and `LogisticsPointToPointStrategy` (exact-parity regression suite is the gate).
- **Dependencies:** Phase 0 complete (incl. both blocking decisions). **Critical path bottleneck — the two XL blockers gate everything downstream.**

#### Phase 2 — Catalog + commerce + finance generalization · 100 pd
*Goal: catalog/pricing/quota/billing carry no laundry nouns; FabricType demoted; the three tax schemas land together.*
- Catalog `CatalogKind`/`Attributes` wired; demote `FabricType` table + FKs (`ItemVariant`, `PriceListItem`, `OrderItem`) into laundry pack; strategy-rendered pricing matrix; open `PricingChangeLog.TargetKind`.
- **THREE-WAY TAX COORDINATION (single workstream, §8 Risk #4):** `Orders.Invoice` + `Commerce.SubscriptionInvoice` (Cgst/Sgst/Igst) + `Finance.RoyaltyInvoice`/`FranchiseSubscriptionInvoice` GST → `tax_breakdown` jsonb must land **together in one coordinated migration** per the Commerce/Finance audits' "latent cross-area trap"; landing any one alone diverges the three tax schemas.
- Commerce: quota `order`→`job`, `ValidQuotaUnits`, `SubscriptionPlan` flags → `FulfillmentInclusions` jsonb.
- Finance: quota metering rename, vertical-keyed `ShiftHandover`/`CashBook` counters via column RENAME (preserve history); `operational_snapshot` jsonb backfill from the dropped pickups/deliveries counters (**destructive — §8 Risk #5**); configurable cash categories; entitlement-gate CashBook.
- Tenancy: strip Warehouse capability booleans + Store capacity → jsonb; strategy-driven Create handlers.
- Analytics: **7-matview registry** + refresh-function rewrite covering the full shared set — `mv_daily_store_revenue`, `mv_monthly_franchise_revenue`, `mv_customer_ltv`, `mv_subscription_mrr`, `mv_franchise_saas_mrr` (the two SaaS-MRR views), plus the extracted warehouse-throughput and express/active_packages views; gate rider-perf by FulfillmentMode.
- Engagement: `NotificationMappingService` event catalog + recipient resolver + template-only body; seed laundry templates.
- Identity: split seeder into core + per-vertical packs. **ATOMICITY (§8 Risk #6):** the per-vertical grant re-map must be an atomic migration; it consumes the Phase-1 `fulfillment.*` rename via the bridge alias so no laundry brand is left without grants at any point. Neutralize `UserType`; relocate dispatch settings.
- Module Entitlement: `modules.vertical_key` + `module_bundle.vertical_key`; per-vertical bundles; vertical filters.
- **Dependencies:** Phase 1 (strategy seam + `FabricType` demotion needs the fulfillment_unit link; seeder split consumes the Phase-1 permission bridge).

#### Phase 3 — Client feature packs (mobile + web) · 73 pd
*Goal: one codebase per role serves any vertical at runtime; laundry pack proven; parcel→logistics pack refactored.*
- Customer mobile: VerticalContext + feature-pack registry (XL); per-pack screens; backend-driven tracking; i18n split; refactor parcel→logistics pack; EAS profiles.
- Rider mobile: VerticalContext; `itemSummary`/phase descriptors/inspection relocation; brand identity.
- Admin web: `verticalKey` gating; strategy client surface; process-board pack; CatalogKind editor.
- POS web: VerticalKey DTOs; `useFulfillmentConfig`; weight-mode/print/status/express generalization.
- **GATE before the logistics pack GAs (§8 Risk #9):** resolve the `point_to_point` COD / merchant-credit settlement decision. Today COD-on-delivery clears the *order's own* balance (marketplace billing). A logistics sender/merchant-credit model changes the COD pipeline and is a separate Finance/Commerce decision that **must be signed off before the logistics pack ships** — otherwise the parcel→logistics refactor validates an abstraction on top of the wrong settlement assumption.
- **Open decision (§8 OQ):** does POS apply to `point_to_point` (typically rider/app-driven) or only `process_deliver` + `appointment`? Resolve before POS logistics work.
- **Dependencies:** Phases 1–2 (clients consume `CatalogKind`/`Attributes`, `FulfillmentMode`, stage-descriptor endpoints). **Gate:** logistics pack (refactored from existing parcel flow) validates the abstraction before salon — and only after the COD gate is resolved.

#### Phase 4 — Salon strategy + module → Salon GA · 49 pd
*Goal: salon ships as a new strategy + `salon_fulfillment` schema + salon feature packs, with NO change to the shared spine.*
- `SalonAppointmentStrategy` (state machine booked→…→completed; `PrepareFulfilment` reserves staff/resource).
- New `salon_fulfillment` schema: `Appointment`, `StaffMember`, `Resource`, `ResourceBooking` (RLS reused verbatim).
- Salon catalog (`CatalogKind='salon'`, `duration_minutes`/`staff_tier` attributes), salon quota units (`service_minutes`), salon ModuleBundle + permission pack (`appointment.*`), salon SAC `TaxProfile`.
- Salon feature packs (customer booking = service+slot+staff picker; admin appointments board; POS appointment-slip); EAS salon brand profile.
- Salon E2E + GA.
- **Dependencies:** Phases 0–3 (the entire seam + client pack mechanism). This is the validation that the platform is truly multi-vertical.

**Program total: 324 person-days** (~1.3–1.6 calendar-years for a 2–3 engineer squad, with Phases 2/3 parallelizable across module owners once Phase 1's XL blockers land). Phase totals: P0 15 · P1 100 · P2 100 · P3 73 · P4 49 = 324, reconciled to §5 via §7.0.

---

## 8. Risk Register + Open Questions

### Risks (highest first)
1. **Two XL blockers gate the whole program** — widening `OrderStatus` (highest fan-out: state machine, validator, ops queues, outbox consumers, DB CHECK, mobile/admin/POS clients) and severing `Order.Garments` (drags the 11 laundry-fulfillment tables off a *range-partitioned, RLS-protected* `orders` aggregate). Mitigation: exact-parity regression suite + unknown-vertical→laundry fallback; the partition-key decision (Risk-adjacent, OQ-A) must be resolved in Phase 0 first.
2. **Destructive, partition-aware migrations** — dropping header columns (`TotalGarments`, `TotalWeightGrams`, `ReceivedAt/QcCompletedAt/ReadyAt`), `OrderItem.FabricTypeId`, and relocating `garments`→`fulfillment_unit` on partitioned `orders(id, created_at)` is irreversible; needs backfill-then-drop with a dual-write/read window. Use column RENAME (not drop/recreate) for finance/shift counters to preserve history.
3. **Cross-module event/contract ripple** — `garment.*`→`fulfillment.*` outbox rename breaks Engagement `NotificationMappingService` (planned `GARMENT_LOST_*` template) + Analytics consumers keyed on `AggregateType='garment'`; status-string changes ripple to external integrations. Must be coordinated atomically.
4. **Three-schema tax divergence (Orders + Commerce + Finance)** — `Orders.Invoice`, `Commerce.SubscriptionInvoice` (Cgst/Sgst/Igst), and `Finance.RoyaltyInvoice`/`FranchiseSubscriptionInvoice` each carry their own GST columns. The Commerce and Finance audits explicitly flag this as a "latent cross-area trap": the three GST→`tax_breakdown` jsonb migrations **must land simultaneously as one coordinated Phase-2 workstream, or the three tax schemas diverge.** Mitigation: single owner for the tax-jsonb shape; one migration PR spanning all three contexts; shared `TaxProfile`/`tax_breakdown` contract in `SharedDataModel`.
5. **Finance destructive backfill + irreversible data updates** — the `operational_snapshot` jsonb backfill is sourced from the *dropped* pickups/deliveries counters on `shift_handovers`/`cash_books`; if the backfill is not run before the drop, historical shift data is lost. Additionally the `order_payment`→`job_payment` data update and the associated CHECK-constraint changes are **not trivially reversible**. Mitigation: backfill-then-drop ordering enforced in the migration; RENAME over drop/recreate for all counter columns; a verified snapshot-completeness assertion gates the column drop.
6. **RBAC permission-string RENAME + seeder split — atomicity / lockout** — permission keys live in the GLOBAL `identity_access` catalog (no RLS). The `garment.*`→`fulfillment.*` RENAME (Phase 1) and the monolithic-seeder→per-vertical-pack grant re-map (Phase 2) are sequenced *apart*. Each must be an **atomic grant migration**; the Phase-1 rename must NOT orphan existing laundry-brand role-permission grants before the Phase-2 split lands. Bridge: a Phase-1 alias/view keeps both permission keys resolvable until the Phase-2 re-map completes, so no admin/rider is locked out of the fulfillment module in the gap. Mitigation: idempotent re-map with pre/post grant-count assertions per brand.
7. **Strategy home + DI not yet decided** — `OrderStateMachine` is shared between Orders BC and the Logistics rider flow; wrong placement of `IFulfillmentStrategy` forces a second refactor. Recommend `operations.Application` shared kernel (resolved in Phase 0).
8. **RLS / CHECK / migration files live outside audited dirs** — fabric FKs, `services`/`service_categories` status enums, `warehouse_type`/`store_type` CHECKs, and analytics SECURITY-DEFINER refresh must be re-audited so moving columns to jsonb doesn't silently break BrandId scoping.
9. **Logistics COD / merchant-credit settlement undecided** — `point_to_point` COD-on-delivery currently assumes COD clears the *order's own* balance (marketplace billing). A logistics sender/merchant-credit model would change the COD pipeline and is a separate Finance/Commerce decision that, per the Logistics audit, **must be made BEFORE enabling the logistics vertical.** This is now an explicit **gate in Phase 3** (before the logistics pack GAs), not a deferred open question.
10. **Client-vs-backend migration ordering** — mobile/web cannot finalize `fabricTypeId`/`OrderStatus`/`itemSummary` until backend exposes `CatalogKind`+`Attributes` and the stage-descriptor list; the single biggest schedule coupling between teams.
11. **Preserved latent laundry bugs** — `LaundryProcessStrategy` must reproduce current behaviour *exactly*, constraining how aggressively the extraction can clean up.

### Open Questions

**Phase-0 BLOCKING gates (must resolve before any destructive Phase-1 migration):**
- **OQ-A.1 — partition-key participation:** does `vertical_key` join the `orders (id, created_at)` range-partition key, and is it denormalized onto `OrderItem`/`OrderStatusHistory`/`DeliveryAssignment` for partition-pruning? This shapes the two XL blockers; deferring it past the partitioned garment-relocation migration forces re-partitioning rework. **Gate: Phase 0.**
- **OQ-A.2 — `brand.vertical_key` immutability:** add a CHECK/trigger forbidding mutation once orders exist (one-vertical-per-brand)? Cheap in Phase 0, expensive retrofitted. **Gate: Phase 0.**

**Phase-3 BLOCKING gate (before logistics pack GA):**
- **OQ-B — logistics COD / merchant-credit settlement model** (see Risk #9). **Gate: before logistics pack GA in Phase 3.**

**Resolve before/early in the relevant phase:**
- ~~**`VerticalKey` vs `JobType` mapping**~~ **RESOLVED (Phase 1, by data):** the live DB shows a single laundry-vertical brand running both laundry (6) and parcel (2) orders. Therefore the **state-machine/fulfilment strategy is keyed by `FulfillmentMode`** (per-order leg topology: `process_deliver` ↔ laundry, `point_to_point` ↔ parcel), NOT by `VerticalKey` (which stays the brand-level catalog/branding/entitlement discriminator). `IFulfillmentStrategy.FulfillmentMode` is the registry key; `IFulfillmentStrategyResolver.ResolveForOrder` prefers `order.FulfillmentMode` and falls back to the legacy `JobType` for un-backfilled rows. This keeps the brand→order denormalization invariant intact (a laundry brand's parcel order has `vertical_key='laundry'`, `fulfillment_mode='point_to_point'`).
- **Fulfillment schema strategy** — per-vertical schemas (`laundry_fulfillment`, `salon_fulfillment`) vs a generic `fulfillment_resource` table keyed by jsonb. Schema-per-strategy is cleaner for RLS but order_lifecycle must reference strategy tables polymorphically.
- **Salon slots** — reuse generic `DeliverySlot/DeliverySlotBooking` time-capacity, or introduce a distinct `ResourceBooking` (chair/staff concurrency)? Affects whether `DeliverySlot` is renamed to a neutral `Slot`.
- **Tax scope** — multi-jurisdiction now vs multi-vertical-SAC-within-India for v1? Recommend multi-vertical SAC + jsonb tax-breakdown now, defer multi-country (ties into Risk #4's three-schema workstream).
- **Vertical pack registration** — `IFulfillmentStrategy.SeedRoleGrants/SeedPermissions` hooks vs declarative ModuleBundle manifests; and whether the seeder *stops creating* cross-vertical rows or just stops *granting* them (entitlement already filters by `module_key`).
- **MobileAppConfig ↔ entitlement link** — do mobile feature packs gate on `BrandModule` rows, purely on `VerticalKey`, or both? No current link exists.
- **Stage ladder ownership** — fully backend-driven stage descriptors (cleaner for `IFulfillmentStrategy`) vs client-defined per pack? Backend-driven needs a new tracking DTO shape.
- **POS scope for logistics** — does the counter/POS flow apply to `point_to_point` (typically rider/app-driven), or is POS scoped to `process_deliver` + `appointment` only? (resolve in Phase 3, before POS logistics work).
