# 03 — Subscription and SaaS Commercialization Specialist

Agent key: `subscription` · AREA code: `SUB` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw`

## Scope and method

**Primary target: (a) SaaS / platform subscriptions**, meaning a business pays the platform. The code has
**three** separate subscription models, and every finding below says which one it is about:

| Tag | Model | Payer → payee | Tables / code |
|---|---|---|---|
| **(a-brand)** | Brand platform tier ("module bundle") | Brand (tenant) → platform | `identity_access.module_bundle` (+price), `bundle_feature`, `brand_feature`, `brand_platform_subscription`, `brand_platform_invoice`; `core.Application/Identity/Entitlements/*`, `core.Application/Identity/Signup/*`, `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs` |
| **(a-franchise)** | Franchise SaaS plan (ADR-010 "module B") | Franchise → platform | `finance_royalty.platform_plans`, `franchise_subscriptions`, `franchise_subscription_invoices/events`; `commerce.Application/Finance/Subscriptions/*` |
| **(b)** | End-customer laundry subscription (ADR-010 "module A") | Customer → brand | `commerce.subscription_plans`, `customer_subscriptions`, `subscription_invoices`, `payment_mandates`, `subscription_billing_attempts`; `SubscriptionBillingService.cs`, `GatewaySubscriptionCharger.cs` |

I covered (b) only where it shares infrastructure with (a) (Razorpay gateway, webhooks, workers) or where it
affects entitlements.

**Inspected (read, not grepped-and-guessed):** migrations 0005–0008, 0011, 0015, 0016, 0019, 0021; patches
`apply_saas_billing_patches.sh`, `phase4_bundle_pricing.sql`, `phase4_brand_platform_subscription.sql`,
`phase4_brand_platform_invoice_paylink.sql`, `phase4_platform_billing_nav.sql`, `seeder_parity_r3sec1.sql`,
`payment_idempotency.sql`, `rls_proposal.sql` (helpers); `db/build_from_scratch.sh`, `db/migrations/README.md`.
Backend: `ScopeResolver`, `PermissionHandler`, `ApiAuthorizationResultHandler`, `FeatureNotInPlan`,
`FeatureCatalog`, `BrandSuspensionMiddleware`, `BrandStatusStore`, `TenantResolutionMiddleware` ordering in
`core.WebApi/Program.cs`, `CommerceHostCurrentTenant`, `WorkerScope`, `RlsConnectionInterceptor`,
`AuditSaveChangesInterceptor`, `PermVersionBumper`, all of `core.Application/Identity/Entitlements/*`,
`CompleteSignup`, `TemplateProvisioner`, `AdminEntitlements` endpoints, `RazorpayPaylinkWebhook` and
`ProcessPaylinkWebhook`, `RazorpayWebhook` and `RazorpayWebhookHandler`, `ProcessPartnerPaylinkWebhook`,
`RazorpayPaymentGateway`, `GatewaySubscriptionCharger`, `SubscriptionBillingService` (charge and invoice
paths), `FranchiseSubscriptionCommands`, `ApiKeyAuthentication` and `kernel.resolve_api_key`,
`IdentitySeeder` role grants, `WorkerOptions`, the appsettings files, `PRODUCTION_ENV.md`, and `deploy/.env.example`.
Clients: admin-web `api/client.ts`, `lib/apiError.ts`, `api/entitlements.ts`, `hooks/useEntitlements.ts`,
`pages/settings/SettingsPage.tsx`, `App.tsx` routes. I also grepped customer-mobile and pos-web for 402 handling.
Tests read (names and scope only): `EntitlementEnforcementTests`, `PlanChangeTests`, `BrandDunningTests`,
`BrandSuspensionMiddlewareTests`, `EntitlementConfigTests`.

**Not done:** I did not build or run anything. There is no .NET SDK, Docker is unavailable, and I started no Postgres
server. All behaviour claims come from reading code. Where a claim depends on runtime semantics (for example
AsyncLocal flow, or RLS evaluation under `app_user`), it is labelled **Partially Verified** or **Suspected**.
Razorpay API behaviour is stated from general knowledge of the public API and was not checked against the network.

## Current-state summary

### (a-brand): the model that drives entitlements

**Plan catalogue.** `identity_access.module_bundle` is the priced tier. It has price, `billing_interval`
(monthly/quarterly/half_yearly/yearly), currency and `is_public`
(`db/patches/phase4_bundle_pricing.sql:L25-L39`). Four public tiers (starter/growth/pro/enterprise) plus
`salon-starter` contain features through `bundle_feature`. Migration 0007 asserts the tiers are cumulative.
Prices are explicitly placeholders (`db/migrations/0007_align_plan_tiers.up.sql:L29-L33`, OQ-4).

**Feature catalogue.** `identity_access.features` has 17 sellable keys plus derived non-sellable ones. Modules point
at a feature through `modules.feature_key` (`0005_split_features_from_modules.up.sql:L39-L131`). A brand owns
features in `brand_feature(enabled, valid_until, source bundle|manual)`, which has RLS `rls_brand`
(`0005:L148-L166`).

**Signup and provisioning (anonymous).** `POST /api/v1/signup/complete` (`Signup.cs:L30-L32`, rate limited)
leads to `CompleteSignupCommandHandler` (`CompleteSignup.cs:L60-L236`). The handler:
1. checks the template;
2. consumes the OTP;
3. inside one transaction, creates the brand (`status='active'`), the owner user, a `brand_admin` brand-scope
   membership and an "OWN" franchise;
4. calls `TemplateProvisioner.ApplyAsync`, which licenses every feature of the template's
   `default_bundle_code` with `source='bundle'` and **no `valid_until`** (`TemplateProvisioner.cs:L52-L77`);
5. inserts `brand_platform_subscription(status='trialing', current_period_end = next_billing_at = now+14d)`
   and **no invoice** (`CompleteSignup.cs:L199-L225`).

Default tiers are: laundry → `pro`, logistics → `growth`, salon → `salon-starter`, tiffin → `growth`
(`0011_vertical_templates.up.sql:L56-L82`). No client in the repo calls signup. It exists only as an API.

**Plan change / operator provisioning.** `POST /api/v1/admin/entitlements/brands/{id}/apply-bundle` requires
`saas.manage` (`AdminEntitlements.cs:L35`), which only `platform_admin` holds
(`seeder_parity_r3sec1.sql:L146-L162`; `brand_admin` is not given it, `IdentitySeeder.cs:L515-L571`).
`ApplyBundleToBrandCommandHandler` (`ApplyBundleToBrand.cs:L22-L151`) does the following:
- re-expands the bundle rows, keeping manual add-ons;
- creates the subscription, or updates the price snapshot and forces `status='active'`;
- issues a proration invoice when a mid-cycle upgrade raises the price;
- issues the period invoice when none exists for `CurrentPeriodStart`;
- calls `PermVersionBumper.BumpBrandMembersAsync`.

**Enforcement (staff APIs).** The real execution path is:
```
admin-web → Gateway → host endpoint .RequireAuthorization("permission:X")
  ← token minted earlier by ScopeResolver.BuildTokenClaimsAsync(enforceEntitlement: config["Entitlement:Enforced"])
       (ScopeResolver.cs:L185-L241: permissions whose permissions.module_key → modules.feature_key is not
        licensed are REMOVED from the "permissions" claim; un-licensed feature keys go into "ent_off")
  → TenantResolutionMiddleware (perm_version check, Auth:EnforceTokenVersion=true in all 3 hosts)
  → BrandSuspensionMiddleware (402 brand_suspended / brand_cancelled)
  → UseAuthorization → PermissionHandler (PermissionHandler.cs:L23-L54: claim lookup; platform_admin bypass)
  → on deny: ApiAuthorizationResultHandler maps permission→feature (FeatureCatalog) and answers
    402 feature_not_in_plan if the feature is in ent_off, else 403 (ApiAuthorizationResultHandler.cs:L90-L117)
```
`Entitlement:Enforced` is `true` in `core.WebApi/appsettings.json:L12-L13` and `.Development.json:L15-L16`.
`EntitlementConfigTests` pins that value. The navigator applies the same feature filter
(`GetNavigator.cs:L56-L80`).

**Billing, renewal, dunning, suspension.** `BrandPlatformBillingService` is a hosted service in commerce.WebApi
(`Program.cs:L318`). It is **off by default** (`WorkerOptions.cs:L140`), and no appsettings file or
`PRODUCTION_ENV.md` enables or documents it. Each cycle it runs `RunDunningAsync` and then the renewal pass
(`BrandPlatformBillingService.cs:L60-L129`).

Dunning works as follows:
- It marks overdue invoices `past_due` and increments `attempt_count`. No charge is attempted.
- It marks the subscription `past_due`.
- After `BrandMaxDunningAttempts` (3) and `BrandDunningGraceDays` (14) it calls
  `kernel.set_brand_suspension(brand, true, 'nonpayment')`.
- When the brand has no unpaid due invoices, it reinstates the brand (`BrandPlatformBillingService.cs:L152-L249`;
  function at `0021_brand_dunning.up.sql:L59-L107`).

The suspension gate is `BrandSuspensionMiddleware`. It reads `kernel.brand_status()` through a
SECURITY DEFINER function with a 30 s cache (`BrandStatusStore.cs:L26-L54`). It returns 402 for any authenticated
non-platform-admin request outside an allow-list (`BrandSuspensionMiddleware.cs:L44-L141`). It is registered in all
three hosts.

**Collection.** A platform admin can create a Razorpay Payment Link for an invoice
(`CollectBrandPlatformInvoice.cs:L21-L42`). Payment is recorded in one of three ways:
- the anonymous webhook `POST /api/v1/webhooks/razorpay-paylink`, with HMAC verified against the platform secret
  (`RazorpayPaylinkWebhook.cs:L25-L39`, `ProcessPaylinkWebhook.cs:L75-L118`);
- a manual pull-sync (`CollectBrandPlatformInvoice.cs:L58-L73`);
- a manual mark-paid (`SetBrandPlatformInvoiceStatus.cs:L23-L39`).

### (a-franchise)
There is admin CRUD for `platform_plans` and assign/cancel for `franchise_subscriptions`, all platform-admin only
(`FranchiseSubscriptionCommands.cs:L27-L104`). Quota columns (`MaxStores`, `MaxUsers`,
`MaxOrdersPerMonth`, …) are copied into the subscription. **Nothing reads them, nothing invoices these
subscriptions, and nothing moves them out of `pending`/`trialing`.** See SA-SUB-011.

### (b) — shared infrastructure only
`SubscriptionBillingService` (opt-in, correctly uses `CreateWorkerAsyncScope`, `L95-L107`) generates invoices
and charges mandates through `GatewaySubscriptionCharger` → `RazorpayPaymentGateway.ChargeMandateAsync`. It has its
own dunning ladder (`SubscriptionBillingService.cs:L343-L440`). Customer payments are reconciled by the per-brand
webhook `/api/v1/webhooks/razorpay` (`RazorpayWebhookHandler.cs`).

### Separation of concerns
| Concern | Where it lives |
|---|---|
| Authentication | JWT bearer (default scheme), API key scheme |
| Tenant membership | `user_scope_memberships` → `ScopeResolver` picks the active scope and brand |
| Subscription entitlement | `brand_feature`, folded into the token's `permissions`/`ent_off` claims at mint |
| Authorization | `PermissionHandler` on the claim; ABAC PDP after it |
| Billing state | `brand_platform_subscription` / `brand_platform_invoice` |
| Operability | `brands.status` (suspension) |

The separation is clean in structure. **However, billing state and entitlement state are not connected.**
Entitlements never read subscription status. Only `brands.status`, set by dunning, reaches the request path
(SA-SUB-006).

## Capability matrix

| Capability | Implementation status | Evidence | Gap |
|---|---|---|---|
| Plan catalogue with price + interval (a-brand) | Implemented & enforced (data) | `phase4_bundle_pricing.sql:L25-L54`; `0007_align_plan_tiers.up.sql:L37-L164` | Prices are placeholders (OQ-4); the bundle schema is in `db/patches`, not migrations (SA-SUB-020) |
| Franchise SaaS plans with quotas (a-franchise) | Data model only | `FranchiseSubscriptionCommands.cs:L56-L86`; no reader of `Max*` (grep) | Never billed, never enforced (SA-SUB-011) |
| Self-serve signup → tenant provisioning | Implemented (API only) | `CompleteSignup.cs:L60-L236`; `Signup.cs:L30-L32` | No client UI; no plan choice at signup; provisioning is not payment-gated (by design: trial) |
| Trials | Implemented, never ends | `CompleteSignup.cs:L199-L225`; `BrandPlatformBillingService.cs:L67-L69` | Nothing converts or invoices `trialing` (SA-SUB-001) |
| Upgrade / downgrade | Implemented, operator-only | `ApplyBundleToBrand.cs:L22-L151`; `AdminEntitlements.cs:L35` | No self-service; trial and mid-cycle billing defects (SA-SUB-012); grants without payment (SA-SUB-006) |
| Cancellation (subscription) | Implemented not enforced | `CancelBrandPlatformSubscription.cs:L58-L70` | Features are kept after cancel (SA-SUB-006) |
| Brand account cancellation / wind-down | Implemented & enforced | `BrandSuspensionMiddleware.cs:L96-L108`; migration 0015 | Out of scope for depth |
| Renewals | Implemented not working | `BrandPlatformBillingService.cs:L60-L129` | Worker off by default (SA-SUB-005); runs without worker scope under RLS (SA-SUB-004); stops after `past_due` (SA-SUB-003) |
| Feature entitlements on staff APIs | Implemented & enforced | `ScopeResolver.cs:L185-L241`; `PermissionHandler.cs:L23-L54`; `appsettings.json:L12-L13` | Module-less sellable features gate nothing (SA-SUB-009); customer, worker and API-key paths are not gated (SA-SUB-010) |
| 402 `feature_not_in_plan` | Implemented & enforced | `ApiAuthorizationResultHandler.cs:L90-L117`; `client.ts:L174-L186` | Upgrade path `/settings?tab=plan` does not exist in admin-web (SA-SUB-007) |
| Entitlement in navigation (frontend) | Implemented & enforced (server-computed) | `GetNavigator.cs:L56-L80` | — |
| Plan limits / quotas / usage metering | Absent (a-brand); Data model only (a-franchise); API-key request metering implemented | `FranchiseSubscriptionCommands.cs:L64-L68`; `ApiKeyAuthentication.cs:L193-L200`; `0016_api_keys.up.sql` | No numeric limits anywhere; Starter "1 location" is modelled as a boolean (SA-SUB-008) |
| `api_access` entitlement on API keys | Implemented & enforced | `0016_api_keys.up.sql:L113-L137`; `ApiKeyAuthentication.cs:L165` | — |
| Payment collection (Razorpay Payment Link) | Implemented, operator-only | `CollectBrandPlatformInvoice.cs:L21-L42` | Only `issued` invoices; no tenant self-pay (SA-SUB-002, SA-SUB-007) |
| Webhook signature verification | Implemented & enforced | `ProcessPaylinkWebhook.cs:L80-L93,L120-L125`; `RazorpayWebhookHandler.cs:L101-L145` | — (positive control) |
| Webhook idempotency | Implemented (state-check only) | `ProcessPaylinkWebhook.cs:L109-L115`; `RazorpayWebhookHandler.cs:L186-L206` | No event-id store, no row lock or concurrency token (SA-SUB-016) |
| Failed payments / dunning / grace | Implemented, partly broken | `BrandPlatformBillingService.cs:L152-L249`; `0021_brand_dunning.up.sql` | No real charge attempts, no notifications, no audit row (SA-SUB-014); paid `past_due` invoices are ignored (SA-SUB-002) |
| Suspension (login-only mode) | Implemented & enforced (JWT traffic) | `BrandSuspensionMiddleware.cs`; `BrandStatusStore.cs:L26-L54` | API-key principals bypass it (SA-SUB-017); workers ignore it (SA-SUB-018) |
| Reactivation | Implemented, broken path | `BrandPlatformBillingService.cs:L160-L182` | Depends on the invoice reaching `paid`, which the webhook cannot do for `past_due` (SA-SUB-002); subscription never returns to `active` (SA-SUB-003) |
| Refunds / credit notes (a-brand) | Absent | no code path | — |
| Billing reconciliation | Implemented, manual | `CollectBrandPlatformInvoice.cs:L58-L73` | No scheduled job |
| GST invoices (a-brand) | Absent | `BrandPlatformInvoice.cs:L9-L31` | No number, tax, GSTIN, SAC or PDF (SA-SUB-013) |
| GST invoices (b) | Implemented (simplified) | `SubscriptionBillingService.cs:L178-L186` | IGST 18% hard-coded; invoice number is count-based |
| Payment history / billing audit trail | Partially implemented | `AuditSaveChangesInterceptor.cs:L82-L101` covers EF writes | Raw-SQL dunning transitions are not audited; no payment record on the brand invoice (SA-SUB-013, SA-SUB-014) |
| Tenant-facing billing UI | Absent | `SettingsPage.tsx:L54-L57` (`platformOnly`); `routePermissions.ts:L49-L50` (`saas.read`) | SA-SUB-007 |

## Findings

### SA-SUB-001 — Self-serve trials never end: `trialing` brand subscriptions are never converted, invoiced or expired, and trial features are perpetual
- Category: (a-brand) Billing lifecycle / revenue leakage
- Severity: High
- Status: Verified (code read end-to-end; not executed)
- Evidence:
  - `core.Application/Identity/Signup/Commands/CompleteSignup.cs:L199-L225`: creates `Status="trialing"`, `NextBillingAt = now+14d`, no invoice.
  - `core.Application/Identity/Signup/TemplateProvisioner.cs:L66-L77`: trial features inserted `Enabled=true`, `ValidUntil` unset (perpetual), `Source="bundle"`.
  - `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs:L67-L69`: the renewal pass selects only `s.Status == "active"`.
  - `BrandPlatformBillingService.cs:L201-L207`: dunning only touches subscriptions that already have a `past_due` invoice, and a trial has no invoice.
  - Repo-wide grep for `trialing` in .cs/.sql found no code that moves a brand subscription out of `trialing`.
- Observed behaviour: every self-signed-up brand gets its template's default tier (`pro` for laundry). That tier never expires, no invoice is ever raised, and the brand can never be suspended for non-payment.
- Reproduction / verification method: sign up (OTP), advance the clock past 14 days, and run the worker with `Worker:BrandPlatformBillingEnabled=true`. Expected: an invoice or conversion. Actual by code: none. Not run.
- Impact: 100 % revenue leakage on the only self-serve acquisition funnel. Signup is anonymous and rate-limited only by OTP, so anyone can create unlimited free Pro tenants.
- Recommended remediation (smallest safe change):
  - In `RunCycleAsync`, add a pass for `status='trialing' AND current_period_end <= now`. It should either:
    - convert to `active` and issue the first invoice, if a payment method is on file or `AutoRenew`; or
    - move to `past_due` so dunning applies.
  - Optionally set `brand_feature.valid_until = trial end` for bundle rows during the trial, so features lapse without the worker.
- Regression tests required: an integration test for the worker conversion pass (trial → invoice → past_due → suspend); a test that a trial brand's token loses features after expiry when it does not convert.
- Dependencies / priority: P0. Needs SA-SUB-004 and SA-SUB-005 fixed first, or the pass will not run.
- Prior-doc cross-ref: `docs/GAP_ANALYSIS.md` E2 marks brand subscription "Done". The code contradicts this for trials.

### SA-SUB-002 — Paying an overdue (`past_due`) brand invoice via Razorpay is ignored, and a payment link cannot be created for it, so the "pay to reinstate" path is broken
- Category: (a-brand) Payments / dunning / reactivation
- Severity: High
- Status: Verified (code read; not executed)
- Evidence:
  - `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs:L186-L195`: dunning sets every overdue invoice to `status='past_due'`.
  - `core.Application/Identity/Entitlements/Commands/ProcessPaylinkWebhook.cs:L109-L115`: the webhook marks the invoice paid **only if `Status == "issued"`**; otherwise it returns `"invoice already past_due"` with 200 OK, so Razorpay does not retry.
  - `core.Application/Identity/Entitlements/Commands/CollectBrandPlatformInvoice.cs:L25-L27`: a payment link can only be created for an `issued` invoice. A link that already exists is returned, but a `past_due` invoice without one cannot get one.
  - Reinstatement requires no `issued`/`past_due` invoices due (`BrandPlatformBillingService.cs:L164-L174`).
- Observed behaviour:
  - Suspension only ever follows `past_due` invoices, so every suspended brand's invoices are `past_due`.
  - A brand that pays one of those links through Razorpay is acknowledged but not credited, and stays suspended.
  - Recovery needs a platform admin to run `sync-payment` (`CollectBrandPlatformInvoice.cs:L58-L73`, which does accept any non-paid status) or to mark the invoice paid manually.
- Reproduction / verification method: issue an invoice, generate a link, let dunning mark it `past_due`, then POST a signed `payment_link.paid` webhook. Expected: paid and reinstated. Actual by code: "invoice already past_due". Not run.
- Impact: paying customers stay locked out. Support load rises, and payment is lost silently, because Razorpay receives 200.
- Recommended remediation: accept `issued` and `past_due` in the webhook and in link creation. Also clear `next_attempt_at` and record `paid_at` and the payment id.
- Regression tests required: a webhook test for a `past_due` invoice, followed by a reinstatement test with the worker.
- Dependencies / priority: P0.

### SA-SUB-003 — A brand subscription never returns from `past_due` to `active`, so after one late payment renewals stop for good
- Category: (a-brand) Billing lifecycle
- Severity: High
- Status: Verified (code read)
- Evidence:
  - `BrandPlatformBillingService.cs:L201-L207` sets the subscription to `past_due`.
  - The reinstatement pass only changes `brands.status` (`L176-L182`).
  - The renewal pass only processes `Status == "active"` (`L67-L69`).
  - The only writer of `Status="active"` on an existing subscription is the operator action `ApplyBundleToBrand.cs:L107`.
  - Entity doc still says `active | cancelled` (`BrandPlatformSubscription.cs:L26-L27`).
- Observed behaviour: once dunning has marked a subscription `past_due`, paying clears the brand suspension but no further renewal invoices are ever issued. The brand keeps its features at no cost.
- Impact: revenue leakage for every brand that is ever late, plus an MRR report that is wrong.
- Recommended remediation: in the reinstatement pass, also set `brand_platform_subscription.status='active'` where it is `past_due` and there are no open invoices.
- Regression tests required: worker test for past_due → paid → active → next renewal invoiced.
- Dependencies / priority: P0.

### SA-SUB-004 — The brand renewal pass runs outside a trusted worker scope, so RLS hides all subscriptions and no renewal invoice is ever issued under `app_user`
- Category: (a-brand) Background workers / tenant context. Related area: DB/RLS.
- Severity: High
- Status: Partially Verified (code read; AsyncLocal and RLS runtime semantics reasoned about, not executed)
- Evidence:
  - `BrandPlatformBillingService.cs:L62-L64`: `await RunDunningAsync(ct);` then `_scopeFactory.CreateAsyncScope()`. This is the plain scope, **not** `CreateWorkerAsyncScope()`.
  - `WorkerScope.cs:L26-L49`: the marker is an `AsyncLocal<bool>`. Setting it inside `RunDunningAsync` (`L156`) does not flow back to the caller after `await`.
  - `CommerceHostCurrentTenant.cs:L80-L90`: with no HttpContext and no marker, `BypassRls=false` and `BrandId=null`.
  - `RlsConnectionInterceptor.cs:L59-L106` writes those GUCs.
  - `phase4_brand_platform_subscription.sql:L58-L72`: policy `rls_bypass() OR brand_id = current_brand_id()`, so zero rows are visible.
  - Production must connect as `app_user` (`deploy/.env.example:L4-L5`, `PRODUCTION_ENV.md:L30`).
- Observed behaviour: `due` is always empty in production, so renewals silently issue nothing. The cycle still logs normally.
- Impact: there is no recurring revenue even when the worker is enabled. It would pass a local test run as the `postgres` superuser, which ignores RLS.
- Recommended remediation: a one-line change to `_scopeFactory.CreateWorkerAsyncScope()` at `L63`. Consider also asserting `WorkerScope.IsWorkerScope` in worker-only code paths.
- Regression tests required: an integration test that runs `BrandPlatformBillingService` against an `app_user` connection with RLS on, and asserts an invoice is created.
- Dependencies / priority: P0.

### SA-SUB-005 — Brand platform billing worker is disabled by default and undocumented
- Category: (a-brand) Operations / configuration
- Severity: Medium
- Status: Verified
- Evidence:
  - `commerce.Infrastructure/Worker/Options/WorkerOptions.cs:L140` sets `BrandPlatformBillingEnabled = false`.
  - `commerce.WebApi/appsettings.json` and `.Development.json` have no `Worker` section.
  - `PRODUCTION_ENV.md` documents `Worker__SubscriptionBillingEnabled` (`L325`) but not `Worker__BrandPlatformBillingEnabled`.
  - `BrandPlatformBillingService.cs:L40-L45` returns immediately when the flag is off.
- Observed behaviour: by default no renewals, no dunning and no automatic suspension happen in any environment.
- Impact: suspension-on-nonpayment, which `0021` describes as shipped, is inert unless an operator knows an undocumented flag.
- Recommended remediation: document the flag, and decide whether it should default to on in Production (it must be fixed together with SA-SUB-004). Add a startup warning in Production when it is off.
- Regression tests required: config test pinning the intended value (the same pattern as `EntitlementConfigTests`).
- Dependencies / priority: P1, after SA-SUB-004.

### SA-SUB-006 — Subscription state and entitlements are disconnected: features are granted without payment and kept after cancellation or non-payment
- Category: (a-brand) Entitlement integrity
- Severity: High
- Status: Verified
- Evidence:
  - `CancelBrandPlatformSubscription.cs:L47-L49,L64-L68`: "The brand keeps any already-licensed features until an operator changes entitlement separately."
  - `ApplyBundleToBrand.cs:L60-L78`: features are licensed immediately. `L107` forces `sub.Status="active"`, which reactivates a `cancelled` or `past_due` subscription without payment. The invoice is only issued, never required.
  - `ScopeResolver.cs:L197-L202`: entitlement reads only `brand_feature`; there is no join to subscription status.
  - The only billing-driven gate is `brands.status='suspended'`, applied by the worker after 3 attempts plus 14 days of grace (`BrandPlatformBillingService.cs:L216-L227`), and that worker is off by default (SA-SUB-005).
- Observed behaviour: a cancelled, `past_due` or never-paid subscription keeps full feature access indefinitely, unless the brand is suspended, which depends on SA-SUB-002 to SA-SUB-005 working.
- Impact: entitlements are not tied to the commercial contract. This weakens Q4.
- Recommended remediation: make subscription status an input to entitlement. Smallest change: on cancel (at period end) and on `past_due` beyond grace, set `brand_feature.valid_until` on `source='bundle'` rows, or have ScopeResolver treat `bundle` rows as entitled only while the subscription is `active` or `trialing`. Keep `manual` add-ons explicit.
- Regression tests required: cancel, then token mint, asserting the bundle features are gone at period end; ApplyBundle on a cancelled subscription without payment, asserting the expected (decided) behaviour.
- Dependencies / priority: P0/P1.

### SA-SUB-007 — No tenant-facing billing: owners cannot see their plan or invoices, cannot pay or upgrade, and are given no usable remediation for 402s
- Category: (a-brand) Commercial UX / self-service. Related area: FE.
- Severity: High
- Status: Verified
- Evidence:
  - All `/api/v1/admin/entitlements/*` endpoints require `saas.read`/`saas.manage` (`AdminEntitlements.cs:L28-L38`), granted only to `platform_admin` (`seeder_parity_r3sec1.sql:L146-L162`). `brand_admin`'s grant list has no `saas.*` (`IdentitySeeder.cs:L515-L571`).
  - `BrandSuspensionMiddleware.cs:L51` allow-lists `/api/v1/admin/entitlements` "to see the tier, the invoices, and pay them", but the suspended owner is refused by the authorization layer.
  - `ApiAuthorizationResultHandler.cs:L42` sends owners to `/settings?tab=plan`. admin-web Settings has no `plan` tab (`SettingsPage.tsx:L21-L57`, where `platform-payments` is `platformOnly`). The toast says "Settings → Licensing" (`client.ts:L184`), which is the `saas.read`-gated EntitlementsTab.
  - `brand_suspended`/`brand_cancelled` 402 codes are not handled by any client (grep of admin-web, pos-web and customer-mobile).
  - No notification is emitted when a brand invoice is issued or goes `past_due` (`BrandPlatformBillingService.cs` has no outbox or notification writes).
- Observed behaviour: owners are never told they owe money. Once suspended they see generic errors and cannot reach their invoice or pay it. Every recovery is a manual operator action.
- Impact: blocks self-serve SaaS. Suspensions without notice also carry legal and reputational risk.
- Recommended remediation:
  - Add brand-scoped read endpoints (for example `GET /api/v1/admin/billing/me`) gated by a new tenant permission (`billing.read`) and keyed to the caller's `brand_id`, never a route parameter.
  - Add an owner-initiated "pay invoice" endpoint that creates or returns the payment link.
  - Add a Billing page in admin-web, and handle `brand_suspended`.
  - Emit notification outbox events on invoice issue and on `past_due`.
- Regression tests required: owner can read only their own invoices; owner can create a link while suspended; another brand's invoice id returns 404.
- Dependencies / priority: P0 for commercial launch.

### SA-SUB-008 — Plan limits / quotas do not exist (a-brand) or are not enforced (a-franchise); Starter "1 location" is modelled so that Starter cannot create any store
- Category: Entitlements / quotas / metering
- Severity: Medium
- Status: Partially Verified (the code chain was read; permission→module ownership depends on seeded data that I could not query)
- Evidence:
  - (a-brand): `module_bundle` and `brand_platform_subscription` have no limit columns (`phase4_bundle_pricing.sql:L25-L29`, `phase4_brand_platform_subscription.sql:L21-L39`).
  - (a-franchise): `MaxStores/MaxWarehouses/MaxUsers/MaxOrdersPerMonth/MaxRiders` are copied at `FranchiseSubscriptionCommands.cs:L64-L68`, and a repo-wide grep shows no reader outside DTOs and commands. `CreateStore`/`CreateWarehouse`/`AddStore` have no count check.
  - Starter = `bookings, scheduling` plus non-sellable features (`0007_align_plan_tiers.up.sql:L58-L77`). The `stores` module is gated by `multi_location` (`0005:L121`). `stores.create` resolves to the `stores` module by exact key match (`IdentitySeeder.cs:L366-L377`). Onboarding AddStore requires `stores.create` (`AdminFranchises.cs:L49`).
  - The only usage metering is API-key request counting (`ApiKeyAuthentication.cs:L193-L200`, `0016_api_keys.up.sql:L140-L157`).
- Observed behaviour: no tier limits users, orders or locations by count. A Starter brand (applied by an operator) has `stores.create` stripped from its token and cannot add a single location. Self-signup avoids this only because no template defaults to `starter`.
- Impact: plans cannot be priced on usage. The Starter tier is unusable as designed.
- Recommended remediation:
  - Add `limits jsonb` (or typed columns) to `module_bundle`, snapshot it on the subscription, and enforce it in the few create handlers that matter (stores, users, riders) with a shared `IPlanLimitGuard` that returns 402 `plan_limit_reached`.
  - Model "1 location" as a limit, not as a missing feature.
- Regression tests required: create-store at the limit returns 402; Starter can create exactly one store.
- Dependencies / priority: P1.

### SA-SUB-009 — Five sellable features gate nothing (`wallet`, `loyalty`, `online_payments`, `item_tracking`, `whatsapp_bot`)
- Category: (a-brand) Entitlement coverage
- Severity: Medium
- Status: Partially Verified (no live DB to list `features` without modules; the migrations and grep agree)
- Evidence:
  - Created module-less at `0005_split_features_from_modules.up.sql:L66-L83`.
  - `0019_premium_feature_modules.up.sql:L4-L11` acknowledges that "a feature with no module … gates absolutely nothing". It fixed only `custom_domain`/`white_label_app` (`L23-L30`) and only emits a `RAISE WARNING` for the rest (`L108-L123`).
  - No later migration or patch inserts a module for these keys (grep of `INSERT INTO identity_access.modules` across `db/`).
  - `ScopeResolver.cs:L224-L240` can only strip permissions reachable through a module.
  - The tier contents still sell them (`0007:L83-L116`).
- Observed behaviour: a Starter or Growth brand can use wallet, loyalty and online-payment endpoints with ordinary permissions (`wallet.*`, `loyalty.manage`, `payment.*`, owned by other modules). `api_access` is the exception and is enforced at key authentication (`0016:L113-L137`).
- Impact: paid tier differentiators are free. Q4 is overstated.
- Recommended remediation: as migration 0019 did, add dedicated modules or permissions for these features and point their permissions' `module_key` at them. Turn the 0019 WARNING into an EXCEPTION in a new migration, so it fails CI.
- Regression tests required: a catalogue test asserting that every `is_sellable` feature is referenced by at least one active module that owns at least one permission.
- Dependencies / priority: P1.

### SA-SUB-010 — Entitlements are enforced only on staff-JWT permission checks; customer-facing APIs, workers and API-key scopes ignore the brand's plan
- Category: (a-brand) Server-side enforcement coverage (answers "can a tenant lacking feature X call X's API?")
- Severity: Medium
- Status: Verified (code read)
- Evidence:
  - Staff path: blocked. `ScopeResolver.cs:L185-L241` strips the permission; `PermissionHandler.cs:L36-L43` denies; `ApiAuthorizationResultHandler.cs:L92-L111` returns 402.
  - Customer path: not blocked. `/api/v1/customer/subscriptions` is gated only by `CustomerOnly` (`SubscriptionsCustomer.cs:L25-L39`). Customer tokens carry no permissions or entitlement claims (`JwtTokenService.cs:L98-L115`).
  - No handler in `commerce.*` or `operations.*` reads `BrandFeatures` (grep: only `core.Application` and `ScopeResolver`/`NpgsqlAbacStore`).
  - `SubscriptionBillingService` charges every active customer subscription regardless of the brand's `customer_subscriptions` feature (`SubscriptionBillingService.cs:L291-L304`).
- Observed behaviour:
  - A brand downgraded off `customer_subscriptions`, `coupons`, `wallet` and similar loses the admin screens and APIs.
  - Its existing customer plans stay purchasable, and recurring charges continue.
  - Customer-side booking, wallet and loyalty endpoints are likewise ungated.
- Impact: a downgrade does not remove customer-facing capability. Q13 is only partially satisfied.
- Recommended remediation: add a small `IBrandEntitlementGate` (cached by brand, invalidated with `perm_version` or on a TTL), and call it at the top of customer-facing groups for feature-owned surfaces through endpoint metadata (`.RequireFeature("customer_subscriptions")`). Skip worker billing for un-entitled brands, or pause their subscriptions on downgrade.
- Regression tests required: a customer subscribe call for a brand without `customer_subscriptions` returns 402.
- Dependencies / priority: P1.

### SA-SUB-011 — Franchise SaaS subscriptions (ADR-010 "module B") are data model + CRUD only, never billed or enforced, which contradicts ADR-010
- Category: (a-franchise) Commercial model consistency
- Severity: Medium
- Status: Verified
- Evidence:
  - `FranchiseSubscriptionCommands.cs:L27-L104` (assign: status `trialing`/`pending`, never advanced).
  - Grep shows `FranchiseSubscriptionInvoices` referenced only by the DbContext and configuration; no worker or handler.
  - ADR-010 (`docs/ADRs/ADR-010-recurring-billing-and-dunning.md`) states "Suspend-on-nonpayment for franchises is a defined lifecycle (module B), enforced by a dunning job".
  - admin-web `PlatformPlansPage` manages these plans (`App.tsx:L111`).
- Observed behaviour: two unrelated SaaS plan catalogues exist (`module_bundle` for brands, `platform_plans` for franchises). Only the first drives entitlements. Operators can "assign a SaaS plan" that bills nothing and limits nothing.
- Impact: confusion, false confidence, and a likely double-modelling of tenant pricing.
- Recommended remediation: decide whether franchises are a separate payer. If not, deprecate `platform_plans`/`franchise_subscriptions` and hide the page. If so, reuse the brand billing worker pattern. Update ADR-010.
- Regression tests required: n/a until decided.
- Dependencies / priority: P2 (product decision).

### SA-SUB-012 — Plan-change billing defects: changing tier during a trial bills the trial window at full price (plus proration); downgrades strip paid-for features immediately
- Category: (a-brand) Upgrade/downgrade correctness
- Severity: Medium
- Status: Verified (code read)
- Evidence: `ApplyBundleToBrand.cs:L100-L143`. For an existing `trialing` subscription:
  - `Status="active"` is set (`L107`) while `CurrentPeriodStart` stays the signup time.
  - If the new price is higher, a proration invoice is added for the remaining trial (`L112-L129`).
  - Then `hasInvoice` is false for the trial start, so a **full-price** invoice for the trial period is added (`L132-L143`).

  For a downgrade, `sub.Price` is lowered at once and bundle rows are deleted at once (`L54-L56`). The comment "takes effect at the next renewal" (`L110-L111`) describes neither.
- Observed behaviour: an upgrade mid-trial produces two invoices, about 14 days of proration plus a full month. A downgrade removes features the brand has already paid for this period.
- Impact: overbilling disputes on one side, underdelivery on the other.
- Recommended remediation:
  - For `trialing`: change the plan only (keep `trialing`, issue no invoice).
  - For downgrade: schedule it (`pending_bundle_code`, applied at renewal), or keep features until `current_period_end`.
- Regression tests required: extend `PlanChangeTests` with trial-upgrade and downgrade billing assertions. They currently assert features only.
- Dependencies / priority: P1.

### SA-SUB-013 — Brand platform invoices are not GST invoices and carry no payment record
- Category: (a-brand) Invoicing / compliance (also notes (b))
- Severity: Medium
- Status: Verified
- Evidence:
  - `BrandPlatformInvoice.cs:L9-L31`: amount, status and link only. There is no invoice number, tax breakdown, supplier/recipient GSTIN, SAC code, place of supply, PDF, `paid_at` or payment id.
  - `phase4_brand_platform_subscription.sql:L41-L53` shows the same.
  - Brand GSTIN is captured only into `brands.config` at signup (`CompleteSignup.cs:L303-L307`).
  - (b): `SubscriptionBillingService.cs:L178-L186` uses IGST 18% always (wrong for intra-state) and `invCount+1` numbering, which is race-prone and not gap-free per brand.
- Observed behaviour: a platform invoice cannot serve as a tax invoice. Payments cannot be reconciled to a gateway payment id.
- Impact: compliance blocker for charging Indian businesses. Weak reconciliation.
- Recommended remediation: add `invoice_number` (DB sequence per financial year), a tax breakdown (reuse `TaxBreakdown`), `paid_at`, `gateway_payment_id` and `payment_method` columns. Populate them from the webhook and sync. Generate PDFs through the existing invoice tooling (`db/patches/invoice_generation.sql` is the customer-side precedent).
- Regression tests required: invoice numbering is gap-free and unique under concurrency; CGST+SGST vs IGST selection.
- Dependencies / priority: P1.

### SA-SUB-014 — Brand dunning makes no charge attempts, sends no notices, and its state changes are not audited
- Category: (a-brand) Dunning / auditability
- Severity: Medium
- Status: Verified
- Evidence:
  - `BrandPlatformBillingService.cs:L184-L207`: an "attempt" is just `attempt_count+1` once per backoff window (default 1440 min). There is no gateway call and no mandate for brands.
  - Brand-side entity comment: "Not auto-charged yet … deferred P0" (`BrandPlatformInvoice.cs:L6-L7`).
  - All transitions run through `ExecuteSqlAsync`/SECURITY DEFINER functions (`L178-L179`, `L186-L207`, `L231-L233`). `AuditSaveChangesInterceptor.cs:L82-L101` only sees EF ChangeTracker entries, so `past_due`, suspension and reinstatement leave only log lines.
- Observed behaviour: brands are suspended about 3 attempts plus 14 days after the due date without a single charge or message.
- Impact: avoidable suspensions, and no evidentiary trail for disputes.
- Recommended remediation: emit an outbox notification event and an explicit `audit_logs` row (`IAuditWriter`) for each transition. Add a mandate or auto-collect for brands later.
- Regression tests required: worker test asserting audit and outbox rows per transition.
- Dependencies / priority: P1.

### SA-SUB-015 — (b) Recurring mandate charge integration is suspect: likely non-existent Razorpay endpoint/header, asynchronous statuses treated as failures, no webhook reconciliation of subscription invoices
- Category: (b) Payment gateway. Related area: PAY/commerce.
- Severity: Medium
- Status: Suspected (Razorpay API semantics from general knowledge; not checked against Razorpay; not run)
- Evidence:
  - `RazorpayPaymentGateway.cs:L226-L282` POSTs `v1/subscriptions/{id}/charge` with a `Razorpay-Idempotency` header. Razorpay's documented recurring flow is order + `payments/create/recurring` with a token. I am not aware of either the endpoint or the header.
  - Any status other than `captured` maps to non-success.
  - `SubscriptionBillingService.cs:L392-L436` treats every non-`success` result, including `created`/`authorized`/`initiated`, as a failure and advances dunning.
  - `RazorpayWebhookHandler.cs:L95-L99` reconciles only `payments.gateway_order_id`, and subscription charges create no `payments` row with an order id.
  - `GatewaySubscriptionCharger.cs:L32` resolves `IPaymentGateway` in a non-worker scope, so the per-brand settings lookup runs without tenant or bypass.
- Observed behaviour by code: successful asynchronous debits can be recorded as failures, leading to customers being wrongly suspended. Per-brand gateway credentials may not resolve in the worker.
- Impact: customer-subscription revenue and churn risk. Shares the gateway and webhook infrastructure that SaaS billing would reuse.
- Recommended remediation: validate against the Razorpay sandbox; treat pending statuses as pending and finalise them by webhook; use `CreateWorkerAsyncScope` in the charger.
- Regression tests required: gateway contract test against recorded sandbox responses.
- Dependencies / priority: P1 for (b). Not a blocker for (a).

### SA-SUB-016 — Webhook idempotency is check-then-act on status only: no event dedupe, no lock or concurrency token, no amount check on brand paylinks
- Category: Payments / idempotency (a-brand and b). Related area: DB, PAY.
- Severity: Low
- Status: Verified (code read); race not reproduced
- Evidence:
  - `ProcessPaylinkWebhook.cs:L109-L115` and `RazorpayWebhookHandler.cs:L186-L233` read the row, check status, and write it. No `SELECT … FOR UPDATE`, no `IsConcurrencyToken`/xmin anywhere in `laundryghar.SharedDataModel` (grep), and no processed-event table keyed on the Razorpay event id or `x-razorpay-event-id`.
  - The brand paylink handler does not compare `amount_paid`/currency with `inv.Amount`.
- Observed behaviour: duplicate deliveries are harmless in sequence (status check). Concurrent duplicates of `payment.captured` can both pass and emit two `payment.captured` outbox events. Positive controls: partner wallet top-up uses an idempotency key (`ProcessPartnerPaylinkWebhook.cs:L99-L115`); refunds have a unique `idempotency_key` and a DB cap trigger (`payment_idempotency.sql:L12-L20`).
- Impact: low for brand invoices (idempotent end state); possible double side effects downstream of the customer outbox.
- Recommended remediation: a `webhook_events(event_id PK, received_at)` insert-first dedupe, or a conditional `UPDATE … WHERE status='pending'` with a row-count check. Verify `amount_paid` on the paylink.
- Regression tests required: two concurrent identical webhooks produce one outbox event.
- Dependencies / priority: P2.

### SA-SUB-017 — Suspension gate does not apply to API-key principals
- Category: (a-brand) Enforcement ordering
- Severity: Low (latent; only `/api/v1/public-api/me` is exposed today)
- Status: Partially Verified (order read; not executed)
- Evidence:
  - The default scheme is JwtBearer (`core.WebApi/Program.cs:L301`). The ApiKey scheme is invoked only during authorization (`ApiKeyAuthentication.cs:L167-L172` comment).
  - `BrandSuspensionMiddleware` runs before `UseAuthorization` (`Program.cs:L563-L572`) and skips unauthenticated users (`BrandSuspensionMiddleware.cs:L130`).
  - `kernel.resolve_api_key` checks `api_access` but not `brands.status` (`0016_api_keys.up.sql:L119-L135`).
- Observed behaviour: a suspended or cancelled brand's API keys keep authenticating.
- Impact: grows with each public-API endpoint added.
- Recommended remediation: return `b.status` from `resolve_api_key` and fail for `suspended`/`cancelled`, or run the suspension check in an authorization handler.
- Regression tests required: a suspended brand's API key returns 402.
- Dependencies / priority: P2 (P1 before expanding the public API).

### SA-SUB-018 — Background workers ignore brand suspension and cancellation
- Category: (a-brand) affects (b) and operations
- Severity: Low
- Status: Verified (grep: no brand-status reads in `commerce.Infrastructure/Worker`)
- Evidence:
  - `SubscriptionBillingService.cs:L291-L304` selects open invoices with no brand filter.
  - `BrandStatusStore` is used only by the HTTP middleware and the cancellation commands.
- Observed behaviour: a brand in login-only mode keeps charging its customers' mandates, and other workers keep running.
- Impact: charging customers of a frozen business creates complaint and refund risk.
- Recommended remediation: join `tenancy_org.brands.status = 'active'` (through a SECURITY DEFINER view or function) in worker selection queries.
- Regression tests required: worker skips a suspended brand.
- Dependencies / priority: P2.

### SA-SUB-019 — Entitlement change propagation and fail-open behaviour (informational)
- Category: (a-brand) Consistency
- Severity: Informational
- Status: Verified
- Evidence:
  - `PermVersionBumper.cs:L36-L44` bumps only brand-scope members after an entitlement change, so franchise- and store-scoped tokens keep old permissions for up to `AccessMinutes` (15, `JwtSettings.cs:L11`).
  - `BrandStatusStore.cs:L47-L53` and `FeatureCatalog.cs:L64-L70` fail open on DB errors.
- Impact: bounded lag of 15 minutes or less. A DB outage disables suspension (documented trade-off).
- Recommended remediation: bump every member whose scope resolves to the brand (join franchises and stores). This is acceptable to defer.
- Dependencies / priority: P3.

### SA-SUB-020 — SaaS billing schema lives only in `db/patches/` behind a separate script; migrations 0008/0021 depend on it
- Category: (a-brand) Schema reproducibility. Related area: DB.
- Severity: Medium
- Status: Partially Verified (scripts read; not executed)
- Evidence:
  - `brand_platform_subscription`/`_invoice` and `module_bundle.price` are created only by `db/patches/phase4_*.sql`, applied by `db/patches/apply_saas_billing_patches.sh:L23-L30`.
  - Neither `db/build_from_scratch.sh` (stages 1–7, `L74-L101`) nor any migration creates them, yet `0008_backfill_entitlements_from_plans.up.sql:L35` reads and `0021_brand_dunning.up.sql:L38-L42` alters `brand_platform_invoice`.
  - The integration fixture skips 0007 because `module_bundle.price` is missing (`RbacEfFixture.cs:L162-L164`).
- Observed behaviour: a fresh environment built by the documented pipeline fails at 0008/0021 unless the separate SaaS script is run first, in the right order. No test applies it.
- Impact: environment drift; the billing tables in production may not match the code.
- Recommended remediation: fold the four phase4 SaaS patches into a versioned migration (idempotent DDL) placed before 0007, or make `build_from_scratch.sh` call the SaaS script.
- Regression tests required: a CI job building from scratch and then running `migrate.sh up`.
- Dependencies / priority: P1.

## Positive controls verified
- **Entitlement enforcement on staff APIs is real and server-side.** `Entitlement:Enforced=true` (`core.WebApi/appsettings.json:L12-L13`) feeds all mint paths (password, OTP, Google, refresh, step-up, impersonation). `ScopeResolver.cs:L185-L241` strips permissions, and `PermissionHandler` denies regardless of the frontend. Covered by 9 integration tests (`EntitlementEnforcementTests.cs`) and a config pin (`EntitlementConfigTests.cs`).
- **Plan-vs-permission denial is distinguishable** (402 `feature_not_in_plan` vs 403) (`ApiAuthorizationResultHandler.cs:L56-L117`; `FeatureNotInPlan.cs`).
- **Live revocation:** entitlement changes bump `perm_version` (`ApplyBundleToBrand.cs:L149`, `SetBrandFeature.cs:L52`), and `Auth:EnforceTokenVersion=true` in all three hosts.
- **Suspension gate** reads status through SECURITY DEFINER `kernel.brand_status` (avoiding the brands-RLS trap), uses a short cache, keeps billing, auth, export and webhooks reachable, and exempts platform admins (`BrandSuspensionMiddleware.cs`, `BrandStatusStore.cs`). Unit-tested (`BrandSuspensionMiddlewareTests.cs`).
- **Suspension reason safety:** auto-reinstatement only reverses `nonpayment`, and never touches `cancelled`/`archived` brands (`0021_brand_dunning.up.sql:L59-L104`; `BrandDunningTests.cs`).
- **Webhook signature verification** uses HMAC-SHA256 over raw body bytes, a fixed-time compare, and fails closed outside Development (`ProcessPaylinkWebhook.cs:L80-L93,L120-L125`; `RazorpayWebhookHandler.cs:L101-L145`).
- **Signup** is transactional, uses a server-generated brand code and requires OTP before any write (`CompleteSignup.cs:L71-L97`). Vertical gating prevents a bundle licensing another vertical's features (`ApplyBundleToBrand.cs:L32-L46`; `TemplateProvisioner.cs:L62-L64`).
- **Saas admin endpoints are platform-only.** `saas.*` is granted only to `platform_admin`, and the `platform_billing`/`platform_plans` modules are excluded from every tenant bundle (`0007:L17-L21`), so the entitlement filter would also strip `saas.*` from a brand token.
- **Tier integrity:** 0007 asserts the tiers are cumulative and that every sellable feature belongs to some tier.
- **API access** is entitlement-checked at key resolution, and API-key usage is metered (`0016_api_keys.up.sql:L113-L157`).
- **Generic audit trail** covers EF writes to `brand_feature`, `brand_platform_subscription` and `brand_platform_invoice` (`AuditSaveChangesInterceptor.cs:L82-L101`).

## Open questions / not verified
- Runtime confirmation of SA-SUB-004: whether the AsyncLocal marker leaks or not, and whether renewal yields zero rows under `app_user`. This needs Postgres with the RLS patches and a worker run.
- The exact permission→module ownership in a seeded DB, which would confirm SA-SUB-008 (Starter cannot create a store) and SA-SUB-009 (which permissions are orphans and always kept).
- Whether a brand admin can assign platform permissions to custom roles: `RoleEditGuard` was not traced. The entitlement filter would strip `saas.*` anyway (positive control above). Belongs to the AUTHZ specialist.
- Razorpay recurring API contract (SA-SUB-015) has not been checked against the sandbox.
- Whether production actually ran `apply_saas_billing_patches.sh` (SA-SUB-020).
- Signup OTP abuse controls (`auth` rate-limit policy values) belong to the auth specialist.

## Verdict inputs
- **Q4 — Plans, billing lifecycle and entitlements implemented and enforced?** **Partially Supported.** The plan catalogue, feature entitlements and staff-API enforcement are real. The billing lifecycle is not:
  - trials never end (SA-SUB-001);
  - renewals are blocked by RLS scope, off by default, and stop after `past_due` (SA-SUB-003, SA-SUB-004, SA-SUB-005);
  - paid `past_due` invoices are ignored (SA-SUB-002);
  - subscription state does not drive entitlements (SA-SUB-006);
  - there are no quotas (SA-SUB-008);
  - there is no tenant self-service (SA-SUB-007).
- **Q13 — Entitlements enforced independently of frontend?** **Partially Supported.** For staff JWT APIs, yes: entitlement is baked into the token and enforced by `PermissionHandler`, so calling an un-entitled API directly gives 403/402. It is not enforced on customer-facing APIs, workers or API-key scopes (SA-SUB-010). Five sellable features gate nothing (SA-SUB-009).
- **DB-Q4 — Payment/webhook idempotency (my view):** **Partially Supported.** HMAC verification is solid and fail-closed. Idempotency rests on status check-then-act with no event-id dedupe, row locks or concurrency tokens (SA-SUB-016). Partner wallet top-ups and refunds have DB-level idempotency keys. The brand paylink webhook's status filter is so strict that it drops legitimate payments (SA-SUB-002).
