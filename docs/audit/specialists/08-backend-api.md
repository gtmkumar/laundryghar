# 08 — Backend, Database-access and API Specialist

## Scope and method

**Scope.** Application/API side of the .NET 10 backend (`backend/laundryghar/`): the YARP gateway, the three
consolidated hosts (`core.WebApi`, `operations.WebApi`, `commerce.WebApi`), the shared `laundryghar.Utilities`
cross-cutting layer (CQRS dispatcher, validation, exception handling, tenant middleware), the
`laundryghar.SharedDataModel` DbContext wiring (RLS interceptor, retry strategy), all in-process background
services, external integrations and webhooks, file storage, and the client call sites in `pos-web/` and
`customer-mobile/` needed to trace the workflows end to end. The DB agent owns the index / idempotency / RLS
matrices; idempotency and concurrency issues found in handlers are recorded here with `Related area: DB`.

**Method.** Read-only static tracing. For every workflow I followed client call → gateway route → host
`Program.cs` middleware order → endpoint group → authorization policy → validation → handler → SQL / EF →
external call → response → outbox / worker consumers. Commands used were `cat`/`sed`/`grep`/`find` plus two
small read-only Python one-liners (run with `-I`) that (a) listed secret-like config **key names** in
`appsettings*.json` without printing values, and (b) diffed the set of FluentValidation validator types against
the set of types that `ValidationFilter<T>` is attached to. Scratch output lives in
`/tmp/claude-0/-home-user-laundryghar/65b29198-109e-5a5c-ba03-c03109e35b59/scratchpad/backend-api/`.

**Could not verify.** No .NET SDK, no running PostgreSQL, no Docker, so nothing was built, run, or load-tested.
Every runtime claim below (race windows, rate-limit behaviour, AsyncLocal flow, RLS outcomes) comes from reading
code and config. Those claims are labelled `Partially Verified` or `Suspected` where reasoning goes beyond what the
code literally does. I did not check Razorpay, MSG91, WhatsApp or Expo API semantics against live sandboxes.

## Current-state summary

**Topology.** The gateway (`laundryghar.Gateway/Program.cs:89-121`) routes 9 path prefixes plus `/mcp` to 3 hosts
and strips the first segment. It does not validate tokens. It applies CORS, a global rate limiter partitioned by
brand or IP (`Program.cs:222-258`, `RateLimitPartitioning.cs:24-86`), response compression and security headers.
Each host validates RS256 JWTs itself: core in-process (`core.WebApi/Program.cs:301-318`), operations/commerce
through JWKS (`commerce.WebApi/Program.cs:136-158`). The middleware order is consistent across hosts:
`ForwardedHeaders(if enabled) → [core: RateLimiter] → ExceptionHandler → Authentication → (anon RLS-bypass shims)
→ TenantResolutionMiddleware → ImpersonationGuard → BrandSuspension → Authorization → OutputCache → endpoints`
(`core.WebApi/Program.cs:507-607`, `operations.WebApi/Program.cs:142-179`, `commerce.WebApi/Program.cs:339-395`).

**Endpoint style.** Minimal APIs are discovered from `IEndpointGroup` classes. Every endpoint file carries
`RequireAuthorization` or `AllowAnonymous`; a scan of all `*/Endpoints/*/*.cs` found no unmarked file. Routes
are hard-coded `/api/v1/...` with no versioning library. Responses use a custom envelope
(`Response{status,message{errorTypeCode,errorMessage,responseMessage}}`) from
`laundryghar.Utilities/Middlewares/ExceptionsMiddleware/ExceptionHandler.cs:198-232`, not RFC 7807.

**CQRS.** `AddCustomCQRS` (`laundryghar.Utilities/CQRS/Extensions/ServiceCollectionExtensions.cs:10-34`) registers
only the plain `Dispatcher` (`Dispatcher/Dispatcher.cs:15-45`). That dispatcher resolves the handler and calls it
directly, so **no pipeline behaviors run**. `BehaviorRegistrar.RegisterBehaviors` has no callers. Validation,
transaction, audit, caching, logging and performance behaviors are therefore dead code. Validation happens only
where an endpoint attaches `ValidationFilter<T>` (`Validation/ValidationFilter.cs:13-52`), and 40 validators are
never attached (SA-API-003). Transactions are explicit per handler through `ExecuteInTransactionAsync`, which wraps
`BeginTransaction` inside the Npgsql retrying execution strategy (`EnableRetryOnFailure(3)`,
`SharedDataModel/DependencyInjection.cs:86-104`; `commerce.Infrastructure/Persistence/CommerceDbContext.cs:103-115`).

**Tenancy at the API layer.** Brand comes from the JWT `brand_id` claim. Platform admins get `bypass_rls` and an
optional `X-Brand-Id` override (`TenantResolutionMiddleware.cs:30-75`). The `RlsConnectionInterceptor` writes every
GUC on every connection open (`RlsConnectionInterceptor.cs:57-121`), which is a solid control. Anonymous webhook,
auth and signup paths set `bypass_rls` explicitly (`core.WebApi/Program.cs:543-561,620-646`;
`commerce.WebApi/Program.cs:355-366`). Background workers in the commerce host bypass RLS only inside a
positively-marked `WorkerScope` (`commerce.Infrastructure/Worker/WorkerScope.cs:118-143`,
`CommerceHostCurrentTenant.cs:80-90`).

**Concurrency.** The backend has **no optimistic concurrency at all**: a grep for
`IsConcurrencyToken|IsRowVersion|ConcurrencyCheck|xmin|DbUpdateConcurrencyException` returns zero hits outside
tests. Entities carry `Version` columns that handlers increment (`order.Version++`, `wallet.Version++`), but EF never
checks them. The only pessimistic lock in the codebase is the partner-wallet `SELECT … FOR UPDATE`
(`CommerceDbContext.cs:118-124`). Slot capacity is handled correctly with atomic conditional `UPDATE`s. Money
counters, balances and order status are read-modify-write.

**Events.** Handlers write `kernel.outbox_events` rows in the same transaction as the business write, which is a
correct transactional outbox. However, the relay's `IEventPublisher` is `LoggingEventPublisher` in **every**
environment (`commerce.WebApi/Program.cs:290`; `Worker/Stubs/LoggingEventPublisher.cs:10-30`), so there is no
broker. Real consumers are in-process pollers over the same table: notification mapping, loyalty earn and partner
debit. All run inside the single commerce host with no leader election.

## Workflow traces (W1–W8)

| # | Workflow | Real path traced (client → gateway → endpoint → auth/tenant → validation → handler → DB/external → response → async) | Key gaps (finding) |
|---|---|---|---|
| W1 | Customer booking (customer-mobile) | `customer-mobile/app/(app)/booking/pay.tsx:390-404` → `src/api/orders.ts:128-136` POST `/orders/api/v1/customer/pickup-requests` (**no Idempotency-Key**) → gateway `orders-route` strips `/orders` → operations `CustomerOrderEndpoints.cs:50,183-212` (`CustomerOnly` group policy, `CustomerOrderEndpoints.cs:32`) → customerId/brandId from token → **no validation filter** (`CustomerSchedulePickupValidator` at `PickupCommands.cs:773-814` is never run) → `CustomerSchedulePickupHandler.HandleAsync` (`PickupCommands.cs:338-495`): idempotency fast path when a key is supplied (`:350-358`), brand-scoped cart FK validation (`:366,522-568`), then `ExecuteInTransactionAsync`: atomic `UPDATE delivery_slots SET booked_count=booked_count+1 WHERE … booked_count<capacity AND brand_id=…` (`:402-414`), slot booking + pickup insert, 23505 on `pickup_requests_customer_idempotency_key` mapped to the existing row (`:464-492`) → 201/200 envelope. No outbox event on pickup create. A laundry booking becomes an order later, when staff convert it after weighing. | 003, 018, 019 |
| W2 | POS order (pos-web) | `pos-web/src/api/orders.ts:38-50` POST `/orders/api/v1/admin/orders` with `Idempotency-Key` header (key also in body) → operations `AdminOrderEndpoints.cs:30-32` policy `permission:orders.create\|pos.order.create`, `ValidationFilter<CreateOrderRequest>` (**no validator exists for that type, so it is a no-op**) → `CreateOrderHandler` (`CreateOrderCommand.cs:51-805`): `RequireBrandId`, idempotency lookup `metadata @> {"idempotency_key":…}` (`:71-94`, **check-then-act, no unique index**), store/customer brand checks and `IsWithinScope` (`:98-115`), per-line `PriceResolver` (`:145-160`), coupon/loyalty/package/promotion reads and in-memory mutation (`:276-500`), order number from `order_lifecycle.next_order_number` (`:806-818`, atomic counter) → single `ExecuteInTransactionAsync` inserting order, items, addons, history, outbox `order.created`, coupon redemption + `CurrentUsageCount++`, loyalty ledger, package ledger, promotion counters (`:757-803`) → 201. | 003, 004, 005 |
| W3 | Status transitions → warehouse → delivery (admin/POS + rider-mobile) | Admin/POS: PATCH `/orders/api/v1/admin/orders/{id}/status` (`AdminOrderEndpoints.cs:33`, `permission:orders.status.update`, no validation filter) → `UpdateOrderStatusHandler` (`UpdateOrderStatusCommand.cs:36-155`): load by id+brand, `IsWithinScope`, strategy `IsKnownStatus`/`EnsureTransition` in memory (`:54-57`), set status, `Version++` (unchecked), history row, outbox `order.status_changed`, `OrderCancellationRefund.QueueAsync` on cancel, one `SaveChanges` (no transaction wrapper, no `WHERE status=@from`). Rider: rider-mobile → `/logistics/api/v1/rider/...` → `RiderSelfEndpoints` (`RiderOnly`) → `UpdateMyTaskStatusHandler` (`UpdateMyTaskStatus.cs:37-260`): leg status written unconditionally (`:96`); on delivery-leg completion it sets `o.Status="delivered"` with history `FromStatus="out_for_delivery"` regardless of the actual status and **without calling the strategy's `EnsureTransition`** (`:152-176`), adds a COD payment and `AmountPaid += cod` (`:186-228`), and emits outbox `delivery.completed`. Async: `NotificationMappingService` → notifications; `LoyaltyEarnService` consumes `delivery.completed`. | 005, 006 |
| W4 | Payment: Razorpay create → client → webhook/verify → paid; refunds | Customer: POST `/commerce/api/v1/customer/payments/initiate` (`PaymentsCustomer.cs:71-79`, `CustomerOnly`, **no validation filter**; `InitiatePaymentValidator` orphaned) → `InitiatePaymentHandler` (`CustomerPaymentHandlers.cs:35-86`): idempotency lookup → `IPaymentGateway.CreateOrderAsync` (Razorpay `v1/orders`; `DevPaymentGateway` in Development) using a **client-supplied amount and unvalidated `OrderId`** → insert `payments` (pending). Verify: `/verify` → `VerifyPaymentHandler` (`:130-177`): HMAC check; a mismatch **sets status `failed`**; a match sets `captured`. No order update, no outbox event. Webhook: anonymous POST `/commerce/api/v1/webhooks/razorpay` (`RazorpayWebhook.cs:26-46`, RLS bypass at `commerce.WebApi/Program.cs:355-366`) → `ProcessRazorpayWebhookHandler` (`RazorpayWebhookHandler.cs:70-310`): parse → lookup by `gateway_order_id` → per-brand HMAC secret, constant-time compare (`:103-150,365-377`) → `payment.captured` acts only if `pending` (`:192-206`), `payment.failed` marks `failed` → outbox event. **Nothing updates `orders.amount_paid`/`payment_status` from an online payment** (grep: only `RecordOfflinePaymentCommand.cs:174-175` and the rider COD path). customer-mobile never calls initiate/verify (`walletTopUp:false`, `customer-mobile/src/constants/config.ts:112-116`; no call sites). Refunds: admin `IssueRefundHandler` (`AdminPaymentHandlers.cs:77-200`), where the Razorpay refund call runs inside the retrying transaction (`:148-220`). Cancellation refunds are queued `pending` by `OrderCancellationRefund.cs:44-80` and **never executed**. | 007, 008, 009 |
| W5 | Tenant signup/onboarding | Anonymous POST `/identity/api/v1/signup/start` → `OtpSendCommand` (rate policy `auth`) → `/complete` (`Signup.cs:30-73`) with RLS bypass for `/api/v1/signup` (`core.WebApi/Program.cs:646`) → `CompleteSignupHandler` (`CompleteSignup.cs:60-235`): template lookup, OTP consume (salted HMAC, attempt cap, `:243-271`), duplicate-phone check (backed by global `UNIQUE(phone_e164)`, `database_scripts/02_bc2_identity_access.sql:23`) → one `ExecuteInTransactionAsync`: brand (generated code; **INR/IN/Asia/Kolkata/en-IN hard-coded**, `:106-128`), owner user, `TemplateProvisioner` (features + catalogue), trial `brand_platform_subscription` (`:198-226`) → 200 with brand code. Platform-tier renewals afterwards depend on `BrandPlatformBillingService` (W7b). | 010, 011, 021 |
| W6 | Login / token refresh | POST `/identity/api/v1/auth/password/login` (`Auth.cs:56-75`, group rate policy `auth`, `ValidationFilter<PasswordLoginRequest>`, RLS bypass on pre-auth path `Program.cs:543-549,620-623`) → `PasswordLoginCommand` → `ScopeResolver` claims → RS256 access token + refresh (HttpOnly cookie `lg_refresh`, path `/identity/api/v1/auth/refresh`, plus body). Refresh: `RefreshTokenHandler` (`RefreshToken/RefreshTokenHandler.cs:39-118`): lookup by hash → reuse detection revokes the family → user status check → mark old `RevokedAt`, mint new token in the same family, one `SaveChanges` (no conditional update). All `/auth/*`, `/customer/auth/*`, `/partner/auth/*`, `/signup/*` and `/oauth` traffic shares the `auth` limiter: **10 req / 60 s keyed on `Connection.RemoteIpAddress`** (`core.WebApi/Program.cs:208-217`). | 001, 020 |
| W7 | Background job: notification pipeline (highest tenant impact) | Any handler writes `kernel.outbox_events` (for example `order.status_changed`, `UpdateOrderStatusCommand.cs:112-147`) → `NotificationMappingService` (`Worker/Services/NotificationMappingService.cs:82-184`), worker scope (RLS bypass, all brands), watermark cursor `(occurred_at,id)` in `notification_event_cursors`, maps 6 event types to `engagement_cms.notifications_outbox` (template by brand/code/channel; **fallback bodies hard-code "Laundry Ghar"/₹**, `:423-437`) → `NotificationDispatcherService` (`NotificationDispatcherService.cs:65-283`) claims rows by re-read + `status='sending'` (no row lock), sends through `RoutingChannelSender` (`Channels/RoutingChannelSender.cs:64-169`), which resolves WhatsApp/SMS credentials from the **singleton, brand-agnostic** `NotificationSettingsCache` (`NotificationSettingsCache.cs:31-62`) → Meta Cloud API / MSG91 / Expo → `notifications_log`; exponential backoff to `failed`. In parallel, `OutboxEventRelayService` marks the same events `published` through the logging stub. (W7b, `BrandPlatformBillingService`, is traced in SA-API-010/011.) | 012, 013, 014, 015 |
| W8 | File upload (inspection photos / rider KYC) | Admin: POST `/warehouse/api/v1/admin/garment-inspections/{id}/photos` (`WarehouseInspections.cs:37-40,63-75`, `permission:fulfillment.inspect`, `RequestSizeLimit 11 MB`, **no validation filter**) → `UploadInspectionPhotoCommandHandler` (`UploadInspectionPhoto.cs:39-81`): brand check on the inspection → `IFileStorageProvider.SaveAsync` with key `{brand:N}/inspections/{uuid}.{ext}` (`FileStorageKeyGenerator.cs:43-54`) → `LocalFileStorageProvider` writes to local disk (`LocalFileStorageProvider.cs:43-58`) → stores the **client-supplied `ContentType`** → 201. Stream: GET `/photos/{photoId}`, brand-filtered (`GetInspectionPhotoStream.cs:34-39`), returned with the stored content-type as an attachment. Rider KYC: POST `/logistics/api/v1/rider/documents` (`RiderSelfEndpoints.cs:69-72`, `RiderOnly`, 6 MB limit) → `UploadRiderDocument.cs:20-80`; its MIME/size/doc-type validator (`:108-120`) is orphaned. Cloud providers throw `NotSupportedException` (`FileStorageProviderFactory.cs:119-139`). | 003, 017 |

## Background jobs / hosted services inventory

All jobs except `OAuthCleanupService` and the ABAC log writer run inside **every** commerce host replica. None has a
leader lock or advisory lock (grep found no `pg_advisory`, `SKIP LOCKED` or `FOR UPDATE` in the workers).
`HostOptions.BackgroundServiceExceptionBehavior = Ignore` (`commerce.WebApi/Program.cs:196-197`), and no health check
covers worker liveness (SA-API-024). The first three rows were traced; the rest were skimmed for scope, trigger and
idempotency markers only.

| Job (file) | Trigger | Tenant handling | Retry / failure | Idempotency / duplicate safety |
|---|---|---|---|---|
| `NotificationMappingService` | poll `EventRelayPollIntervalSeconds` | `CreateWorkerAsyncScope` (RLS bypass, all brands) | per-event try/catch, **a bad event is skipped permanently** (`:164-171`) | Watermark cursor (commit-order gap, SA-API-015). Multi-replica runs duplicate mapping. |
| `NotificationDispatcherService` | poll `NotificationPollIntervalSeconds` | worker scope; **credentials not per brand** (SA-API-012) | backoff `2^n` min, capped at 24 h, to `failed` at `MaxAttempts` | Claim via re-read + `sending` with no row lock; a row stuck in `sending` is never reclaimed (SA-API-014). |
| `OutboxEventRelayService` | poll | worker scope | backoff, `dead_letter` | Publishes to `LoggingEventPublisher` only (no broker). Same claim race. |
| `BrandPlatformBillingService` (opt-in) | poll `BrandPlatformBillingPollIntervalSeconds` | dunning pass: worker scope; **renewal pass: plain `CreateAsyncScope` (no bypass)** (`:62-69`) | chunk retry next cycle | `AnyAsync` pre-check + unique `(subscription_id,billing_period_start)` (`phase4_brand_platform_subscription.sql:56-57`) |
| `SubscriptionBillingService` (opt-in) | poll | worker scope (`:97`) | dunning attempts | `AnyAsync` invoice check + `billing_attempts.idempotency_key` (`:146,352-383`) |
| `LoyaltyEarnService` | poll 15 s | worker scope | per batch | Unique ledger `(order_id,transaction_type,brand_id)`; strict `OccurredAt >` watermark **skips ties/late commits** (`:95-115`; acknowledged in `PartnerBookingDebitService.cs:31-33`) |
| `PartnerBookingDebitService` | poll | worker scope | terminal cancel on insufficient balance | Per-event inbox marker (`outbox_consumed_events`) + unique wallet idempotency key. **Correct pattern** (positive control). |
| `RoyaltyGenerationService` (opt-in) | poll, day-of-month | worker scope | next poll | "skip franchises already invoiced" set lookup |
| `DailyReconService` (opt-in) | 5-min poll | worker scope | next poll | per-warehouse-per-day `AnyAsync` |
| `AutoDispatchService` (opt-in) | poll | worker scope | next poll | `AnyAsync` assignment check (no lock; multi-replica double-assign possible, Suspected) |
| `CustomerErasureService` | poll | worker scope | next poll | not traced |
| `RetentionSweepService` | daily | worker scope per sweep | next sweep | not traced |
| `MatviewRefreshService` | interval | worker scope | next tick | refresh is naturally idempotent |
| `PartitionMaintenanceService` | daily (default on) | not traced | next tick | DB function documented as idempotent |
| `OAuthCleanupService` (core) | hourly | not traced | next tick | delete-only |
| `ChannelDecisionLogWriter` (ABAC, all hosts when enabled) | channel consumer | n/a | not traced | n/a |

## External integrations inventory

| Integration | Where | Credentials source | Resilience | Webhook / verification | Notes |
|---|---|---|---|---|---|
| Razorpay Orders / Verify / Refund / Mandates (customer) | `commerce.Infrastructure/Gateway/RazorpayPaymentGateway.cs`, `SettingsFirstPaymentGateway.cs` | per-brand `system_settings` → env fallback (`commerce.WebApi/Program.cs:74-106`) | named client `razorpay`, 8 s/20 s timeouts, concurrency 15, circuit breaker | `/api/v1/webhooks/razorpay`, per-brand HMAC-SHA256, constant-time, fail-closed outside Development | No event-id dedupe or timestamp window; state-based idempotency only. Refund call sits inside the retried DB transaction (SA-API-009). Dev uses `DevPaymentGateway`. |
| Razorpay Payment Links (platform billing) | core `RazorpayLinkClient`; `ProcessPaylinkWebhook.cs` | platform setting → env | `razorpay-core` client | `/api/v1/webhooks/razorpay-paylink`, platform HMAC | Only `issued` invoices are marked paid (SA-API-011). |
| Razorpay Payment Links (RaaS partner) | `PartnerRazorpayLinkClient`, `ProcessPartnerPaylinkWebhook.cs` | platform setting → env | `razorpay-partner` client | `/api/v1/webhooks/razorpay-partner-paylink` | Not traced in depth. |
| WhatsApp Cloud (OTP) | `core.Infrastructure/Auth/Otp/RoutingOtpSender.cs:48-80` | brand-scoped row preferred, platform row fallback | `whatsapp-otp` 5 s/8 s | n/a | Brand-aware (positive). |
| WhatsApp Cloud (notifications) | `RoutingChannelSender.cs:98-130` | **singleton cache, arbitrary row across brands** | `whatsapp` client | n/a | SA-API-012 |
| MSG91 SMS (OTP / notifications) | `Msg91OtpDispatcher.cs`, `Msg91SmsChannelSender.cs` | as for WhatsApp | `msg91-otp` / `sms` clients | n/a | OTP log masks phone (`Msg91OtpDispatcher.cs:59`). |
| Expo Push | `ExpoPushChannelSender.cs:14-26` | env `Notifications:Push:AccessToken` (single platform token) | `push` client | n/a | One Expo project/token for all tenants, so white-label apps per brand are not supported. |
| Google Sign-In | core `GoogleIdTokenVerifier` (issuer/audience/signing-key validation, lines 33-139) | `GoogleAuth` config (platform-wide audience list) | default | n/a | Per-tenant OAuth client IDs need config edits. |
| SMTP email | `core.Infrastructure/Email/SettingsMailer.cs` | settings | n/a | n/a | Default sender name "Laundry Ghar" (`:97`). |
| Google Sheets import | `operations.WebApi/Program.cs:61-67` | none | 15 s timeout, redirects off (SSRF guard) | n/a | |
| MCP downstream (token forwarding) | `core.WebApi/Program.cs:412-436` | caller bearer token | **raw `HttpClientHandler`, no resilience** | n/a | Low risk. |

## Findings

### SA-API-001 — Production auth rate limiter collapses into one global bucket for all tenants (gateway IP)
- Category: Availability / multi-tenancy / security
- Severity: Critical
- Status: Partially Verified (code + deploy config read; not run)
- Evidence: `backend/laundryghar/core.WebApi/Program.cs:208-217` ("auth" policy partitions on `httpContext.Connection.RemoteIpAddress`, 10/60 s); `:507` `UseForwardedHeadersIfEnabled()` is a no-op unless `ForwardedHeaders:Enabled`; `deploy/docker-compose.yml:25` ("ForwardedHeaders stays OFF on the services — the gateway is the trusted edge") and `:83-84` (gateway only, commented out); `laundryghar.ServiceDefaults/Extensions.cs:267-287`. The policy covers `Auth.cs:56`, `CustomerAuth.cs:45`, `PartnerAuth.cs:33`, `Signup.cs:31-32` and `OAuth.cs:73,172,198,317,351`.
- Observed behaviour: in the shipped topology every request reaches `core` from the gateway container. `RemoteIpAddress` is therefore the gateway's IP for all users, and the "auth" limiter becomes **one 10-requests-per-minute bucket shared by every login, OTP send/verify, token refresh, Google sign-in, partner auth and signup across all tenants**. The same applies to `oauth_register` (3/hour platform-wide) and the anonymous `api_key` bucket. `LoginHistory.IpAddress` and OTP audit IPs also record the gateway IP.
- Reproduction / verification method: deploy the compose stack and send 11 `POST /identity/api/v1/auth/otp/send` from two different client IPs within 60 s. Expected: the 11th returns 429 regardless of source IP.
- Impact: platform-wide login and refresh outage at trivial load. A refresh 429 logs users out. One abusive client can lock out every tenant. Auth audit trails lose client IPs.
- Recommended remediation (smallest safe change): enable `ForwardedHeaders` on the three services with `KnownProxies`/`KnownIPNetworks` set to the gateway or compose network rather than clearing them. Remove `/refresh` endpoints from the 10/min policy and give them their own per-user or per-family limit.
- Regression tests required: integration test with two distinct `X-Forwarded-For` values from a trusted proxy that asserts independent partitions; test that refresh is not throttled by login traffic.
- Dependencies / priority: P0. Related area: SEC / OPS.

### SA-API-002 — Gateway rate-limit partition is attacker-controlled (bypass and targeted tenant throttling)
- Category: Security / availability
- Severity: High
- Status: Verified (code read)
- Evidence: `laundryghar.Gateway/RateLimitPartitioning.cs:24-31` (brand partition preferred), `:34-38` (unauthenticated `X-Brand-Id` header accepted as the key), `:40-63` (unverified JWT `brand_id`), `:80-86` (leftmost raw `X-Forwarded-For`, read directly from the header); `Program.cs:218` (brand bucket = 10× the IP limit).
- Observed behaviour: (a) A client can send a random GUID in `X-Brand-Id` on every request and get a fresh 3,000/min bucket each time, so per-IP limiting is fully bypassable. The class comment says forging "cannot … raise a limit", which is incorrect. (b) An attacker who knows a victim brand's id can send traffic with that `X-Brand-Id` and exhaust the victim tenant's whole budget, so all of that tenant's users get 429. (c) Spoofed `X-Forwarded-For` rotates the IP partition.
- Reproduction / verification method: unit test `RateLimitPartitioning.Resolve` with arbitrary header values. The behaviour follows directly from the code.
- Impact: the outer DoS backstop is ineffective, and cross-tenant denial of service is possible.
- Recommended remediation: key the brand partition only on a validated token (validate the JWT at the gateway, or move brand partitioning into the services after authentication). Never key on an unauthenticated header. Use the forwarded-headers middleware with known proxies instead of parsing XFF by hand.
- Regression tests required: `Resolve` ignores `X-Brand-Id` for unauthenticated requests; spoofed XFF from an untrusted hop is ignored.
- Dependencies / priority: P1. Related area: SEC.

### SA-API-003 — Validation pipeline not wired: 40 FluentValidation validators never execute; CQRS behaviors are dead code
- Category: Input validation / architecture
- Severity: High
- Status: Verified (code read + type-set diff)
- Evidence: `laundryghar.Utilities/CQRS/Extensions/ServiceCollectionExtensions.cs:14` registers only `Dispatcher`; `CQRS/Dispatcher/Dispatcher.cs:15-45` calls handlers directly; `CQRS/Registration/BehaviorRegistrar.cs:13-25` has no callers. The only validation path is `Validation/ValidationFilter.cs:13-52`. Orphaned validators include `CreateOrderValidator` (`operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:886-925`), `CustomerSchedulePickupValidator` (`Pickup/Commands/PickupCommands.cs:773-814`), `UpdateOrderStatusValidator` (`UpdateOrderStatusCommand.cs:160-175`), `UploadInspectionPhotoValidator` (`UploadInspectionPhoto.cs:84-115`, MIME allowlist + 10 MB), rider document validator (`UploadRiderDocument.cs:108-120`), `InitiatePaymentValidator`, `WalletTopUpInitiateValidator`, `RefreshTokenCommand`, `CancelOrder*`, `RateOrder`, `RequestPayout`, `RegisterCustomerPushToken` and others: 40 types in total. `AdminOrderEndpoints.cs:31` attaches `ValidationFilter<CreateOrderRequest>`, but no `AbstractValidator<CreateOrderRequest>` exists (likewise `UpsertSettingRequest`), so that filter is a no-op.
- Observed behaviour: rules such as "Quantity > 0", "channel in list", "≤ 50 cart items", "Amount > 0", "image/jpeg|png|webp only" and "≤ 5 MB KYC" are never enforced on these paths. DB CHECKs backstop some of them (for example `order_items.quantity > 0`, `database_scripts/04_bc4_order_lifecycle.sql:154`), returning 422 through `ExceptionHandler`, but many have no backstop: cart size, upload MIME and size limits for KYC, string lengths stored in jsonb.
- Reproduction / verification method: POST a pickup with 10,000 `cartItems`, or upload a `text/html` file as an inspection photo. Both are accepted per the handler code.
- Impact: unbounded payloads, wrong data stored, and security-relevant upload restrictions missing. Existing unit tests that exercise validators directly give false assurance.
- Recommended remediation: register `ValidationBehavior` (call `RegisterBehaviors`, or wire just validation into `Dispatcher`), and add an architecture test that fails when a validator's target type is never validated. Remove or rename dead behaviors.
- Regression tests required: a reflection test that every `AbstractValidator<T>` is reachable; endpoint tests for the cases above.
- Dependencies / priority: P1.

### SA-API-004 — POS CreateOrder idempotency is check-then-act on jsonb with no unique constraint (duplicate orders and double balance debits)
- Category: Idempotency / data integrity
- Severity: High
- Status: Verified (code read); race Not Tested
- Evidence: `CreateOrderCommand.cs:71-94` (lookup `metadata @> {"idempotency_key":…}`), `:685-689` (key embedded through string interpolation), `:757-803` (insert plus coupon, loyalty and package debits). No unique index on the order idempotency key exists in `database_scripts/` or `db/migrations|patches` (grep for `idempotency` indexes lists only payments, wallet_transactions, refunds, pickups and partner wallet). Client: `pos-web/src/api/orders.ts:38-50`.
- Observed behaviour: two concurrent submissions with the same key (POS double-tap, or an axios retry after a 401-refresh in `pos-web/src/api/client.ts:90-93`) both miss the lookup and both commit. The result is two orders, two coupon redemptions, double loyalty burn and double package debit. A key containing `"` produces invalid JSON (22P02 → 422), and a crafted key can inject extra metadata fields.
- Impact: financial double-charges and duplicate orders under real network conditions.
- Recommended remediation: add an `idempotency_key` column with a partial `UNIQUE (brand_id, idempotency_key)` (mirroring `pickup_idempotency_and_source.sql`) and catch 23505 to return the winner. Serialise metadata with `JsonSerializer`.
- Regression tests required: a parallel-request integration test (Testcontainers) asserting one order and one ledger debit.
- Dependencies / priority: P1. Related area: DB.

### SA-API-005 — No optimistic concurrency anywhere; balances and counters are lost-update prone
- Category: Concurrency / data integrity
- Severity: High
- Status: Verified (absence confirmed by grep; code paths read); races Not Tested
- Evidence: zero matches for `IsConcurrencyToken|IsRowVersion|ConcurrencyCheck|xmin|DbUpdateConcurrencyException` in non-test code. Read-modify-write examples: coupon `CurrentUsageCount++` after a separate max-uses check (`CreateOrderCommand.cs:292-293,771`); loyalty balance and `Version++` (`:388-415`); package `CreditValueUsed +=` (`:485`); wallet `Balance +=` (`CustomerWalletHandlers.cs:181`, `AdminPaymentHandlers.cs:184`); order `AmountPaid +=` (`RecordOfflinePaymentCommand.cs:174-175`, `UpdateMyTaskStatus.cs:225`); order status (`UpdateOrderStatusCommand.cs:59-64`). The only row lock is the partner wallet (`CommerceDbContext.cs:118-124`). `ExceptionHandler.cs:306-345` would map a concurrency exception to a generic 400.
- Observed behaviour: concurrent operations on the same customer, coupon or order overwrite each other (last write wins). Coupon global caps and per-customer caps can be exceeded, and wallet and loyalty balances drift from their append-only ledgers.
- Impact: money drift between the balance column and the ledger, promotional budget overrun, and silent status regressions.
- Recommended remediation: map Postgres `xmin` as a concurrency token on money-bearing aggregates (wallet, customer loyalty, coupon, customer package, order) and translate `DbUpdateConcurrencyException` to 409. Alternatively use atomic `UPDATE … SET x = x + @d WHERE … AND x + @d <= cap` as already done for slots.
- Regression tests required: parallel debit/credit tests asserting `balance == SUM(ledger)`; coupon cap race test.
- Dependencies / priority: P1. Related area: DB.

### SA-API-006 — Order state machine is not enforced atomically, and the rider delivery path bypasses it
- Category: Workflow integrity / multi-vertical
- Severity: High
- Status: Verified (code read)
- Evidence: `UpdateOrderStatusCommand.cs:43-64,152` (in-memory `EnsureTransition`, then an unconditional UPDATE through `SaveChanges`). `UpdateMyTaskStatus.cs:96` sets leg status with no transition check. `:152-176` sets `o.Status = "delivered"` and history `FromStatus = "out_for_delivery"` with no `EnsureTransition` and no check of the current status; `:156` guards only on `DeliveredAt == null`.
- Observed behaviour: an admin cancel racing a rider completion can leave the order `delivered` after `cancelled`, with a refund queued for a delivered order, or the reverse. A rider can complete delivery of an order in any status, and the history records a false `from` status. Leg status can move backwards (`completed` → `started`). The hard-coded laundry vocabulary ignores the per-vertical strategy that the rest of the code uses.
- Impact: corrupted order lifecycle, wrong refunds or COD, and the vertical seam is broken for the point-to-point, salon and recurring strategies.
- Recommended remediation: perform transitions as `UPDATE orders SET status=@to … WHERE id=@id AND status=@from` (or with an `xmin` token) and treat 0 rows as 409. Route the rider completion through `strategy.EnsureTransition` and `LifecycleStateFor`, and validate leg transitions.
- Regression tests required: concurrent cancel vs complete test; rider completion on a cancelled order returns 409.
- Dependencies / priority: P1. Related area: DB.

### SA-API-007 — Customer online payment lifecycle is broken end to end (captured money can be ignored; orders never marked paid)
- Category: Payments / correctness
- Severity: High
- Status: Verified (code read); Razorpay multi-attempt semantics not tested against sandbox
- Evidence: `RazorpayWebhookHandler.cs:200-206` (`captured` acted on only when `pending`; otherwise "acknowledged"); `:268-283` (`payment.failed` sets terminal `failed`); `CustomerPaymentHandlers.cs:158-166` (an invalid client signature sets `failed`); `:48-81` (client-supplied `Amount`, `OrderId` not validated against the customer's order or amount due). Grep for `AmountPaid`/`PaymentStatus` writes finds only offline and COD paths. `VerifyPaymentHandler` writes no outbox event despite the webhook comment (`RazorpayWebhookHandler.cs:215`). Client: no call sites of initiate/verify in `customer-mobile/app`, and `walletTopUp: false` (`customer-mobile/src/constants/config.ts:112-116`).
- Observed behaviour: (1) a first failed attempt (`payment.failed`), or a tampered or buggy `/verify`, makes the payment terminal. A later successful capture on the same Razorpay order is then ignored with 200, so the customer is charged and the system shows failed. (2) Even a captured payment never updates `orders.amount_paid` or `payment_status`. (3) Wallet top-ups are credited only by the client `/verify` call; the webhook does not credit the wallet, so an app crash after payment loses the credit.
- Impact: lost revenue reconciliation, wrongly unpaid orders, and support load. The online payment workflow is **not production-ready**.
- Recommended remediation: treat `failed` as non-terminal while the Razorpay order is open (accept `captured` from `pending|failed`). Bind amount and order server-side from the order's `amount_due`. Apply payment effects (order `amount_paid`, wallet credit) in one idempotent "on captured" routine called by both verify and webhook.
- Regression tests required: webhook sequence `failed` → `captured` ends captured and paid; verify plus webhook race credits exactly once.
- Dependencies / priority: P0 before taking online payments.

### SA-API-008 — Cancellation refunds are queued but never executed
- Category: Payments / workflow completeness
- Severity: High
- Status: Partially Verified (absence of a processor confirmed by grep across all hosts and workers)
- Evidence: `operations.Application/Orders/Common/OrderCancellationRefund.cs:44-80` inserts `payment_refunds` with `Status="pending"` and an outbox `refund.initiated`. The only other `PaymentRefunds` consumers are `AdminPaymentHandlers.cs:103-117` (reads). `NotificationMappingService.cs:433` sends "A refund … has been initiated".
- Observed behaviour: cancelling a paid order creates a pending refund row and notifies the customer, but no job or handler calls the gateway or credits the wallet for pending refunds.
- Impact: customers are told they are refunded when they are not.
- Recommended remediation: add an idempotent refund executor (worker plus inbox marker as in `PartnerBookingDebitService`) that moves `pending` to `succeeded|failed` through the gateway or wallet.
- Regression tests required: cancel a paid order and assert the refund is executed once.
- Dependencies / priority: P0 (with SA-API-007).

### SA-API-009 — Admin refund calls Razorpay inside a retried DB transaction; cumulative cap is racy
- Category: Payments / idempotency
- Severity: High
- Status: Verified (code read)
- Evidence: `AdminPaymentHandlers.cs:111-118` (cap computed outside any lock); `:148-220` (`ExecuteInTransactionAsync` → `_gateway.InitiateRefundAsync(...)` inside the lambda); `CommerceDbContext.cs:103-115` (the execution strategy re-runs the whole lambda on transient failure); `RazorpayPaymentGateway.cs:117-150` (no idempotency header or receipt on the refund call).
- Observed behaviour: a transient DB error at `SaveChanges` or commit re-executes the lambda and calls Razorpay a second time, giving a double refund. A gateway success followed by a DB failure leaves a refund at Razorpay with no record. Two concurrent refunds with different or no idempotency keys can together exceed the captured amount; wallet refunds then over-credit.
- Impact: direct money loss.
- Recommended remediation: persist the refund row as `processing` first and commit, call the gateway outside the transaction with a deterministic idempotency reference, then update. Lock the payment row (`FOR UPDATE`) when computing the cap.
- Regression tests required: simulated transient failure that asserts a single gateway call; parallel refunds that assert the cap holds.
- Dependencies / priority: P1. Related area: DB.

### SA-API-010 — Platform (tenant) subscription renewals run without the worker RLS bypass, so renewal invoices are never issued
- Category: SaaS billing / tenancy
- Severity: High
- Status: Partially Verified (code read plus .NET AsyncLocal semantics; not run)
- Evidence: `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs:62-69`: `RunDunningAsync` uses `CreateWorkerAsyncScope` (`:156`), but the renewal query uses `_scopeFactory.CreateAsyncScope()` (`:63`). `WorkerScope.cs:118-143` sets an `AsyncLocal` inside the callee. The marker set inside the awaited `RunDunningAsync` does not flow back to the caller. `CommerceHostCurrentTenant.cs:80-90` returns `BypassRls=false` and no brand when there is no HttpContext and no marker. The table is RLS-enabled with `brand_id = current_brand_id()` (`db/patches/phase4_brand_platform_subscription.sql:62-75`).
- Observed behaviour: under `app_user` the renewal query sees zero `brand_platform_subscription` rows, so no renewal invoices are issued and dunning never has anything to escalate. The class comment claims "RLS-bypassed, all brands".
- Impact: SaaS subscription revenue is never billed after the first period (the job is opt-in, so this applies once it is enabled).
- Recommended remediation: use `CreateWorkerAsyncScope()` on line 63, and add a test that fails if any `BackgroundService` calls `CreateAsyncScope` directly.
- Regression tests required: worker integration test under `app_user` that issues a renewal for a due subscription.
- Dependencies / priority: P1. Related area: DB.

### SA-API-011 — Paylink webhook ignores `past_due` invoices, so a late-paying tenant is never reinstated
- Category: SaaS billing / correctness
- Severity: High
- Status: Verified (code read across two files)
- Evidence: `core.Application/Identity/Entitlements/Commands/ProcessPaylinkWebhook.cs:69-77` marks paid only when `inv.Status == "issued"` and otherwise returns 200 "invoice already …". `BrandPlatformBillingService.cs:186-195` moves overdue invoices `issued` → `past_due`. Reinstatement requires no `issued|past_due` invoice (`:164-182`).
- Observed behaviour: a tenant who pays an overdue invoice by payment link (the normal dunning case) has the webhook acknowledged but the invoice stays `past_due`. The brand stays suspended, or is suspended after paying.
- Impact: wrongful suspension of paying customers.
- Recommended remediation: accept `issued|past_due` in the webhook, and verify that the paid amount and link id match.
- Regression tests required: webhook on a `past_due` invoice ends paid and the brand is reinstated on the next dunning pass.
- Dependencies / priority: P1.

### SA-API-012 — Notification worker sends every tenant's WhatsApp/SMS with one arbitrary tenant's credentials
- Category: Multi-tenancy / data protection
- Severity: High
- Status: Verified (code read); which row wins at runtime Not Tested
- Evidence: `commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:31-62` (singleton; RLS-bypassed query over all `system_settings` rows of category `whatsapp`/`sms`; `FirstOrDefault` with no brand filter and no ordering; the comment claims platform rows are preferred, but the code does not do that). `RoutingChannelSender.cs:98-130,132-158` ignores `request.BrandId`. Registered as a singleton at `commerce.WebApi/Program.cs:259`. Contrast with OTP, which is brand-aware (`RoutingOtpSender.cs:53-57`).
- Observed behaviour: once any brand saves its own WhatsApp or MSG91 settings, the worker may send all brands' customer notifications (order status, payment, refund) from that brand's sender account and phone number, using that brand's templates and DLT ids.
- Impact: cross-tenant data exposure (customers' phone numbers and order details go through another tenant's business account), wrong branding, and billing to the wrong tenant. This blocks white-label SaaS.
- Recommended remediation: resolve credentials per `request.BrandId` (brand row, then platform row, deterministically), and cache per brand.
- Regression tests required: two brands with distinct credentials; assert each notification uses its own brand's credentials.
- Dependencies / priority: P0 for multi-tenant launch. Related area: SEC.

### SA-API-013 — Hard-coded "Laundry Ghar" brand identity in customer-facing messages and emails
- Category: White-label / multi-tenancy
- Severity: Medium
- Status: Verified
- Evidence: `NotificationMappingService.cs:423-437` (all fallback SMS/WhatsApp bodies say "Laundry Ghar" and use ₹); `core.Application/Identity/Settings/EmailTemplates.cs:14-49`; `core.Infrastructure/Email/SettingsMailer.cs:82-97`; `RazorpayPaymentGateway.cs:183`.
- Observed behaviour: any tenant without a configured template sends customers messages branded as another company.
- Impact: brand leakage and loss of trust for every non-LaundryGhar tenant.
- Recommended remediation: render fallbacks with `{brand.name}` and the brand currency symbol, and source the email header from brand white-label settings.
- Regression tests required: fallback body for a brand named "X" contains "X" and not "Laundry Ghar".
- Dependencies / priority: P2.

### SA-API-014 — Workers have no claim locking or leader election; stuck rows are never recovered; there is no real event broker
- Category: Reliability / scalability
- Severity: Medium
- Status: Partially Verified (code read; multi-replica behaviour reasoned)
- Evidence: `NotificationDispatcherService.cs:314-336` and `OutboxEventRelayService.cs:101-120` "claim" by re-reading status then updating, with no `FOR UPDATE SKIP LOCKED`, no conditional update and no concurrency token. Only these two files write the statuses `sending`/`publishing`, and nothing reclaims them. `commerce.WebApi/Program.cs:290` registers `LoggingEventPublisher` unconditionally; `:196-197` sets `BackgroundServiceExceptionBehavior.Ignore`; there are no worker health checks (SA-API-024).
- Observed behaviour: with 2+ commerce replicas, both can claim and send the same notification and run every billing or dispatch loop in parallel. A crash between claim and outcome leaves the row in `sending`/`publishing` forever, so the message is lost. Outbox "publish" only logs.
- Impact: duplicate customer messages and duplicate work; the commerce host cannot be scaled horizontally safely.
- Recommended remediation: claim with `UPDATE … SET status='sending', locked_until=now()+x WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`, and reclaim expired leases. Either run workers in a single dedicated replica or use advisory-lock leader election per job.
- Regression tests required: two dispatcher instances against one DB send each row once; a stale `sending` row is retried.
- Dependencies / priority: P2 (P1 before scaling out). Related area: DB / OPS.

### SA-API-015 — Watermark cursors can skip events permanently (notifications, loyalty earn)
- Category: Event processing correctness
- Severity: Medium
- Status: Verified (code read; defect acknowledged in-code)
- Evidence: `NotificationMappingService.cs:119-178` (cursor `(occurred_at,id)`, where `occurred_at` is the application's `UtcNow` taken at handler start, not commit time; a failed event is skipped at `:164-171`); `LoyaltyEarnService.cs:95-115` (strict `OccurredAt >` without a tiebreak); `PartnerBookingDebitService.cs:20-33` documents both defects and fixes them only for the partner path.
- Observed behaviour: an event whose transaction commits after a later-stamped event has already been consumed is never seen. Loyalty additionally drops events that tie on `OccurredAt` beyond the batch.
- Impact: silently missing order and refund notifications and missing loyalty points under concurrent load.
- Recommended remediation: move both consumers to the `outbox_consumed_events` inbox pattern already used by `PartnerBookingDebitService`.
- Regression tests required: an interleaved-commit test where an event committed late is still processed.
- Dependencies / priority: P2. Related area: DB.

### SA-API-016 — Unbounded `pageSize` on list endpoints
- Category: Performance / DoS
- Severity: Medium
- Status: Verified
- Evidence: `laundryghar.Utilities/Common/PaginatedList.cs:11-42` (no maximum); endpoints only clamp the lower bound, for example `AdminOrderEndpoints.cs:52`, `CustomerOrderEndpoints.cs:74,221` and `WarehouseInspections.cs` `GetAll`. The only caps are in `commerce.WebApi/Endpoints/Analytics/AnalyticsAdmin.cs:75,89`.
- Observed behaviour: `?pageSize=1000000` is passed through to `Take()`.
- Impact: memory and DB pressure; one tenant's request can degrade the shared database for every tenant.
- Recommended remediation: clamp in `PaginatedList.CreateAsync` (for example to a maximum of 200) and document it in OpenAPI.
- Regression tests required: `pageSize=10000` returns at most 200 items.
- Dependencies / priority: P2.

### SA-API-017 — File storage is local-disk only and upload content checks are unenforced
- Category: Storage / security / operability
- Severity: Medium
- Status: Verified
- Evidence: `operations.Infrastructure/Storage/FileStorageProviderFactory.cs:119-139` (`s3`/`azure-blob` throw); `LocalFileStorageProvider.cs:43-58`; `PRODUCTION_ENV.md:160-178` (default `/tmp/laundryghar-uploads`, "Not suitable for Production"); `deploy/docker-compose.yml:48-58` (no volume for operations); `UploadInspectionPhoto.cs:51-66` stores the client `ContentType`; the MIME allowlist is in the orphaned validator (SA-API-003). `ResolvePath` prefix check without a trailing separator (`:95-99`) is mitigated because keys are server-generated.
- Observed behaviour: KYC documents and inspection photos are lost on container recreation and are not visible across replicas. Any content type is accepted (a request size limit of 6 to 22 MB still applies) and echoed back on download (with attachment disposition). KYC PII is stored unencrypted on local disk.
- Impact: data loss of evidentiary and compliance files; horizontal scaling breaks uploads.
- Recommended remediation: implement the S3/Blob provider with per-tenant prefixes (the key scheme already supports this), enforce the MIME allowlist plus magic-byte sniffing, and use server-side encryption.
- Regression tests required: upload of `text/html` is rejected; provider contract tests.
- Dependencies / priority: P1 for production. Related area: OPS / SEC.

### SA-API-018 — Customer app does not send an idempotency key for booking, so the server guard is unused
- Category: Idempotency (client/server contract)
- Severity: Medium
- Status: Verified
- Evidence: `customer-mobile/src/api/orders.ts:128-136` (no header); `customer-mobile/app/(app)/booking/pay.tsx:390-404` (no `idempotencyKey` in the body); server support at `CustomerOrderEndpoints.cs:192-194` and `PickupCommands.cs:350-358,464-492`.
- Observed behaviour: retries (timeout or 401-refresh-retry) create duplicate pickup requests and consume an extra slot unit each.
- Impact: duplicate bookings and slot exhaustion.
- Recommended remediation: generate a UUID per checkout attempt in the client and send it as `Idempotency-Key`.
- Regression tests required: client unit test that the header is present; server test already exists for the key path.
- Dependencies / priority: P2.

### SA-API-019 — Admin/POS-created pickups ignore slot capacity, but rejection releases capacity
- Category: Double booking / data integrity
- Severity: Medium
- Status: Verified
- Evidence: `PickupCommands.cs:38-50` (admin path calls `CreatePickup` directly, with no `booked_count` increment and no slot-brand check), `:104` (`PickupSlotId = req.SlotId`), `:657-672` (`RejectPickup` decrements `booked_count` for any pickup with a slot).
- Observed behaviour: slots can be overbooked through the admin path, and rejecting those pickups decrements capacity that was never taken, which then lets customers overbook as well.
- Impact: double booking and capacity drift.
- Recommended remediation: route admin creation through the same atomic slot-increment transaction as the customer path.
- Regression tests required: admin booking on a full slot is rejected; booked count stays consistent after reject.
- Dependencies / priority: P2. Related area: DB.

### SA-API-020 — Refresh-token rotation is not atomic
- Category: Auth / concurrency
- Severity: Medium
- Status: Verified (code read); race Not Tested
- Evidence: `core.Application/Identity/Auth/Commands/RefreshToken/RefreshTokenHandler.cs:43-112` (read, then set `RevokedAt` in memory, insert child, `SaveChanges`; no `WHERE revoked_at IS NULL` condition or concurrency token).
- Observed behaviour: two concurrent refreshes with the same token both succeed, leaving two live child tokens and defeating single-use rotation. Conversely, sequential near-simultaneous refreshes from two tabs trigger reuse detection and revoke the family, causing a forced logout. This combines with SA-API-001's shared bucket.
- Impact: weakened theft detection and spurious logouts.
- Recommended remediation: `UPDATE refresh_tokens SET revoked_at=now() WHERE id=@id AND revoked_at IS NULL` and treat 0 rows as reuse. Optionally add a short grace window that returns the already-minted child.
- Regression tests required: parallel refresh test produces exactly one success.
- Dependencies / priority: P2. Related area: SEC.

### SA-API-021 — Single-region and laundry-only assumptions baked into the API
- Category: Multi-business / multi-vertical readiness
- Severity: Medium
- Status: Partially Verified
- Evidence: `CompleteSignup.cs:115-120` (INR / IN / Asia/Kolkata / en-IN forced for every new tenant); `UpdateMyTaskStatus.cs:159,173` (laundry statuses hard-coded); `LoyaltyEarnService.cs:94,109` (earn only on `delivery.completed`, so appointment-mode orders never earn); no customer appointment, booking or staff-slot endpoint exists for the salon strategy (grep `appointment` in `*/Endpoints` returns nothing; the strategy exists in `operations.Application/Fulfillment/Salon/SalonAppointmentStrategy.cs`); the customer booking API is pickup-centric (`CustomerOrderEndpoints.cs:50-54`).
- Observed behaviour: the platform can only onboard Indian tenants and fully serve laundry or parcel flows. Salon and other verticals have a state machine but no booking API or loyalty hook.
- Impact: blocks the "one vertical per tenant" target for anything other than laundry or parcel.
- Recommended remediation: take currency, country, timezone and locale from signup input or the vertical template; emit vertical-neutral completion events (`order.completed`) from strategies; add an appointment booking slice before selling salon.
- Regression tests required: signup with a non-IN template; loyalty earn on a salon completion.
- Dependencies / priority: P2.

### SA-API-022 — Inconsistent error contract; no API versioning
- Category: API contract
- Severity: Low
- Status: Verified
- Evidence: `ExceptionHandler.cs:198-232,368-369` (custom envelope; exception type names used as error keys); endpoints return bare `Results.NotFound()`, `Results.Unauthorized()` and `BadRequest(string)` (for example `CustomerOrderEndpoints.cs:73,84`, `RazorpayWebhook.cs:45`); the gateway assumes `application/problem+json` (`laundryghar.Gateway/Program.cs:141-156`); a `DbUpdateConcurrencyException` would become a generic 400 (`ExceptionHandler.cs:327-345`); no `Asp.Versioning` package (grep).
- Impact: clients must handle three error shapes, and breaking changes cannot be versioned per tenant.
- Recommended remediation: adopt `AddProblemDetails` with the envelope as an extension, return typed errors consistently, and map concurrency to 409.
- Dependencies / priority: P3.

### SA-API-023 — Development credentials and OTP master codes committed to appsettings
- Category: Secrets management
- Severity: Low
- Status: Verified (key names only; values not copied)
- Evidence: `core.WebApi/appsettings.Development.json`, `operations.WebApi/appsettings.Development.json` and `commerce.WebApi/appsettings.Development.json` contain non-empty `ConnectionStrings:Default` (user `app_user`, host localhost) and `ConnectionStrings:Admin` (user `postgres`, host localhost). `core.WebApi/appsettings.Development.json:21-23` contains `Otp:TestCode` / `Otp:CustomerTestCode`. Non-Development `appsettings.json` files contain no secrets. No `.env`, key or pem files are tracked (`git ls-files`). The startup guard rejects test codes only when `IsProduction()` (`core.WebApi/Program.cs:136-145`), so a Staging environment with these keys set would accept master OTPs.
- Impact: low (localhost dev values), but reused passwords or a mis-set environment name would expose them.
- Recommended remediation: move dev values to user-secrets or `.env`, and extend the guard to every non-Development environment.
- Dependencies / priority: P3. Related area: SEC.

### SA-API-024 — Health checks are shallow and the gateway aggregate probe cannot reach services in the compose topology
- Category: Operability
- Severity: Low
- Status: Verified (config read)
- Evidence: `laundryghar.ServiceDefaults/Extensions.cs:172-201` (only a "self" check, and endpoints are mapped only when `HealthChecks:Expose`); `deploy/docker-compose.yml:80` sets `HealthChecks__Expose` only on the gateway; `laundryghar.Gateway/HealthServicesEndpoint.cs:50-70` probes `{service}/health`.
- Impact: dead workers and DB outages are invisible, and `/health/services` reports services as down in production.
- Recommended remediation: add DB readiness and worker heartbeat checks; expose them on the internal network.
- Dependencies / priority: P3. Related area: OPS.

### SA-API-025 — Plaintext email addresses in logs
- Category: Privacy / logging
- Severity: Low
- Status: Verified
- Evidence: `GoogleLoginHandler.cs:78`, `InviteEmailSender.cs:46`, `SetPersonStatus.cs:85`, `SettingsMailer.cs:58,65,70`. By contrast OTP SMS logs mask the phone (`Msg91OtpDispatcher.cs:59`), and DevLog OTP output is Development-only (`OtpChannelPlanner.cs:47-48`).
- Impact: PII in centralised logs (DPDP minimisation).
- Recommended remediation: mask with the same helper used for phones.
- Dependencies / priority: P3. Related area: SEC.

## Positive controls verified
- **Webhook HMAC**: per-brand secret resolved from the matched payment, constant-time comparison, fail-closed outside Development (`RazorpayWebhookHandler.cs:103-150,365-377`); the RLS bypass applies only to exact POST routes (`commerce.WebApi/Program.cs:355-366`).
- **Slot double-booking guard**: atomic conditional `UPDATE … booked_count < capacity AND brand_id = …` inside the same transaction as the insert, with rollback on idempotency collision (`PickupCommands.cs:395-492`; reschedule `CustomerPickupCommands.cs:179-212`).
- **Customer pickup idempotency**: per-customer partial unique index plus 23505 → existing row (`PickupCommands.cs:464-512`; `db/patches/pickup_idempotency_and_source.sql:61-65`).
- **Order numbering**: atomic DB counter, not `COUNT+1` (`CreateOrderCommand.cs:807-818`).
- **RLS GUC hygiene**: every GUC is rewritten on each connection open, with an "unresolved" sentinel (`RlsConnectionInterceptor.cs:57-121`); worker bypass requires a positive marker (`CommerceHostCurrentTenant.cs:80-90`).
- **Partner wallet money path**: `FOR UPDATE` lock plus inbox-marker consumer, which is a correct no-skip, idempotent design (`CommerceDbContext.cs:118-124`; `PartnerBookingDebitService.cs:20-45`).
- **JWT validation**: RS256 pinned, issuer, audience and lifetime validated, 30 s skew, on all hosts (`core.WebApi/Program.cs:301-338`; `operations.WebApi/Program.cs:89-111`).
- **Exception handling** never leaks DB text; SQLSTATE 23505/23514/22P02 are mapped (`ExceptionHandler.cs:304-366`).
- **File keys** are server-generated and tenant-prefixed, and the photo stream is brand-filtered (`FileStorageKeyGenerator.cs:43-54`; `GetInspectionPhotoStream.cs:34-39`).
- **Store-scope (sub-brand) checks** in CreateOrder, UpdateOrderStatus and IssueRefund (`IsWithinScope`).
- **Resilience pipelines** (timeouts, concurrency limit, circuit breaker) on all payment and messaging HttpClients; the gateway forwarder also has its own (`ResilientForwarderHttpClientFactory.cs`).
- **Tenant data export** streams NDJSON using the caller's own tenant id, never a client-supplied one (`AdminCancellation.cs:51-97`).

## Open questions / not verified
- Runtime confirmation of the race findings (004, 005, 006, 009, 020) needs Testcontainers, and Docker was unavailable here.
- Whether production connects as `app_user` (RLS enforced) or as a superuser. If the latter, SA-API-010 disappears but RLS protections disappear with it. The DB agent should confirm.
- Razorpay behaviour for multiple payment attempts on one order (`payment.failed` then `payment.captured`) is assumed from public API semantics, not sandbox-tested.
- `AutoDispatchService`, `CustomerErasureService`, `RetentionSweepService`, `RoyaltyGenerationService` and `SubscriptionBillingService` were skimmed, not traced end to end.
- The tenant export is gated by `settings.manage` and keyed to the brand claim. Whether a franchise-scoped holder of `settings.manage` can export the entire brand was not traced (Authz agent).
- MCP downstream clients and the partner paylink webhook were not traced in depth.

## Verdict inputs
- **Q9 (backend side: production-grade multi-tenant API foundation)**: Partially Supported. Tenant context, RLS wiring, JWT and webhook HMAC are solid, but there is a platform-wide auth throttling defect (001), cross-tenant notification credentials (012), no concurrency control (005) and unwired validation (003).
- **Q15 (workflows reliable end to end)**: Not Supported. Online payments do not mark orders paid and can drop captures (007), cancellation refunds never execute (008), platform renewals and paylink reinstatement are broken (010, 011), and the state machine can be bypassed (006).
- **DB-Q3 (duplicate operations under concurrency/retries, app view)**: Partially Supported. Pickups, payments, refunds, wallet ledger and partner debits are unique-key backed, but POS orders (004), refund gateway calls (009), balances and counters (005), refresh rotation (020) and multi-replica workers (014) can duplicate or lose updates.
- **DB-Q4 (transaction boundaries / consistency)**: Partially Supported. Explicit `ExecuteInTransactionAsync` with a correct execution strategy and a transactional outbox exist, but external calls run inside retried transactions (009), consumers use lossy watermarks (015) and there is no optimistic concurrency (005).
