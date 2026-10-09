# 05 — OOP and SOLID Compliance Report

LaundryGhar SaaS audit · Report 5 of the consolidated deliverables · 2026-10-09

## Summary

- **Q14 (OOP/SOLID in material areas): Partially Supported.** The structural design is sound. The project graph is acyclic and points in the right direction. Handlers depend on interfaces. Ports and adapters are used for payments, channels, OTP and storage. The `IFulfillmentStrategy` seam is a good Strategy/Template-Method implementation.
- That design breaks down where the money and order-lifecycle rules live. All 143 entities are anemic, mutable and string-typed (3,556 public `{ get; set; }`, 0 behaviour methods). Invariants therefore sit in transaction-script handlers and are copied:
  - 5 order-status writers;
  - 3 coupon implementations;
  - 2 dispatch paths;
  - 2 payment-state writers, with online capture writing none.
- Those copies have already drifted into verified defects: [SA-SOLID-001](../../FINDINGS.md#sa-solid-001) (High), [SA-SOLID-003](../../FINDINGS.md#sa-solid-003) (High), [SA-API-007](../../FINDINGS.md#sa-api-007) (High, dup SA-SOLID-002), [SA-SOLID-006](../../FINDINGS.md#sa-solid-006) and [SA-SOLID-011](../../FINDINGS.md#sa-solid-011).
- **OCP is the weakest principle for the SaaS target.** The vertical seam exists but is bypassed. Order creation, invoicing, rating, notifications and the admin-web transition table are laundry-coded ([SA-VERT-001](../../FINDINGS.md#sa-vert-001), dup SA-SOLID-004; [SA-FE-004](../../FINDINGS.md#sa-fe-004)). **Q7 is Not Supported.**
- Cross-cutting policy is opt-in. The CQRS pipeline is dead code, and **39** validators never run ([SA-API-003](../../FINDINGS.md#sa-api-003), Medium; QA-A dissents at High).
- Recommended remediation is proportionate and needs no rewrite. Add four small owners for invariants:
  - `OrderTransitionService`;
  - an order-payment projector;
  - `CouponEligibilityPolicy`;
  - `PickupAssignmentService`.
  Also wire one validation pipeline, add architecture tests, and create a `commerce.Tests` project. These sit inside the modular-monolith target in [01b](specialists/01b-architect-challenge-review.md) §4.6.

---

## 1. Scope, sources and evaluation method

### 1.1 Sources

| Source | Used for |
|---|---|
| [07 — OOP/SOLID](specialists/07-oop-solid.md) | Primary evidence: SRP size table, SOLID matrix, duplicated-rule table, SA-SOLID-001…014 |
| [01 — Architecture](specialists/01-architecture.md) | Project graph, layering, CQRS framework, SA-ARCH-001/002/005/009/010 |
| [10b — QA platform](specialists/10b-qa-verification-platform.md) | Independent re-verification, factual corrections, dedupe groups G1/G4/G5/G6/G7/G19 |
| [01b — Architect challenge review](specialists/01b-architect-challenge-review.md) | Root causes RC2, RC3, RC9; target architecture §4.6; roadmap |
| [FINDINGS](../../FINDINGS.md) / `findings-registry.json` | Canonical IDs, final severities, statuses, phases |
| Repository files | Re-opened for this report where marked "re-checked" (see 1.4) |

### 1.2 Evaluation method

1. **Material areas.** These are the areas where an OOP/SOLID failure changes business outcomes:
   - the order lifecycle;
   - payments and settlement;
   - coupons and promotions;
   - dispatch and assignment;
   - tenant-facing notifications;
   - the vertical extension seam;
   - layering and data access.

   Seeders, DTO files, MCP adapters and the composition roots were inspected but treated as non-material. 07's SRP table classifies them as data, configuration or adapters.
2. **Expectation per principle.** Each principle gets one concrete, checkable design expectation (stated under each heading).
3. **Evidence classes.**
   - *Compliance example*: code that meets the expectation in a material area.
   - *Violation*: code that does not meet it, with a canonical ID.
   - Each violation is classified as either a **genuine defect** (it has produced, or will deterministically produce, wrong behaviour, or it blocks the stated SaaS target) or a **reasonable trade-off** (a deliberate, documented choice whose cost is acceptable at the current scale).
4. **Principle verdict (qualitative, three levels).**
   - **Holds**: no genuine defect in a material area.
   - **Holds with gaps**: the expectation is met structurally, but at least one genuine defect exists in a material area.
   - **Not met**: violations are the norm in material areas, or the expectation is unachievable without core edits.

   No compliance percentages are given. Raw counts (for example "5 writers" or "39 validators") come from the cited greps and are reported as counts only.
5. **Labels.** Statuses are the registry's: Verified / Partially Verified / Suspected / Not Tested. Severities are the registry's FINAL values. Where QA or the architect disagreed, both values are shown (§17).

### 1.3 Environment limits

No .NET SDK and no Docker were available, so nothing was compiled or executed at the HTTP or .NET level. Every behavioural claim here comes from static tracing. CI results quoted in §16 come from GitHub Actions logs read by QA-B (10b, commands 8–9).

### 1.4 Re-checked for this report (static, 2026-10-09)

I opened each of the following files and confirmed the cited lines:
- the csproj `ProjectReference` graph (all 20 projects);
- `Utilities/CQRS/Dispatcher/Dispatcher.cs` (46 lines; L15-45 call handlers directly) and `CQRS/Extensions/ServiceCollectionExtensions.cs:14` (registers only `Dispatcher`);
- `NotificationSettingsCache.cs:19` (`Ttl = 60 s`);
- `database_scripts/04_bc4_order_lifecycle.sql:38` (channel CHECK);
- `CreateOrderCommand.cs` (925 lines; L618-622 `isParcel` binary; L643-646 VerticalKey comment; transaction L757-801);
- `UpdateMyTaskStatus.cs` (399 lines; L159, L173, L238, L264, L355-392);
- `FulfillmentStrategyResolver.cs:27-30` (silent laundry fallback);
- `LoggingChannelSender.cs` (returns `ChannelSendResult("logging-stub")`, logs phone and email at Information);
- `SettingsFirstPaymentGateway.cs:124` and `RoutingChannelSender.cs:118,154` (`_logger as ILogger<Other>`);
- `SalonAppointmentStrategy.cs` (sentinel `PostPickupStatus`) and `StateMachineStrategyBase.ApplyTransitionEffects` (keyed to laundry `OrderStatus`);
- `IOperationsDbContext.cs` (75 `DbSet`s; comments at L79, L97, L100, L110; transaction XML doc ending at L183);
- the entity counts (143 files, 3,556 `{ get; set; }`, 0 private or init setters; the three `*.Domain` folders hold only a csproj);
- the endpoint DbContext usage (4 of 102 endpoint files: core 3, operations 1, commerce 0);
- `RoyaltyCommands.cs:102` and `RoyaltyGenerationService.cs:198` (`"completed"`);
- `IsFirstOrderOnly`/`CustomerEligibility` writers (admin CRUD only) and `CustomerCouponHandlers.cs:110-112` (no rounding);
- `customer-mobile/src/lib/minOrder.ts`, `app/(app)/booking/pay.tsx:95-110`, `app/(app)/orders/tracking/[id].tsx:266-280`.

---

## 2. Scorecard

| Principle / topic | Verdict | Material genuine defects (canonical) | Main trade-offs accepted |
|---|---|---|---|
| **SRP** | Holds with gaps | SA-SOLID-010, SA-SOLID-001 (via duplication), SA-SOLID-011 | Thin endpoints and vertical-slice handlers; seeders and DTO files are large but single-purpose |
| **OCP** | **Not met** for verticals; holds for gateways and channels | SA-VERT-001 (dup SA-SOLID-004), SA-FE-004, SA-VERT-004, SA-VERT-006 | Channel routing by `switch` is acceptable at 5 channels |
| **LSP** | Holds with gaps | SA-SOLID-008 | Salon inherits laundry effects: latent until salon is operable |
| **ISP** | Holds with gaps | Cross-context write surface (SA-SOLID-009, SA-ARCH-001) | Wide `DbSet` interfaces are a deliberate EF-LINQ-first choice |
| **DIP** | Holds (by convention) | None material; SA-SOLID-012 and SA-SOLID-013 are Low | Utilities exposes ASP.NET and Npgsql to Application (SA-ARCH-009) |
| Encapsulation / invariants | Not met | SA-SOLID-009, SA-SOLID-001, SA-SOLID-003, SA-API-007, SA-SOLID-006 | DB-first scaffolding itself is acceptable |
| Inheritance vs composition | Holds | Fragile base in salon (latent, SA-VERT-001 family) | — |
| Circular dependencies | **None** | — | — |
| Testability | Holds with gaps | SA-ARCH-010, SA-SOLID-010, SA-QB-002 | — |

---

## 3. Single Responsibility Principle (SRP)

**Expectation.** Each handler or service has one reason to change. Business rules that more than one use case needs live in one reusable application or domain service, not in each handler. Infrastructure hosts adapters, not business policy.

### 3.1 Verified compliance

| Example | Evidence |
|---|---|
| Endpoints are thin. Only 4 of 102 endpoint files touch a DbContext (core 3/35, operations 1/41, commerce 0/26). The rest delegate to `IDispatcher`. | [07](specialists/07-oop-solid.md) SOLID matrix; re-counted for this report. The 4 are `OAuth.cs`, `PartnerAuth.cs`, `AdminBrandDomains.cs` and `AdminSupportEndpoints.cs` (07, positive controls). |
| A uniform vertical-slice pattern: endpoint → command/query → handler, with handlers discovered by Scrutor | `Utilities/CQRS/Extensions/ServiceCollectionExtensions.cs:10-34` ([01](specialists/01-architecture.md) positive controls) |
| Large files that are data or configuration, not god classes: `IdentitySeeder.cs` (976 lines, seed tables), `CommerceDtos.cs` (597, 32 records), `LaundryTools.cs` (865, MCP adapters), `core.WebApi/Program.cs` (646, composition root) | 07 SRP table rows 1, 4, 7 and 8 |
| Multi-type files that are file organisation, not multi-responsibility classes: `PickupCommands.cs` (814 lines, 13 types), `SelfCommands.cs` (9 handlers), `ExpenseCommands.cs` | 07 SRP table rows 5, 10 and 14 |

### 3.2 Verified violations

| Canonical ID (sev, status, phase) | Evidence (path:lines) | Defect or trade-off |
|---|---|---|
| [SA-SOLID-010](../../FINDINGS.md#sa-solid-010) (Medium, Verified, P3): god-handlers without tests | `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:51-806`: one `HandleAsync` of about 755 lines with about 15 responsibilities (section markers L62-756). `Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs:37-280` mixes the leg state machine, COD, payout, order transition, Payment creation and outbox writes. `core.WebApi/Endpoints/Identity/OAuth.cs:47-886` is a static class with inline EF and HTML (L647+). | **Genuine defect** for CreateOrder and UpdateMyTaskStatus: they host the duplicated rules below and have no tests. **Trade-off** for OAuth: the design is documented as deliberate (L19-45), but it is weakly testable. |
| [SA-SOLID-001](../../FINDINGS.md#sa-solid-001) (High, Verified, P3; dup SA-API-006) | The order-transition responsibility is spread over 5 handlers (§14) | **Genuine defect**: missed notifications, loyalty earned only on the rider path, state-machine bypass |
| [SA-SOLID-011](../../FINDINGS.md#sa-solid-011) (Low, Verified, P3) | Assignment rules sit in a commerce Infrastructure worker: `commerce.Infrastructure/Worker/Services/AutoDispatchService.cs:301-376` vs `PickupCommands.cs:204-278` | **Genuine defect** (drifted), low impact |
| Business policy in Infrastructure workers (no separate ID; evidence for SA-SOLID-009 and SA-SOLID-011) | `SubscriptionBillingService.cs` (551 lines, billing rules); `AutoDispatchService.cs` (479 lines); `RoyaltyGenerationService.cs` | Trade-off while there is one worker host. It becomes a defect when rules are duplicated, as in dispatch. |

### 3.3 Consequences for adding verticals

Every vertical-specific pricing, tax, coupon or fulfilment rule today has to be added as another branch inside `CreateOrderHandler` or `UpdateMyTaskStatusHandler`. Neither has a direct test (SA-SOLID-010, SA-QB-002). Each new vertical therefore raises the regression risk on the laundry money path.

### 3.4 Recommended refactoring (proportionate)

Extract pure collaborators from CreateOrder:
- `IOrderPricingService` (lines and add-ons);
- `IDiscountPipeline` (coupon, loyalty, package, promotions);
- `IOrderFactory` (Order, history, outbox), which `CreateParcelOrderCommand.cs:100-200` also uses.

Keep the single transaction (L757-801). Split `UpdateMyTaskStatusHandler` into leg transition + `OrderTransitionService` (§14) + COD settlement. Leave OAuth as is until it is in scope for security work.

### 3.5 Regression tests to add

- Golden-total tests for CreateOrder: express, add-ons, coupon, loyalty, GST, unregistered franchise. Run them **before** extraction as a parity net.
- Rider-flow state tests: pickup collect, delivery complete, failed leg.

---

## 4. Open/Closed Principle (OCP)

**Expectation.** A new vertical, fulfilment mode, payment gateway or channel is added by registering a new implementation (DI or config), without editing shared handlers, shared enums, the notification policy or client code.

### 4.1 Verified compliance

| Example | Evidence |
|---|---|
| `IFulfillmentStrategy` + `StateMachineStrategyBase` (template method) + dictionary resolver, with 4 strategies DI-registered | `operations.Application/Fulfillment/FulfillmentStrategyResolver.cs:10-44`; `operations.Application/DependencyInjection.cs:33-40`; parity, salon and recurring tests in `tests/operations.Tests/Fulfillment/*` |
| The admin status-change path really uses the seam | `UpdateOrderStatusCommand.cs:53-68` delegates transition validation, lifecycle super-state and timestamp effects. 01b confirms: "RC3 is about bypass, not design". |
| `CreateOrder` takes initial status and legs from the strategy, not literals | `CreateOrderCommand.cs:623-627` (`strategy.ResolveLegs`, `strategy.InitialStatus`) |
| The rider pickup-hop path walks the strategy's `ForwardPath` and applies `ApplyTransitionEffects` | `UpdateMyTaskStatus.cs:355-392` (re-checked) |
| `IPaymentGateway` port with a per-brand settings-first decorator; `IChannelSender` composite; `IFileStorageProvider` | 07 SOLID matrix; `SettingsFirstPaymentGateway.cs:83-88` (brand-keyed) |
| Neutral `LifecycleState` used in at least one query | `OpsQueuesQuery.cs:115` (07 positive controls) |

### 4.2 Verified violations

| Canonical ID (sev, status, phase) | Evidence (path:lines) | Defect or trade-off |
|---|---|---|
| [SA-VERT-001](../../FINDINGS.md#sa-vert-001) (High, Verified, P3; dups SA-SOLID-004, SA-ARCH-003, SA-ONB-004): the seam is bypassed at creation | `CreateOrderCommand.cs:618-622`: `isParcel ? PointToPoint : ProcessDeliver` (re-checked). The comment at L643-646 defers `VerticalKey`. `FulfillmentMode.DefaultFor` (`SharedDataModel/Enums/FulfillmentMode.cs:30-36`) and `CatalogKind.DefaultFor` have no callers. Salon and recurring strategies are registered but unreachable. | **Genuine defect** against the multi-vertical target |
| SA-VERT-001 family: laundry status literals in shared handlers | `OrderQueries.cs:30-36` (active/history split), `GenerateInvoiceCommand.cs:38`, `RateOrderCommand.cs:26`, `RateRiderCommand.cs:22`. `OrderLifecycleState.TerminalArray` exists for this purpose but is unused there (07 SA-SOLID-004 evidence). | Genuine defect for non-laundry modes |
| [SA-VERT-006](../../FINDINGS.md#sa-vert-006) (Medium, Verified, P3) | `commerce.Infrastructure/Worker/Channels/NotificationChannelPreferencePolicy.cs:43-62` hard-codes the `(event, status)` → template switch. The vertical-tagged `notification_event_catalog` has no C# reader. | Genuine defect |
| [SA-API-013](../../FINDINGS.md#sa-api-013) (Medium, Verified, P4; dup SA-ONB-006) | "Laundry Ghar" fallback copy in `NotificationMappingService.cs:423-437` | Genuine defect (white-label) |
| [SA-VERT-004](../../FINDINGS.md#sa-vert-004) (High, Verified, P3) | Laundry SAC and description hard-coded in `InvoiceTaxCalculator.cs:18` and `InvoicePdfRenderer.cs:44,106`. Live today for parcel orders (10b). | Genuine defect |
| [SA-FE-004](../../FINDINGS.md#sa-fe-004) (High, Verified, P3): client-side copy | `admin-web/src/pages/orders/orderStatus.ts:43-76`, used unconditionally at `OrderDetailDrawer.tsx:504-505`; it ignores the server's `allowedTransitions` (`OrderQueries.cs:128,202`) | **Genuine defect, live**: parcel orders cannot be advanced past `picked_up` from admin-web (10b, strengthened) |
| Catalog-kind mapping drift (SA-VERT-001 family) | `SharedDataModel/Enums/CatalogKind.cs:32-37` maps Tiffin to LaundryGarment (unused); `TemplateProvisioner.cs:146-152` maps Tiffin to Product | Genuine defect (two sources of truth) |
| Resolver silently falls back to laundry for unknown modes | `FulfillmentStrategyResolver.cs:27-30` (re-checked). Its doc comment says the fallback preserves legacy rows. | **Trade-off** for null or legacy modes. **Defect** for an unknown non-null mode, which should fail loudly. |
| `RoutingChannelSender` if/switch on channel | `RoutingChannelSender.cs:65-97` | **Trade-off**: 5 channels, and adding one is a one-file change |
| `SettingsFirstPaymentGateway` always builds Razorpay | `SettingsFirstPaymentGateway.cs:113-127` | **Trade-off**: there is one real gateway today. Add a keyed factory only when a second one is contracted. |

### 4.3 Consequences for adding verticals

These are the edits a new vertical currently needs:
- `CreateOrderCommand` and `CreateParcelOrderCommand`;
- at least 4 shared handlers that use status literals;
- the notification policy and its copy;
- the invoice tax identity;
- admin-web `orderStatus.ts`;
- `CatalogKind`/`TemplateProvisioner`;
- the operations DI.

01 adds the shared SharedDataModel assembly (rebuilding all hosts), the central `IdentitySeeder`, SQL patches and CHECK constraints. That is "widespread modification": **Q7 Not Supported** (07, 01, 10b and 01b agree). Root cause **RC9**: the vertical is modelled as metadata, not as modules ([01b](specialists/01b-architect-challenge-review.md) §3).

### 4.4 Recommended refactoring (proportionate)

1. In both create handlers, use `mode = isParcel ? PointToPoint : FulfillmentMode.DefaultFor(brand.VerticalKey)` and set `VerticalKey` from the brand. Reject the order when the mode's flow is not operable (SA-VERT-001; SA-VERT-002 hides non-operable templates in P0).
2. Replace status lists with `LifecycleState` checks (`Terminal`, `Completed`).
3. Make the resolver throw for an unknown **non-null** mode. Keep the fallback for null or legacy rows.
4. Admin-web renders `order.allowedTransitions` (POS already does this: `pos-web/src/pages/orders/OrderDetailPage.tsx:50`).
5. Delete one of the two catalog-kind maps.
6. Later (P3): add the `IVerticalModule` registration contract (strategy, catalog kind, tax profile, templates, permission pack, terminology), compile-time registered as in 01b §4.6. No runtime plugin loading.

### 4.5 Regression tests to add

- Laundry brand → `process_deliver`/`laundry`.
- Laundry parcel → `point_to_point`.
- Salon brand → `appointment`/`booked`, or an explicit refusal.
- A salon order run to `completed` lands in history and is invoiceable and rateable.
- An unknown mode fails.
- An admin-web unit test that the drawer renders server transitions for a parcel order.
- A CI check that every public signup template's mode has a create-path test.

---

## 5. Liskov Substitution Principle (LSP)

**Expectation.** Any registered implementation of an interface honours the contract the consumer relies on. Substituting it does not silently change correctness, for example by reporting success for work that was not done.

### 5.1 Verified compliance

| Example | Evidence |
|---|---|
| Storage providers fail fast at **startup** rather than per call, so substitution can't fail silently | `FileStorageProviderFactory.cs:16-37` (07) |
| The dev payment gateway is environment-gated | `DevPaymentGateway` is registered only when `IsDevelopment()` (`commerce.WebApi/Program.cs:80-106`) |
| The laundry, parcel, salon and recurring strategies share one abstract contract, with parity tests | `tests/operations.Tests/Fulfillment/FulfillmentStrategyParityTests.cs`, `SalonStrategyTests.cs`, `RecurringStrategyTests.cs` |

### 5.2 Verified violations

| Canonical ID (sev, status, phase) | Evidence (path:lines) | Defect or trade-off |
|---|---|---|
| [SA-SOLID-008](../../FINDINGS.md#sa-solid-008) (Medium, Verified, P1): a null object breaks the `IChannelSender` contract in production | `commerce.Infrastructure/Worker/Stubs/LoggingChannelSender.cs` returns `new ChannelSendResult("logging-stub")` and logs `phone=`/`email=` at Information (re-checked). It is registered unconditionally (`commerce.WebApi/Program.cs:262,287`). `RoutingChannelSender.cs:80-95` routes email, in_app, voice, unknown channels and credential-less WhatsApp/SMS to it. The dispatcher marks the row `sent` (`NotificationDispatcherService.cs:170-225`). | **Genuine defect**: undelivered messages are recorded as sent and never retried; PII goes to logs |
| Salon inherits laundry-keyed effects (SA-VERT-001 family) | `StateMachineStrategyBase.ApplyTransitionEffects` switches on laundry `OrderStatus` values (re-checked). `SalonAppointmentStrategy` doesn't override it, so no salon completion timestamp is stamped. It declares a sentinel `PostPickupStatus => CheckedIn` with the comment "a sentinel keeps the contract total". | **Latent** (salon is unreachable today). The sentinel is a **reasonable trade-off** that the code documents. The missing override is a defect once salon is enabled. |

### 5.3 Consequences for adding verticals

Each new strategy inherits laundry timestamp semantics by default. If a vertical author forgets to override, the result is a silent no-op, not a compile error. Each new tenant without channel credentials gets silent notification loss instead of a visible "not configured" state.

### 5.4 Recommended refactoring (proportionate)

- **Channels.** Outside Development, return a non-delivered outcome (for example `Delivered=false`) or throw `ChannelNotConfiguredException`, store `suppressed` or `failed:not_configured`, and mask PII.
- **Strategies.** Make `ApplyTransitionEffects` abstract, or move the laundry body into `LaundryProcessStrategy`, so each mode must declare its effects. Split the 15-member `IFulfillmentStrategy` (§6) so leg-related members (`PostPickupStatus`, `ResolveLegs`) are only implemented by modes that have legs.

### 5.5 Regression tests to add

- In Production mode with no credentials, the outbox row is not `sent`.
- The salon strategy stamps a completion timestamp on `completed`.
- A contract test that runs every registered `IFulfillmentStrategy` through its happy path and asserts a timestamp effect per non-initial status.

---

## 6. Interface Segregation Principle (ISP)

**Expectation.** Consumers depend on small, purpose-specific interfaces. In particular, a bounded context's data interface does not grant **write** access to tables another context owns.

### 6.1 Verified compliance

`IPaymentGateway` (5 members), `IChannelSender` (1), `IFileStorageProvider` (4), `IFulfillmentStrategyResolver` (2) (07 SOLID matrix).

### 6.2 Verified violations

| Canonical ID (sev, status, phase) | Evidence (path:lines) | Defect or trade-off |
|---|---|---|
| Wide DbContext interfaces (evidence for SA-SOLID-012 and SA-ARCH-009) | `IOperationsDbContext` has 75 `DbSet`s, `ICoreDbContext` 51, `ICommerceDbContext` 42 (re-counted). The interface doc says "no repositories … write EF Core LINQ directly" (`IOperationsDbContext.cs:14-19`). | **Trade-off** (deliberate EF-LINQ-first design, documented) |
| [SA-SOLID-009](../../FINDINGS.md#sa-solid-009) (Medium, Verified, P3; dup SA-ARCH-002) / [SA-ARCH-001](../../FINDINGS.md#sa-arch-001) (Medium, Verified, P3): cross-context write surface | `IOperationsDbContext.cs` exposes commerce and finance sets: `Payments` (L97, "COD payment rows on delivery completion"), coupon/loyalty/package/promotion/refund sets (L100, "order placement money flow") and `CashBooks`/`CashBookEntries` (L110). `ICommerceDbContext.cs:84` exposes `Orders`, which `RecordOfflinePaymentCommand.cs:174-180` writes. | **Genuine defect** in effect. The cross-context writes are *intentional and documented*, but no single owner enforces the shared invariants (see §8). |
| `IFulfillmentStrategy` has 15 members, mixing the state graph with order-creation and pickup hooks | 07 SOLID matrix | Low-cost trade-off today; fix it with the LSP change in §5.4 |

> **Correction to 07.** 07 reads the `"READ-ONLY here"` comment as applying to the commerce and finance sets. In the file it applies only to `PartnerWalletAccounts` (`IOperationsDbContext.cs:79`). The `Payments` and money-flow sets are documented as written by operations. The conclusion stands (no single owner of those ledgers), but the cross-context writes are a design decision, not a comment being ignored.

### 6.3 Consequences for adding verticals

A vertical module given `IOperationsDbContext` can write coupons, payments and cash books with no compile-time barrier. Each vertical would add more sets to the shared interface.

### 6.4 Recommended refactoring (proportionate)

- Keep the `DbSet` interfaces; do not introduce generic repositories (§12).
- Where a context only reads a foreign table, expose `IQueryable<T>` instead of `DbSet<T>`.
- Route foreign writes through the owning module's in-process API in the caller's transaction (01 SA-ARCH-001; 01b roadmap Phase 3), for example a commerce-owned `ICouponRedemptionService` called by CreateOrder.

### 6.5 Regression tests to add

An architecture test: handlers outside the owning context must not call `.Add`, `.Update` or `.Remove` on a foreign context's sets (07 SA-SOLID-009). Existing order-placement atomicity must stay green.

---

## 7. Dependency Inversion Principle (DIP)

**Expectation.** Application depends only on abstractions it owns. Infrastructure implements them. Application has no dependency on web, driver or concrete persistence types. The rule is enforced by tests, not by convention.

### 7.1 Verified compliance

| Example | Evidence |
|---|---|
| Correctly directed, acyclic project graph: `*.WebApi → *.Infrastructure → *.Application → *.Domain → SharedDataModel`, with Application → Utilities. No Application project references an Infrastructure project. | All csproj `ProjectReference`s (re-checked; §15) |
| Handlers inject `ICoreDbContext`/`IOperationsDbContext`/`ICommerceDbContext`, never `LaundryGharDbContext` | grep of the `*.Application` trees (07); thin adapters such as `operations.Infrastructure/Persistence/OperationsDbContext.cs:22-80` |
| Ports for the gateway, OTP, storage and channels are defined in Application and implemented in Infrastructure | 07 SOLID matrix and positive controls |
| Safe raw-SQL seams through `FormattableString` | `IOperationsDbContext.cs:165,174` (re-checked) |

### 7.2 Verified violations

| Canonical ID (sev, status, phase) | Evidence (path:lines) | Defect or trade-off |
|---|---|---|
| [SA-SOLID-012](../../FINDINGS.md#sa-solid-012) (Low, Verified, P3) | `laundryghar.Utilities.csproj` has `FrameworkReference Microsoft.AspNetCore.App` (L10), Npgsql and MailKit. `IFormFile` is used in 6 Application commands (for example `UploadProofPhoto.cs:22`). `using Npgsql`/`PostgresException` appears in `PickupCommands.cs:10,500-508`. `IAuditWriter` writes through the concrete context (`Auth/Audit/IAuditWriter.cs:41-48`). The legacy `ICurrentUserService` has 0 callers. | **Mostly a trade-off**, Low. No handler misuses the concrete context today, but nothing prevents erosion. |
| [SA-ARCH-009](../../FINDINGS.md#sa-arch-009) (Low, Verified, P3) | Utilities is a cross-cutting "god library" (`laundryghar.Utilities.csproj:9-32`). Composition roots are copy-pasted across hosts. | Trade-off with a maintenance cost |
| Empty Domain projects | `core.Domain`, `operations.Domain`, `commerce.Domain` contain only a csproj (re-checked) | Placeholder. Not a DIP break in itself, but the "Clean Architecture" ring is nominal (01). |

### 7.3 Consequences for adding verticals

Low today. The risk is erosion: a vertical module could reference ASP.NET, Npgsql or the concrete context with no failing test.

### 7.4 Recommended refactoring (proportionate)

- Add NetArchTest rules: Application must not depend on `*.Infrastructure`, `LaundryGharDbContext` or `Microsoft.AspNetCore.Http`.
- Replace `IFormFile` with a `(Stream, contentType, fileName)` value at the endpoint.
- Move unique-violation detection behind `IOperationsDbContext.IsUniqueViolation(ex)`.
- Delete `ICurrentUserService`.
- In P3, split Utilities into `Platform.Abstractions` (no ASP.NET) and `Platform.Web` (SA-ARCH-009).

### 7.5 Regression tests to add

The architecture tests themselves, run in CI.

---

## 8. Encapsulation and domain invariants

**Expectation.** Each high-value invariant has one owner: order status, order payment state, payment-status vocabulary, coupon eligibility and assignment. Statuses are constrained values, not free strings.

**Observed (Verified, re-counted).**
- `SharedDataModel/Entities` contains 143 entity files with 3,556 `{ get; set; }` properties, 0 private or init setters and no behaviour methods.
- Every status is a `string`, for example `Order.Status`, `Order.PaymentStatus` and `Payment.Status` (`Entities/OrderLifecycle/Order.cs:11-159`).
- One `LaundryGharDbContext` maps 144 `DbSet`s (`Persistence/LaundryGharDbContext.cs:34-218`).
- The DB `orders_status_check` constraint was dropped (`db/patches/phase1_slice_b_order_lifecycle_state.sql:75-84`), so for order status nothing below the application layer backstops an invalid value (07 SA-SOLID-001).

| Invariant | Canonical ID | Consequence |
|---|---|---|
| Order status + history + outbox | [SA-SOLID-001](../../FINDINGS.md#sa-solid-001) (High) | 5 divergent writers (§14) |
| Order `AmountPaid`/`PaymentStatus` | [SA-API-007](../../FINDINGS.md#sa-api-007) (High, P0; dup SA-SOLID-002) | Online capture never updates the order (§14) |
| Payment "settled" vocabulary | [SA-SOLID-003](../../FINDINGS.md#sa-solid-003) (High, P2) | Royalty filters on `"completed"`, a value the DB CHECK does not permit (`database_scripts/06_bc6_commerce.sql:379-381`); royalties come out as 0 |
| Payment `FranchiseId` | [SA-QB-001](../../FINDINGS.md#sa-qb-001) (Medium, P2) | COD and online rows have no franchise, so royalty under-counts even after SA-SOLID-003 is fixed |
| Refund type vocabulary (same pattern) | [SA-QC-001](../../FINDINGS.md#sa-qc-001) (High, Partially Verified, P0) | API contract `"gateway"/"wallet"` vs DB CHECK `full/partial/goodwill/dispute_loss`; the gateway refund is issued before the failing INSERT |
| Coupon eligibility | [SA-SOLID-006](../../FINDINGS.md#sa-solid-006) (Medium); usage cap [SA-DB-010](../../FINDINGS.md#sa-db-010) (Medium, Partially Verified) | First-order and eligibility flags are never enforced; caps are enforced only in the app |

**Defect or trade-off.** Database-first scaffolding is deliberate (`LaundryGharDbContext.cs:17-33`) and is a **reasonable trade-off**. The **genuine defect** is that nothing compensates for the missing invariant owner (SA-SOLID-009, Medium). This is root cause **RC3** in [01b](specialists/01b-architect-challenge-review.md) §3: "Anemic shared model; invariants live in handlers and are duplicated".

**Recommendation (proportionate; no DDD rewrite).**
1. Use the `CommercePaymentStatus` constants everywhere, add a `Settled = {Captured, Succeeded}` set, and ban raw status literals with an analyzer or banned-API rule (SA-SOLID-003). Apply the same approach to `RefundType` (SA-QC-001).
2. Add four small services (application-layer, or entity partial-class methods, which EF tolerates): `OrderTransitionService`, `OrderPaymentProjector`, `CouponEligibilityPolicy` and `PickupAssignmentService` (§14).
3. Either delete the empty Domain projects or move these policies into them. Do not leave them as misleading placeholders.

**Regression tests.**
- A royalty calculation over seeded captured and succeeded payments is non-zero and includes COD, online and offline rows.
- `payments.franchise_id` is not null when `order_id` is set.
- A refund request with each `RefundType` value is accepted or rejected **before** any gateway call.

---

## 9. Inheritance vs composition

**Verified.** Composition is the dominant style. There are only **5** abstract classes in non-test code (re-counted). Behaviour is composed through DI:
- a routing composite for channels and OTP;
- a decorator for settings-first payment;
- a factory for storage;
- a strategy resolver for fulfilment.

The one significant inheritance hierarchy, `StateMachineStrategyBase` → 4 strategies, is a proper Template Method. Subclasses supply `Transitions`, `HappyPath`, `TerminalStatuses` and `InitialStatus`.

**Gap.** The base class carries one vertical's behaviour: the `ApplyTransitionEffects` switch on laundry `OrderStatus`. That is a fragile-base-class risk for non-laundry modes (§5.2). Verdict: **Holds**. The fix is the abstract-method change in §5.4, not a change of style.

---

## 10. Dependency injection and dependency direction

| Aspect | Verified state | ID / evidence | Assessment |
|---|---|---|---|
| Direction | Correct and acyclic (§15) | csproj graph | Holds |
| Registration | Scrutor handler discovery; `AddScoped<IDispatcher, Dispatcher>` | `ServiceCollectionExtensions.cs:14` (re-checked) | Holds |
| Environment gating | The dev gateway is Development-only, but `LoggingChannelSender` is registered in **all** environments | `commerce.WebApi/Program.cs:80-106` vs `:262,287` | Defect: SA-SOLID-008 |
| Logger propagation in settings-built providers | `_logger as ILogger<RazorpayPaymentGateway>` (and the WhatsApp and MSG91 equivalents) is always null at runtime by language rules, so `NullLogger` is used | `SettingsFirstPaymentGateway.cs:124`; `RoutingChannelSender.cs:118,154` (re-checked) | [SA-SOLID-013](../../FINDINGS.md#sa-solid-013) (Low, Verified). Fix: inject `ILoggerFactory`. Not compiled, so this rests on C# cast semantics. |
| Tenant-agnostic singleton behind a tenant-scoped setting | `NotificationSettingsCache.GetAsync` has no brand parameter, no `OrderBy`, and uses `FirstOrDefault`; the TTL is **60 s** | `commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:10-65`, `:19` (re-checked); `RoutingChannelSender.cs:103,138` | [SA-API-012](../../FINDINGS.md#sa-api-012) (High, P0; dup SA-SOLID-007). This is a **cross-tenant data flow** (01b §2.2). |
| Scope trust per call site | `CreateAsyncScope()` vs `CreateWorkerAsyncScope()` decides whether a worker sees all tenants | `BrandPlatformBillingService.cs:63` vs `:156` | [SA-SUB-004](../../FINDINGS.md#sa-sub-004) (High, Partially Verified); root cause RC5/RC7 (out of SOLID scope, noted for DI hygiene) |
| Pipeline composition | Behaviour-aware dispatchers and `BehaviorRegistrar.RegisterBehaviors` exist but are never registered or called | `Dispatcher.cs:15-45`; `BehaviorRegistrar.cs:13-25` (re-checked: no caller) | [SA-API-003](../../FINDINGS.md#sa-api-003) (§11) |

---

## 11. Separation of concerns (cross-cutting policy)

**Expectation.** Validation, transactions and audit are applied uniformly, not opted into per endpoint.

**Observed: [SA-API-003](../../FINDINGS.md#sa-api-003)** (Medium, Verified, P1; dups SA-SOLID-005, SA-ARCH-005). This is root cause **RC2** in [01b](specialists/01b-architect-challenge-review.md) §3: "Cross-cutting policy is opt-in per endpoint/handler".
- `Dispatcher` runs no behaviours. Validation happens only where an endpoint attaches `ValidationFilter<T>` (`Validation/ValidationFilter.cs:13-51`).
- QA-B's type-set diff found 130 validator target types and 92 filtered types, leaving 40 unfiltered. One of them, `PartnerBookingLocation`, runs as a child validator through `SetValidator` (`PartnerBookingValidators.cs:9-14`). That leaves **39 validators that never run** ([10b](specialists/10b-qa-verification-platform.md), command 1).
- Examples: `CreateOrderValidator` (`CreateOrderCommand.cs:886-925`; the endpoint filters `CreateOrderRequest`, which has no validator) and `RateOrderValidator` (`RateOrderCommand.cs:58-68`).
- Mitigations (10b):
  - DB CHECKs backstop quantity (`04_bc4_order_lifecycle.sql:154`) **and the channel whitelist** (`:38`, `CHECK (channel IN ('walkin','app','whatsapp','call','web','pos'))`, re-checked). 07 wrongly said channel was not backstopped.
  - Upload endpoints have `RequestSizeLimit`, and files are served as attachments.
  - What remains unbacked: type and size checks inside those caps, cart size, and lengths stored in jsonb.
- The dead `TransactionBehavior` would use a bare `BeginTransactionAsync`, which `NpgsqlRetryingExecutionStrategy` rejects (`IOperationsDbContext.cs` XML doc on `ExecuteInTransactionAsync`). If enabled, `ExceptionBehavior` would wrap business errors in an exception type that `ExceptionHandler` does not map (01 SA-ARCH-005).

**Other separation-of-concerns observations.**
- Business policy in Infrastructure workers (§3.2).
- OAuth renders HTML inside a static endpoint class (SA-SOLID-010, trade-off).
- SharedDataModel also hosts application-ish services: `BrandExportService`, `TlsDomainHealthChecker`, `FeatureCatalog` (`SharedDataModel/DependencyInjection.cs:73-84`, from 01).

**Defect or trade-off.** A **genuine defect**: the validation that looks complete isn't run, and new vertical endpoints can ship without validation. Severity dissent is recorded in §17.

**Recommendation.** Pick one mechanism (01b §4.3 prefers the pipeline). Make `Dispatcher` run a curated pipeline with validation only, throwing the application's own `ValidationException`. Delete `TransactionBehavior`, `IUnitOfWorkCommand`, `CachingBehavior` and the unused dispatchers. Roll out per module behind tests, because enabling validation will surface latent 400s.

**Regression tests.**
- A dispatcher test with a failing validator and no endpoint filter.
- An exception-mapping test for `BusinessRuleException` through the dispatcher.
- An architecture test that every `AbstractValidator<T>` is reachable.
- Endpoint tests posting `quantity: 0`, `channel: "x"` and `score: 9` that expect a 4xx.

---

## 12. Repository and unit of work

**Verified design.** There are no repositories, by explicit choice: "no repositories … write EF Core LINQ directly" (`IOperationsDbContext.cs:14-19`; `ICoreDbContext.cs:12-17`). The per-context DbContext interfaces act as the persistence ports.

The unit of work is the EF change tracker:
- a single `SaveChangesAsync` for one-step commands (for example `CancelOrderCommand.cs:123`, `CancelOrderByCustomerCommand.cs:113`, re-checked);
- `ExecuteInTransactionAsync` for multi-step units, run inside the retrying execution strategy (`IOperationsDbContext.cs:183`).

**Assessment.** This is a **reasonable trade-off** and should be kept. Generic repositories over EF Core would add an abstraction without adding an invariant owner. The value is in the *domain services* in §8 and §14, not in repositories.

The defects in this area are:
- the dead `IUnitOfWorkCommand`/`TransactionBehavior` (SA-API-003), which suggests a UoW pipeline that does not exist;
- side effects outside the UoW boundary (§13).

---

## 13. Domain vs application services and transaction boundaries

| Aspect | Verified state | Evidence | Assessment |
|---|---|---|---|
| Domain services | None. Domain projects are empty. Rules live in application handlers and Infrastructure workers. | §7.2, §8 | Defect via duplication (RC3) |
| CreateOrder atomicity | Order, items, add-ons, history, outbox, coupon redemption, loyalty debit, package ledger and promotion counters are committed in **one** transaction | `CreateOrderCommand.cs:757-801` (re-checked) | **Positive control.** Any refactor must preserve it (01 option C was rejected for this reason). |
| Outbox in the same transaction | The status history and outbox rows are added before `SaveChangesAsync` | `UpdateMyTaskStatus.cs:232-259`; `CreateOrderCommand.cs` | Positive |
| Side effect outside the transaction | `RiderLoad.DecrementAsync` runs **after** the transaction, on every `completed`/`failed` call, including repeats | `UpdateMyTaskStatus.cs:262-264` (re-checked; also cited in 01b §1.1 for SA-MOB-001) | Genuine defect, part of [SA-MOB-001](../../FINDINGS.md#sa-mob-001) (High, P0) |
| Cross-context writes inside one transaction | Operations writes commerce ledgers (CreateOrder, rider COD `Payment`); commerce writes `Orders` (`RecordOfflinePaymentCommand.cs:174-180`); the commerce worker writes logistics assignments (`AutoDispatchService.cs:92,301-376`) | [SA-ARCH-001](../../FINDINGS.md#sa-arch-001) (Medium) | **Atomicity is a trade-off worth keeping.** The missing piece is ownership: move the writes behind owning-module APIs called in the same transaction (01b Phase 3). |
| Inter-context events | The outbox is polled by consumers with three different cursor semantics, and event types are free strings | [SA-ARCH-006](../../FINDINGS.md#sa-arch-006) (Medium, Partially Verified; skipping is Suspected) | Defect. Standardise on the inbox pattern in `PartnerBookingDebitService.cs:100-125` and add typed event contracts. |
| Optimistic concurrency | No `xmin`/rowversion on aggregates. `Version += 1` is applied by hand on some paths only. | RC4 (01b); §14 drift | Out of SOLID scope; see 01b Phase 1 |

---

## 14. Duplicated business rules (server and client)

All rows are Verified (static) unless marked otherwise. "Drift" means the copies already behave differently.

| Rule | Copies (evidence) | Drift observed | Canonical ID | Single owner to introduce |
|---|---|---|---|---|
| **Order status transition + history + event** | `UpdateOrderStatusCommand.cs:52-117` (strategy-checked); `CancelOrderCommand.cs:48-101`; `CancelOrderByCustomerCommand.cs:45-94`; `UpdateMyTaskStatus.cs:152-256` (delivery: `o.Status = "delivered"` at L159, `FromStatus = "out_for_delivery"` at L173, **no `EnsureTransition`**); `UpdateMyTaskStatus.cs:355-392` (pickup hops: strategy path, **no outbox row**) | Cancels and admin updates emit `order.status_changed`; rider delivery emits `delivery.completed`; pickup hops emit nothing. The notification policy has no `cancelled` mapping, and nothing emits `order.cancelled`. Loyalty earns only on `delivery.completed` (`LoyaltyEarnService.cs:94,109`). A rider can overwrite `disputed`/`returned` with `delivered`. | [SA-SOLID-001](../../FINDINGS.md#sa-solid-001) (High; dup SA-API-006); rider-leg part [SA-MOB-001](../../FINDINGS.md#sa-mob-001) (High, P0); [SA-MOB-016](../../FINDINGS.md#sa-mob-016) (customer cancel does not release legs) | `OrderTransitionService.TransitionAsync(order, to, actor, reason)`: `EnsureTransition` → set Status/LifecycleState/Version → effects → history → **always** `order.status_changed`. 01b rule: the only writer of `orders.status`. |
| **Order payment state** (`AmountPaid`, `PaymentStatus`) | `RecordOfflinePaymentCommand.cs:174-180` (sets `"partial"`, bumps `Version`); `UpdateMyTaskStatus.cs:225-227` (neither). **Never** set by online capture (`RazorpayWebhookHandler.cs:168-240`; `CustomerPaymentHandlers.cs:141-170`). No DB trigger (10b command 3). | The two writers disagree. Online capture is ignored, so the rider is told to collect COD on a prepaid order and revenue is double-counted. | [SA-API-007](../../FINDINGS.md#sa-api-007) (High, P0; dup SA-SOLID-002; dissent §17) | `OrderPaymentProjector.ApplySettledPaymentAsync`: recompute from settled payments, idempotent per payment id |
| **Payment "settled" literal** | Writers use `"captured"` (webhook, verify, offline L162) and `"succeeded"` (COD, `UpdateMyTaskStatus.cs:215`). Readers use `"completed"` (`RoyaltyCommands.cs:102`, `RoyaltyGenerationService.cs:198`, re-checked) and `"captured"\|"completed"` (`AdminPaymentHandlers.cs:95`, `OrderCancellationRefund.cs:49`). | `"completed"` is not a DB-permitted value; COD rows are not refundable through the admin path | [SA-SOLID-003](../../FINDINGS.md#sa-solid-003) (High); [SA-QB-001](../../FINDINGS.md#sa-qb-001) | `CommercePaymentStatus.Settled` set + banned literals |
| **Coupon eligibility and discount** | `CreateOrderCommand.cs:268-338` (rounded); `CustomerCouponHandlers.cs:59-150` (**not rounded**, L110-112, re-checked); `CustomerPickupCommands.cs:46-100` (rounded) | `IsFirstOrderOnly`/`CustomerEligibility` are written only by admin CRUD and enforced nowhere (re-checked; the XML doc at `CustomerCouponHandlers.cs:50` claims otherwise). Validate-apply trusts a client `OrderId`/`OrderSubtotal` and increments global usage. | [SA-SOLID-006](../../FINDINGS.md#sa-solid-006) (Medium; QA did not re-trace); [SA-DB-010](../../FINDINGS.md#sa-db-010) (usage cap app-only) | Pure `CouponEligibilityPolicy.Evaluate(coupon, customerStats, subtotal)` used by all three; validate-apply loads the order |
| **Pickup assignment** | Manual `AssignPickupHandler` (`PickupCommands.cs:204-278`) seeds `CodAmount` and emits no event. Auto `AutoDispatchService.AssignPickupAsync` (`:301-376`) seeds no `CodAmount` but emits `assignment.auto_assigned`. | Expected cash differs between the paths (partly re-derived at collection, `UpdateMyTaskStatus.cs:69-74`) | [SA-SOLID-011](../../FINDINGS.md#sa-solid-011) (Low); concurrency [SA-MOB-002](../../FINDINGS.md#sa-mob-002) (High, P1) | `PickupAssignmentService` in operations.Application, resolved by the worker from a scope |
| **Pickup COD amount / rider load SQL** | `PickupCommands.cs:196-202` vs `Logistics/Common/PickupCod.cs:17`; `operations.Application/Logistics/Common/RiderLoad.cs:26-48` vs `SharedDataModel/Logistics/RiderLoadHelper.cs:26-58` | Identical today (latent drift) | SA-SOLID-011 | Delete `RiderLoadHelper` and the duplicate COD helper |
| **Vertical → catalog kind** | `CatalogKind.cs:32-37` vs `TemplateProvisioner.cs:146-152` | Tiffin is mapped differently | SA-VERT-001 family | One map |
| **Minimum order value** | Server `MinOrderValueRule` (CreateOrder L215-217; pickup `PickupCommands.cs:91-93`); client `customer-mobile/src/lib/minOrder.ts` | The client gate is UX only, and its own header says the server re-checks (re-checked). The pickup re-check uses a **client-supplied** estimate (`PickupCommands.cs:81-93`). | [SA-SOLID-014](../../FINDINGS.md#sa-solid-014) (Low, P1); demo prices feeding the estimate: [SA-FE-010](../../FINDINGS.md#sa-fe-010) (Medium) | Re-price pickup lines server-side with `PriceResolver`; treat the client estimate as display only |
| **Client: order transition graph** | `admin-web/src/pages/orders/orderStatus.ts:43-76` (used unconditionally); `pos-web/src/lib/utils.ts:146-171` (fallback only; prefers server `allowedTransitions`) | admin-web shows wrong buttons for parcel orders today | [SA-FE-004](../../FINDINGS.md#sa-fe-004) (High); [SA-VERT-007](../../FINDINGS.md#sa-vert-007) (Medium) | The server's `allowedTransitions` is the only source; keep the client map as a fallback only |
| **Client: coupon discount** | `customer-mobile/app/(app)/booking/pay.tsx:95-110` displays the server's `discountPreview` (re-checked) | No duplication of the rule. **Positive.** | — | — |

**Consequence for verticals.** Each new vertical multiplies these copies. A salon order would need its own transition writes, payment projection and template mapping in each copy. This is why Q7 depends on consolidation first: 07 sequences SA-SOLID-001 as a prerequisite for SA-SOLID-004, and 01b puts both in Phase 3.

**Regression tests (consolidated).**
- For each of the 5 status paths: exactly one history row with the correct `FromStatus`, one `order.status_changed` outbox row, and a template that resolves.
- A rider completing a `disputed` order is rejected.
- Loyalty earns for both POS-delivered and rider-delivered orders.
- Online capture marks the order paid and creates no rider COD.
- Partial offline payment followed by COD produces correct totals.
- A replayed webhook does not double count.
- A coupon policy table test (rounding, cap, minimum, first order, eligibility) run against all three call sites.
- Auto and manual assignment produce identical `DeliveryAssignment` fields and events.
- A pickup with an inflated `EstimatedAmount` is rejected against a server-priced cart below the minimum.

---

## 15. Circular dependencies

**None at project level.** This is Verified: I re-read every csproj `ProjectReference` for this report. The graph is a DAG:

```
SharedDataModel            (no project refs)
ServiceDefaults            (no project refs)
Utilities                  → SharedDataModel
{core,operations,commerce}.Domain         → SharedDataModel
{core,operations,commerce}.Application    → own Domain, Utilities
{core,operations,commerce}.Infrastructure → own Application, Utilities, SharedDataModel
{operations,commerce}.WebApi → ServiceDefaults, Utilities, SharedDataModel, own Application, own Infrastructure
core.WebApi                → ServiceDefaults, Utilities, SharedDataModel, core.Infrastructure
Gateway                    → ServiceDefaults
AppHost                    → 3 WebApis, Gateway
```

- No Application project references another context's Application or any Infrastructure project.
- The only test oddity is `tests/operations.Tests → laundryghar.Gateway`. It is test-only and introduces no cycle.
- Runtime coupling exists without compile-time cycles: core → operations over MCP HTTP (01 SA-ARCH-011), and the outbox table shared between operations producers and commerce consumers.
- **Not analysed:** namespace-level cycles *inside* an assembly (for example within `operations.Application`). No tool was run for that.

---

## 16. Testability

| Aspect | Verified state | Evidence / ID |
|---|---|---|
| Constructor injection of interfaces | Handlers are unit-testable in principle; an InMemory pattern already exists | `tests/operations.Tests/Catalog/Import/ImportTestSupport.cs:23-27` (07) |
| Test inventory | CI run on `274b7af`: core.Tests 137/137, operations.Tests 426/426, operations.IntegrationTests 284/284 passed | 10b commands 8–9. 07's figures (64/311/243) count `[Fact]`/`[Theory]` attributes, not executed cases, so the two sets of numbers measure different things and do not conflict. |
| Commerce | **No test project** references `commerce.Application`/`commerce.Infrastructure` | [SA-ARCH-010](../../FINDINGS.md#sa-arch-010) (Medium, P1) |
| Critical untested handlers | 0 test hits for `CreateOrderHandler`, `UpdateMyTaskStatus`, `CancelOrderHandler`, `ValidateApplyCoupon`, `RazorpayWebhook`, `SubscriptionBilling`, `AutoDispatch`, `RiderRanker`, `NotificationDispatcher`, `RoutingChannelSender` | [SA-SOLID-010](../../FINDINGS.md#sa-solid-010); 10b |
| Integration suite integrity | 60 early `return`s across 17 files pass silently without Docker; only 13 of 33 migrations are applied by tests, and no `.down.sql` is run | [SA-QB-002](../../FINDINGS.md#sa-qb-002) (Medium, P1) |
| Hard-to-test constructs | Static `OAuth` endpoint class with inline EF; workers with business rules inside `BackgroundService` | SA-SOLID-010; §3.2 |
| Architecture tests | None (no NetArchTest or equivalent) | SA-SOLID-012, SA-ARCH-009 |
| Clients | admin-web and pos-web have no unit tests; customer-mobile jest 170/170 and rider-mobile 91/91 passed locally | 10b test inventory |

**Assessment.** The **design** is testable (DI, interfaces, small ports). The **coverage** does not reach the money and lifecycle paths where every High defect in this report sits. Sequencing:
1. Add `commerce.Tests` and handler parity tests **before** any refactor in §§3–14 (01 SA-ARCH-010; 01b Phase 1).
2. Fix the silent-pass pattern with `SkippableFact`, or fail when `CI=true`.

---

## 17. Disagreements and corrections preserved

| Item | Positions | Registry outcome |
|---|---|---|
| **SA-API-007 / SA-SOLID-002**: online capture never updates the order | 07 and 08: **High**. QA-B (10b): **Medium (latent)**, because no shipped client calls initiate or verify (`customer-mobile/src/api/commerce.ts:149-167` has no call sites). 01b: Phase 0. | Canonical SA-API-007 **High, P0**. The registry records the dissent ("blocks enabling online payment"). SA-SOLID-002's own registry `severity` field still reads High although QA-B corrected it. |
| **SA-API-003**: dead validators | 08 and QA-A: **High** (upload MIME and size rules exist only in dead validators). QA-B, 07 and 01: **Medium** (DB CHECKs, request-size limits and attachment serving bound the impact). | **Medium**, dissent recorded |
| **SA-ARCH-001**: shared model and overlapping ownership | 01: High. QA-B: Medium (structural; the harms are separate findings). | **Medium** |
| Validator count | 07, 08, 01b RC2 and both registry titles say **40**. QA-B: **39** (`PartnerBookingLocation` runs through `SetValidator`). | 39 is correct; the titles were not updated |
| Channel whitelist backstop | 07: "not backstopped". QA-B: **is** backstopped (`04_bc4_order_lifecycle.sql:38`). | Corrected (re-checked) |
| Notification cache TTL | 07: 5 minutes. QA-B: **60 s** (`NotificationSettingsCache.cs:19`). | Corrected (re-checked) |
| Q14 wording | 07: layering "sound". 01: layering "nominal". 01b: consistent by convention and unenforced. | All three: **Partially Supported** |

---

## 18. Root causes and roadmap alignment

| Root cause ([01b](specialists/01b-architect-challenge-review.md) §3) | SOLID facet | Findings in this report | Roadmap phase (01b §5) |
|---|---|---|---|
| **RC2**: cross-cutting policy is opt-in | SRP / separation of concerns | SA-API-003 (dups SA-SOLID-005, SA-ARCH-005) | Phase 1: dispatcher validation pipeline |
| **RC3**: anemic shared model; invariants duplicated | Encapsulation, SRP, ISP | SA-SOLID-001/003/006/009/011, SA-API-006 (dup), SA-API-007, SA-MOB-001/016, SA-FE-004, SA-DB-010 | Phase 0: SA-API-007, SA-MOB-001. Phase 2: royalty status (SA-SOLID-003). Phase 3: `OrderTransitionService`, coupon policy, dispatch module, table ownership + architecture tests. |
| **RC9**: vertical as metadata, not modules | OCP, LSP | SA-VERT-001 (dup SA-SOLID-004), SA-VERT-004/006/007, SA-FE-004, SA-API-013 | Phase 0: hide non-operable templates (SA-VERT-002). Phase 3: `IVerticalModule`, creation from `Brand.VerticalKey`. Phase 5: salon module. |

Target architecture: a modular monolith with a separate worker host, keeping the shared DB with RLS. 01 and 01b compared and rejected microservices, DB-per-tenant and runtime plugin assemblies. Nothing in this report's recommendations needs more than that.

---

## 19. Observations for registry triage

These are not new IDs. They are citation corrections and one small client observation.

1. **Citation corrections in 07.**
   - `Dispatcher.cs` is 46 lines, so 07's `L74-L88`/`L74-L104` cannot be right. The correct range is `L15-45` (as in 01, 08 and 10a).
   - `ServiceCollectionExtensions.cs` is 35 lines, so 07's `L107` cannot be right. The registration is at `L14`.
   - The registry evidence for SA-SOLID-005 inherits these line numbers.
2. **The `IOperationsDbContext` "READ-ONLY" comment** applies only to `PartnerWalletAccounts` (L79), not to the commerce and finance sets (see §6.2). The SA-SOLID-009 evidence text should be narrowed. The finding stands.
3. **Titles.** SA-API-003's and SA-SOLID-005's titles still say "40 validators"; the corrected count is 39. SA-SOLID-007's text says the cache refreshes every 5 minutes; it is 60 s.
4. **Severity field.** The registry entry for SA-SOLID-002 shows `severity: High` with QA-B's Medium in `qa`. That is consistent with the orchestrator keeping High for canonical SA-API-007, but readers of the duplicate row may miss the dissent.
5. **Phase vs specialist priority.**
   - SA-SOLID-001: specialist P1, registry P3. The P0 rider-path part is covered by SA-MOB-001.
   - SA-SOLID-003: specialist P0 for franchise billing, registry P2. SA-QB-001's own text says "P0 together with SA-SOLID-003", but it is filed at P2.
   - SA-SOLID-014: specialist P3, registry P1.
   These follow 01b's roadmap, but the differences should be stated in the roadmap report.
6. **Client-side constant (Suspected, display only).** `customer-mobile/src/constants/config.ts:80` hard-codes `EXPRESS_SURCHARGE = 50`. `app/(app)/orders/tracking/[id].tsx:276-280` uses it to back-derive the displayed coupon discount. The server models express surcharge per item (`ExpressSurcharge` in `CatalogDtos.cs:202,222`; `ItemAuditSnapshot.cs:17`). Divergence would only affect a displayed estimate. I did not trace how the server computes the pickup estimate's express fee, so this is unverified.

---

## 20. Not verified

- Runtime behaviour of every item above (no .NET SDK, no Docker). In particular: the SA-SOLID-013 cast result, which `system_settings` row wins in SA-API-012, and the event skipping in SA-ARCH-006.
- Whether any live tenant has online order payment enabled (decides whether SA-API-007 is live or latent).
- Whether the remaining 37 dead validators, beyond CreateOrder and RateOrder, have DB backstops (only samples were checked: quantity, channel).
- Namespace-level dependency cycles inside assemblies.
- SA-SOLID-006, -009, -010, -011, -012, -013 and -014 were not independently re-traced by QA. The registry accepts the specialist evidence. I re-checked only the lines listed in §1.4.

---

## Verdicts

**Q14 — OOP/SOLID in material areas: Partially Supported.** Layering direction, DI, the port and adapter abstractions and the fulfilment Strategy/Template-Method seam are sound, and the project graph has no cycles. But the order, payment, coupon and dispatch rules sit in an anemic, string-typed shared model and are duplicated across 2–5 handlers and services. That duplication has already produced verified defects (SA-SOLID-001, SA-API-007 [dup SA-SOLID-002], SA-SOLID-003, SA-SOLID-006, SA-SOLID-011). The validation pipeline is dead (SA-API-003), and none of this is enforced by architecture tests.

**Q7 — Add verticals without widespread modification: Not Supported.** The `IFulfillmentStrategy` seam exists and salon and recurring strategies are tested, but order creation never uses the brand's vertical (SA-VERT-001, dup SA-SOLID-004). Laundry vocabulary is hard-coded in shared handlers, invoices (SA-VERT-004), notification templates (SA-VERT-006, SA-API-013) and the admin-web transition table (SA-FE-004). Adding a vertical today means editing all of those places plus the shared model, seeders and SQL constraints (root cause RC9).
