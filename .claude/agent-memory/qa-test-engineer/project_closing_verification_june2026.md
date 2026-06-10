---
name: closing-verification-june2026
description: Final closing verifier pass — gap-remediation initiative; tasks #17/#33; all gates June 2026
metadata:
  type: project
---

Final coordinated restart + closing verification sweep, 2026-06-10.

**Build / Test gate results:**
- dotnet build: 0 errors, 22 warnings (all pre-existing NU1510/NU1603)
- Backend tests: 8 projects, 528 total — Commerce 44, Engagement 40, Finance 41, Identity 54, Logistics 7, Orders 141, ServiceDefaults 82, Worker 119 — ALL PASS
- customer-mobile: tsc 0 errors, 115 jest tests PASS
- rider-mobile: tsc 0 errors, 63 jest tests PASS
- admin-web: tsc 0 errors, build clean
- pos-web: tsc 0 errors, build clean
- Smoke: 22/22 PASS (confirmed twice — pre-billing-restart and final)

**Stack health:** 9×200 (5050, 5001–5008) + Worker (2 PIDs) — confirmed healthy. No recurring exceptions in final DCP stdout.

**Dev flags (all reverted to defaults at end):**
- Worker:SubscriptionBillingEnabled temporarily set to true, reverted to false/absent. Final stack has it disabled (confirmed in DCP log).

**Task #17 — Subscriptions live verdict:**
- Admin Commerce: POST /api/v1/admin/subscription-plans → 201 PASS; plan CRUD routes registered and functional
- Admin Finance: GET/POST/DELETE /api/v1/admin/platform-plans → 200/201/200 PASS
- Customer: GET /api/v1/customer/subscriptions/plans → 200 (shows active plan) PASS
- Customer: POST /api/v1/customer/subscriptions → 201 (status=pending, mandate created) PASS
- Customer: GET /api/v1/customer/subscriptions → 200 PASS
- Customer: POST /customer/subscriptions/{id}/cancel → 200 (cancelAtPeriodEnd=true) PASS

**DEFECT FOUND — Admin GET/PUT subscription-plans returns 500 (pre-existing, not new from #17):**
- Endpoint: GET /api/v1/admin/subscription-plans and PUT /{id}
- Cause: Pre-existing Wave-0 RLS defect — `RlsConnectionInterceptor` sets `app.current_brand_id = ''` for platform admin tokens (no brand_id JWT claim). RLS policy `(bypass_rls = 'true') OR (brand_id = ''::uuid)` — PostgreSQL evaluates both arms regardless of short-circuit, `''::uuid` cast fails with 22P02.
- Affected: ALL admin endpoints backed by `LaundryGharDbContext` when called by platform-admin tokens without a `brand_id` JWT claim. Workaround: none at API layer; requires RLS policy patch to use `NULLIF` or skip the cast when value is empty.
- Worker dunning: Same defect blocks `SubscriptionBillingService.GenerateDueInvoicesAsync` — every cycle fails with 22P02.
- Root cause location: `/laundryghar.SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs` line 52 (uses `_currentTenant.BrandId` which reads JWT claim only) + all RLS policies with the `brand_id = current_setting(...)::uuid` pattern.
- FIX REQUIRED: Either (a) `RlsConnectionInterceptor` should read `Items["brand_id_override"]` for platform admins, or (b) RLS policies should use `NULLIF(current_setting('app.current_brand_id',true),'')` to skip the cast when empty.

**Dunning dry-run:** BLOCKED by the 22P02 RLS defect above. Could not complete end-to-end billing cycle. The `SubscriptionBillingService` starts correctly (pollIntervalSeconds=60, maxDunning=2 confirmed), but crashes on first DB query.

**nameLocalized / features JSON-string pattern (#17 usability finding, not a blocker):**
Both `subscription_plans.name_localized` (Commerce) and `platform_plans.features` (Finance) are `jsonb` columns backed by C# `string` properties. Callers must pass a JSON-encoded string (e.g. `"{\"en\":\"...\"}"`), NOT a JSON object. This is a usability hazard — no validator enforces it; 22P02 surfaces as a raw 400. Document or add a validator.

**Task #33 fixes confirmed live:**
- POST /customer/addresses with label='work' → 422 with `"label must be one of: home, office, other."` PASS (not raw 23514)
- GET /customer/serviceability?pincode=12AB → 400 with `"Pincode must be exactly 6 digits."` PASS
- rider app_settings rows: 4 rows (2 brands × 2 platforms, app_type='rider') PASS
- mobile_app_config natural-key dupes: 0 PASS; unique index confirmed present

**DB artifacts left in DB after cleanup:**
- commerce.subscription_plans: QA-TEST-MONTHLY soft-deleted (deleted_at set, EF query filter hides it)
- commerce.customer_subscriptions: 0 rows remaining (hard deleted)
- commerce.payment_mandates: 0 rows remaining (hard deleted)
- finance_royalty.platform_plans: QA-SAAS-STARTER hard deleted (DELETE endpoint used, not soft delete)
- No kernel.outbox_events generated during test (subscription was in pending status throughout, no billing triggered)

**i18n JSON:** customer-mobile en/hi + rider-mobile en/hi — all valid JSON, confirmed.

**Why:** Final release-candidate quality gate for the gap-remediation initiative tree.
**How to apply:** The Wave-0 RLS uuid cast defect is the one remaining blocker; it affects subscriptions admin lane AND the Worker dunning cycle. Everything else is green.
