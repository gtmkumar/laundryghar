# 07 — OOP and SOLID Principal Engineer

## Scope and method

**Inspected:** the .NET backend under `backend/laundryghar/**`, meaning all 20 `.csproj` files, the CQRS kit in `laundryghar.Utilities/CQRS`, the `SharedDataModel` entities and DbContext, the three per-context DbContext interfaces and their adapters, the fulfilment-strategy seam, order, payment, coupon and dispatch handlers, notification workers, and the payment, storage and channel abstractions. On the client side I looked only at domain logic that copies server business rules: `admin-web/src/pages/orders/orderStatus.ts`, `pos-web/src/lib/utils.ts` and the customer-mobile coupon, minimum-order and estimate code.

**How:** I read the code and followed callers and DI registrations. I checked string literals against the DDL in `database_scripts/` and `db/patches/`. Commands I ran include:
- `wc -l` over all `*.cs` files. There is no `Migrations/` folder because the schema is DB-first.
- `grep` for every writer of `Order.Status`, `Order.PaymentStatus`, `Order.AmountPaid` and `OrderStatusHistory`.
- `grep` for every emitter and consumer of each outbox `EventType`.
- A comparison of every `AbstractValidator<T>` type against every `ValidationFilter<T>` type. The working files are under `scratchpad/solid/` (`validated.txt`, `filtered.txt`, `orphan.txt`).
- Member counts for the DbContext interfaces.
- Counts of public setters on entities.
- A check of the test inventory, counting `[Fact]` and `[Theory]` attributes.

**Not verified:** nothing was compiled, run or reproduced. There is no .NET SDK, no running Postgres and no Docker (see the brief). Every behavioural claim below comes from static tracing. Where a finding depends on runtime data (for example, which `system_settings` row Postgres returns first), I say so. "Q7" is read here as *"can a new business vertical or business rule be added without modifying core logic?"*. If the orchestrator defines Q7 differently, re-map that line.

## Current-state summary

**Layering and dependency direction (DIP).** The project graph has no cycles and is conventionally layered: `*.WebApi → *.Infrastructure → *.Application → *.Domain → SharedDataModel`, with `*.Application → laundryghar.Utilities → SharedDataModel` (csproj files read in full). No `*.Application` project references an Infrastructure project. Application handlers inject `ICoreDbContext`, `IOperationsDbContext` or `ICommerceDbContext`, never the concrete `LaundryGharDbContext`. A grep of the three `*.Application` trees finds the concrete context only in XML comments. Thin adapters implement these interfaces in Infrastructure, for example `operations.Infrastructure/Persistence/OperationsDbContext.cs:L22-L80`.

The isolation, however, is maintained by convention only:
- The three `*.Domain` projects are **empty** (each folder holds only a csproj that references SharedDataModel).
- `laundryghar.Utilities` is referenced by every Application project. It carries `FrameworkReference Microsoft.AspNetCore.App`, Npgsql, MailKit and SharedDataModel, so the concrete DbContext, `PostgresException` and `IFormFile` are all reachable from Application code, and some Application code does use them.
- No architecture tests exist (no NetArchTest or similar in `tests/`).

**Domain model (encapsulation).** `SharedDataModel/Entities` contains 143 EF entities with **3,556 `{ get; set; }` properties, zero private or init setters and zero behaviour methods**. Every status is a free `string`, for example `Order.Status`, `Order.PaymentStatus` and `Payment.Status` (`Entities/OrderLifecycle/Order.cs:L11-L159`). One `LaundryGharDbContext` maps 144 `DbSet`s across all bounded contexts (`Persistence/LaundryGharDbContext.cs:L34-L218`). Invariants are therefore enforced, if at all, inside each handler, so the same rule gets re-implemented in several places. Most of the High findings below come from that duplication.

**Real execution path for an order status change** (traced end to end). The UI calls the Gateway, which routes to `operations.WebApi` (for example `AdminOrderEndpoints.cs:L30-L32`). The endpoint applies its authorization policy and `ValidationFilter<TRequest>`, then calls `IDispatcher.SendAsync`. `Utilities/CQRS/Dispatcher/Dispatcher.cs:L74-L88` resolves the handler directly, with **no pipeline behaviours**. The handler:
1. loads the `Order` through `IOperationsDbContext`;
2. calls `ICurrentUser.IsWithinScope`;
3. resolves an `IFulfillmentStrategy` and calls `EnsureTransition`;
4. sets the entity fields by hand;
5. adds `OrderStatusHistory` and `OutboxEvent` rows;
6. calls `SaveChangesAsync`.

The outbox is later read by commerce workers (`NotificationMappingService`, `LoyaltyEarnService`). Five separate handlers each re-implement steps 3 to 6, and they differ from one another (SA-SOLID-001).

**The strongest OOP element is the `IFulfillmentStrategy` seam** (`operations.Application/Fulfillment/*`). It has an interface, an abstract template base, a dictionary-backed resolver, DI registration of four strategies (`DependencyInjection.cs:L33-L39`) and parity tests. The seam is only partly used, though: order creation and several handlers and clients bypass it (SA-SOLID-004).

## SRP: the 15 largest `.cs` files

The measure is `find … -name '*.cs' | xargs wc -l | sort -rn`. There are no EF migrations because the project is DB-first.

| # | Lines | File | What the size reflects | Verdict |
|---|---|---|---|---|
| 1 | 976 | `core.Infrastructure/Seeders/IdentitySeeder.cs` | Mostly seed tables for roles and permissions (for example L412-L433). Dev-only bootstrap. | Data/config. Acceptable. |
| 2 | 925 | `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs` | `CreateOrderHandler.HandleAsync` runs from **L51 to L806 (about 755 lines)**. It covers idempotency, store and customer guards, scope, settings resolution, per-line pricing, TAT, add-ons, minimum order value, coupon, loyalty burn, package debit, promotions, fare, numbering, Order/Item/History/Outbox construction and the transaction (section markers L62-L756). | **Genuine multi-responsibility.** SA-SOLID-010 |
| 3 | 886 | `core.WebApi/Endpoints/Identity/OAuth.cs` | OAuth/PKCE protocol, inline EF against the concrete context, token issuance and an inline HTML/JS consent page (L647+), all in a static endpoint class. The design is documented as deliberate (L19-L45). No tests. | Trade-off with weak testability. SA-SOLID-010 |
| 4 | 865 | `core.WebApi/Mcp/Tools/LaundryTools.cs` | 9 MCP tool adapters that call downstream HTTP, mostly description strings and formatting. | Adapter. Acceptable. |
| 5 | 814 | `operations.Application/Orders/Pickup/Commands/PickupCommands.cs` | 13 types (several commands, handlers and validators) in one file. | File organisation, not a god class. It contains duplicated rules (SA-SOLID-011). |
| 6 | 683 | `tests/operations.IntegrationTests/Rbac/RbacRlsFixture.cs` | Test fixture. | n/a |
| 7 | 646 | `core.WebApi/Program.cs` | Composition root (sections L41-L611) with 2 inline maps. | Config. Acceptable. |
| 8 | 597 | `commerce.Application/Commerce/Common/Dtos/CommerceDtos.cs` | 32 DTO records. | Data. Acceptable. |
| 9 | 551 | `commerce.Infrastructure/Worker/Services/SubscriptionBillingService.cs` | Billing business rules inside an Infrastructure `BackgroundService`. | Misplaced layer (SA-SOLID-009) |
| 10 | 528 | `operations.Application/Catalog/Customer/Self/Commands/SelfCommands.cs` | 9 handlers in one file. | File organisation. |
| 11 | 492 | `operations.Application/Catalog/Catalog/Dtos/CatalogDtos.cs` | DTOs. | Data. |
| 12 | 479 | `commerce.Infrastructure/Worker/Services/AutoDispatchService.cs` | Rider ranking and assignment rules (a logistics concern) in a commerce Infrastructure worker. | Misplaced and duplicated. SA-SOLID-011 |
| 13 | 475 | `tests/operations.IntegrationTests/Rbac/EntitlementEnforcementTests.cs` | Tests. | n/a |
| 14 | 467 | `commerce.Application/Finance/Expenses/Commands/ExpenseCommands.cs` | Several handlers in one file. | File organisation. |
| 15 | 462 | `commerce.Infrastructure/Worker/Services/NotificationMappingService.cs` | Event-to-template mapping plus hard-coded "Laundry Ghar" fallback copy (L423-L437). | OCP issue (SA-SOLID-001, SA-SOLID-004) |

Also outside the top 15: `UpdateMyTaskStatusHandler` (`operations.Application/Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs`, 399 lines, `HandleAsync` L37-L280). It mixes the leg state machine, COD, payout calculation, the order transition, Payment creation, and order payment-state and outbox writes.

## SOLID matrix

| Principle | Design expectation | Verified compliance | Verified violations | Defect or trade-off |
|---|---|---|---|---|
| **SRP** | One reason to change per handler or service. Business rules live in reusable domain or application services. | Most endpoints are thin: only 4 of 102 endpoint files touch a DbContext (core 3/35, operations 1/41, commerce 0/26). Most handlers are small vertical slices. | `CreateOrderHandler` (L51-L806). `UpdateMyTaskStatusHandler` (L37-L280). Business rules in Infrastructure workers (`AutoDispatchService`, `SubscriptionBillingService`, `RoyaltyGenerationService`). | Genuine defect where rules are duplicated as a result (001, 006, 011). |
| **OCP** | A new vertical, status, gateway or channel is added by extension through registration or config, not by editing core. | `IFulfillmentStrategy` and its resolver (`FulfillmentStrategyResolver.cs:L10-L44`), DI-registered (`DependencyInjection.cs:L33-L39`). `IPaymentGateway` decorator. `IChannelSender` composite. | `CreateOrderCommand.cs:L620-L622` hard-codes the parcel/laundry binary. Laundry status lists in `OrderQueries.cs:L30-L36`, `GenerateInvoiceCommand.cs:L38`, `RateOrderCommand.cs:L26`, `RateRiderCommand.cs:L22`, `NotificationChannelPreferencePolicy.cs:L47-L62`, `NotificationMappingService.cs:L428-L436`. `RoutingChannelSender.cs:L65-L97` uses if/switch on the channel. `SettingsFirstPaymentGateway.cs:L113-L127` always builds Razorpay. `admin-web/.../orderStatus.ts:L43-L76`. | Genuine defect for the multi-vertical target (004). |
| **LSP** | Implementations honour the contract, so substitution doesn't change correctness. | `LocalFileStorageProvider` honours `IFileStorageProvider`. Cloud providers fail fast at **startup** (`FileStorageProviderFactory.cs:L16-L37`) instead of throwing per call, so this is not an LSP break. `DevPaymentGateway` is registered only when `IsDevelopment()` (`commerce.WebApi/Program.cs:L80-L106`). | `LoggingChannelSender` (null object) reports success and is used in **all** environments for email, in_app and voice and when credentials are missing (008). `SalonAppointmentStrategy` inherits `ApplyTransitionEffects` keyed to the laundry vocabulary (silent no-op) and declares a sentinel `PostPickupStatus` (`SalonAppointmentStrategy.cs:L23-L25`) (004). | 008 is a genuine defect. The salon part is latent. |
| **ISP** | Consumers depend on small, purpose-specific interfaces. | `IPaymentGateway` (5 members), `IChannelSender` (1), `IFileStorageProvider` (4), `IFulfillmentStrategyResolver` (2). | `IOperationsDbContext` has 75 DbSets and 4 methods. `ICoreDbContext` has 51 and 2. `ICommerceDbContext` has 42 and 6. Each exposes other contexts' tables with **write** access. `IFulfillmentStrategy` (15 members) mixes the state graph with order-creation and pickup hooks. | Partly a trade-off (EF LINQ-first design, recorded in the interface XML docs). The cross-context write surface is the real problem (009). |
| **DIP** | Application depends on abstractions it owns. Infrastructure implements them. | csproj graph is clean. Handlers inject interfaces. Gateway, OTP, storage and channel abstractions live in Application. | Application reaches ASP.NET (`IFormFile` in `UploadProofPhoto.cs:L22` and 5 other commands) and Npgsql (`PickupCommands.cs:L10`, `L500-L508`), transitively through Utilities. Empty `*.Domain` projects. `IAuditWriter` in Utilities writes through the concrete context (`Auth/Audit/IAuditWriter.cs:L41-L48`). No architecture tests. | Mostly a trade-off. Low severity (012). |

## Duplicated business rules (server and client)

| Rule | Copies (evidence) | Observed drift |
|---|---|---|
| Order status transition plus side effects | `UpdateOrderStatusCommand.cs:L52-L117`, `CancelOrderCommand.cs:L48-L101`, `CancelOrderByCustomerCommand.cs:L45-L94`, `UpdateMyTaskStatus.cs:L152-L256` (delivery), `UpdateMyTaskStatus.cs:L361-L392` (pickup hops) | Different events, history rows, version bumps and state-machine enforcement (001) |
| Order payment state (`AmountPaid`, `PaymentStatus`) | `RecordOfflinePaymentCommand.cs:L174-L180`, `UpdateMyTaskStatus.cs:L225-L227`. **Never** set by online capture (`RazorpayWebhookHandler.cs:L208-L232`, `CustomerPaymentHandlers.cs:L169`) | "partial" status and Version++ appear in one path only. Online payments are ignored (002). |
| Payment "settled" status literal | Writers: `"captured"` (webhook, verify, offline L162), `Succeeded` = `"succeeded"` (COD, `UpdateMyTaskStatus.cs:L215`). Readers: `"completed"` (`RoyaltyCommands.cs:L102`, `RoyaltyGenerationService.cs:L198`), `"captured"\|"completed"` (`AdminPaymentHandlers.cs:L95`, `OrderCancellationRefund.cs:L49`) | `"completed"` is not a permitted DB value (`database_scripts/06_bc6_commerce.sql:L379-L381`) (003) |
| Coupon eligibility and discount | `CreateOrderCommand.cs:L268-L338`, `CustomerCouponHandlers.cs:L59-L150`, `CustomerPickupCommands.cs:L39-L100` | Rounding differs (commerce L110-L112 has no `Math.Round`). `IsFirstOrderOnly` and `CustomerEligibility` are enforced nowhere (006). |
| Pickup assignment | `PickupCommands.cs:L204-L278` and `AutoDispatchService.cs:L301-L376` | COD seeding happens in the manual path only. The outbox event is emitted on the auto path only (011). |
| Pickup COD amount | `PickupCommands.cs:L196-L202` and `Logistics/Common/PickupCod.cs:L17` | Identical today (011) |
| Rider load SQL | `operations.Application/Logistics/Common/RiderLoad.cs:L26-L48` and `SharedDataModel/Logistics/RiderLoadHelper.cs:L26-L58` | Identical today (011) |
| Vertical to catalog kind | `SharedDataModel/Enums/CatalogKind.cs:L32-L37` (Tiffin falls back to LaundryGarment; **unused**) and `TemplateProvisioner.cs:L146-L152` (Tiffin maps to Product) | Already divergent (004) |
| Order transition graph (client) | `admin-web/src/pages/orders/orderStatus.ts:L43-L76` (used unconditionally at `OrderDetailDrawer.tsx:L504-L505`) and `pos-web/src/lib/utils.ts:L146-L171` (fallback only; prefers server `allowedTransitions`, `OrderDetailPage.tsx:L50`) | admin-web ignores the server-computed `allowedTransitions` (`OrderQueries.cs:L128`, `L202`). Wrong buttons for parcel and salon orders (004). |
| Minimum order value | Server `MinOrderValueRule` (CreateOrder L215-L217; pickup `PickupCommands.cs:L91-L93`). Client `customer-mobile/src/lib/minOrder.ts` | The server re-checks, but in the pickup flow against a **client-supplied** estimate (015). |

## Findings

### SA-SOLID-001: Order status transitions are implemented separately in 5 write paths and have diverged (missed notifications, loyalty only via the rider app, state-machine bypass)
- Category: SRP / encapsulation / duplicated business rules
- Severity: High
- Status: Verified (static trace. Not run.)
- Evidence:
  - The writers are `UpdateOrderStatusCommand.cs:L52-L117`, `CancelOrderCommand.cs:L48-L101`, `CancelOrderByCustomerCommand.cs:L45-L94` and `UpdateMyTaskStatus.cs`. The rider handler writes the order in two places: delivery completion at `L152-L256`, which hard-codes `o.Status = "delivered"` at L159 and `FromStatus = "out_for_delivery"` at L173-L174, and pickup-leg hops in `AdvancePickupLegAsync` at L318-L397.
  - Event producers: cancels and admin updates emit `"order.status_changed"` (L93, L87, L117). Rider delivery emits `"delivery.completed"` (`UpdateMyTaskStatus.cs:L238`). Pickup hops emit **no outbox event** (L361-L392).
  - Consumer: `NotificationMappingService.cs:L109-L117` subscribes to `order.status_changed` and `order.cancelled`. `NotificationChannelPreferencePolicy.ResolveTemplate` (`L47-L62`) maps `("order.status_changed","delivered"|"picked_up"|…)` but has **no entry for `cancelled`**. The `("order.cancelled", _)` template is unreachable because nothing emits `order.cancelled` (grep across the backend). An unmapped template returns 0 (`NotificationMappingService.cs:L214-L221`).
  - `LoyaltyEarnService.cs:L94`, `L109` earns only on `"delivery.completed"`.
- Observed behaviour:
  - (a) Customers get **no** ORDER_DELIVERED notification when the rider completes a delivery, which is the main path.
  - (b) No ORDER_PICKED_UP notification when the rider collects.
  - (c) No cancellation notification on any path.
  - (d) Loyalty points are earned only when a rider completes a delivery. Staff or POS orders moved to `delivered` through `UpdateOrderStatus` never earn.
  - (e) The rider delivery path never calls `EnsureTransition`. It is gated only on `DeliveredAt == null` (L152), so an order an admin has moved to `disputed` or `returned` (both reachable from `out_for_delivery`, `LaundryProcessStrategy.cs:L46`) is silently overwritten to `delivered`, and the history row records the wrong `FromStatus`. The DB `orders_status_check` constraint was dropped (`db/patches/phase1_slice_b_order_lifecycle_state.sql:L75-L84`), so nothing below the application layer catches this.
- Reproduction / verification method: grep for `new OrderStatusHistory` and `LifecycleState =` (5 writers), and for every `EventType = "order.…"` emitter and its consumers. Read each path.
- Impact: customer communication failures and inconsistent loyalty accrual across channels. Audit history can be wrong. Each new vertical multiplies these copies.
- Recommended remediation (smallest safe change): add one application service, `IOrderTransitionService.TransitionAsync(order, toStatus, actor, reason, ct)`, that calls `strategy.EnsureTransition`, sets `Status`, `LifecycleState` and `Version`, applies `ApplyTransitionEffects`, adds the history row and **always** emits `order.status_changed` with `fromStatus` and `toStatus`. Route all 5 paths through it. Keep `delivery.completed` as an additional event. Add `("order.status_changed","cancelled")` to the template map, or emit `order.cancelled`. Decide whether loyalty should key off `LifecycleState == Completed`.
- Regression tests required: for each path (admin PATCH, admin cancel, customer cancel, rider pickup collect, rider delivery complete), assert exactly one history row with the correct `FromStatus`, one `order.status_changed` outbox row, and that the template resolves. Assert that a rider completing a `disputed` order is rejected. Assert that loyalty earns for both POS-delivered and rider-delivered orders.
- Dependencies / priority: P1. Prerequisite for SA-SOLID-004.
- Prior-doc cross-ref: `docs/MULTI_VERTICAL_BLUEPRINT.md` §§ L286 and L368 ("route ALL OrderStateMachine call sites") is only partly done. Related area: BUSINESS / NOTIFICATIONS.

### SA-SOLID-002: Online payment capture never updates the order's `AmountPaid` or `PaymentStatus`, so riders are told to collect COD on prepaid orders
- Category: Encapsulation / missing single owner of a derived state
- Severity: High
- Status: Verified (static: all writers enumerated, no DB trigger found. Not reproduced at runtime.)
- Evidence:
  - Every writer of `Order.PaymentStatus` and `Order.AmountPaid` (grep `PaymentStatus\s*=`): `RecordOfflinePaymentCommand.cs:L174-L176`, `UpdateMyTaskStatus.cs:L225-L227`, plus the initialisers at creation.
  - `RazorpayWebhookHandler.HandleCapturedAsync` (`L170-L240`) and the verify handler (`CustomerPaymentHandlers.cs:L141-L170`) update only the `Payment` row.
  - No SQL trigger on `payments` maintains `orders.amount_paid`. The only payments trigger is `trg_check_refund_cap` (`db/patches/payment_idempotency.sql:L76`).
  - Rider COD logic: `UpdateMyTaskStatus.cs:L118-L126` sets `CodAmount = AmountDue` when `PaymentStatus != "paid"`, and then `o.AmountPaid += cod` (L225).
- Observed behaviour: after a successful online payment the order still shows `pending` with the full `AmountDue`. The rider task (`RiderTaskMapper.cs:L119`) shows cash to collect. On completion a COD Payment row is created and `AmountPaid` is incremented again, double-counting revenue. The two remaining writers also disagree: offline sets `"partial"` and bumps `Version`; COD sets neither.
- Reproduction / verification method: grep the writers, read the webhook and verify handlers, grep the SQL for triggers.
- Impact: customers are charged twice or disputes follow. Settlement and reconciliation errors.
- Recommended remediation: add one `OrderPaymentProjector.ApplySettledPaymentAsync(order, amount)` used by the webhook, the verify handler, offline and COD. It recomputes `AmountPaid` from settled payments, sets `PaymentStatus` (pending, partial or paid) and bumps `Version`. Make it idempotent per payment id.
- Regression tests required: online capture marks the order paid and creates no rider COD; partial offline then COD gives the correct totals; replaying a webhook does not double count.
- Dependencies / priority: P0 if online order payment is live. Related area: PAYMENTS.

### SA-SOLID-003: Payment status is an unconstrained string. Royalty revenue filters on `"completed"`, which the DB never permits, so franchise royalties come out as 0
- Category: Encapsulation (string-typed state) / OCP
- Severity: High
- Status: Verified (static plus DDL. Not run.)
- Evidence:
  - The `CommercePaymentStatus` constants (`SharedDataModel/Enums/CommercePaymentStatus.cs`) have no `"completed"`.
  - The DB CHECK is `('pending','initiated','authorized','captured','succeeded','failed','cancelled','refunded','partially_refunded','disputed')` (`database_scripts/06_bc6_commerce.sql:L379-L381`). No patch adds `'completed'` (grep of `db/`).
  - The readers that need `"completed"` are `RoyaltyCommands.cs:L98-L107` and `RoyaltyGenerationService.cs:L195-L203`.
  - `AdminPaymentHandlers.cs:L95` and `OrderCancellationRefund.cs:L49` accept `"captured"|"completed"`, which excludes COD `"succeeded"` rows (`UpdateMyTaskStatus.cs:L215`).
- Observed behaviour: the automatic royalty worker and the manual royalty calculation (without `GrossRevenueOverride`) both sum zero payments. COD-settled payments are not refundable through the admin refund path.
- Reproduction / verification method: compare string literals with the DDL CHECK.
- Impact: franchisor royalty revenue is not billed. Finance reports are wrong.
- Recommended remediation: use the `CommercePaymentStatus` constants everywhere (forbid raw literals with an analyzer or banned-API rule) and add a `CommercePaymentStatus.Settled = { Captured, Succeeded }` set used by royalty, refund and reconciliation queries.
- Regression tests required: a royalty calculation over seeded captured and succeeded payments returns non-zero; a refund of a COD payment follows a defined rule.
- Dependencies / priority: P0 for franchise billing. Related area: FINANCE.

### SA-SOLID-004: The vertical extension seam exists but is bypassed. New verticals still need core edits on the server and in admin-web.
- Category: OCP (and LSP for inherited laundry defaults)
- Severity: High (against the multi-vertical SaaS target)
- Status: Verified
- Evidence:
  - `CreateOrderCommand.cs:L618-L627` chooses `FulfillmentMode` only from `isParcel` (PointToPoint or ProcessDeliver). `Order.VerticalKey` keeps its laundry default (`Order.cs:L44`, comment at `CreateOrderCommand.cs:L643-L646`).
  - `FulfillmentMode.DefaultFor(vertical)` (`FulfillmentMode.cs:L30-L36`) and `CatalogKind.DefaultFor` have **no callers**.
  - The Salon and Recurring strategies are registered (`DependencyInjection.cs:L37-L39`), but no flow can create an `appointment` or `recurring` order (grep).
  - Laundry status literals remain in shared handlers: `OrderQueries.cs:L30-L36` (active/history split; salon `completed` and `no_show` would stay "active"), `GenerateInvoiceCommand.cs:L38`, `RateOrderCommand.cs:L26`, `RateRiderCommand.cs:L22`, `NotificationChannelPreferencePolicy.cs:L49-L53`, and "Laundry Ghar" copy in `NotificationMappingService.cs:L428-L436`. `OrderLifecycleState.TerminalArray` (`OrderLifecycleState.cs`) exists for this purpose but is unused there.
  - `SalonAppointmentStrategy` doesn't override `ApplyTransitionEffects` (base `StateMachineStrategyBase.cs:L89-L102` is keyed to `OrderStatus`), so no completion timestamp is ever stamped.
  - The resolver silently falls back to laundry for unknown modes (`FulfillmentStrategyResolver.cs:L27-L30`).
  - Client side: `admin-web/src/pages/orders/orderStatus.ts:L43-L76` holds the laundry graph and points to a non-existent file (`laundryghar.Orders/Application/Common/OrderStateMachine.cs`, L4-L5). `OrderDetailDrawer.tsx:L504-L505` uses it instead of the server's `allowedTransitions`.
  - Mapping drift: `TemplateProvisioner.cs:L146-L152` sends Tiffin to Product, while `CatalogKind.DefaultFor` sends Tiffin to LaundryGarment.
- Observed behaviour: a salon brand cannot place appointment orders. If one were created, its completed bookings would never be invoiceable, rateable or archived, and admin-web would show laundry buttons.
- Reproduction / verification method: grep for callers and string literals, and read the strategies.
- Impact: every new vertical requires edits to CreateOrder, five or more handlers, the notification policy and admin-web. Q7 is not met.
- Recommended remediation (smallest): in CreateOrder, derive the mode from `FulfillmentMode.DefaultFor(brand.VerticalKey)` with a parcel override, and set `VerticalKey` from the brand. Replace the status lists with `LifecycleState` checks (`Terminal`, `Completed`). Make the resolver throw, or log loudly, on unknown non-null modes. Have admin-web use `order.allowedTransitions`. Delete one of the two catalog-kind maps.
- Regression tests required: create a salon-brand order and assert `appointment`/`booked`; run a salon order through to `completed` and assert it lands in "history" and is invoiceable and rateable; assert an unknown mode fails; add an admin-web unit test that the drawer renders the server transitions.
- Dependencies / priority: P1, after SA-SOLID-001. Related area: VERTICAL.
- Prior-doc cross-ref: `MULTI_VERTICAL_BLUEPRINT.md:L14` and `L234` claims are partly resolved but not complete.

### SA-SOLID-005: The CQRS pipeline behaviours are dead code, and 40 validators never run (including `CreateOrderValidator` and `RateOrderValidator`)
- Category: SRP / cross-cutting concerns / dead abstraction
- Severity: Medium
- Status: Verified
- Evidence:
  - `BehaviorRegistrar.RegisterBehaviors` (`Utilities/CQRS/Registration/BehaviorRegistrar.cs:L13-L25`) has no caller.
  - The registered `IDispatcher` is `Dispatcher` (`CQRS/Extensions/ServiceCollectionExtensions.cs:L107`), which calls handlers directly (`Dispatcher.cs:L74-L104`). `CommandDispatcher` and `QueryDispatcher`, which do run behaviours, are never registered.
  - Validation happens only through `ValidationFilter<T>` on endpoints (`Validation/ValidationFilter.cs:L13-L51`). Comparing `AbstractValidator<T>` against `ValidationFilter<T>` gives **40 validator types with no filter**, 34 of them typed on `*Command` or `*Query`, for example `CreateOrderValidator : AbstractValidator<CreateOrderCommand>` (`CreateOrderCommand.cs:L886-L925`).
  - The endpoint filters on `CreateOrderRequest` (`AdminOrderEndpoints.cs:L31`), for which **no validator exists**, so the filter passes (`ValidationFilter.cs:L28`).
  - `RateOrderValidator` (`RateOrderCommand.cs:L58-L68`) has the same problem.
  - `TransactionBehavior` (`L49-L50`) uses a bare `BeginTransactionAsync`, which the interface docs say `NpgsqlRetryingExecutionStrategy` rejects (`IOperationsDbContext.cs:L170-L178`), and no command implements `IUnitOfWorkCommand`.
  - `docs/referance.md:L43` claims "MediatR pipeline behaviors: Validation → Performance → Authorization". That is contradicted.
- Observed behaviour: the channel and JobType whitelists, `Quantity > 0` and `Score 1..5` are not checked at the API. Some rules are backstopped by DB CHECKs (`04_bc4_order_lifecycle.sql:L108`, `L154`), which the exception handler maps (`ExceptionHandler.cs:L16`). The others, such as the channel whitelist and the comment length on some paths, are not.
- Reproduction / verification method: compare the two type lists in the scratchpad and grep for the callers.
- Impact: validation that looks complete isn't run. Contributors relying on command validators get no protection. The dead transaction behaviour would fail if enabled.
- Recommended remediation: choose one mechanism. Either register `ValidationBehavior` by switching `IDispatcher` to the behaviour-aware dispatchers and drop the unused behaviours, or retype the 34 validators to the request DTOs. Delete `TransactionBehavior`, `IUnitOfWorkCommand`, `CachingBehavior` and the unused dispatchers, or make them use `CreateExecutionStrategy`.
- Regression tests required: an architecture test that every `AbstractValidator<T>` is reachable (T is bound by a `ValidationFilter<T>` endpoint, or the pipeline is active). Endpoint tests posting `quantity: 0`, `channel: "x"` and `score: 9` expect 422.
- Dependencies / priority: P1. Related area: API / QA.

### SA-SOLID-006: Coupon rules are implemented three times and have drifted. First-order and eligibility rules are never enforced, and validate-apply trusts client totals.
- Category: Duplicated business rules / encapsulation
- Severity: Medium
- Status: Verified
- Evidence:
  - The three copies are `CreateOrderCommand.cs:L268-L338` (with `Math.Round`), `CustomerCouponHandlers.cs:L59-L150` (no rounding, L110-L112) and `CustomerPickupCommands.cs:L46-L100` (with rounding).
  - `IsFirstOrderOnly` and `CustomerEligibility` are only read and written by admin CRUD (`CouponHandlers.cs:L90-L93`, `L153-L156`). The commerce handler's XML doc claims step 4 enforces first-order-only (`CustomerCouponHandlers.cs:L50`), but no code does.
  - `ValidateApplyCouponHandler` takes `OrderId` and `OrderSubtotal` from the client (`CommerceDtos.cs:L592-L597`), never loads the order, inserts a redemption and increments `CurrentUsageCount` (L121-L143).
- Observed behaviour: "first order only" coupons are usable on any order. Any authenticated customer can burn a coupon's global usage cap through `/customer/coupons/validate-apply` (live endpoint `CouponsCustomer.cs:L21`; the mobile app defines but doesn't call it, `customer-mobile/src/api/commerce.ts:L101-L109`).
- Reproduction / verification method: read the three copies side by side and grep the flags.
- Impact: promotion leakage, and the shared coupon budget can be exhausted by one customer.
- Recommended remediation: extract `CouponEligibilityPolicy.Evaluate(coupon, customerStats, subtotal)` (pure, in commerce.Application or shared) and call it from all three. Implement first-order and eligibility checks. Make validate-apply load the order (brand, customer and subtotal from the DB), or remove the endpoint.
- Regression tests required: unit tests of the policy (rounding, cap, minimum, first order, eligibility); an integration test that validate-apply rejects a foreign or mismatched order.
- Dependencies / priority: P2. Related area: BUSINESS / API.

### SA-SOLID-007: The notification worker uses another brand's WhatsApp or SMS credentials for every tenant
- Category: Abstraction misuse (tenant-agnostic cache behind a tenant-scoped setting)
- Severity: High
- Status: Verified (static. Which row Postgres returns first was not determined at runtime.)
- Evidence:
  - Credentials are stored per brand: `UpdateWhatsApp.cs:L27-L42` upserts with the caller's brandId, and `SettingsStore.cs` sets `ScopeType = "brand"` or `"platform"`.
  - The worker cache `NotificationSettingsCache.GetAsync` (`commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:L31-L65`) has no brand parameter. It queries all active `whatsapp/cloud` and `sms/provider` rows with **no `OrderBy`** and takes `FirstOrDefault`, despite the comment "Platform-level rows are preferred" (L44). The comment at L13-L15 admits brand overrides are "not yet supported".
  - `RoutingChannelSender.ResolveWhatsAppAsync` (`L99-L132`, cache call at L103) and `ResolveSmsAsync` (`L134-L169`, cache call at L138) use the result for every `ChannelSendRequest` regardless of `request.BrandId`.
- Observed behaviour: tenant A's customers can receive messages sent from tenant B's WhatsApp Business number or SMS account, and the result can change unpredictably as the 5-minute cache refreshes.
- Reproduction / verification method: read the code path from `NotificationDispatcherService.cs:L74` and `L172` through `RoutingChannelSender` to the cache.
- Impact: cross-tenant leakage of customer phone numbers and order data to another tenant's provider account, billing on the wrong account, and possible brand impersonation.
- Recommended remediation: key the cache by `request.BrandId` (as `SettingsFirstPaymentGateway` already does, `L83-L88`), resolving brand first, then platform (`BrandId IS NULL`), then env, with an explicit `OrderBy`.
- Regression tests required: two brands with different credentials; assert that each send uses its own brand's PhoneNumberId.
- Dependencies / priority: P0. Related area: TENANCY / INTEGRATIONS.

### SA-SOLID-008: The `LoggingChannelSender` null object breaks the `IChannelSender` contract in production, so undelivered notifications are recorded as "sent"
- Category: LSP
- Severity: Medium
- Status: Verified
- Evidence:
  - `Stubs/LoggingChannelSender.cs:L17-L31` returns success and logs `phone=` and `email=` at Information level.
  - It is registered unconditionally (`commerce.WebApi/Program.cs:L262`, `L287`). `RoutingChannelSender.cs:L80-L95` routes email, in_app, voice, any unknown channel, and WhatsApp or SMS without credentials to it.
  - The dispatcher treats any non-throwing return as success: it sets `Status = "sent"` (`NotificationDispatcherService.cs:L170-L225`).
- Observed behaviour: email notifications, and all WhatsApp and SMS sends for tenants without credentials, are marked `sent` with provider `logging-stub` and are never retried. The only trace is a Debug-level log line.
- Impact: silent customer-communication failure that operators can't see. PII also ends up in logs.
- Recommended remediation: outside Development, return a distinct outcome (for example a `ChannelSendResult` carrying `Delivered=false`) or throw a `ChannelNotConfiguredException`, and store the status as `suppressed` or `failed:not_configured`. Mask PII in the log line.
- Regression tests required: with no credentials in Production mode, the outbox row is not `sent`.
- Dependencies / priority: P1. Related area: NOTIFICATIONS / SEC.

### SA-SOLID-009: Anemic, fully mutable shared data model. Bounded contexts write each other's tables, and Domain projects are empty.
- Category: Encapsulation / domain invariants / bounded-context separation
- Severity: Medium (architectural)
- Status: Verified
- Evidence:
  - 143 entities, 3,556 public setters, 0 behaviour methods (`SharedDataModel/Entities/**`). One 144-set context (`LaundryGharDbContext.cs`). `core.Domain`, `operations.Domain` and `commerce.Domain` hold only csproj files.
  - `IOperationsDbContext.cs:L78-L108` exposes commerce and finance sets (`Payments`, `Coupons`, `CouponRedemptions`, `LoyaltyPointsLedger`, `PackageUsageLedger`, `CashBookEntries`). The "READ-ONLY here" note (L80-L83) is a comment only. CreateOrder writes coupon, loyalty and package ledgers, and the rider flow writes `Payments`.
  - In the other direction, `ICommerceDbContext.cs:L84` exposes `Orders` (written by `RecordOfflinePaymentCommand.cs:L174-L180`). Commerce Infrastructure workers write operations aggregates through the concrete context (`AutoDispatchService.cs:L92`, `L301-L376`).
- Observed behaviour: no type owns any invariant. SA-SOLID-001, 002, 003, 006 and 011 are concrete results of this.
- Impact: every new vertical or rule copies field-level mutation code. Ownership of money ledgers is unclear.
- Recommended remediation (proportionate, no rewrite): DB-first scaffolding is a reasonable trade-off, so keep it. Add small domain services, or entity partial-class methods (EF tolerates them), for the few high-value invariants: order transition (001), payment projection (002), coupon policy (006) and dispatch assignment (011). Narrow the cross-context `DbSet` exposure to `IQueryable<T>` read-only properties where the comment says read-only. Either delete the empty Domain projects or move the policies into them.
- Regression tests required: the ones listed under each dependent finding, plus an architecture test that handlers outside the owning context don't call `.Add` or `.Update` on a foreign context's sets.
- Dependencies / priority: P2 (incremental). Trade-off: database-first is deliberate (`LaundryGharDbContext.cs:L17-L33`). The defect is that nothing compensates for the missing invariant owner.

### SA-SOLID-010: God-handlers with no direct tests: `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `OAuth`
- Category: SRP / testability
- Severity: Medium
- Status: Verified
- Evidence:
  - `CreateOrderCommand.cs:L51-L806` is one method with five injected dependencies (L35-L39) and about 15 responsibilities (section markers L62-L756).
  - `UpdateMyTaskStatus.cs:L37-L280`.
  - `OAuth.cs:L47-L886` is static, uses the concrete context and renders HTML. `ResolveDefaultBrandIdAsync` (`L607-L626`) hard-codes a single default brand (`CustomerAuth:DefaultBrandCode`, defaulting to `LG-MAIN`). Related area: TENANCY.
  - Tests: `grep` for `CreateOrderHandler`, `UpdateMyTaskStatus`, `CancelOrderHandler`, `ValidateApplyCoupon`, `RazorpayWebhook`, `SubscriptionBilling`, `AutoDispatch`, `RiderRanker`, `NotificationDispatcher` and `RoutingChannelSender` in `tests/` returns 0 hits each. OAuth is not tested beyond settings (`tests/core.Tests/Auth/GoogleAuthSettingsTests.cs`).
  - There is **no commerce test project** (only `tests/operations.Tests/Commerce/QuotaUnitTests.cs`). Totals: 618 cases (core 64, operations 311, integration 243), concentrated on auth, ABAC, RLS, fulfilment strategies and catalog.
- Observed behaviour: the most business-critical code paths have no regression net. Defects 001 to 003 and 006 sit in exactly these untested areas.
- Recommended remediation: extract pure collaborators from CreateOrder: `IOrderPricingService` (lines and add-ons), `IDiscountPipeline` (coupon, loyalty, package, promotions), `IOrderFactory` (Order, history, outbox; also used by `CreateParcelOrderCommand.cs:L100-L200`). Unit-test them with the InMemory pattern already used (`tests/operations.Tests/Catalog/Import/ImportTestSupport.cs:L23-L27`). Add `tests/commerce.Tests`.
- Regression tests required: golden-total tests for CreateOrder (express, add-ons, coupon, loyalty, GST, unregistered franchise); rider-flow state tests; webhook idempotency tests.
- Dependencies / priority: P1 for tests, P2 for refactor. Related area: QA.

### SA-SOLID-011: Dispatch and assignment logic is duplicated across bounded contexts and has drifted
- Category: Duplicated business rules / misplaced responsibility
- Severity: Low
- Status: Verified
- Evidence:
  - Manual assignment `AssignPickupHandler` (`PickupCommands.cs:L204-L278`) seeds `CodAmount` (L242, L255) and emits no outbox event.
  - Auto assignment `AutoDispatchService.AssignPickupAsync` (`commerce.Infrastructure/Worker/Services/AutoDispatchService.cs:L301-L376`) seeds no `CodAmount` but emits `assignment.auto_assigned`.
  - Twin helpers: `RiderLoad` / `RiderLoadHelper` (paths in the table above), and `ResolvePickupCodAmount` in `PickupCommands.cs:L196` and `PickupCod.cs:L17`.
  - Partly mitigated: the rider "collected" step re-derives COD (`UpdateMyTaskStatus.cs:L69-L74`).
- Impact: drift in expected cash between assignment and collection for auto-dispatched pickups. Assignment rules live in a commerce worker, away from the logistics code.
- Recommended remediation: a single `PickupAssignmentService` in operations.Application, invoked by both the handler and the worker (the worker can resolve it from a scope). Delete `RiderLoadHelper` and the duplicate COD helper.
- Regression tests required: auto and manual assignment produce identical `DeliveryAssignment` fields and events.
- Dependencies / priority: P3. Related area: LOGISTICS.

### SA-SOLID-012: Dependency inversion holds by convention only. Application reaches ASP.NET Core and Npgsql through a catch-all Utilities project, and nothing enforces the layering.
- Category: DIP / ISP
- Severity: Low
- Status: Verified
- Evidence:
  - `laundryghar.Utilities.csproj` has `FrameworkReference Microsoft.AspNetCore.App` (L10), Npgsql, MailKit and a SharedDataModel reference. It spans 7,514 lines across CQRS, ABAC, auth, middleware, OpenAPI and email.
  - `IFormFile` appears in Application commands (`UploadProofPhoto.cs:L22`, `UploadInspectionPhoto.cs`, `UploadRiderDocument.cs`, `SubmitPickupInspection.cs`, `ItemImageCommands.cs`, `ItemImportParseCommands.cs`).
  - `using Npgsql` and `PostgresException` appear in `PickupCommands.cs:L10` and `L500-L508`. `operations.Application.csproj` references Npgsql directly.
  - DbContext interfaces have 46 to 79 members (ISP).
  - The unused legacy `ICurrentUserService` (`Utilities/Services/ICurrentUserService.cs`, 0 callers).
  - No architecture tests.
- Impact: low today. Handlers don't misuse the concrete context. But nothing prevents erosion, and Application can't be reused outside ASP.NET.
- Recommended remediation: add NetArchTest rules: Application must not depend on `*.Infrastructure`, `LaundryGharDbContext` or `Microsoft.AspNetCore.Http`. Replace `IFormFile` with a `(Stream, contentType, fileName)` value object at the endpoint. Move the unique-violation detection behind `IOperationsDbContext.IsUniqueViolation(ex)`. Delete `ICurrentUserService`.
- Regression tests required: the architecture tests themselves.
- Dependencies / priority: P3. Trade-off: largely acceptable.

### SA-SOLID-013: Settings-resolved providers lose their logger (`_logger as ILogger<OtherType>` always evaluates to null)
- Category: DI misuse
- Severity: Low
- Status: Verified
- Evidence: `SettingsFirstPaymentGateway.cs:L113-L127` (`Build`, cast at L124) passes `_logger as ILogger<RazorpayPaymentGateway>` where `_logger` is `ILogger<SettingsFirstPaymentGateway>`. The two types are unrelated sealed classes, so the covariant cast fails and the result is `NullLogger`. The same pattern appears in `RoutingChannelSender.cs:L118` (`WhatsAppCloudChannelSender`) and `L154` (`Msg91SmsChannelSender`).
- Observed behaviour: in production (non-Development) the Razorpay `LogError` calls (`RazorpayPaymentGateway.cs:L63`, `L133`, `L195`) and the WhatsApp sender logs are discarded when credentials come from DB settings. The exceptions still propagate.
- Recommended remediation: inject `ILoggerFactory` and call `CreateLogger<RazorpayPaymentGateway>()`.
- Regression tests required: a unit test that the built inner gateway receives a non-null logger.
- Dependencies / priority: P3. Related area: OBSERVABILITY.

### SA-SOLID-014: The pickup flow enforces minimum order value and sets the expected COD from client-supplied prices
- Category: Business rule placed on the client side
- Severity: Low
- Status: Verified
- Evidence: `PickupCommands.cs:L81-L93` computes `estimatedAmount = req.EstimatedAmount ?? Σ(EstimatedUnitPrice × qty)` from the request and runs `MinOrderValueRule` against it. The rider's expected cash comes from `EstimatedAmount` (`PickupCommands.cs:L196-L202`, `L242`). The server prices the actual order later in CreateOrder (L138-L209), which re-checks the minimum (L215-L217).
- Impact: the pickup-stage minimum can be bypassed (send a large estimate), and the rider COD expectation is client-controlled. Final billing is still computed server-side, which limits the damage.
- Recommended remediation: re-price cart lines server-side with `PriceResolver` when item IDs are present, and treat the client estimate as display-only.
- Regression tests required: a pickup with an inflated `EstimatedAmount` against a server-priced cart below the minimum is rejected.
- Dependencies / priority: P3. Related area: BUSINESS.

## Positive controls verified
- **Acyclic, correctly directed project graph.** No Application project references Infrastructure (all csproj files read). Handlers never inject `LaundryGharDbContext` (grep of the `*.Application` trees).
- **Thin endpoints.** 98 of 102 endpoint files delegate to `IDispatcher`. The 4 exceptions are `OAuth.cs`, `PartnerAuth.cs`, `AdminBrandDomains.cs` and `AdminSupportEndpoints.cs`.
- **Strategy pattern done well where it is used.** `IFulfillmentStrategy`, `StateMachineStrategyBase` (template method), dictionary resolver, DI multi-registration, plus parity, salon and recurring tests (`tests/operations.Tests/Fulfillment/*`). `UpdateOrderStatusCommand.cs:L52-L66` and both cancel handlers do enforce transitions through the strategy. `OpsQueuesQuery.cs:L115` uses the neutral `LifecycleState`.
- **Port and adapter abstractions** for the payment gateway (with a Dev/real split by environment and a per-brand settings-first decorator), OTP senders (routing composite), channel senders and file storage. Composition is favoured over inheritance: only 5 abstract classes in non-test code.
- **Safe raw-SQL seams.** `ExecuteSqlInterpolatedAsync` and `SqlQueryScalarAsync` take `FormattableString` (`IOperationsDbContext.cs:L150-L165`). Multi-step transactions run inside the retrying execution strategy (`ExecuteInTransactionAsync`). CreateOrder commits order, items, add-ons, history, outbox and ledgers in one transaction (L756-L806).
- **`ValidationFilter<T>` fails fast** when attached to an endpoint that doesn't bind T (`ValidationFilter.cs:L22-L26`), which guards against one class of mis-wiring.
- **The DB CHECK exception mapping** (`ExceptionHandler.cs:L16`, `L150-L197`) turns many constraint violations into client errors instead of 500s.

## Open questions / not verified
- Runtime confirmation of SA-SOLID-002 and 003 (needs Postgres plus the services). Whether online order payments are enabled in any live tenant.
- Which `system_settings` row Postgres returns first in SA-SOLID-007 (unordered query). The defect holds regardless.
- Whether a separate service outside this repo consumes `delivery.completed` and sends notifications. None was found in the repo.
- Whether the remaining 38 orphan validators (beyond `CreateOrder` and `RateOrder`) have DB-constraint backstops. I sampled only two.
- No build was run, so compile-level claims (the covariance cast in 013) rest on language rules, not execution.

## Verdict inputs
- **Q14 (consistent OOP/SOLID where it matters): Partially Supported.** The layering, DI, port and adapter abstractions and the fulfilment-strategy seam are sound. But the core order, payment, coupon and dispatch logic is a fully anemic, string-typed model with business rules duplicated across 3 to 5 handlers and services. That duplication has already produced verified defects (SA-SOLID-001, 002, 003, 006 and 011), and the advertised validation pipeline is dead (005).
- **Q7 (adding a vertical or rule without modifying core): Not Supported.** Salon and recurring strategies exist and are tested, but are unreachable. Order creation, list and history, invoicing, rating, notification templates and copy, and the admin-web transition table are laundry-hard-coded (SA-SOLID-004). Adding a vertical today means editing those places.
- **Supporting input for multi-tenant SaaS readiness:** the notification credential selection is not tenant-scoped (SA-SOLID-007, High), and the OAuth/MCP flow is bound to a single default brand (SA-SOLID-010).
