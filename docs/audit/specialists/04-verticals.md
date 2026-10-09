# 04 — Multi-Vertical Product and Domain Specialist

Agent key: `verticals` · AREA code: `VERT` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw`

## Scope and method

**Question:** can LaundryGhar host several business categories (laundry/dry-cleaning, salon/appointments, hyperlocal marketplace, local delivery/logistics, recurring delivery such as tiffin) as separate tenants without turning into one tightly coupled app? And what is actually built, as opposed to planned?

**What I inspected (all read directly; paths are repo-relative):**
- **Discriminators and enums:** `backend/laundryghar/laundryghar.SharedDataModel/Enums/{VerticalKey,FulfillmentMode,CatalogKind,OrderLifecycleState,JobType}.cs`, plus the entities `Brand.cs`, `Order.cs`, `Item.cs`, `Service.cs`, `FulfillmentUnit.cs`, `DeliverySlot*.cs`, `OperatingHour.cs` and `Holiday.cs`.
- **Strategy seam:** `operations.Application/Fulfillment/**` (interface, resolver, base class, Laundry, Logistics, Salon, Recurring, `DeliveryCadence`, config query) and its DI registration in `operations.Application/DependencyInjection.cs`.
- **Order spine handlers:** `CreateOrderCommand.cs`, `CreateParcelOrderCommand.cs`, `CancelOrderCommand.cs`, `GenerateInvoiceCommand.cs`, `InvoiceTaxCalculator.cs`, `InvoicePdfRenderer.cs`, `PriceResolver.cs`, `PickupCommands.cs`, `CustomerPickupCommands.cs`, `DeliverySlotCommands.cs`, and the warehouse endpoints and `CreateGarment.cs`.
- **Onboarding and entitlements:** `CompleteSignup.cs`, `TemplateProvisioner.cs`, `GetSignupTemplates.cs`, `CreateBrand.cs`, `UpdateBrand.cs`, `ApplyBundleToBrand.cs`, `SetBrandFeature.cs`, `GetNavigator.cs`, `GetAccessRoles.cs`, `GrantMembership.cs`, `ScopeResolver.cs` (token-mint entitlement filter), `PermissionHandler.cs`, `FeatureNotInPlan.cs`, and `core.WebApi/appsettings.json` (`Entitlement:Enforced`).
- **Notifications:** `NotificationChannelPreferencePolicy.cs` and `NotificationMappingService.cs`.
- **Database:**
  - Migrations `db/migrations/0004, 0005, 0007, 0008, 0010, 0011, 0012`.
  - Patches `db/patches/phase0_multi_vertical.sql`, `phase1_slice_b_*`, `phase4_salon_pack.sql`, `phase4_salon_fulfillment_schema.sql`, `phase2_slice_i_matview_registry.sql`, `phase2_slice_j_notification_event_catalog.sql`, `seed_navigator_modules.sql`, `permission_canonical_module.sql`.
  - Base schema `database_scripts/04_bc4_order_lifecycle.sql` (delivery slots), plus `db/build_from_scratch.sh` and `db/patches/apply_patches.sh`.
- **Clients:**
  - admin-web: `App.tsx` routes, `pages/orders/orderStatus.ts`, `OrderDetailDrawer.tsx`, `lib/fulfillment.ts`, `lib/verticalTerms.ts`.
  - pos-web: `OrderDetailPage.tsx`.
  - customer-mobile: `app/(app)/(tabs)/_layout.tsx`, the `parcel/*` screens, `src/lib/terminology.ts`.
  - rider-mobile: grep for garment-specific screens.
- **Tests (read, not run):** `operations.Tests/Fulfillment/*`, `operations.Tests/Catalog/VerticalModuleGatingTests.cs`, `operations.IntegrationTests/Phase4Salon*Tests.cs`.
- **Prior documents, used as claims to verify:** `docs/MULTI_VERTICAL_BLUEPRINT.md`, `docs/MULTI_VERTICAL_SLICE_C.md`, `PLATFORM_STRATEGY.md`, `docs/GAP_ANALYSIS.md`.

**Commands run:** read-only `grep`, `sed`, `cat` and `ls` over the repo. Scratch directory `scratchpad/verticals/` was not needed.

**What I could not verify, and why:**
- No .NET SDK, so I built nothing and ran no tests. Everything stated about the strategy unit tests comes from reading them.
- No running PostgreSQL, so the runtime contents of `identity_access.permissions.module_key`, `bundle_feature` and `brand_feature` are inferred from patch and migration SQL, not queried.
- I did not drive any client UI.
- I did not check whether `db/patches/phase*.sql` have been applied to any live environment.

## Current-state summary

**1. The vertical discriminator is real, which contradicts the blueprint.** `docs/MULTI_VERTICAL_BLUEPRINT.md` §1 says "No discriminator exists". That is now false.
- `Brand.VerticalKey` exists (`Brand.cs:13-16`, `BrandConfiguration.cs:19`). It is NOT NULL, defaults to `laundry`, and is constrained by CHECK to laundry/salon/logistics/tiffin (`phase0_multi_vertical.sql:29-36`, widened by `0004`).
- A DB trigger makes it immutable once the brand has orders (`phase0_multi_vertical.sql:75-97`).
- `Order.VerticalKey` and `Order.FulfillmentMode` exist (`Order.cs:37-48`) together with a neutral `LifecycleState` (`OrderLifecycleState.cs`).
- The vertical is assigned once, at self-signup, from a template: `CompleteSignup.cs:73-75` (public template only) and `:114` (`VerticalKey = template.VerticalKey`).
- There is no API to change it: `UpdateBrandRequest` has no vertical field (`BrandDtos.cs:20-27`).

**2. The strategy seam is real and consistently used for state transitions.**
- `IFulfillmentStrategy` (`IFulfillmentStrategy.cs:21-100`) is keyed by **FulfillmentMode**, not VerticalKey (stated at `:10-14`).
- `FulfillmentStrategyResolver` (`:10-43`) falls back to laundry `process_deliver`.
- Four strategies are registered as singletons (`DependencyInjection.cs:33-40`):
  - `LaundryProcessStrategy` — the wash/QC pipeline, including rewash.
  - `LogisticsPointToPointStrategy`.
  - `SalonAppointmentStrategy` — its own booked→confirmed→checked_in→in_service→completed vocabulary.
  - `RecurringDeliveryStrategy`.
- Cancel and status updates resolve the strategy per order (`CancelOrderCommand.cs:25-60`).
- The `orders.status` CHECK was dropped so that strategies own sub-status validity (`phase1_slice_b_order_lifecycle_state.sql:72-85`).
- The laundry tables were moved into a `laundry_fulfillment` schema, as the EF configs `ToTable("fulfillment_unit","laundry_fulfillment")` etc. confirm.

**3. Only two of the four modes can actually be reached when an order is created.**
- The only trace from order creation is: `POST /orders` → `CreateOrderHandler.HandleAsync` → `isParcel = req.JobType == JobType.Parcel` (`CreateOrderCommand.cs:60`) → `fulfillmentMode = isParcel ? PointToPoint : ProcessDeliver` (`:620-622`) → `Order{ FulfillmentMode = …, VerticalKey = <entity default "laundry"> }` (`:629-647`, `Order.cs:44`).
- The brand's vertical is never read. `FulfillmentMode.DefaultFor(vertical)` and `CatalogKind.DefaultFor(vertical)` have no callers (grep).
- No code path ever creates an order in `appointment` or `recurring` mode. Those strategies are reachable only from unit tests and from `GET /api/v1/fulfillment-config`.

**4. Salon is a schema, an enum and a nav row; it is not a product.**
- `db/patches/phase4_salon_fulfillment_schema.sql` creates `salon_fulfillment.{staff_members,resources,appointments,resource_bookings}` with RLS.
- There are no EF entities, handlers or endpoints for these tables (grep for `salon_fulfillment|StaffMember|ResourceBooking` in `backend/` finds only the schema test and an unrelated onboarding count).
- The salon nav module points at `/appointments` with permission `appointment.manage` (`phase4_salon_pack.sql:19-25`):
  - admin-web has no such route, and unknown paths redirect to `/` (`admin-web/src/App.tsx:87-116`);
  - the permission is not seeded anywhere (grep over `db/`, `database_scripts/` and `IdentitySeeder.cs`).
- Despite all this, the salon template is public at signup (`0011_vertical_templates.up.sql:41,71-73`).

**5. Tiffin (recurring delivery) is a table, a pure date calculator and a strategy. There is no generator.**
- `0012_recurring_fulfillment_mode.up.sql:49-145` creates `delivery_schedules` and `delivery_schedule_occurrences` (a well-designed idempotency ledger).
- No C# entity, generator, endpoint or client references either table (grep finds only XML comments).
- `DeliveryCadence` (`Recurring/DeliveryCadence.cs`) is called only by its tests.
- 0012 nonetheless flips the tiffin template to `is_public = true` (`:155-156`), reversing 0011's own rule at `:76-79,90` ("Listing an unrunnable template to a provider at signup would be worse than not offering it").

**6. Logistics (point-to-point) is genuinely operable.** It has its own order command (`CreateParcelOrderCommand.cs:93,109`), a signed fare quote (`CreateOrderCommand.cs:587-603`), rider assignment and self-service, geofence, proof of delivery (`DeliveryAssignment.cs:26,72-75`), COD settlement, RaaS partner dispatch, and customer-mobile parcel screens (`customer-mobile/app/(app)/parcel/{pickup,drop,vehicle,quote}.tsx`). It still rides the laundry-shaped order spine and invoice (see SA-VERT-004).

**7. How vertical gating actually works.** The gate (`VerticalKey.IsAvailableTo`) is applied in:
- the navigator (`GetNavigator.cs:38-80`);
- role lists (`GetAccessRoles.cs:56`, `GetRoles.cs:37`);
- bundle application (`ApplyBundleToBrand.cs:29-46`);
- template provisioning (`TemplateProvisioner.cs:53-79`).

It is **not** applied in the token-mint entitlement filter (`ScopeResolver.cs:173-246`), in `SetBrandFeature` or `GrantMembership`, or in any operations handler. API-side protection therefore depends entirely on which *features* a brand owns. The laundry warehouse module is gated by the **vertical-neutral** feature `processing_facility` (`0005_split_features_from_modules.up.sql:66-71,112-115`; `seed_navigator_modules.sql:42`).

**8. Hardcoded `if vertical == …` branches are rare.** In the backend, outside enums, the only vertical-keyed switches are `TemplateProvisioner.CatalogKindFor` (`:146-152`) and `GetTerminology.DefaultVertical` (`:31`). The coupling is mostly implicit instead:
- `JobType`-based branching (`isParcel`);
- laundry status literals in shared handlers (invoice billable statuses, the notification template switch);
- laundry-only constants (SAC 999712, TAT hours, express surcharge, `UnitOfMeasure = "piece"`);
- client-side mirrored laundry state machines.

## Domain capability matrix

Legend: **Y** = works end-to-end (code read) · **P** = partial / laundry-shaped but reusable · **S** = schema or enum only, no executable path · **N** = absent (searched backend, db, database_scripts, four clients).

| Capability | Shared platform? | Laundry | Salon | Hyperlocal marketplace | Logistics | Evidence | Missing |
|---|---|---|---|---|---|---|---|
| Tenant + single vertical discriminator | Y | Y | Y (set at signup) | N (no `marketplace` key) | Y | `Brand.cs:13-16`; `CompleteSignup.cs:114`; `phase0_multi_vertical.sql:75-97` | Admin `CreateBrand` cannot choose vertical (SA-VERT-009); no marketplace vertical value |
| Customers / addresses / consent | Y (generic) | Y | Y | P | Y | `Entities/CustomerCatalog/Customer*.cs` | — |
| Staff, roles, RBAC | P | Y | P (roles `salon_manager`/`salon_staff` seeded, `IdentitySeeder.cs:415-416`) | P | Y (`hub_*`, `partner_*`) | `IdentitySeeder.cs:396-454` | Role vertical gate is UI-only (SA-VERT-005) |
| Locations (franchise → store → warehouse) | P | Y | P (studio = `warehouse` scope via terminology) | N (no seller/vendor entity) | P (hub = warehouse) | `0010_vertical_terminology.up.sql:52-56`; `verticalTerms.ts:35-41` | Seller/vendor model for a marketplace |
| Service catalog | P (laundry-shaped) | Y | S (`catalog_kind='service'` seeded, no duration/staff tier) | P (`product` kind, no inventory) | P | `Service.cs` (TAT/express/RequiresQc); `Item.cs:14-16`; `TemplateProvisioner.cs:86-152` | Duration, staff-tier pricing, product stock |
| Pricing | P | Y (price list × service × item × variant, value slabs, fabric multipliers, express) | N (no duration/tier pricing) | N | Y (fare quote by vehicle tier) | `PriceResolver.cs:16-59`; `CreateOrderCommand.cs:147-203,587-603` | Per-vertical pricing strategy |
| Order / booking spine | Y (orders, items, history, outbox, payments) | Y | N (orders created as `process_deliver`) | P | Y | `CreateOrderCommand.cs:60,620-647` | Mode resolution from brand vertical (SA-VERT-001) |
| State machine | Y (strategy seam) | Y | S (strategy unreachable) | N | Y | `Fulfillment/**`; `DependencyInjection.cs:33-40` | Creation path for `appointment`/`recurring` |
| Pickup/delivery slots + capacity | P | Y (atomic counter, unique slot) | N | N | P | `04_bc4_order_lifecycle.sql:347-365`; `PickupCommands.cs:399-414` | Staff/resource dimension, duration |
| Staff availability / calendars | N | n/a | N (table only) | N | P (rider duty/capacity config) | `phase4_salon_fulfillment_schema.sql:25-37`; `RiderCapacityConfig` | Availability service |
| Service duration | N | n/a | S (`appointments.attributes` jsonb) | N | n/a | `phase4_salon_fulfillment_schema.sql:52-69` | First-class duration |
| Operating hours / holidays | S | S | S | S | S | `OperatingHour.cs`, `Holiday.cs` — no handler uses them (grep) | Enforcement in booking/slot logic |
| Double-booking protection | P (slot counter only) | Y (slot capacity) | N (no exclusion constraint, no code) | N | P | `phase4_salon_fulfillment_schema.sql:52-84` | `EXCLUDE USING gist` on staff/resource time ranges |
| Recurring delivery | S | n/a | n/a | n/a | n/a (tiffin) | `0012...up.sql:49-145`; `DeliveryCadence.cs` | Entity + generator + API (SA-VERT-002) |
| Dispatch / riders / GPS / proof of delivery / COD | Y | Y | n/a | P | Y | `operations.Application/Logistics/**`; `DeliveryAssignment.cs:26,72-75` | Route optimisation (not searched beyond `SequenceNumber`) |
| Payments / wallet / coupons / loyalty / packages | Y | Y | P | P | Y | `CreateOrderCommand.cs:640-670` | — |
| Invoicing / tax | P (laundry SAC) | Y | N (wrong SAC) | N | N (wrong SAC) | `InvoiceTaxCalculator.cs:18`; `InvoicePdfRenderer.cs:44`; `GenerateInvoiceCommand.cs:37-38,156` | Tax profile per vertical (SA-VERT-004) |
| Notifications | P | Y | N | P | Y | `NotificationChannelPreferencePolicy.cs:43-60` | Vertical-driven template mapping (SA-VERT-006) |
| Reporting / analytics | P | Y | P | P | Y | `phase2_slice_i_matview_registry.sql:40-51` (warehouse throughput tagged laundry) | Salon/marketplace KPIs |
| Item tracking / processing / QC | P (neutral feature `processing_facility`) | Y | n/a | n/a | n/a | `Warehouse/**` endpoints; `0005...up.sql:112-115` | Order-mode guard (SA-VERT-005) |
| Inventory (stock-on-hand) | N | n/a | n/a | N | n/a | grep `stock_quantity|on_hand|inventory` → only garment tag inventory | Product inventory |
| Terminology | P | Y | P (admin access-control screens only) | N | P | `0010_vertical_terminology.up.sql`; `GetTerminology.cs`; admin `useTerminology` only in access-control pages | Wiring in POS/customer/rider apps (SA-VERT-007) |
| Entitlements / bundles | Y | Y | P (`salon-starter` bundle) | N | Y | `ApplyBundleToBrand.cs:29-46`; `phase4_salon_pack.sql:30-40` | — |
| Onboarding templates | Y | Y | P (public but not operable) | N | Y | `0011...up.sql:62-86`; `TemplateProvisioner.cs` | Gate templates on operability |
| Client apps | P | Y | N | N | Y (customer parcel flow, rider) | `customer-mobile/app/(app)/(tabs)/_layout.tsx:68-178` | Vertical feature packs |

## Hypothesis matrix validated against code

| Vertical | Dimension | Hypothesis | What the code does | Verdict |
|---|---|---|---|---|
| Laundry | Catalog/pricing | item × service × fabric/variant, express, weight/value slabs | Exactly this: `PriceResolver` + `ValueSlabResolver` + fabric module (laundry-tagged) + `IsExpress`/TAT | Confirmed |
| Laundry | Scheduling | pickup/delivery slots with capacity | `delivery_slots` (`slot_type IN ('pickup','delivery')`, `UNIQUE(store_id,slot_date,slot_start,slot_type)`), atomic `booked_count < capacity` UPDATE | Confirmed |
| Laundry | Primary resource | processing facility + riders | warehouse/fulfillment_unit/QC + rider dispatch | Confirmed |
| Laundry | Fulfillment | collect → process → QC → deliver | `LaundryProcessStrategy` 19-status graph incl. rewash | Confirmed |
| Laundry | Constraints | TAT, QC, garment tagging | `TatCalculator`, `quality_checks`, `fulfillment_unit_tags` | Confirmed |
| Salon | Catalog/pricing | service with duration, staff-tier price | `catalog_kind='service'` only; `Service` has TAT hours, no duration; no tier pricing | **Not supported** |
| Salon | Scheduling | slot = staff × time × duration, no overlap | Not implemented. Only a schema without overlap constraint; slots are pickup/delivery | **Not supported** |
| Salon | Primary resource | staff + chairs/rooms | `staff_members`, `resources` tables with no code | Schema only |
| Salon | Fulfillment | booked → checked-in → in-service → completed, no-show | `SalonAppointmentStrategy` exists but no order is ever created in `appointment` mode | Unreachable |
| Salon | Constraints | operating hours, breaks, buffers, cancellation window | `OperatingHour` entity unused; `CanCustomerCancel` rule only | **Not supported** |
| Hyperlocal marketplace | Catalog/pricing | multi-seller products, stock | `CatalogKind.Product` exists; no seller entity, no stock-on-hand | **Not supported** |
| Hyperlocal marketplace | Scheduling | delivery ETA/slots | delivery slots reusable | Partial |
| Hyperlocal marketplace | Primary resource | inventory + riders | riders only | Partial |
| Hyperlocal marketplace | Fulfillment | pick → pack → deliver | closest is `recurring` (no pickup) or `point_to_point`; no "store-originated one-off delivery" mode | **Not supported** |
| Hyperlocal marketplace | Constraints | stock reservation, seller commission | none | **Not supported** |
| Logistics | Catalog/pricing | distance/vehicle-tier fare | signed `FareQuoteToken` with vehicle tier | Confirmed |
| Logistics | Scheduling | on-demand + optional slot | on-demand; slots reusable | Partial |
| Logistics | Primary resource | riders/vehicles | rider capacity config, assignments, partner dispatch | Confirmed |
| Logistics | Fulfillment | pickup → in transit → delivered, POD | `LogisticsPointToPointStrategy` + POD photo/OTP | Confirmed |
| Logistics | Constraints | geofence, COD, route | geofence + COD yes; route optimisation not found | Partial |
| Tiffin (recurring) | Scheduling | repeating calendar → occurrences | `DeliveryCadence` + `delivery_schedules` table; no generator | Schema/calc only |
| Tiffin (recurring) | Fulfillment | outbound delivery only | `RecurringDeliveryStrategy` (unreachable) | Unreachable |

## Hardcoded vertical / laundry assumptions (with paths)

**Explicit vertical branches.** These are the only ones found; this is a small number.
1. `backend/laundryghar/core.Application/Identity/Signup/TemplateProvisioner.cs:146-152` — `switch` over VerticalKey → CatalogKind. It duplicates `CatalogKind.DefaultFor` (`Enums/CatalogKind.cs:32-37`), which **lacks tiffin**, so tiffin falls back to `laundry_garment`. The two copies diverge.
2. `backend/laundryghar/laundryghar.SharedDataModel/Enums/FulfillmentMode.cs:30-36` — `DefaultFor(vertical)`. It is correct but has no callers.
3. `backend/laundryghar/core.Application/Identity/TenancyOrg/Terminology/GetTerminology.cs:31` — `DefaultVertical = "laundry"`.
4. `admin-web/src/lib/verticalTerms.ts:35-71` — per-vertical noun maps; `onsiteUserType` special-cases laundry.

**JobType/parcel branches used in place of a vertical policy:**

5. `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:60,215,591,620-622,906-923` — `isParcel` decides mode, minimum-order rule, fare and validation. Everything else is laundry.
6. `operations.Application/Fulfillment/FulfillmentStrategyResolver.cs:36-40` — the JobType fallback.
7. `admin-web/src/lib/fulfillment.ts:19-23`, `pos-web/src/lib/fulfillment.ts:20`, `customer-mobile/src/lib/fulfillmentTracking.ts:23`, `rider-mobile/src/lib/fulfillmentTracking.ts:21` — `jobType === 'parcel'`; any other value is treated as laundry `process_deliver`.

**Laundry defaults in shared entities and DTOs:**

8. `SharedDataModel/Entities/OrderLifecycle/Order.cs:39,44,48` — `JobType = "laundry"`, `VerticalKey = Laundry`, `FulfillmentMode = ProcessDeliver`.
9. `SharedDataModel/Entities/TenancyOrg/Brand.cs:16` — `VerticalKey = Laundry`.
10. `SharedDataModel/Entities/CustomerCatalog/Item.cs:16` — `CatalogKind = LaundryGarment`.
11. `operations.Application/Catalog/Catalog/Commands/Item/ItemCommands.cs:38` — new items default to `laundry_garment` whatever the brand's vertical.
12. `operations.Application/Orders/Orders/Dtos/OrderDtos.cs:35,149` — `JobType = "laundry"`. `OrderDto` exposes neither `FulfillmentMode` nor `VerticalKey`.

**Laundry business rules inside shared handlers:**

13. `operations.Application/Orders/Invoices/InvoiceTaxCalculator.cs:18` — `DefaultSacCode = "999712"`.
14. `operations.Application/Orders/Invoices/InvoicePdfRenderer.cs:44` — footer reads "Laundry & Dry-Cleaning Services".
15. `operations.Application/Orders/Invoices/Commands/GenerateInvoiceCommand.cs:37-38,156` — billable statuses are `ready/delivered/closed`; the SAC is always the default.
16. `SharedDataModel/Entities/OrderLifecycle/Invoice.cs:14,47` — documents SAC 999712 as the model.
17. `commerce.Infrastructure/Worker/Channels/NotificationChannelPreferencePolicy.cs:43-60` — template switch on laundry/logistics status literals. The DB catalog `engagement_cms.notification_event_catalog` (`phase2_slice_j…sql`) is never read by code.
18. `CreateOrderCommand.cs:194` — `UnitOfMeasure = "piece"`. Lines `:217-223,264-265` — TAT promised-delivery and express surcharge are applied to every non-parcel order.
19. `SharedDataModel/Entities/CustomerCatalog/Service.cs` — `BaseTatHours`, `ExpressTatHours`, `ExpressMultiplier`, `RequiresInspection`, `RequiresQc`; no duration.
20. `operations.Application/Warehouse/Garments/Commands/GenerateTags/GenerateTags.cs:42` — tag prefix `LG-`.

**Laundry-shaped client logic:**

21. `admin-web/src/pages/orders/orderStatus.ts:1-60` — a hardcoded copy of the laundry `AllowedTransitions`. `OrderDetailDrawer.tsx:504` uses it and ignores the backend's `allowedTransitions`.
22. `customer-mobile/app/(app)/(tabs)/_layout.tsx:68-178` — every brand's FAB offers "Laundry" and "Parcel" flows.
23. `admin-web/src/App.tsx:73,92` — `warehouse/board` and `catalog/fabrics` routes; no appointment route.
24. `pos-web/src/components/print/GarmentTags.tsx` — garment tags.

## Findings

### SA-VERT-001 — Order creation ignores the brand's vertical; every non-parcel order is a laundry `process_deliver` order stamped `vertical_key='laundry'`
- Category: Domain model / multi-vertical seam
- Severity: High
- Status: Verified
- Evidence:
  - `backend/laundryghar/operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:51-60` — `isParcel` comes from `req.JobType`. `:620-627` — mode is `PointToPoint` or `ProcessDeliver`, nothing else. `:629-647` — `Order` is built without setting `VerticalKey`. The comment at `:643-646` admits "Phase 2 sets it explicitly from Brand.VerticalKey once multiple verticals coexist".
  - `Order.cs:44,48` — the entity defaults are `Laundry` and `ProcessDeliver`.
  - `JobType.cs` — `All = {laundry, parcel}`.
  - grep shows `FulfillmentMode.DefaultFor` and `CatalogKind.DefaultFor` have no callers.
- Observed behaviour: A salon or tiffin brand (both creatable through public signup) that places an order through `POST /orders` gets a laundry state machine (`placed → pickup_scheduled → … → sorting → qc …`), a TAT-based promised delivery date, and `orders.vertical_key='laundry'`. `SalonAppointmentStrategy` and `RecurringDeliveryStrategy` are registered but unreachable from any creation path.
- Reproduction / verification method: Static trace of `CreateOrderHandler.HandleAsync`. To confirm at runtime: sign up with `templateKey=salon`, create a catalog price, `POST /api/v1/orders`, then `SELECT vertical_key, fulfillment_mode, status FROM order_lifecycle.orders`. I did not run this (no .NET/DB).
- Impact: Breaks the "one brand = one vertical" promise at the data layer. Vertical-keyed reporting (`orders.vertical_key` index in `phase0_multi_vertical.sql:72`) mislabels orders. Salon and tiffin tenants run laundry workflows.
- Recommended remediation (smallest safe change): Load `brand.VerticalKey` in `CreateOrderHandler` and set `order.VerticalKey` from it. Derive the mode from a single policy: `JobType.Parcel → point_to_point`, otherwise `FulfillmentMode.DefaultFor(brand.VerticalKey)`. Reject a mode the brand's vertical does not allow (for example `process_deliver` on a salon brand) with a 422. Add a DB trigger or CHECK that `orders.vertical_key` equals the brand's vertical.
- Regression tests required: Handler tests asserting a salon brand gets `appointment`/`booked`, a laundry brand gets `process_deliver`, and parcel on a laundry brand gets `point_to_point` with `vertical_key='laundry'`. A parity test that laundry behaviour is unchanged.
- Dependencies / priority: P1. Blocks any salon or tiffin onboarding.
- Prior-doc cross-ref: `MULTI_VERTICAL_BLUEPRINT.md` §2.1 (backfill plan); `GAP_ANALYSIS.md` M4 ("Done") overstates this.

### SA-VERT-002 — Salon and tiffin templates are publicly sellable at signup, but neither vertical is operable
- Category: Product readiness / onboarding
- Severity: High
- Status: Verified
- Evidence:
  - **Templates.** `db/migrations/0011_vertical_templates.up.sql:41,71-73` — salon template `is_public` defaults to true. `db/migrations/0012_recurring_fulfillment_mode.up.sql:155-156` sets tiffin `is_public = true`. `CompleteSignup.cs:73-75` and `GetSignupTemplates.cs:29` offer any public template.
  - **Salon.** `phase4_salon_fulfillment_schema.sql` creates the tables, but there are no EF entities, handlers or endpoints (grep `salon_fulfillment|StaffMember|ResourceBooking` in `backend/`). The appointments module routes to `/appointments` with `appointment.manage` (`phase4_salon_pack.sql:19-25`). `admin-web/src/App.tsx:87-116` has no such route (`*` redirects to `/`). `appointment.manage` is not seeded in `IdentitySeeder.cs` or any SQL.
  - **Tiffin.** `delivery_schedules`/`delivery_schedule_occurrences` (`0012…:49-145`) have no entity, generator, endpoint or client reference (grep). `DeliveryCadence.cs` is referenced only by `DeliveryCadenceTests.cs`.
- Observed behaviour: A provider can sign up as a salon or tiffin business and is provisioned (features, seeded catalog, terminology). It then has no way to take an appointment or set up a recurring delivery. The only order path is the laundry one (SA-VERT-001), and the salon "Appointments" menu item leads back to the dashboard (and is visible only to platform admins, since nobody holds `appointment.manage`).
- Reproduction / verification method: Static read of migrations, signup handler, routes and grep. Runtime signup not executed.
- Impact: Revenue and reputation risk: a paying tenant is sold a product that does not work. 0012 also contradicts 0011's own stated gate ("Listing an unrunnable template … would be worse than not offering it", `0011…:76-79`).
- Recommended remediation: Immediately set `is_public=false` for `salon` and `tiffin` in a new migration, and tie public visibility to an operability checklist (creation path, booking API, client screen). Then build each vertical as its own feature module (see the module-boundary recommendation below).
- Regression tests required: A test that `GetSignupTemplates` returns only templates whose fulfillment mode has a creation path. An E2E signup → first booking test per public template.
- Dependencies / priority: P0 for the visibility flip; P1/P2 for building the verticals.
- Prior-doc cross-ref: `GAP_ANALYSIS.md` M3 "Missing" and V4 "Missing" are stale (0011/0012 exist), but their underlying point that the vertical is not operable still holds. `phase4_salon_pack.sql:6-8` claims "salon becomes a fully entitleable vertical", which is contradicted.

### SA-VERT-003 — No appointment-grade scheduling: no staff availability, service duration, operating-hours enforcement or double-booking protection
- Category: Scheduling / capacity
- Severity: High (for the salon/appointment target); Informational for laundry
- Status: Verified
- Evidence:
  - `database_scripts/04_bc4_order_lifecycle.sql:347-365` — `delivery_slots.slot_type IN ('pickup','delivery')`, an integer `capacity`, `UNIQUE(store_id, slot_date, slot_start, slot_type)`. There is no staff or resource dimension and no duration.
  - `PickupCommands.cs:399-414` — an atomic `booked_count < capacity` increment (counter only).
  - `phase4_salon_fulfillment_schema.sql:52-84` — `appointments(staff_member_id, scheduled_start, scheduled_end)` with only a non-unique index `idx_appointments_staff_slot` (`:72`). `resource_bookings(booked_from, booked_to)` has no `EXCLUDE` constraint. `appointments.order_id` has no FK, despite the header comment at `:10-11` claiming a composite FK.
  - `OperatingHour.cs` and `Holiday.cs` are mapped in EF but used by no handler (grep: only entity, config and DbContext files).
  - `Service.cs` has TAT hours, not a duration in minutes.
- Observed behaviour: The platform can cap how many pickups go into a store's time window. It cannot answer "is stylist X free from 15:00 to 15:45", cannot block overlapping bookings of the same staff member or chair, and does not enforce opening hours or holidays on any booking.
- Reproduction / verification method: Schema and code read; grep for consumers.
- Impact: The salon (and any booking-based service) value proposition cannot be met. If appointment rows were written as-is, double-booking would be unprevented at both the DB and the code level.
- Recommended remediation: When salon is built, use `tstzrange` with `EXCLUDE USING gist (staff_member_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&) WHERE status NOT IN ('cancelled','no_show')` (requires `btree_gist`), and the same on `resource_bookings`. Add a small availability service that composes operating hours, holidays, staff shifts and existing bookings. Add `duration_minutes` to the service catalog. Add the missing FK to `orders(id, created_at)`.
- Regression tests required: Concurrent double-booking test (two transactions, same staff, overlapping range: one must fail). Operating-hours rejection test. Holiday rejection test.
- Dependencies / priority: P2 (only needed once salon is pursued; gate salon behind SA-VERT-002 until then).
- Prior-doc cross-ref: `PLATFORM_STRATEGY.md` §2 says "Orders + delivery slots + capacity → Bookings + scheduling (any vertical)". The slot model does not support that claim for appointments.

### SA-VERT-004 — Invoices hardcode laundry tax identity (SAC 999712, "Laundry & Dry-Cleaning Services") and laundry billable statuses for every vertical
- Category: Compliance / shared-service coupling
- Severity: High
- Status: Verified
- Evidence:
  - `operations.Application/Orders/Invoices/InvoiceTaxCalculator.cs:18` sets `DefaultSacCode = "999712"`.
  - `GenerateInvoiceCommand.cs:156` sets `SacCode = InvoiceTaxCalculator.DefaultSacCode` for every order. `:37-38` limits billable statuses to `ready/delivered/closed`.
  - `InvoicePdfRenderer.cs:44` prints `SAC {code} — Laundry & Dry-Cleaning Services` on every PDF.
  - `Invoice.cs:14,47`.
- Observed behaviour: A courier (logistics) brand, which IS operable through the public `courier` template, gets GST invoices for delivered parcel orders that declare laundry SAC 999712 and the laundry service description. A salon order in `completed` could never be invoiced, because `completed` is not billable.
- Reproduction / verification method: Static read; no branch on vertical or mode exists in `GenerateInvoiceHandler`.
- Impact: Incorrect GST tax documents for non-laundry tenants (a compliance and legal exposure in India). The blueprint already planned a per-strategy `TaxProfile` (`MULTI_VERTICAL_BLUEPRINT.md` §2.2), but it was never built.
- Recommended remediation: Add `TaxProfile` (SAC + description) to `IFulfillmentStrategy`, or a per-vertical row in config with brand override. Make billable statuses `LifecycleState ∈ {completed, closed}` plus the explicit laundry `ready` prepaid case, instead of laundry literals.
- Regression tests required: Invoice tests per mode: laundry gets 999712, point_to_point gets the configured courier SAC, salon `completed` is billable.
- Dependencies / priority: P1 for logistics tenants already onboardable.
- Related area: Finance/Compliance.

### SA-VERT-005 — The vertical boundary is enforced only in navigation and bundle application, not at the API or aggregate level
- Category: Authorization / module boundary
- Severity: Medium
- Status: Partially Verified. Code verified; runtime `permissions.module_key` mapping inferred from SQL.
- Evidence:
  - **Token filter.** `core.Application/Identity/Auth/Common/ScopeResolver.cs:173-246` keeps permissions whose module's *feature* is entitled. It has no vertical check. `PermissionHandler.cs:17-56` checks only the claim.
  - **Feature mapping.** `0005_split_features_from_modules.up.sql:66-71,112-115`: `warehouse` module → `processing_facility` feature with `vertical_key NULL`. `seed_navigator_modules.sql:42`: the warehouse module owns `{warehouse,fulfillment,qc,stockrecon,…}` permissions. `0007_align_plan_tiers.up.sql:88-120`: `pro`/`enterprise` include `processing_facility`.
  - **Manual grants.** `SetBrandFeature.cs:20-40` grants any active feature with no vertical check (unlike `ApplyBundleToBrand.cs:29-46`). `GrantMembership.cs:58-80` checks the role's *feature*, not `role.VerticalKey`. Role vertical filtering exists only in the list queries (`GetAccessRoles.cs:56`, `GetRoles.cs:37`).
  - **Aggregate.** `CreateGarment.cs:35-57` attaches a fulfillment unit to any order item in the brand without checking `order.FulfillmentMode`/`RequiresStoreDrop`.
- Observed behaviour: A salon or logistics brand on a tier that includes `processing_facility`, or a brand given a laundry-only feature (for example `fabrics`) via `SetBrandFeature`, holds laundry warehouse/QC/fabric permissions in its token and can call those endpoints, even though the navigator hides the modules. A laundry-only role (`warehouse_staff`) can be granted on a salon brand by crafting the request. Tenant isolation is NOT affected: everything stays brand-scoped by RLS and explicit `BrandId` filters.
- Reproduction / verification method: Static trace of the token mint → endpoint policy chain. Runtime confirmation would need a DB with a salon brand on `pro`, followed by `GET /api/v1/warehouse/fulfillment-units/board`. Not run.
- Impact: The sidebar and the API disagree (`GetNavigator.cs:51-53` claims they "can never disagree", which holds for entitlement but not for the vertical gate). Laundry data can be created against non-laundry orders. The vertical boundary is cosmetic.
- Recommended remediation:
  - (a) Apply `VerticalKey.IsAvailableTo(module.VerticalKey, brand.VerticalKey)` in the `ScopeResolver` entitlement block, mirroring `GetNavigator`.
  - (b) Add the vertical check to `SetBrandFeature` and to `GrantMembership` (`role.VerticalKey`).
  - (c) Guard laundry warehouse commands with `strategy.RequiresStoreDrop` on the owning order.
  - (d) Decide product-wise whether `processing_facility` is truly neutral (tailor, shoe repair) or should be laundry-tagged for now.
- Regression tests required: A token-mint test that a salon brand's token excludes `fulfillment.*` and `fabric.*` even when the feature is enabled. `GrantMembership` rejects a laundry role on a salon brand. `CreateGarment` rejects a `point_to_point` or `appointment` order.
- Dependencies / priority: P2.
- Related area: AUTHZ / ENT.

### SA-VERT-006 — Notification templates are a hardcoded laundry/logistics status switch; the vertical-tagged event catalog is never read
- Category: Shared service coupling
- Severity: Medium
- Status: Verified
- Evidence:
  - `commerce.Infrastructure/Worker/Channels/NotificationChannelPreferencePolicy.cs:43-60` hardcodes `(event, status)` → template. It covers `pickup_scheduled`, `picked_up`, `ready`, `out_for_delivery`, `delivered`, `fulfillment.lost`, and so on.
  - It is called from `NotificationMappingService.cs:214`.
  - `db/patches/phase2_slice_j_notification_event_catalog.sql:28-49` creates `engagement_cms.notification_event_catalog` with a `vertical_key`. grep shows no C# reader.
- Observed behaviour: Salon statuses (`booked`, `confirmed`, `checked_in`, `completed`, `no_show`) map to `null`, so no customer notification is sent. Adding a vertical requires a code change in the commerce worker. The DB catalog drifts silently from code.
- Reproduction / verification method: Static read and grep.
- Impact: Every new vertical needs a code deploy for notifications, which contradicts "config, not code" (`PLATFORM_STRATEGY.md` §3).
- Recommended remediation: Have `ResolveTemplate` read the catalog (cached), keeping the switch as a fallback. Add salon/recurring rows.
- Regression tests required: Template-resolution tests per mode, driven by catalog rows.
- Dependencies / priority: P2.

### SA-VERT-007 — Clients are laundry-shaped and only superficially vertical-aware; the order DTO does not expose the mode
- Category: Client architecture
- Severity: Medium
- Status: Verified
- Evidence:
  - **Admin web — transitions.** `admin-web/src/pages/orders/orderStatus.ts:1-60` hardcodes the laundry transition map ("Client-side mirror of … OrderStateMachine"). `OrderDetailDrawer.tsx:504` renders status buttons from it via `advanceableTargets(order.status)`. By contrast, POS uses the backend's `order.allowedTransitions` (`pos-web/src/pages/orders/OrderDetailPage.tsx:50`), which the backend populates from the strategy (`OrderDtos.cs:131-137`).
  - **Admin web — mode list.** `admin-web/src/lib/fulfillment.ts:9-13` has no `recurring`.
  - **Order DTO.** `OrderDtos.cs:35,149` exposes `JobType` but not `FulfillmentMode` or `VerticalKey`, so all four clients infer the mode from `jobType === 'parcel'`.
  - **Customer mobile.** `customer-mobile/app/(app)/(tabs)/_layout.tsx:68-178` offers laundry and parcel to every brand. The terminology lib exists in each client, but outside tests it is used only by admin access-control screens (grep for `useTerminology|termFrom`).
- Observed behaviour: The admin drawer shows laundry actions (for example `picked_up → received`) on parcel orders, which the backend then rejects with 422. A courier brand's customer app shows a "Laundry" booking option.
- Reproduction / verification method: Static read. UI not driven.
- Impact: Terminology leakage (the risk `PLATFORM_STRATEGY.md` §12 itself names), operator confusion, and failed actions. Every new vertical requires edits in four apps.
- Recommended remediation: Expose `fulfillmentMode` and `verticalKey` on `OrderDto`. Switch the admin drawer to `allowedTransitions`, as POS already does. Drive the customer FAB options from the brand's vertical and modes. Wire the server terminology pack into customer, rider and POS.
- Regression tests required: Admin drawer test that a parcel order shows the backend-provided targets. Customer-mobile test that a logistics brand shows no laundry option.
- Dependencies / priority: P2.
- Related area: CLIENT.

### SA-VERT-008 — Catalog discriminator is inert, and the service model is laundry-shaped
- Category: Domain model
- Severity: Low
- Status: Verified
- Evidence:
  - `Item.cs:14-16` and `ItemConfiguration.cs:18` store `catalog_kind`, but nothing in backend or clients branches on it (grep `\.CatalogKind\b` and `catalogKind`).
  - `ItemCommands.cs:38` defaults new items to `laundry_garment` whatever the brand's vertical.
  - `CatalogKind.DefaultFor` (`CatalogKind.cs:32-37`) omits tiffin, whereas `TemplateProvisioner.CatalogKindFor` (`:146-152`) maps tiffin → `product`.
  - `Service.cs` carries TAT/express/QC flags and no duration.
  - `CreateOrderCommand.cs:194` hardcodes `UnitOfMeasure = "piece"`.
- Observed behaviour: A salon item created in admin is labelled `laundry_garment`. The vertical-to-kind mapping exists twice and disagrees.
- Reproduction / verification method: Static read.
- Impact: Data quality; future per-kind logic would misbehave on existing rows.
- Recommended remediation: Use one `CatalogKind.DefaultFor` (add tiffin), call it from `ItemCommands` using the brand's vertical, and delete the duplicate in `TemplateProvisioner`.
- Regression tests required: Unit test of `DefaultFor` for all four verticals. Item creation on a salon brand yields `service`.
- Dependencies / priority: P3.

### SA-VERT-009 — Only self-signup can set a brand's vertical; platform-admin brand creation always yields a laundry brand with no template provisioning
- Category: Onboarding (Q5)
- Severity: Low
- Status: Verified
- Evidence:
  - `core.Application/Identity/TenancyOrg/Brands/Commands/CreateBrand/CreateBrand.cs:24-43` never sets `VerticalKey`, so the entity default `laundry` applies (`Brand.cs:16`).
  - `CreateBrandRequest` (`BrandDtos.cs:9-18`) has no vertical or template field.
  - `UpdateBrandRequest` (`BrandDtos.cs:20-27`) has none either, which is correct for immutability.
- Observed behaviour: A sales-led provider created by a platform admin is always laundry. Its vertical can only be changed by SQL, and only before it has orders (trigger at `phase0_multi_vertical.sql:75-97`).
- Reproduction / verification method: Static read.
- Impact: The "exactly one primary vertical at onboarding" rule is enforced, but only one of the two onboarding paths can choose it.
- Recommended remediation: Accept `templateKey` (or `verticalKey`) on admin `CreateBrand` and reuse `TemplateProvisioner`.
- Regression tests required: Admin create with `verticalKey=logistics` persists it and provisions the template.
- Dependencies / priority: P3.

### SA-VERT-010 — Multi-vertical schema lives only in `db/patches/phase*.sql`, which the documented fresh-build path does not apply
- Category: Schema reproducibility
- Severity: Medium
- Status: Partially Verified (static read; not executed)
- Evidence:
  - `brands.vertical_key`, `orders.vertical_key/fulfillment_mode` come from `db/patches/phase0_multi_vertical.sql:29-47`; `modules.vertical_key` from `phase2_slice_b_fabric_module.sql:26`.
  - `database_scripts/*.sql` contain no `vertical_key` (grep) and neither does `docs/SCHEMA_FULL.sql` (grep count 0).
  - `db/build_from_scratch.sh` stages 1-7 run `apply_schemas.sh`, `apply_patches.sh` (FK patches only, `apply_patches.sh:35-44`), triggers, discriminators, auth lineage, RLS and partman. No `phase*` patch is applied.
  - Migrations `0005` (`m.vertical_key` at `:93`) and `0008` (`:41,59`) assume these columns exist.
- Observed behaviour: A fresh environment built by the documented script, followed by `migrate.sh up`, would lack the vertical columns (and the salon schema), so 0005 would fail. Unverified at runtime.
- Impact: Multi-vertical capability is not reproducible from source. Each environment depends on hand-applied patches.
- Recommended remediation: Fold the `phase0`/`phase1`/`phase2`/`phase4` patches into a baseline migration, or into `build_from_scratch.sh` in a deterministic order. Regenerate `SCHEMA_FULL.sql`.
- Regression tests required: CI job: empty DB → build_from_scratch → migrate up → EF model validation.
- Dependencies / priority: P1.
- Related area: DB (dedupe with the DB specialist).

### SA-VERT-011 — Documentation contradicts code on multi-vertical status
- Category: Documentation drift
- Severity: Informational
- Status: Verified
- Evidence:
  - `docs/MULTI_VERTICAL_BLUEPRINT.md` §1, item 1 says "No discriminator exists … no `IFulfillmentStrategy` anywhere". It is contradicted by `VerticalKey.cs` and `Fulfillment/**`.
  - `VerticalKey.cs:17-20` says tiffin is "NOT yet operable … blocked on OQ-2"; `FulfillmentMode.cs:19-22` and `0012` say OQ-2 is resolved.
  - `GAP_ANALYSIS.md` M3 and V4 say "Missing", which is stale (0011/0012 exist). Its M4 "Done" omits that only two modes can be created.
  - `phase4_salon_pack.sql:6-8` claims "salon becomes a fully entitleable vertical"; `phase4_salon_fulfillment_schema.sql:10-11` claims a composite FK that is not declared.
- Impact: Planning decisions made on stale status.
- Recommended remediation: Add a single "vertical readiness" table in `docs/` that lists, per vertical, the creation path, booking API, client screens and tax profile, and update it whenever one of those changes.
- Dependencies / priority: P3.

## Positive controls verified

- **Single vertical per brand, immutable once trading.** `Brand.VerticalKey` is NOT NULL with a CHECK. `trg_brand_vertical_immutable` (`phase0_multi_vertical.sql:75-97`). No API mutates it (`UpdateBrand.cs:20-26`).
- **Clean strategy seam for state transitions.** One `StateMachineStrategyBase` (`:11-103`). Per-mode graphs. A resolver with a safe fallback. Cancel and status update go through the resolved strategy (`CancelOrderCommand.cs:49-60`). Salon maps its private vocabulary to the neutral `OrderLifecycleState` (`SalonAppointmentStrategy.cs:35-43`). There are unit tests (`SalonStrategyTests` 11, `RecurringStrategyTests` 8, `FulfillmentStrategyParityTests` 27, `DeliveryCadenceTests` 12 facts/theories; read, not run).
- **Laundry processing isolated in its own schema.** EF configs map the 11 tables to `laundry_fulfillment`, and `Order.Garments` navigation was severed, per the Slice C design.
- **Terminology is data, not code.** `identity_access.vertical_terms` has a completeness assertion (`0010…:58-108`), served by `GetTerminology.cs`.
- **Template-driven onboarding is transactional and vertical-aware.** `CompleteSignup.cs` wraps brand, owner, features and catalog in one transaction. `TemplateProvisioner.cs:53-79` filters bundle features by vertical.
- **Bundle application respects vertical.** `ApplyBundleToBrand.cs:29-46` rejects a vertical-specific bundle on another vertical and drops vertical-tagged features.
- **Pickup-slot overbooking protection.** An atomic conditional `UPDATE … WHERE booked_count < capacity` inside a transaction (`PickupCommands.cs:395-414`), plus a unique slot key (`04_bc4_order_lifecycle.sql:361`).
- **Recurring-delivery idempotency design.** `delivery_schedule_occurrences` PK `(schedule_id, scheduled_for)` correctly works around partitioned-table unique-index limits (`0012…:105-145`). It is not wired to anything yet.
- **Logistics vertical works end-to-end** on the shared spine: fare quote, point-to-point strategy, rider proof of delivery, COD, partner dispatch.
- **Tenant isolation is unaffected by the vertical issues.** Every path read filters by `BrandId` (for example `CreateOrderCommand.cs:97-115`, `CreateGarment.cs:47-54`), and new salon and recurring tables carry brand RLS.

## Open questions / not verified

1. The runtime contents of `permissions.module_key` for `fulfillment.*`/`qc.*`/`fabric.*`, and which features `salon-starter` actually maps to after 0005's `module_bundle_item → bundle_feature` conversion (`0005…:189-191`). These determine the practical reach of SA-VERT-005. Needs a live DB.
2. Whether any deployed environment has the `phase4_salon_*` patches applied.
3. Where the signup UI lives. `/api/v1/signup/*` is anonymous (`core.WebApi/Endpoints/Identity/*Signup*.cs:24-32`), but none of the four clients calls it; a marketing site may.
4. Whether a "hyperlocal marketplace" means many sellers under one brand (no seller model exists) or a brand per seller (works today, apart from inventory). This is a product decision; I made no recommendation beyond the gaps listed.
5. Route optimisation and multi-drop batching for logistics. I found only `DeliveryAssignment.SequenceNumber` and did not search further.
6. No tests were executed (.NET SDK unavailable).

## Module-boundary recommendation (proportionate)

Keep the **modular monolith and the shared order spine**. The existing seams (`Brand.VerticalKey` + `FulfillmentMode` strategies + per-vertical private schemas + feature entitlements) are the right shape, and nothing here justifies microservices, a plugin framework or database-per-vertical. The problem is that the seam is wired at only one point (state transitions) and bypassed at creation, tax, notifications, authorization and in the clients. The smallest changes, in order:

1. **Stop selling what does not run.** Set `is_public=false` for salon and tiffin in a new migration (SA-VERT-002).
2. **Add one vertical policy.** A small `VerticalPolicy` (or a column on `vertical_templates`) lists the allowed `FulfillmentMode`s per vertical, plus the default. `CreateOrderHandler` sets `VerticalKey` from the brand and the mode from this policy (SA-VERT-001).
3. **Widen the strategy contract only where shared handlers branch today:**
   - `TaxProfile` (SA-VERT-004);
   - billable/lifecycle checks via `LifecycleState`;
   - the notification catalog (SA-VERT-006).

   Do not add hooks speculatively.
4. **Enforce the vertical gate where tokens are minted.** Apply it in `ScopeResolver`, in `SetBrandFeature` and in `GrantMembership`, and add a mode guard on laundry warehouse commands (SA-VERT-005).
5. **Build each new vertical as a feature module inside `operations`:** its own folder (`Fulfillment/Salon/*` with Domain, Application, Endpoints), its own schema (already in place), its own permission pack and nav module. It talks to the spine only through `orders` plus the strategy. For salon, that means:
   - an availability service;
   - an appointment booking command that creates the order in `appointment` mode and writes `salon_fulfillment.appointments` in the same transaction;
   - `EXCLUDE` constraints against double-booking (SA-VERT-003).

   For tiffin, it means:
   - a `DeliverySchedule` entity, an API, and a background generator using `DeliveryCadence` and the occurrence ledger.
6. **Make clients mode-driven, not jobType-driven:**
   - expose `fulfillmentMode`/`verticalKey` on `OrderDto`;
   - use the backend's `allowedTransitions`;
   - use the `fulfillment-config` stages and the terminology pack (SA-VERT-007).

## Verdict inputs

- **Q5 — exactly one primary business type at onboarding (domain view): Partially Supported.**
  - Supported: one immutable `Brand.VerticalKey`, chosen from a template at self-signup.
  - Not supported: admin creation cannot choose it (SA-VERT-009), orders ignore it (SA-VERT-001), and two of the four selectable templates cannot operate (SA-VERT-002).
- **Q6 — vertical-specific behaviour isolated, not tightly coupled: Partially Supported.**
  - A strategy registry and separate schemas exist, and there are few explicit `if vertical` branches.
  - However, shared handlers embed laundry rules (invoice SAC and billable statuses, notifications, TAT/express, `piece`), creation bypasses the seam, and clients hardcode laundry state machines (SA-VERT-001/004/006/007/008).
- **Q7 — one vertical cannot reach another's functionality: Partially Supported.**
  - Navigation, bundles and provisioning are gated by vertical.
  - The token-side filter, manual feature grants, role grants and laundry aggregates have no vertical check (SA-VERT-005). Cross-tenant isolation is not affected.
- **Q10 — scheduling, capacity, availability and fulfillment rules for target verticals:**
  - Laundry and logistics: **Partially Supported.** Pickup-slot capacity, dispatch and proof of delivery work; operating hours and holidays are unenforced.
  - Salon / appointments: **Not Supported.** There is no availability, duration or double-booking protection, and no booking path (SA-VERT-003).
  - Recurring (tiffin): **Not Supported.** Schema and date calculator only; no generator.
  - Marketplace: **Not Supported.** No inventory and no seller model.
