---
name: project-subscriptions-adr010
description: ADR-010 subscriptions module implementation — schema placement, gateway seam design, dunning worker, and DB patch canonicalization convention
metadata:
  type: project
---

## ADR-010 Subscriptions Module (Task #17)

Module A (commerce schema) + Module B (finance_royalty schema) + Worker dunning job.

**Schema placement:**
- Module A tables → `commerce` schema (subscription_plans, payment_mandates, customer_subscriptions, subscription_invoices, subscription_billing_attempts, subscription_usage_ledger)
- Module B tables → `finance_royalty` schema (platform_plans, franchise_subscriptions, franchise_subscription_invoices, franchise_subscription_events)
- MVs → `analytics` schema (consistent with all other MVs in the project)
- `platform_plans` has no RLS (global catalog, platform_admin only)
- Append-only tables (billing_attempts, usage_ledger, franchise_subscription_events) have no RLS

**Why:** Matches existing BC schema boundaries; analytics schema is the only one hosting MVs (convention from prior BC-9 work).

**DB patch canonicalization:**
Subscription schema patch (`db/patches/subscriptions_module.sql`) is NOT back-ported into `build_from_scratch.sh` or `apply_patches.sh`. Only `fk_patch_*.sql` FK-completion patches belong in `apply_patches.sh`. Feature schema patches are standalone files applied directly to the DB.

**Gateway seam for mandates:**
Added `CreateMandateAsync` / `ChargeMandateAsync` to `IPaymentGateway` in `laundryghar.Commerce`. Dev stub returns fake IDs; Razorpay impl uses `/v1/subscriptions`. `ChargeMandateAsync` returns a failed result (does not throw) on non-2xx so the caller can record the attempt and advance dunning. The Worker dunning job does NOT call the gateway directly (avoids cross-project coupling) — uses an in-process stub; production wiring is a deferred task.

**Worker dunning service:**
`SubscriptionBillingService` — opt-in via `Worker:SubscriptionBillingEnabled=false`. Pattern mirrors `RoyaltyGenerationService`: IServiceScopeFactory.CreateAsyncScope() per cycle, per-row isolation, outbox events.

Dunning ladder: active → past_due (attempt 1) → past_due (attempt 2..N-1) → suspended (attempt N ≥ MaxDunningAttempts). Success at any point resets dunning counters and emits `subscription.renewed` outbox event.

**`ComputeNextPeriod` is public static** — enables unit testing without DI/DB. Period math uses .NET `AddMonths/AddYears/AddDays` directly; `AddMonths` handles end-of-month clamping (Jan 31 + 1 month = Feb 28/29).

**Known deferred item:**
Production mandate charging from the Worker requires extracting a cross-service `ISubscriptionCharger` abstraction or an HTTP call to Commerce. Currently the Worker stub always succeeds. This is noted in PRODUCTION_ENV.md.

**Subscription number format:** `SUB-{yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}`
(Bug was present in original generation using invalid inline range indexer on string interpolation — fixed to use local variable.)

**How to apply:** When extending the subscriptions domain, check these constraints before modifying:
- `subscription_invoices.amount_due` is a GENERATED ALWAYS AS STORED column — mapped with `ValueGeneratedOnAddOrUpdate()`, property is `decimal?` (nullable)
- `CustomerSubscription.IntervalCount` is `short` not `int`
- `platform_plans` entity uses `ISoftDeletable` only (no `IAuditableEntity`) because the table was designed with manual created_by/updated_by columns, not the standard audit interface pattern

Related: [[project-worker-service.md]], [[project-shared-data-model.md]]
