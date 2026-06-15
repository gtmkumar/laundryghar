---
name: project-commerce-core-migration
description: Commerce core sub-domain migration (payments/subscriptions/packages/wallet/coupons/loyalty + Razorpay gateway + webhook) into commerce.WebApi:5242 — gateway/webhook layering decisions
metadata:
  type: project
---

Largest Commerce slice (admin + customer + Razorpay webhook + payment-gateway infra) migrated into commerce.WebApi:5242. Mirrors [[project-commerce-finance-migration]] + [[project-commerce-analytics-migration]]. Non-derivable decisions:

**Payment gateway layering — interface in Application, impls in Infrastructure, wired in Program.cs.**
`IPaymentGateway` (+ result records `GatewayOrderResult`/`CreateMandateRequest`/`GatewayMandateResult`/`GatewayChargeResult`) lives in `commerce.Application/Common/Interfaces` so handlers inject it without referencing Infrastructure. Impls (`DevPaymentGateway`, `RazorpayPaymentGateway`+`RazorpaySettings`, `SettingsFirstPaymentGateway`) in `commerce.Infrastructure/Gateway`. Program.cs does the Dev-vs-prod selection (Dev→`DevPaymentGateway` singleton; else `Configure<RazorpaySettings>` + named **"razorpay"** `AddHttpClient` (base https://api.razorpay.com/, 30s) + `AddScoped<IPaymentGateway, SettingsFirstPaymentGateway>`). `SettingsFirstPaymentGateway` had to be made **public** (was `internal` in legacy when it lived in the same assembly as Program.cs).
- **Why:** layering rule — Application must NOT reference Infrastructure; the host owns env-gated registration.

**GatewaySettingsCache split into IGatewaySettingsCache (Application) + GatewaySettingsCache (Infrastructure).**
SEC-2 per-brand cache. The Razorpay webhook handler runs in Application and needs the cache, but can't reference the Infrastructure type — so a thin `IGatewaySettingsCache` abstraction lives in `Application/Common/Interfaces`. `GetAsync` was changed to take `ICommerceDbContext` (not the concrete `LaundryGharDbContext`) so it works through the Application surface. Registered in `AddCommerceInfrastructure` under BOTH the concrete type (for `SettingsFirstPaymentGateway`) AND the interface (`AddSingleton<IGatewaySettingsCache>(sp => sp.GetRequiredService<GatewaySettingsCache>())`). Keep [[project-consolidation-security-invariants]] SEC-2 ordering: webhook resolves payment by gateway_order_id FIRST, then `GetAsync(db, payment.BrandId, ct)`, then HMAC.

**Webhook RLS bypass = exact-route middleware in Program.cs, BEFORE auth + set again in the endpoint.**
`app.Use(...)` matches POST `/api/v1/webhooks/razorpay` exactly (not a prefix — SEC-3) and sets `Items["bypass_rls"]=true` before `UseAuthentication`, so the RLS interceptor sees it at connection-open. The endpoint also sets it defensively before dispatch. Endpoint is `RazorpayWebhook : IEndpointGroup` (RoutePrefix `/api/v1/webhooks/razorpay`, `AllowAnonymous`), reads raw body via `EnableBuffering`+copy before binding for HMAC.

**Manual transactions → `ICommerceDbContext.ExecuteInTransactionAsync` seam.** Legacy handlers using `_db.Database.CreateExecutionStrategy()`+`BeginTransactionAsync` (AdminWalletAdjust, WalletTopUpVerify, PurchasePackageVerify, ValidateApplyCoupon, RecordOfflinePayment) and IssueRefund (which used a BARE BeginTransactionAsync — latent retry-strategy bug, see [[project-retry-strategy]]) all route through the seam; captured-variable pattern for the result entity. RecordOfflinePayment reads `Order.AmountDue` (generated column) WITHOUT reload — preserved verbatim (legacy didn't reload).

**Customer id source moved to ICurrentUser at the endpoint.** Legacy customer endpoints read `sub`/`brand_id` claims via a local `GetIds(http)`; commands still take `CustomerId`/`BrandId` params (so the `new InitiatePaymentHandler(_db,_gateway)` shared-handler pattern stays verbatim), but endpoints now populate them from `ICurrentUser.UserId`/`BrandId` (401 when `UserId is null`). Shared handlers `InitiatePaymentHandler`/`VerifyPaymentHandler` inject only `(ICommerceDbContext, IPaymentGateway)` — no ICurrentUser — so the manual `new` still works.

**Validators retargeted Command→Request DTO** (ValidationFilter<TRequest> validates the body). Dropped `IdempotencyKey` NotEmpty rules (key is a command param the endpoint always supplies, not a body field). PATCH status validators target the body DTO (`PatchSubscriptionPlanStatusRequest`, `PatchCustomerSubscriptionStatusRequest`).

**NEW DbSets added** to ICommerceDbContext + CommerceDbContext: PaymentRefunds, PaymentMethods, Packages, CustomerPackages, PackageUsageLedger, Coupons, CouponRedemptions, Promotions, LoyaltyPrograms, LoyaltyPointsLedger, WalletAccounts, WalletTransactions, SubscriptionPlans, CustomerSubscriptions, PaymentMandates, Orders, Customers, OutboxEvents, SystemSettings. (Payments/CashBooks/CashBookEntries already existed.) `Customer` is in `Entities.CustomerCatalog` (not Users); subscriptions in `Entities.Commerce.Subscriptions`.

**csproj deltas:** commerce.Application gained Configuration/Hosting/Logging.Abstractions (webhook handler injects IConfiguration/IHostEnvironment/ILogger). commerce.Infrastructure gained Http/Options/Logging.Abstractions/EntityFrameworkCore and DROPPED `IsAotCompatible` (RazorpayPaymentGateway uses reflection-based `PostAsJsonAsync` — IL2026/IL3050).

**Deferred/skipped:** `CommerceSeeder.cs` (permission seeder) — out of scope, NOT migrated; permission codes (paymentmethod.manage, packages.manage, promotions.manage, coupons.manage, loyalty.manage, payment.read/record/refund, wallet.read/adjust, subscription.manage/read) assumed already seeded by the RBAC pass.

Boot smoke (no DB): openapi 200; all admin/customer routes 401; Analytics+Finance still 401 (no regression); webhook bad-sig→400 (fail-closed reject), no-sig→500 (Npgsql empty-connection at the DB-query step, NOT a DI failure — proves handler+cache+bypass resolved). 0 warnings, 0 errors.
