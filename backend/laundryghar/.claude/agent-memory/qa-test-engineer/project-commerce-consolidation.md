---
name: project-commerce-consolidation
description: Commerce service (11→3 consolidation) live QA 2026-06-13 — webhook HMAC-skip + snake_case bug, rider-performance 500, schema map, gotchas
metadata:
  type: project
---

Commerce service (port 5005) = former Commerce + Finance + Analytics + Worker, after 11→3 service consolidation. Live-QA'd 2026-06-13. See also [[project-commerce-bc6]] (the pre-consolidation BC-6 pass) and [[project-core-consolidation]] (Identity/Engagement/Mcp pass).

**Why:** First QA pass after Finance/Analytics/Worker were folded into the Commerce process. Confirms in-host background jobs run and money flows still pass.

**How to apply:** Re-run webhook + rider-performance checks on any Commerce PR; reuse the schema map below to skip rediscovery.

## Defects found (2026-06-13)
- **DEF-A (HIGH): `/api/v1/admin/analytics/rider-performance` returns HTTP 500.** `InvalidCastException: Column 'avg_duration_min' is null.` Entity `RiderPerformance` (laundryghar.SharedDataModel/Entities/Analytics/RiderPerformance.cs) declares `AvgDurationMin`, `RatingAverage`, `TotalKm`, `CompletionRate` as non-nullable `decimal`, but matview `analytics.mv_rider_performance` has NULL in all four for every row. EF throws on first NULL→decimal cast. Fix: make those four `decimal?`. Owner: dotnet-backend-developer (+ matview author). `/dashboard` is unaffected only because it doesn't query RiderPerformance.
- **DEF-B (MED, dev-only): Razorpay webhook HMAC verification SKIPPED in Development.** When `Razorpay:WebhookSecret` is empty (it is, in appsettings.json) AND no DB gateway-settings row supplies one, RazorpayWebhookHandler logs a warning and lets the request through with NO signature check. Proven live: a bad-signature, well-formed `payment.captured` payload returned 200. Fail-closed IS enforced in non-Development (handler line ~84), so prod is safe — but the dev skip means "bad HMAC → rejected" does NOT hold in dev. Owner: security-code-reviewer.
- **DEF-C (MED): Webhook can't parse real Razorpay payloads.** Handler deserializes with only `PropertyNameCaseInsensitive=true`, no snake_case naming policy. Razorpay sends `order_id`/`error_code` (snake_case) → bind to nothing → `entity.OrderId` null → every real webhook returns 400 "Missing gateway order/payment id." camelCase `orderId` binds fine (that's how DEF-B was proven). Fix: add `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` (or [JsonPropertyName] attrs). Owner: dotnet-backend-developer.

## Verified-good (no defect)
- Admin CRUD all pass: promotions, coupons, packages, subscription-plans (PATCH status + PUT/DELETE), payment-methods, loyalty-programs (singleton: one per brand, create blocked with 422 BusinessRuleException), expense-categories (no DELETE endpoint), expenses (no DELETE endpoint).
- Customer lane pass: packages browse/my, loyalty/balance, coupons browse, wallet + transactions, subscriptions plans/list, payment initiate→verify and wallet topup initiate→verify on DevPaymentGateway (status `captured`, `transaction_type='topup'` — BC-6 DEF-2 still fixed).
- Webhook idempotency works: replaying captured event = no-op, doesn't overwrite gateway_payment_id.
- Worker-in-host CONFIRMED running: `kernel.outbox_events` all status=`published` (0 pending/failed); `engagement_cms.notifications_outbox` rows status=`sent` with provider=`logging-stub`, sent_at ~4-5s after created_at. OutboxEventRelayService + NotificationDispatcherService are live in the Commerce process.
- Error shapes: 401 (no token), 403 (customer token on admin route), 404 (missing resource + malformed :guid). Gateway 8080 routes /commerce/* /finance/* /analytics/* all 200.

## Gotchas / environment
- Admin token (admin@laundryghar.local) has NO `brand_id` claim — it's platform-admin; brand comes from `X-Brand-Id` header. Omitting the header → 401 "Brand context required."
- `nameLocalized` on packages/subscription-plans/payment-methods MUST be a JSON object string e.g. `{"en":"Name"}`, NOT plain text. Plain text → 400 with RAW Npgsql `22P02 invalid input syntax for type json` leaked in the error envelope (minor error-shape defect: internal exception text surfaced to client).
- Customer OTP: request is `POST /api/v1/customer/auth/otp/send` with E.164 phone `+919999000003`; verify is `POST .../otp/verify` body `{"phone":"+91...","code":"123456"}` (field is `code`, not `otp`). OTP delivery does NOT write to `engagement_cms.notifications_outbox` (Identity uses its own WhatsApp→SMS→devlog path) — don't use OTP to test the CMS dispatcher worker.
- Soft-delete: package/coupon/subscription-plan DELETE sets `deleted_at` but leaves `status='active'`. Lists filter on `deleted_at IS NULL` so soft-deleted rows are correctly hidden from admin AND customer. The stale `status` is cosmetic only (low/trivial).
- No franchise-settlements read endpoint exists in Finance (spec asked for it) — Finance has franchise-subscriptions instead. Mark settlements N-A.

## DB schema map (laundry_ghar_db, real schema names — NOT what code namespaces suggest)
- `commerce.{coupons,promotions,packages,subscription_plans,payment_methods,loyalty_programs,payments,wallet_accounts,wallet_transactions}`
- `finance_royalty.{expense_categories,expenses,...}`  ·  `tenancy_org.{franchises,stores}`  ·  `order_lifecycle.orders`
- `kernel.outbox_events` (cols: status, published_at, publish_attempts — NO processed_at) · `engagement_cms.notifications_outbox` (cols: status, sent_at, provider)
- mv: `analytics.mv_rider_performance`, `analytics.mv_daily_store_revenue`, `analytics.mv_monthly_franchise_revenue`
- psql is SELECT-ONLY by policy — DELETE is blocked by the sandbox classifier. Clean up QA data via API DELETE endpoints only; report no-DELETE-endpoint leftovers.
