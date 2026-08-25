# LaundryGhar — Platform Gap Analysis (2026-08-24)

> **Scope:** what `PLATFORM_STRATEGY.md` (white-label, subscription-based, multi-vertical hyperlocal
> scheduling platform) requires, versus what actually exists in this repository today.
>
> **Method:** every capability was traced to real files / real live-DB objects. A capability is
> **Done** or **Partial** only when a concrete path is cited. **No evidence = Missing.**
>
> **Authority:** `db/` + `database_scripts/` SQL is canonical. Where a document disagrees with the
> SQL, the SQL wins and the disagreement is recorded in §4.
>
> The three earlier registers are historical and unchanged: `GAP_ANALYSIS_R1.md` (2026-06-10, the
> file previously named `GAP_ANALYSIS.md`), `GAP_ANALYSIS_R2.md`, `GAP_ANALYSIS_R3.md`.

---

## 0. Documents that do not exist

Two inputs named in the brief are not in the repository:

| Named | Reality |
|---|---|
| `docs/RESEARCH_SUMMARY.md` | **Does not exist.** No file matching `*RESEARCH*` anywhere in the tree. The nearest equivalents are `docs/MULTI_VERTICAL_BLUEPRINT.md` (48 KB, 324-person-day audited program plan), `docs/rbac-entitlement-plan.md`, and `docs/SAAS_PLATFORM_ARCHITECTURE.md`. Those were read in its place. |
| `RBAC.md` (repo root) | **Does not exist at the root.** The authorization model referenced by `PLATFORM_STRATEGY.md` §6.3 is `docs/rbac.md`. Also present: `RBAC_Navigation_PaaS_PostgreSQL.md` (root), `docs/rbac-entitlement-plan.md`, `docs/rbac-paas-gap.md`. `docs/rbac.md` was read as the authoritative RBAC spec. |

---

## 1. Baseline — verified green before any change (2026-08-24)

| Surface | Command | Result |
|---|---|---|
| Backend build | `dotnet build laundryghar.slnx` | **0 errors**, 62 warnings (all pre-existing: NU1902/NU1903 NuGet advisories on `MessagePack` + `Microsoft.OpenApi`, and IL2026/IL3050 trim/AOT analyzer warnings) |
| Backend tests | `dotnet test laundryghar.slnx` | **337 passed / 0 failed** (core.Tests 58 · operations.Tests 208 · operations.IntegrationTests 71) |
| admin-web | `tsc -b`, `eslint .` | typecheck clean · **0 errors**, 12 warnings (pre-existing react-hooks) |
| pos-web | `tsc -b`, `eslint .` | typecheck clean · **0 errors**, 2 warnings (pre-existing react-refresh) |
| customer-mobile | `jest`, `tsc --noEmit` | **164 passed** · typecheck clean |
| rider-mobile | `jest`, `tsc --noEmit` | **85 passed** · typecheck clean |

**Total: 586 tests green.** Local DB `laundry_ghar_db` reachable (PostgreSQL 18.4 client, per
`HANDOFF.md` gotcha — use `postgresql@18`, not `@16`).

---

## 2. Gap matrix

State key: **Done** = built and reachable · **Partial** = built but incomplete, unwired, or off ·
**Missing** = no evidence found. Effort: **S** ≤ 1 day · **M** 2–5 days · **L** > 5 days.

### 2.1 Product modes (`PLATFORM_STRATEGY.md` §3)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| M1 | Mode 1 — Full Service (pickup → process → deliver) | §3 | **Done** | `db/patches/phase0_multi_vertical.sql` (`orders.fulfillment_mode` CHECK incl. `process_deliver`); `backend/laundryghar/operations.Application/Fulfillment/Laundry/`; `IFulfillmentStrategy.cs` | — | — |
| M2 | Mode 2 — Pick & Drop (point-to-point) | §3 | **Done** | `operations.Application/Fulfillment/Logistics/`; `orders.fulfillment_mode = 'point_to_point'`; `db/patches/order_job_type.sql` | — | — |
| M3 | Mode 3 — Recurring Schedule (tiffin/milk/water) | §3 | **Missing** | `orders_fulfillment_mode_check` allows only `process_deliver`, `appointment`, `point_to_point` (live DB, canonical). No `recurring` strategy under `operations.Application/Fulfillment/`. `commerce.customer_subscriptions` is a *billing* subscription, not a delivery calendar. | No repeating-delivery calendar mode: no `recurring` fulfillment_mode, no schedule generator, no per-occurrence order materialisation | **L** |
| M4 | Strategy seam keyed by mode, resolver, DI | §3 | **Done** | `operations.Application/Fulfillment/FulfillmentStrategyResolver.cs`, `IFulfillmentStrategyResolver.cs`, `StateMachineStrategyBase.cs`; tests `operations.Tests/Fulfillment/SalonStrategyTests.cs` | — | — |

### 2.2 Vertical templates & terminology (§3, §11-P3)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| V1 | `vertical_key` discriminator on tenant + order | §3 | **Done** | `db/patches/phase0_multi_vertical.sql`; `tenancy_org.brands.vertical_key` + `brands_vertical_key_check`; `laundryghar.SharedDataModel/Enums/VerticalKey.cs` | — | — |
| V2 | One-vertical-per-brand immutability | §3 | **Done** | trigger `trg_brand_vertical_immutable` on `tenancy_org.brands` (live DB) | — | — |
| V3 | Vertical-tagged modules + roles (feature gating by vertical) | §3, §6.3 | **Done** | `identity_access.modules.vertical_key`, `identity_access.roles.vertical_key`; `GetNavigator.cs` (`VerticalKey.IsAvailableTo`), `GetAccessRoles.cs`; `db/patches/phase4_role_vertical_key.sql` | — | — |
| V4 | **Vertical template as a first-class object** (mode + terminology + catalog preset + default feature set, applied at onboarding) | §3 | **Missing** | No `vertical_template` table in the live DB; no template entity/handler in `backend/`. The salon vertical was landed as a hand-written one-off patch (`db/patches/phase4_salon_pack.sql`) — module row + bundle + quota widening — not as a reusable template record. | Adding a vertical today = writing a bespoke SQL patch. No declarative template, no "pick a template at onboarding" path | **L** |
| V5 | Terminology pack covering *every* user-facing string | §3, §12 (risk: terminology leakage) | **Partial** | `admin-web/src/lib/verticalTerms.ts` + `admin-web/src/hooks/useActiveVertical.ts` — covers exactly 3 things (on-site location noun, on-site `user_type`, a designation placeholder) in the admin console only. | No backend terminology source of truth; nothing in `pos-web`, `customer-mobile`, `rider-mobile`; i18n bundles (`*/src/i18n/locales/*.json`) are laundry-worded and vertical-blind | **L** |
| V6 | Launch templates: Laundry, Courier/Parcel, Tiffin | §3 | **Partial** (advanced 2026-08-25 — **OQ-1 answered: keep salon, add tiffin**) | The vertical *vocabulary* is settled and shipped: `db/migrations/0004_add_tiffin_vertical.up.sql` widened all **eight** vertical CHECK constraints (brands, orders + partitions, modules, module_bundle, features, roles, users, matview_registry, notification_event_catalog) to `{laundry, salon, logistics, tiffin}`; `VerticalKey.cs` and `admin-web/src/lib/verticalTerms.ts` carry tiffin. A tiffin brand is creatable today, verified live. | The template *content* is still missing (T-13/T-15), and tiffin is strategy Mode 3 — it is registered but not operable until the recurring calendar lands (M3/OQ-2). | **M** |

### 2.3 White-label & multi-domain (§4)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| W1 | T1 — Listed provider (row in `brands`) | §4 | **Done** | `tenancy_org.brands` (37 columns incl. theming, locales, support channels); `core.WebApi/Endpoints/Identity/AdminBrands.cs` | — | — |
| W2 | **`brand_domains` table** (`brand_id`, `domain`, `verification_txt`, `verified_at`, `ssl_status`, `is_primary`) | §4.2 | **Done** ~~Missing~~ (closed 2026-08-25, task **T-08**) | `db/migrations/0002_brand_domains.up.sql` / `.down.sql`; `SharedDataModel/Entities/TenancyOrg/BrandDomain.cs` + `BrandDomainConfiguration.cs`; brand-scoped `rls_brand` policy matching the house shape; citext + globally-unique `domain`, one-primary-per-brand partial index. 6 tests in `Rbac/BrandDomainTests.cs`. | — | — |
| W3 | **Host-header brand-resolution middleware** | §4.2 item 4 | **Done** ~~Missing~~ (closed 2026-08-25, task **T-09**) | `core.Infrastructure/Services/BrandResolver.cs` now resolves Host → verified `brand_domains` row ahead of the existing header/query/default chain, cached 30s (hits **and** misses). The lookup goes through `kernel.resolve_brand_domain(text)` (`db/migrations/0003`), SECURITY DEFINER because the caller is anonymous and the table is RLS-protected — a targeted grant, not a blanket bypass. 5 tests in `Rbac/BrandResolverHostTests.cs`. | Confined to the **anonymous** path by design: an authenticated request's brand comes from the JWT, and Host must never override it. | — |
| W4 | Domain ownership verification (CNAME + TXT) | §4.2 items 2 | **Done** ~~Missing~~ (closed 2026-08-25, task **T-10**) | `core.Application/Identity/TenancyOrg/BrandDomains/` (challenge issuance under a private `_lg-verify` label, 256-bit CSPRNG token, add/verify/delete/list); `core.Infrastructure/Dns/DnsTxtLookup.cs` (DnsClient, caching off, NXDOMAIN ≠ lookup failure); `core.WebApi/Endpoints/Identity/AdminBrandDomains.cs`; admin UI `admin-web/src/pages/settings/CustomDomainsPanel.tsx`. 40 tests; browser-verified end to end. | — | — |
| W5 | SSL automation (Let's Encrypt / CF-for-SaaS) | §4.2 item 3 | **Missing** | `deploy/` contains Docker/compose only; no ACME/cert automation | Ops work, plus an `ssl_status` feedback loop | **L** |
| W6 | Per-provider sender identity (email domain, WhatsApp number, SMS sender ID) | §4.2 item 5 | **Done** ~~Partial~~ | **Corrected 2026-08-25 — the original entry below was wrong.** All three channels are already per-brand: `SettingsMailer.LoadAsync(brandId)` reads brand-scoped SMTP (host, credentials, from-address, from-name) from `kernel.system_settings`; `UpdateSmsHandler` writes a brand-scoped `sms/provider` row carrying **`SenderId`** and `DltTemplateId` via `SettingsStore.ResolveBrandIdAsync`; WhatsApp has a brand-scoped `whatsapp/cloud` row plus `brands.whatsapp_number`. Verified in the live DB: the `email/smtp`, `sms/provider` and `whatsapp/cloud` rows all carry a non-null `brand_id`. | ~~no per-brand SMS sender-ID field~~ — **false, `SenderId` exists and is brand-scoped.** The only residue is a *verified* sender-domain (SPF/DKIM) state, and that is not ours to verify: each provider supplies their own SMTP credentials and from-address, so DKIM/SPF alignment sits on their infrastructure. It would only become a gap if we ever sent on their behalf from **our** mail infrastructure, which is not what is built. | — |
| W7 | T3 — White-label mobile app builds | §4 tier T3, §11-P4 | **Missing** | `customer-mobile/app.config.ts` hardcodes `name: 'Laundry Ghar'`, `slug`, `bundleIdentifier: 'com.laundryghar.customer'`, `package`, and `OLIVE_700` brand colour. Same shape in `rider-mobile/app.config.ts`. | No config-driven app factory, no per-provider EAS profile generation | **L** |

### 2.4 Monetization & entitlements (§5)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| E1 | SaaS billing engine (plans, subscriptions, invoices, dunning, suspend-on-nonpay) | §5, §9 | **Done** | `finance_royalty.platform_plans`, `finance_royalty.franchise_subscriptions`, `finance_royalty.franchise_subscription_invoices`, `franchise_subscription_events` (live DB); `commerce.Infrastructure/Worker/Services/SubscriptionBillingService.cs` (dunning → `suspended` + outbox event); `db/patches/subscriptions_module.sql` | — | — |
| E2 | Brand-level (provider ↔ platform) subscription + invoicing + payment link | §5, §9 | **Done** | `identity_access.brand_platform_subscription`, `identity_access.brand_platform_invoice`; `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs`; `core.Application/Identity/Entitlements/Commands/{CollectBrandPlatformInvoice,SetBrandPlatformInvoiceStatus,ProcessPaylinkWebhook}.cs`; `db/patches/phase4_brand_platform_subscription.sql`, `phase4_brand_platform_invoice_paylink.sql` | — | — |
| E3 | Entitlement store: per-brand module licensing | §5 | **Done** | `identity_access.brand_module` (+ RLS `rls_brand`), `identity_access.module_bundle`, `module_bundle_item`, `modules.is_core`; `db/patches/brand_module_entitlement.sql`; entities `SharedDataModel/Entities/IdentityAccess/{BrandModule,ModuleBundle}.cs` | — | — |
| E4 | Entitlement **enforcement** in the token | §5 | **Done** ~~Partial~~ (closed 2026-08-25, tasks **T-01 + T-03b** — `Entitlement:Enforced` is now **true**, made safe by migration 0008's backfill; verified per-brand that zero modules and zero permissions are lost) | `ScopeResolver.cs:169` filters effective permissions to entitled modules when `Entitlement:Enforced` is true (read in all five mint paths). T-01 made the flag **explicit** in `core.WebApi/appsettings.json` + `.Development.json` and added 7 tests covering the enforced path (`Rbac/EntitlementEnforcementTests.cs`), 6 of which fail if the filter is broken. | **Still OFF by deliberate choice.** Verified against the live DB: the only brand `LG-MAIN` is licensed for **19 of 26** active non-core modules (missing `appointments`, `audit`, `fabrics`, `items`, `partner_booking`, `platform_billing`, `report`), because the original backfill grandfathered only the modules that existed then. Flipping it today strips those seven from every user, and until **T-04** bridges plans→entitlements a brand has no way to buy access back. Turning it on is now an acceptance criterion of T-04. | **S** (after T-04) |
| E5 | Entitlement enforcement in navigation | §5 | **Done** ~~Partial~~ (closed 2026-08-25, tasks **T-01 + T-03b**) | `GetNavigator.cs` gates modules by `Entitlement:Enforced` + `brand_module`; now covered by 3 navigator tests (un-entitled hidden, `is_core` never hidden, no-brand-context applies no entitlement). | Same flag, still off — see E4. | **S** (after T-04) |
| E6 | `402 feature_not_in_plan` + upgrade link | §5 "Enforcement" | **Done** ~~Missing~~ (closed 2026-08-25, task **T-05**) | `laundryghar.Utilities/Auth/ApiAuthorizationResultHandler.cs` (renamed from `StepUpAuthorizationResultHandler`) emits `402 feature_not_in_plan` carrying the feature key + upgrade path; new `ent_off` token claim (`TokenClaims`/`ScopeResolver`/`JwtTokenService`) + `IFeatureCatalog`/`FeatureCatalog` for the permission→feature hop; admin-web `lib/apiError.ts` + `api/client.ts` render it. 6 tests. | Only observable once `Entitlement:Enforced` is on (T-04). | — |
| E7 | Feature catalog = the 17 named entitlements | §5 | **Done** ~~Partial~~ (closed 2026-08-25, task **T-02**, per Goutam's OQ-3 answer "split features and modules") | `db/migrations/0005_split_features_from_modules.up.sql` creates `identity_access.features` (33 rows: all **17** §5 keys, is_sellable, + 16 auto-derived from existing modules so nothing is orphaned), `brand_feature`, `bundle_feature`, and `modules.feature_key`. Entities `AppFeature`/`BrandFeature`/`BundleFeature`; `ScopeResolver` + `GetNavigator` resolve permission → module → feature → `brand_feature`. The migration asserts per-brand entitlement parity and aborts rather than let a brand lose a module. | — | — |
| E8 | Plans (Starter/Growth/Pro/Enterprise) mapped to the feature catalog | §5 | **Done** ~~Partial~~ (closed 2026-08-25, task **T-03**, migration 0007 — `growth` added; all four tiers rebuilt from §5 and asserted cumulative. **Prices remain placeholders**, OQ-4) | `identity_access.module_bundle` holds `starter`, `pro`, `enterprise`, `salon-starter` (live DB); seeded in `db/patches/brand_module_entitlement.sql` §5 | `growth` tier missing; bundle contents are laundry-module lists, not §5's feature lists; `module_bundle.price` is nullable and unset for the seeded three | **S** |
| E9 | **`plan.features` ∪ purchased add-ons** as the entitlement source | §5 "Enforcement" | **Done** ~~Missing~~ (closed 2026-08-25, tasks **T-02 + T-03b**) — on the BRAND axis: `module_bundle → bundle_feature → brand_feature`, proven live across upgrade and downgrade with add-ons surviving both. `platform_plans.features` is NOT the source — see conflict D9 | `finance_royalty.platform_plans.features` is a `jsonb` column that **no C# code reads** (grep: only DDL + DTO passthrough). Entitlement is resolved exclusively from `identity_access.brand_module`. | Two disconnected entitlement systems (`platform_plans.features` for the franchise-billing engine, `brand_module` for module licensing) with no bridge. Buying a plan does not grant its features | **M** |
| E10 | À-la-carte add-ons | §5 | **Done** ~~Partial~~ (closed 2026-08-25, task **T-02**) — `SetBrandFeature` writes `source='manual'`; `ApplyBundleToBrand` preserves manual rows across a plan change. Proven live: `custom_domain` bought as an add-on survived an upgrade to Pro and a downgrade back to Starter | `brand_module.source` CHECK `('bundle','manual')` — the data model distinguishes a plan-granted module from a per-brand add-on; `SetBrandModule.cs` can set one manually | No purchase flow, no price per add-on, no self-serve buy — an add-on can only be toggled by a platform admin holding `saas.manage` | **M** |
| E11 | Usage overage metering | §5 | **Partial** | `finance_royalty.platform_plans` carries `max_orders_per_month`, `max_stores`, `max_users`, `max_riders`, `overage_per_order/store/user`; `commerce.subscription_usage_ledger` exists | No metering job counts brand usage against those caps for the **platform** subscription; `BrandPlatformBillingService` bills a flat bundle price | **M** |
| E12 | Roles follow features | §5, §6.3 | **Done** ~~Partial~~ (closed 2026-08-25, task **T-06**) | `identity_access.roles.feature_key` (`db/migrations/0006_roles_follow_features.up.sql`) maps `rider→fleet`, `warehouse_*→processing_facility`, `partner_*→raas_partner`; `core.Application/Identity/AccessControl/BrandFeatureGate.cs` is applied by BOTH `GetAccessRoles` (not offered) and `GrantMembership` (not grantable, keyed on the TARGET brand). 7 tests, 4 mutations caught. | The salon/logistics analogues (`salon_*`, `hub_*`) are deliberately left ungated — the strategy never maps them, and they are already constrained by `roles.vertical_key`. Tracked with OQ-5. | — |

### 2.5 Roles & permissions (§6)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| R1 | Scoped RBAC engine (scopes, ancestor-or-self, union) | §6.3, `docs/rbac.md` §6 | **Done** | `ScopeResolver.cs` (ancestor-key set, membership union); `operations.IntegrationTests/Rbac/ScopeResolverTests.cs` (71 integration tests green) | — | — |
| R2 | Deny-wins + per-user overrides | `docs/rbac.md` §7 | **Done** | `ScopeResolver.cs` (`effective = (roleAllowed − roleDenied ∪ userAllow) − userDeny`); `identity_access.user_permission_override` (live DB); `db/patches/permission_overrides.sql`, `rbac_deny_rows.sql`, `permission_override_scope_expiry.sql` | — | — |
| R3 | Step-up auth for high/critical | `docs/rbac.md` §8 | **Done** | `ScopeResolver.cs` `stepUpPerms` claim; `core.Application/Identity/Auth/Commands/StepUpVerify/` | — | — |
| R4 | Backend-driven navigation | `docs/rbac.md` §11 | **Done** | `GetNavigator.cs`; `core.WebApi/Endpoints/Identity/Navigator.cs`; `db/patches/seed_navigator_modules.sql` | — | — |
| R5 | **The 8-role preset surface** (Platform Admin, Platform Support, Owner, Manager, Staff, Rider, Facility Staff, Customer) | §6.1 | **Missing** | Live DB holds **17 system roles**: `platform_admin, brand_admin, regional_manager, franchise_owner, store_admin, store_staff, warehouse_supervisor, warehouse_staff, hub_supervisor, hub_operator, salon_manager, salon_staff, rider, auditor, support, partner_admin, partner_operator`. None is named `owner`, `manager`, `staff`, or `facility_staff`. | The strategy's simplification layer (§6.3 "8 named roles as presets on the engine") does not exist. Naming and count both diverge | **M** |
| R6 | **The 10 permission groups** surface | §6.2 | **Missing** | Permissions are `module.action` codes across ~34 modules (`docs/rbac.md` §5); `AccessControlDtos.cs` / `ModuleMatrix` build a per-module matrix, not a 10-row group matrix | No grouping layer mapping ~300 permissions onto 10 plain-language rows | **M** |
| R7 | "Customize by subtraction only" (clone-minus) | §6.3 | **Partial** | Custom brand roles exist in the live DB (`catalogue_manager`, `finance_manager`, `operations_manager`, `support_lead` — `is_system=false`); `core.WebApi/Endpoints/Identity/AdminRoles.cs` supports create/clone | Nothing *enforces* subtraction — a custom role can be built up from scratch with any permission set | **S** |
| R8 | Law 1 — money/branding/subscription = Owner only | §6 Law 1 | **Partial** | `AdminEntitlements.cs` gates every subscription route on `permission:saas.read` / `saas.manage` | Those are platform-side permissions; there is no provider-side "Owner" role that owns branding + domain + subscription and excludes Manager. Follows from R5 | **M** |
| R9 | Law 2 — cross-provider = Platform only | §6 Law 2 | **Done** | RLS on `brand_id` across every tenant table (`db/patches/rls_enable_*.sql`, `kernel.rls_bypass()`); `harden_app_user_and_rls_bypass.sql` | — | — |

### 2.6 Multi-provider management (§7) & lifecycle (§9)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| P1 | Provider self-signup (business details + phone OTP) | §9 | **Missing** | `core.Application/Identity/Onboarding/` is **franchise** onboarding inside an existing brand (`StartOnboarding`, `SaveDetails`, `SaveCommercials`, `AddStore`, `InviteOwner`, `ActivateFranchise`). A brand is created only by a platform admin via `AdminBrands.cs`. | No self-serve provider signup at all — the entry point of the entire funnel | **L** |
| P2 | Onboarding wizard: template → plan/trial → locations → catalog seed → staff invites → gateway → live | §7, §9 | **Missing** | no brand-level wizard in `backend/` or `admin-web/src/pages/` | Depends on P1 + V4 | **L** |
| P3 | Live on a sub-domain in minutes | §7 | **Missing** | depends on W3 (Host resolution) | — | **M** |
| P4 | MRR/ARR + adoption monitoring | §7 | **Done** | `GetPlatformBillingSummary.cs`; `admin-web/src/pages/finance/PlatformBillingPage.tsx`; `mv_franchise_saas_mrr` (live DB); `db/patches/phase4_platform_billing_nav.sql` | — | — |
| P5 | Suspend / reactivate a provider | §7, §9 | **Done** ~~Partial~~ (closed 2026-08-25, task **T-18**) | `tenancy_org.brands.status` CHECK `('active','suspended','archived')`; `SubscriptionBillingService.cs` sets subscription status to `suspended` after N dunning attempts | Brand `status='suspended'` is not read by any auth or request path — a suspended brand keeps operating normally | **M** |
| P6 | Suspension = **login-only mode** (owner can see + pay invoices; operations frozen) | §9 | **Done** ~~Missing~~ (closed 2026-08-25, task **T-18**) — `BrandSuspensionMiddleware` + `kernel.brand_status` (migration 0009). Verified live: operations `402 brand_suspended`; login, navigator and billing still 200; reinstatement restores service | no such mode anywhere | Needs a request-path gate that permits only auth + billing routes for a suspended brand | **M** |
| P7 | Feature kill-switches | §7 | **Done** | `kernel.feature_flags` (brand/franchise/store scoped, live DB) | — | — |
| P8 | Per-provider rate limits | §7 | **Done** ~~Missing~~ (closed 2026-08-25, task **T-19**) — gateway partitions on `brand_id`, falling back to IP for anonymous traffic. Per-PLAN quotas deferred to T-23's metering | `laundryghar.Gateway/Program.cs:213-242` — a fixed-window limiter partitioned by **client IP** only | No brand-keyed partition, no per-plan quota | **M** |
| P9 | Support impersonation with consent + full audit | §7, §6.1 Platform Support | **Missing** | only hit for "impersonat" is an unused enum member in `SharedDataModel/Enums/AuthMethod.cs` | No consent record, no impersonation token, no audit trail for it. The Platform Support role (`support`) exists but has no read-any-provider mechanism | **L** |
| P10 | Cancellation → data export → retention → DPDP deletion | §9 | **Partial** | `db/patches/dpdp_erasure_pipeline.sql`; `commerce.Infrastructure/Worker/Services/{CustomerErasureService,CustomerAnonymizer,RetentionSweepService}.cs` — all **customer**-level | No brand-level wind-down: no provider data export, no brand retention window, no brand deletion pipeline | **L** |

### 2.7 Partner / public API (§11-P4)

| # | Capability | Required by | State | Evidence | Gap | Effort |
|---|---|---|---|---|---|---|
| A1 | Public/Partner API with issued credentials | §11 P4, §5 (`api_access` feature) | **Missing** | no API-key table in the live DB, no key middleware in `backend/`. `core.WebApi/Endpoints/Identity/PartnerAuth.cs` is RaaS **partner-user OTP login** (a human logging into a partner app), not a machine API. `db/patches/oauth_authorization_server.sql` + `OAuth.cs` implement an OAuth server for the MCP/assistant use case, not partner API access. | No API key/secret issuance, no scoped machine tokens, no per-key rate limiting or usage metering | **L** |

---

## 3. Phased execution plan (gaps only)

Ordered so that each phase is independently shippable and each task's dependencies are already
satisfied. Effort totals are indicative, not commitments.

### P1 — Entitlements made real (foundation for everything sellable)

*Turns the built-but-dormant entitlement machinery into the actual gate, and gives it a feature
catalog that matches what §5 says we sell.*

1. **P1-1** Enable + prove entitlement enforcement (E4, E5) — add `Entitlement:Enforced` to config with an explicit value, add integration tests that mint a token with `enforceEntitlement: true` and assert un-entitled permissions are stripped and un-entitled nav items hidden.
2. ~~**P1-2** Feature catalog completion (E7)~~ — **DONE** (T-02, migration 0005) — add the missing sellable feature keys to `identity_access.modules`, separating *sellable feature* from *nav module* (`show_in_nav=false` for pure features).
3. **P1-3** Bundle/plan alignment (E8) — add the `growth` bundle, populate bundle→feature membership per §5, set prices.
4. **P1-4** Bridge `platform_plans.features` → `brand_module` (E9) — one resolver so buying/changing a plan expands into entitlement rows; add-ons layer on top as `source='manual'`.
5. ~~**P1-5** `402 feature_not_in_plan` (E6)~~ — **DONE** (T-05) — a distinct response with the feature key + upgrade link, emitted where an un-entitled feature is requested; clients render an upgrade prompt.
6. ~~**P1-6** Roles follow features (E12)~~ — **DONE** (T-06) — a feature→role mapping so un-entitled roles are hidden and ungrantable, not merely greyed.
7. **P1-7** Terminology config, backend-owned (V5) — a per-vertical terminology pack served from the backend and consumed by all four clients, replacing the admin-only 3-noun map.

### P2 — Custom domains (T2 white-label) — **3 of 5 shipped 2026-08-25**

8. ~~**P2-1** `brand_domains` schema (W2)~~ — **DONE** (T-08) — table + RLS + EF entity + config, per §4.2's named columns.
9. ~~**P2-2** Host-header resolution middleware (W3)~~ — **DONE** (T-09) — extend `IBrandResolver` with Host lookup ahead of the header/query fallbacks; cache per host.
10. ~~**P2-3** Domain verification workflow (W4)~~ — **DONE** (T-10) — TXT-record challenge, verify endpoint, admin UI.
11. ~~**P2-4** Per-provider sender identity (W6)~~ — **withdrawn 2026-08-25: not a gap.** See W6 above; email, SMS and WhatsApp sender identity are already per-brand. Nothing to build.
12. **P2-5** SSL automation + domain-health checks (W5) — ops; `ssl_status` written back.

### P3 — Vertical templates + provider self-onboarding

13. **P3-1** Vertical template as data (V4) — a template record carrying mode + default bundle + catalog seed + terminology pack; `phase4_salon_pack.sql` becomes its first instance rather than a bespoke patch.
14. **P3-2** Mode 3 — recurring schedule (M3) — new `fulfillment_mode`, strategy, and delivery-calendar generator.
15. **P3-3** Launch templates per the resolved §4-D1 decision (V6).
16. **P3-4** Provider self-signup (P1 in §2.6) — business details + phone OTP → brand + trial.
17. **P3-5** Onboarding wizard (P2 in §2.6) — template → plan/trial → locations → catalog seed → staff invites → gateway → live on sub-domain.
18. **P3-6** Suspend/reactivate + login-only mode (P5, P6).
19. **P3-7** Per-provider rate limits (P8) and support impersonation with consent + audit (P9).
20. **P3-8** Provider cancellation: export → retention → deletion (P10).

### P4 — White-label app factory + public API

21. **P4-1** Config-driven Expo app factory (W7) — brand identity, colours, bundle IDs, icons from a per-provider config; EAS profile generation.
22. **P4-2** Partner/public API (A1) — API key issuance, scoped machine tokens, per-key rate limits + usage metering, tied to the `api_access` entitlement.

### Cross-cutting (not a phase — do alongside)

23. **X-1** The 8-role preset + 10-permission-group surface (R5, R6, R7, R8). This is the §6 simplification layer over the existing engine. Sequenced with P1-6 because "roles follow features" and "8 presets" are the same surface.

---

## 4. Doc ↔ code conflicts (SQL wins)

| # | Conflict | Doc says | SQL / code says (canonical) | Resolution |
|---|---|---|---|---|
| **D1** | Launch verticals | `PLATFORM_STRATEGY.md` §3: "Launch templates: **Laundry** (Mode 1), **Courier/Parcel** (Mode 2), **Tiffin** (Mode 3)" | `brands_vertical_key_check` allows exactly `laundry`, `salon`, `logistics`. `salon` is fully built (`phase4_salon_pack.sql`, `Fulfillment/Salon/`, `salon_manager`/`salon_staff` roles); `tiffin` does not exist | **SQL wins:** the shipped verticals are laundry/salon/logistics. `logistics` covers Courier. `salon` is an unlisted fourth vertical the strategy does not mention; `tiffin` is unbuilt. **Open question** — see §5 OQ-1 |
| **D2** | Product modes vs fulfillment modes | §3: three modes — Full Service, Pick & Drop, Recurring Schedule | `orders_fulfillment_mode_check` allows `process_deliver`, `appointment`, `point_to_point` | **SQL wins.** `appointment` (salon) is a real fourth mode the strategy omits; `recurring` is a strategy mode with no implementation |
| **D3** | "Feature entitlements — already done" | §2 table: "`feature_flags` + `platform_plans.features` JSONB — **Feature entitlements — already done**" | Entitlement is resolved from `identity_access.brand_module`, seeded by `db/patches/brand_module_entitlement.sql`. `platform_plans.features` jsonb is **read by no code**. `kernel.feature_flags` is a rollout kill-switch, a different mechanism | **Code wins.** The entitlement axis exists but its source is `brand_module`, not `platform_plans.features`; and it is **switched off** (E4). "Already done" overstates it |
| **D4** | RBAC role catalog | `PLATFORM_STRATEGY.md` §6.1: max 8 roles. `docs/rbac.md` §4: 13 roles | Live DB: **17 system roles** (adds `hub_supervisor`, `hub_operator`, `salon_manager`, `salon_staff` — the vertical packs) | **DB wins.** Both docs understate the count. The 8-role surface is a *preset layer* that does not exist yet (R5) |
| **D5** | `docs/rbac.md` §7 "schema additions required (not yet in base schema)" | Lists `role_permissions.effect` and `user_permission_overrides` as outstanding | Both shipped: `role_permissions.effect` and `identity_access.user_permission_override` are live (`db/patches/rbac_deny_rows.sql`, `permission_overrides.sql`), and `ScopeResolver.cs` implements deny-wins | **Doc is stale** — the checklist item is done. Same for most of `docs/rbac.md` §14's checklist |
| **D6** | `docs/rbac.md` §9 / §2 table name | `user_permission_overrides` (plural) | `identity_access.user_permission_override` (singular) | **SQL wins** |
| **D7** | `INDEX.md` status counts | "109 tables / 7 materialized views" (verified 2026-06-10) | The multi-vertical patches (`laundry_fulfillment` schema relocation, salon pack, brand entitlement, brand platform subscription) have landed since | **Stale count** — needs re-verification; not load-bearing for this plan |
| **D9** | Entitlement source of truth | §2/§5 point at `platform_plans.features` (jsonb) as the entitlement store | `finance_royalty.platform_plans` holds **2 rows, both test data** (`QA-SAAS-STARTER → ["orders","catalog"]`, `VERIFY_PP_7024 → {"api_access":true}`) in two incompatible JSON shapes, and sits on the **franchise** billing axis. Entitlement runs on the **brand** axis: `module_bundle → bundle_feature → brand_feature` | **Code wins.** The brand axis is what §5 actually describes (a provider buying from us) and is what works end to end. `platform_plans.features` should be removed or documented as franchise-tier metadata — added 2026-08-25 |
| **D10** | `custom_domain` and `white_label_app` are sellable Enterprise features in §5 with **no module** — and entitlement is enforced along `permission → module → feature`, so both gated **nothing**: custom domains and the white-label config were reachable on any tier. Two of Enterprise's four selling points were free. | **SQL wins** — migration `0019` adds the modules and feature-backed permissions, and grandfathers every brand already using a custom domain. Its assertion then found five more orphaned sellable features (OQ-14). |
| **D8** | `PRODUCTION_SPEC.md` §8 schema path | Points to `database/README.md` + `database/*.sql` | The directory is `database_scripts/` (+ `db/patches/`) | **Stale path** in `PRODUCTION_SPEC.md`; `INDEX.md` has it right |

---

## 5. Open questions (product decisions — not mine to make)

These are genuinely absent from the strategy documents, or the strategy contradicts what is built.
Each blocks or reshapes a planned task.

- ~~**OQ-1**~~ **ANSWERED 2026-08-25 by Goutam: keep `salon`, add `tiffin`.** The vertical set becomes
  `laundry`, `salon`, `logistics`, `tiffin`. Note that tiffin is strategy **Mode 3 (Recurring
  Schedule)**, which is still undesigned — see OQ-2 — so tiffin can be *registered* as a vertical
  before it can *operate*. Original question:
- ~~OQ-1 (blocks P3-3, V6):~~ the strategy names Laundry / Courier / Tiffin as launch templates, but
  the codebase ships Laundry / Salon / Logistics. Is `salon` a first-class launch vertical? Is
  `tiffin` still wanted, and does it replace or join the three? Adding a vertical means widening
  three CHECK constraints, so the answer changes the schema.
- **OQ-2 (blocks P3-2, M3):** Mode 3 "Recurring Schedule" has no design. Does a recurring booking
  materialise one order per occurrence (schedule → N orders) or a single long-lived order with
  occurrence rows? This determines whether it is a new `fulfillment_mode` or a new aggregate.
- ~~**OQ-3**~~ **ANSWERED 2026-08-25 by Goutam: SPLIT `features` from `modules`.** A sellable feature
  and a navigation entry become separate tables with a mapping, so a feature with no menu
  (`custom_domain`, `api_access`, `white_label_app`) finally has somewhere to live. Original question:
- ~~OQ-3 (blocks P1-2, E7):~~ §5's feature catalog and `identity_access.modules` are different
  shapes — a sellable feature vs a nav menu entry. Should they stay one table (a module gains
  `is_sellable`) or split into `features` + `modules` with a mapping? The first is cheaper; the
  second is cleaner and matches §5's language.
- **OQ-4 (blocks P1-3, E8):** §5's example plan tiers (Starter/Growth/Pro/Enterprise) carry no
  prices ("low monthly", "mid", "higher", "custom"). Real prices are needed to seed
  `module_bundle.price` and the plan catalog.
- **OQ-5 (blocks X-1, R5):** mapping the 17 shipped system roles onto the 8 presets is lossy —
  `regional_manager`, `auditor`, `support`, `partner_admin`, `partner_operator`,
  `warehouse_supervisor` vs `warehouse_staff`, and the salon/logistics variants have no home in the
  8. Are the 8 a *new naming* that replaces the 17 (a migration), or a *display grouping* over
  them (additive)? These have very different blast radii.
- **OQ-6 (P2-5, W5):** which SSL path — Let's Encrypt via our own edge, or Cloudflare-for-SaaS?
  §4.2 lists both. This is an infrastructure + cost decision, and it determines whether `ssl_status`
  is polled or webhook-driven.
- **OQ-7 (P4-1, W7):** T3 is described as "one-time fee". Who runs the store submission — us on the
  provider's developer account, or the provider? That decides whether the factory needs credential
  handling at all.
- **OQ-8 (blocks P1-7, V5) — added 2026-08-25:** the terminology **vocabulary** does not exist. §3
  gives one illustrative triple ("garment" ⇄ "parcel" ⇄ "meal box") for verticals we do not ship, and
  `verticalTerms.ts` covers three nouns of the admin console. Needed per shipped vertical: the
  item/unit word and its plural, the on-site location noun (Warehouse / Studio / Hub — already
  exist), the booking noun (order / appointment / shipment), and the customer-facing action verb.
  The *mechanism* is unblocked and should extend the existing `GET /api/v1/fulfillment-config` seam
  (`GetFulfillmentConfigQuery.cs`), which already serves backend-driven descriptors to all four
  clients; it is mode-keyed, and terminology is vertical-keyed, so the pack sits alongside it.
  Writing the vocabulary myself would be inventing the product's voice in three industries.

---

- **OQ-9 (from T-24; corrected 2026-08-25) — five cells where the grants exceed §6.2.** A full
  80-cell diff (not the two originally reported) leaves five real over-grants after migration `0020`
  fixed four labelling bugs: `platform_support`→customers `full` (§6.2 `V`), `platform_support`→money
  `full` (§6.2 `V`, and see OQ-16), `manager`→billing `view` (§6.2 `—`; it is `feature_flag.view`,
  i.e. seeing what you bought), `rider`→dispatch `view` (§6.2 `O` — the derivation cannot express
  "own" for an engine role), `facility_staff`→bookings `view` (§6.2 `—`; it is `orders.read`, which a
  facility worker plausibly needs). Twelve cells grant LESS than §6.2, led by **Platform Support
  holding almost nothing** where §6.2 gives it `V` across eight groups. **Closing any of these is a
  grant change, not a matrix edit.**

- **OQ-16 (from the §6.2 audit) — Platform Support can issue refunds.** The `support` role holds
  `payment.refund`. §6.1 says Support "DOES NOT change money"; §8.1 says read-first. This is a real
  grant, not a labelling artefact, and it is the one over-grant with a security consequence. Left in
  place deliberately: revoking a live permission to make a table agree with a document is the same
  class of mistake as mislabelling one.

- **OQ-17 (from the §6.2 audit) — nine permission modules sit outside §6.2's ten groups.**
  `stores`, `franchises`, `franchise_agreements`, `territories`, `holidays`, `operating_hours`,
  `platforms`, `impersonation`, `api_keys`. §6's ten groups are a simplification of the PROVIDER's
  surface, not a partition of every permission. Forcing them in made the matrix *worse* (see T-24),
  so they are left unclassified and named by a WARNING on every apply. Either §6.2 gains a group —
  which §12 explicitly warns against — or the matrix documents that it covers ten groups and not
  everything.

- **OQ-10 (from T-21) — what a deletion keeps.** `kernel.purge_brand` deliberately preserves audit
  logs (7-year ledger), our platform invoices/subscriptions (statutory retention), and the
  cancellation record itself. The strategy does not decide this; it is one constant away from
  changing, and it is the difference between "we deleted everything" and "we deleted everything we
  are allowed to".

- **OQ-11 (from T-04; SHARPENED 2026-08-25 by a live browser pass) — the "Platform plans" screen
  is pointed at an empty table.** This is worse than the column-level finding below, and it took
  opening the console to see it. The sidebar has a top-level entry **Platform plans** →
  `/platform-plans` → `GET /commerce/api/v1/admin/platform-plans`, which reads
  `finance_royalty.platform_plans`. That table holds exactly two rows, `QA-SAAS-STARTER` and
  `VERIFY_PP_7024`, **both soft-deleted in June 2026**. So the screen correctly renders
  *"No platform plans yet."*

  The tiers that actually exist — the ones entitlement enforces and all 12 brands are subscribed to
  — live in `identity_access.module_bundle` and surface under **Users & Roles → Licensing**
  (`EntitlementsTab.tsx`), where they render correctly: Starter ₹999, Growth ₹1,999, Pro ₹2,999,
  Enterprise ₹7,999, Salon Starter ₹1,499.

  **Why this matters for OQ-4.** The prices that need confirming are not editable on the screen
  named after them. An operator who opens "Platform plans" to set pricing sees an empty table, and
  the real tiers are one tab deep inside an unrelated section. Settling OQ-4 through that screen
  would mean editing rows that gate nothing. Two ways out — point the screen at `module_bundle`, or
  retire it — and both are product calls, not mine.

- **OQ-11 (original) — `finance_royalty.platform_plans.features` is read by nothing.** Two rows of
  incompatible test junk on the *franchise* billing axis. Either dead weight from that engine or an
  unbuilt franchise-tier feature. A product call.

- **OQ-12 (from T-23) — what the public API should expose.** §11 P4 says "partner/public API" and §5
  sells `api_access`; neither names an operation. The mechanism ships complete (issuance, scopes,
  revocation, per-key limits, metering, entitlement) with **one** endpoint proving the chain.
  Choosing the business operations and scope names is product, not code — each is one line once
  decided.

- **OQ-13 (from T-17) — there is no payment-gateway configuration surface anywhere.** No table, no
  screen. The wizard's "connect online payments" step reads `brands.config->>'paymentGateway'`, so
  it is honest today (a cash-only provider defers it) and becomes live the moment that screen exists.

- **OQ-14 (from T-22) — five sellable features still gate nothing.** `wallet`, `online_payments`,
  `loyalty`, `item_tracking`, `whatsapp_bot` are on the price list and have **no console module**, so
  the `permission → module → feature` chain never reaches them. (`api_access` is also flagged by the
  assertion but IS enforced, directly in `kernel.resolve_api_key`.) Migration `0019` **WARNs on every
  apply** rather than papering over it. Fixing them means inventing module and permission mappings
  for surfaces that do not exist.

- **OQ-15 (from the build) — three transitive dependency advisories.** `MessagePack` (Aspire
  AppHost), `Microsoft.OpenApi`, `SSH.NET`. All arrive through other packages, so clearing them means
  pinning someone else's dependency tree — a real change with its own blast radius, belonging to no
  task here.

- **OQ-18 (from the sidebar/dashboard review) — a global date range and Export were mocked, never
  wired.** The Topbar carried a `Today / 7 days / 30 days` `<select>` with no `onChange` and an
  Export button with no `onClick`, on **every page**, directly above the dashboard KPIs. Picking
  "30 days" changed nothing. Both have been **removed** — a control that silently does nothing is
  worse than an absent one, because the reader trusts the number beside it — and building them back
  is a product decision, not a bug fix:
    - Is the range **global** (it renders on every page) or does it belong to the dashboard alone?
    - What does it mean for the three right-now gauges — Pending Pickup, In Wash, Out for Delivery
      are current counts with no time axis; a range either silently skips them (today's behaviour,
      but then the control lies about its scope) or has to be redefined for them.
    - Export of **what** — the KPI row, the revenue series, or the underlying orders? Each is a
      different endpoint. Note the list screens already have their own working `Export CSV` in
      `FilterableTable`, so a second, different export needs a reason to exist.
  A dead `Sun` "Toggle theme" button in the sidebar footer was removed for the same reason; there is
  no theme store anywhere in the app, so dark mode is a feature to build, not a button to restore.

## 6. Honest summary

The platform is much further along than §11's four phases imply, and further along in a different
shape than §2's "already done" claims describe.

**Genuinely done:** the scoped RBAC engine with deny-wins, step-up and backend-driven navigation;
RLS tenant isolation; the multi-vertical discriminators and the fulfillment-strategy seam with three
real implementations; both billing engines (franchise-level and brand-to-platform) including
dunning, invoices and payment links; the per-brand module entitlement store with bundles and an
admin console.

**The single highest-leverage gap is not new code — it is a switch.** Entitlement enforcement is
fully implemented in `ScopeResolver` and `GetNavigator` and is disabled everywhere because
`Entitlement:Enforced` appears in no config file, and no test covers the enforced path. Until that
is on and proven, none of the monetization in §5 is real.

**Genuinely missing, in dependency order:** the feature catalog as §5 defines it; the
plan→entitlement bridge; `402`; `brand_domains` + Host resolution (the whole T2 mechanic, of which
literally nothing exists); vertical templates as data; Mode 3; provider self-signup and the
onboarding wizard; login-only suspension; impersonation; the white-label app factory; the public
API; and the 8-role/10-group simplification surface.

**Estimate honesty:** `docs/MULTI_VERTICAL_BLUEPRINT.md` §5 audits the multi-vertical program alone
at 324 person-days. The gaps above are a superset of the remainder. This is a multi-quarter
program, not a sitting.
