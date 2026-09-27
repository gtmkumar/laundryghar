# LaundryGhar — SaaS Architecture Audit

**Date:** 2026-09-27 · **Scope:** the whole repository at commit `274b7af` (backend, 4 clients, `database_scripts/`, `db/`, `deploy/`, `ops/`, CI, docs) · **Method:** static read of source and SQL, cross-checked against the repo's own docs and git history; no live database or running environment was available, so every claim below is tied to a file and line you can open. Where a finding depends on the state of the live database it is marked **(verify live)**.

---

## 1. Verdict

**Is LaundryGhar architected as a SaaS application?** Partly, and less than the documentation says.

- It **is** a genuine multi-tenant application. The tenant boundary (brand → franchise → store) is enforced in the database with Row-Level Security, the runtime connects as a non-superuser role, the tenant is taken from a signed JWT rather than a client header, and there are real per-tenant export/purge, entitlement, and consented-impersonation mechanisms. This is more than most "SaaS" codebases have.
- It is **not yet a SaaS platform**. The commercial loop does not close (a trial never ends, the billing worker is off everywhere, an owner cannot see or pay an invoice, nothing is metered), the tenant cannot self-serve anything from a UI, white-label custom domains are dead behind the gateway, the schema cannot be rebuilt from the repo, and the runtime is single-instance (in-memory caches and limiters, in-process workers, local disk for uploads).
- The **retrofit left seams**. Tenancy, entitlements, billing, sub-brand scoping and ABAC were layered onto a finished single-brand laundry app between June and September 2026, each as a new mechanism beside the old one. The token lanes, the three service hosts and the SQL policies drifted apart. Two of those seams are serious today: customer-level isolation on the money tables fails open in the commerce host, and the sub-brand policy shipped on 2026-09-05 denies customer tokens outright on orders and payments.

### Scorecard

| SaaS pillar | Score (0-5) | One-line reason |
|---|---|---|
| Tenant data model and DB isolation | 3 | RLS by brand on 136 of 160 tables with a NULL-safe house policy; but the identity tables holding PAN/Aadhaar/bank data were never enabled, the bypass is a session variable the app role can set itself, and document numbers are globally unique |
| Request-pipeline tenant context | 3 | Correct design (JWT → 12 session GUCs per connection open); two divergent `ICurrentTenant` adapters broke it in the commerce host |
| Identity and access | 4 | RS256/JWKS, Argon2id, refresh-token rotation with reuse detection, live revocation, consented impersonation, scoped roles |
| Billing and entitlements | 2 | Entitlement engine and 402 exist; three billing engines, one of them dead with a UI, none metering usage, trial never expires |
| Tenant lifecycle and self-serve | 2 | Signup / onboarding / cancellation APIs exist; no client screen calls any of them; suspension only via dunning that is switched off |
| White-label and multi-domain | 1.5 | Domain table, TXT verification and Host resolution exist; Host never reaches the services through the gateway; no TLS automation; mobile is one hard-coded build per tenant |
| Configuration and extensibility | 2.5 | Four-level settings hierarchy resolved per request; `feature_flags` table has no reader; terminology is per vertical, not per tenant |
| Operability (deploy, secrets, observability, scale-out) | 1.5 | One compose file, one replica, no tenant id in logs, no OTLP endpoint, secrets layer documented but absent, uploads on `/tmp` |
| Schema and change management | 1.5 | Three coexisting mechanisms; 142 SQL patches of which 85 are referenced by no script, test or CI; migrations depend on hand-applied tables; CI never builds the schema |
| Tenancy test coverage | 2.5 | Real Testcontainers RLS tests exist; 146 of them silently pass when Docker is absent; no HTTP-level cross-tenant test |
| Documentation integrity | 1 | ~2 MB across 202 Markdown files; `HANDOFF`, `INDEX`, `TESTING`, `PRODUCTION_ENV`, `DEPLOYMENT` and `GAP_ANALYSIS` contradict the code and each other |

---

## 2. What was done right

These are worth protecting; several later mistakes are regressions against them.

- **Isolation lives in the database, not in handler discipline.** ADR-001 chose RLS over schema-per-tenant; policies follow one house pattern (`kernel.rls_bypass() OR brand_id = kernel.current_brand_id()`) and are `FOR ALL TO app_user … WITH CHECK`, so inserts are constrained too (`db/patches/rls_proposal.sql:270-278`).
- **The runtime role cannot bypass RLS.** `app_user` is `NOSUPERUSER NOCREATEDB NOCREATEROLE` and does not own the tables (`db/patches/harden_app_user_and_rls_bypass.sql:41-66`), so RLS applies without `FORCE`.
- **Tenant identity comes from the signed token.** `TenantResolutionMiddleware.cs:32-48` honours `X-Brand-Id` only for `platform_admin`; everyone else's header is ignored. Claims are minted from database memberships (`core.Application/Identity/Auth/Common/ScopeResolver.cs:28-101`), never from request bodies.
- **The session variables are re-written on every connection open** (`laundryghar.SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs:93-120`), and no connection string disables Npgsql's reset, so pooled connections cannot carry one tenant's context into the next request.
- **Auth is mature**: RS256 with JWKS, key fails closed outside Development (`core.Infrastructure/Auth/RsaJwtKeyProvider.cs:30-54`), Argon2id, refresh-token families with reuse detection (`RefreshTokenHandler.cs:41-133`), `perm_version` live revocation.
- **Entitlement engine is real**: features split from modules, plan bundles plus manual add-ons, permission stripping at token mint, `402 feature_not_in_plan` with an upgrade path, "roles follow features" (`db/migrations/0005`, `0006`, `0019`; `ApiAuthorizationResultHandler.cs:90-115`).
- **Tenant-safe caching and storage by default**: output-cache keys fold in brand, franchise and store (`laundryghar.Utilities/Caching/OutputCaching.cs:44-94`); file keys are server-generated `{brandId}/{area}/{uuid}` and only readable through brand-filtered handlers (`FileStorageKeyGenerator.cs:43-54`).
- **Per-tenant data ownership exists**: NDJSON export, retention window, and `kernel.purge_brand()` (`db/migrations/0015`), plus DPDP customer erasure.
- **Consented support impersonation** with request/approve split, per-request re-check, and audit (`db/migrations/0014`; `ImpersonationGuardMiddleware.cs`).
- **Real RLS tests** run against PostgreSQL in Testcontainers as `app_user` (`tests/operations.IntegrationTests/Rbac/RlsIsolationTests.cs`, `UserTenantIsolationTests.cs`, `SubBrandScopeRlsTests.cs`).

---

## 3. The mistakes, ranked

Severity: **Critical** = tenant data leak, or an outage of a tenant-facing path today. **High** = the SaaS cannot be sold or operated as designed. **Medium** = latent defect or design debt that will produce the next Critical.

### Critical

**C1. The commerce host never sets `app.current_customer_id`, so customer-versus-customer isolation on the money tables fails open to brand level.**
`commerce.Infrastructure/Worker/CommerceHostCurrentTenant.cs` implements only `BrandId` (line 51), `FranchiseId` (67), `StoreId` (68), `UserId` (69), `PartnerId` (70) and `BypassRls` (80). `CustomerId`, `UserType`, `TokenUse`, `ScopeNodes`, `Permissions` and `Roles` fall through to the interface defaults of `null` (`laundryghar.SharedDataModel/Contracts/ICurrentTenant.cs:31-54`). The interceptor then writes an empty customer id (`RlsConnectionInterceptor.cs:64`). The policy on `commerce.payments`, `payment_refunds`, `wallet_accounts`, `wallet_transactions`, `loyalty_points_ledger`, `customer_packages`, `package_usage_ledger`, `coupon_redemptions`, `notification_preferences` and `whatsapp_message_log` is `brand_id = current_brand_id() AND (current_customer_id() IS NULL OR customer_id = current_customer_id())` (`db/patches/rls_proposal.sql:270-278`). With the GUC empty the `IS NULL` arm is true and the database stops distinguishing customers. The same fix was applied to core/operations in `HttpContextCurrentTenant.cs:29-46` and never to the commerce adapter. Today the only thing keeping one customer out of another's wallet is the `CustomerId ==` predicate in each handler of the service that holds the money.

**C2. Customer and API-key tokens carry no `scope_nodes`, and migration 0031's RESTRICTIVE policy denies them on orders, pickups, slots, price lists and payments.** **(verify live)**
Customer JWTs contain `token_use`, `brand_id`, `phone` only (`core.Infrastructure/Auth/JwtTokenService.cs:104-143`); `HttpContextCurrentTenant.ScopeNodes` reads the absent claim as null (`:53`), the interceptor writes the sentinel `?` (`RlsConnectionInterceptor.cs:85-88`), `kernel.split_setting` maps it to NULL, and `kernel.within_scope_cols` returns NULL for a NULL claim (`db/migrations/0031_subbrand_scope_rls.up.sql:74-79`). The policy is `AS RESTRICTIVE` on 39 named tables including `order_lifecycle.orders`, `order_items`, `pickup_requests`, `delivery_slots`, `delivery_assignments`, `customer_catalog.price_lists` and `commerce.payments` (`0031:130-210`), with no carve-out for `token_use` (grep finds none). The repo's own test asserts an unresolved claim "denies everything" (`SubBrandScopeRlsTests.cs:264-271`). `docs/FIX_REPORT.md:25,128` records 0031 as applied to the live database on 2026-09-05 and verified only at the four staff scope levels. If that is the state of production, the customer app has been reading zero orders and slots since then. Either way, the token lanes and the DB policy were not designed together.

**C3. The schema cannot be rebuilt from the repository.**
Three mechanisms coexist: `database_scripts/` (two builders, `apply_all.sh` targets one `public` schema and `apply_schemas.sh` targets ten), `db/patches/` (142 SQL files plus three apply scripts), and `db/migrations/` (33 up/down pairs with `db/tools/migrate.sh`). Only 10 FK patches are applied by `db/patches/apply_patches.sh`; `db/build_from_scratch.sh` adds six named patches plus `rls_proposal.sql`, which by its own header "DOES NOT enable RLS on any table" (line 11). No script anywhere applies `rls_enable_*.sql`, `harden_app_user_and_rls_bypass.sql`, `fix_legacy_*_rls_policies.sql`, `subscriptions_module.sql` or `brand_module_entitlement.sql`. Migrations 0005, 0007, 0008, 0011 and 0015 depend on `identity_access.brand_module`, `module_bundle` and `brand_platform_subscription`, which only those hand-applied patches create. The integration fixture re-declares the kernel functions inline rather than sourcing the repo SQL (`RbacRlsFixture.cs:135-160`). CI never runs `migrate.sh`. Following `deploy/README.md`'s bootstrap (build_from_scratch then migrate up) produces a database with RLS disabled and missing tables. The production schema is therefore a hand-crafted artifact on one machine; staging parity, disaster recovery and onboarding a second engineer all depend on tribal knowledge.

**C4. The gateway's per-tenant rate limiter is keyed on an unverified brand id, and the login limiter shares one bucket for the whole platform.**
`laundryghar.Gateway/RateLimitPartitioning.cs:33-70` partitions on the raw `X-Brand-Id` header, else the `brand_id` claim of a bearer token decoded by hand with, as the comment says, "no signature check, no expiry check", else the leftmost `X-Forwarded-For` regardless of any trusted-proxy setting (`:79-86`). Any client can rotate random GUIDs to escape the per-IP cap (each brand gets a 10× bucket, `Gateway/Program.cs:216-218`) or burn a victim tenant's budget. Separately, Identity's `auth` policy is keyed on `RemoteIpAddress` (`core.WebApi/Program.cs:198-216`) while `deploy/docker-compose.yml:25` keeps forwarded headers off on the services, so every login arrives from the gateway's IP and all tenants share ten logins per minute.

**C5. Outbound notifications use one tenant's WhatsApp/SMS credentials for everyone.**
`commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:43-53` loads every brand's `whatsapp/cloud` and `sms/provider` rows under RLS bypass and takes `rows.FirstOrDefault(...)`; the file's own comment (lines 12-15) says brand overrides "are not yet supported for Worker notification channels". Per-brand sender settings are collected in the admin UI and encrypted at rest, then ignored at send time. The first tenant to store its own provider credentials may end up sending every other tenant's OTPs.

**C6. The identity tables that hold staff PAN, Aadhaar, bank and UPI details were never put under RLS.**
`identity_access.user_profiles` (columns added in `database_scripts/02_patch_user_employment_kyc.sql:14-21`), `user_scope_memberships`, `otp_codes`, `refresh_tokens`, `login_history` and `password_resets` all received an `rls_user_self` policy in `db/patches/rls_proposal.sql:296-343`, but no script or migration ever runs `ENABLE ROW LEVEL SECURITY` on them (grep across `database_scripts/`, `db/patches/`, `db/migrations/` finds none). Enablement was deferred twice, in `db/patches/_applied_rls_bc1_bc2.sql:23-30` ("not yet set by the app middleware") and again in `db/migrations/0029_users_brand_rls.up.sql:149-153`, and never revisited. These tables also have no `brand_id`, so `kernel.export_brand()` and `kernel.purge_brand()` (which iterate tables that have one) neither export nor delete them: a cancelled tenant's staff KYC survives the purge. At the database level any `app_user` session can read every tenant's staff financial identifiers; the only boundary is the application predicate that the 2026-08-31 audit found missing in two of three handlers.

### High

**H1. The commercial loop never closes: trials are perpetual, renewals and dunning are off, and nobody can suspend a tenant by hand.**
`CompleteSignup.cs:217` creates the platform subscription as `trialing`; `BrandPlatformBillingService.cs:68` only renews `Status == "active"` rows, and the only other reference to `trialing` (`:204`) moves it to `past_due` if an invoice is already overdue, which cannot happen because no invoice is ever issued to a trial. `WorkerOptions.BrandPlatformBillingEnabled` defaults to false and is set in no `appsettings*.json` or in `PRODUCTION_ENV.md`. The only code path that suspends a brand is that worker's dunning; there is no admin endpoint over `kernel.set_brand_suspension` for a ToS or manual suspension.

**H2. A tenant owner cannot see or pay their own invoice.**
Every route under `/api/v1/admin/entitlements` requires `saas.read`/`saas.manage` (`core.WebApi/Endpoints/Identity/AdminEntitlements.cs`), which no seed grants to `brand_admin` (`db/patches/seed_subscriptions_modules.sql:14`). The suspension allow-list opens that prefix "so the owner can pay", so a suspended owner gets 403. The 402 response's `UpgradePath` is `/settings?tab=plan`, and no such tab exists in `admin-web/src/pages/settings/SettingsPage.tsx`.

**H3. Three billing engines; the one the docs call "already done" is dead, and none meters usage.**
Engine A (customer subscriptions, `commerce.subscription_*`) has a full job and gateway charger. Engine B (`finance_royalty.platform_plans`, `franchise_subscriptions`) has tables and CRUD but no invoice generation, no job, and a `features` JSONB read by no C# code; its two rows are soft-deleted test data, and `PlatformPlansPage.tsx` renders that empty table from the sidebar. Engine C (`identity_access.brand_platform_subscription`) is the one that works: bundle apply, proration, Razorpay Payment Links, webhook. Its invoice has `amount, currency_code, status, due_at` and nothing else: no number, no CGST/SGST/IGST breakdown (ADR-010 requires it), no payment row, no PDF; `status` is free text with no CHECK. Plan caps (`max_stores/users/orders`, overage rates) are written and never read; the only metered thing in the platform is API-key calls (`db/migrations/0016:87`).

**H4. Provider self-service has no user interface.**
Signup, onboarding wizard, cancellation/export and white-label endpoints all exist in `core.WebApi`; `grep` for `/signup`, `/admin/provider-onboarding`, `/admin/cancellation`, `/admin/white-label` across `admin-web/src`, `pos-web/src` and both mobile apps finds no caller. `admin-web/src/App.tsx:57-116` routes login, accept-invite and operator pages only. A tenant is provisioned by the platform operator with curl.

**H5. White-label tier T2 is dead behind the gateway, TLS is not automated, and the mobile apps are one build per tenant.**
`BrandResolver.cs:70` resolves `context.Request.Host.Host`; YARP rewrites `Host` to the destination and the gateway declares only a `PathPattern` transform (`Gateway/Program.cs:46-68`); the services' forwarded-headers middleware processes `XForwardedFor | XForwardedProto` only (`laundryghar.ServiceDefaults/Extensions.cs:275`). Behind the gateway every service sees `Host: core`, so a verified custom domain never matches. `BrandResolverHostTests.cs` uses `DefaultHttpContext` and cannot catch this. `ssl_status` is observed (`TlsDomainHealthChecker.cs`) but never issued or renewed; OQ-6 is still open. `customer-mobile/app.config.ts:10-57` hard-codes name, bundle id, colours and `DEFAULT_BRAND_CODE='LG-MAIN'`; `google-services.json:12` names package `com.launddryghar.app` and `GoogleService-Info.plist:12` `com.laundrygahar.ios` (both misspelt) against the real `com.laundryghar.customer`, so push and Google sign-in cannot bind; `GetAppConfig.cs` computes per-brand app identity that no build consumes.

**H6. The runtime assumes a single instance.**
Rate limiter, output cache, brand-status and domain caches are in-process (`OutputCaching.cs:27-29` says so); all background jobs are `BackgroundService`s inside the commerce container iterating every brand sequentially (`BrandPlatformBillingService.cs:80-85`); `IEventPublisher` is a logging stub (`commerce.Infrastructure/Worker/Stubs/LoggingEventPublisher.cs:7-10`) although three docs describe RabbitMQ; file storage is `LocalFileStorageProvider` under `/tmp/laundryghar-uploads` with no volume in compose, and `s3`/`azure-blob` throw `NotSupportedException` (`operations.Infrastructure/Storage/FileStorageProviderFactory.cs:24-33`). A second replica or a redeploy loses uploads, cache coherence and limiter state.

**H7. No tenant context in telemetry.**
No logging scope adds `brand_id` (grep for `BeginScope` finds nothing), no `X-Correlation-Id` is ingested or propagated, Sentry has no brand tag, OTLP export is configured but `OTEL_EXPORTER_OTLP_ENDPOINT` is set nowhere in `deploy/`, and health checks are `self` only with no database probe (`Extensions.cs:174-176`). A tenant-specific incident cannot be found in logs.

**H8. Secrets and credentials.**
`PRODUCTION_ENV.md:85-134` and `HANDOFF.md` §6 describe an `ISecretsProvider` / `Secrets:Provider` layer with seven tests; `laundryghar.ServiceDefaults/` contains only `Extensions.cs` and `ExternalDependencyResilience.cs`, and no `ISecretsProvider` exists anywhere. `db/patches/harden_app_user_and_rls_bypass.sql:47` unconditionally runs `ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user'`, so re-applying the patch in any environment resets the production runtime password to a public value. `run-stack.sh`, `backup.sh`, `restore.sh` and `migrate.sh` carry `postgres/postgres` and `app_user/app_user` as defaults; `Admin@123` is in `scripts/smoke.sh:16-17`.

**H9. The RLS bypass is a session variable that the application role can set for itself.**
`kernel.rls_bypass()` reads the custom GUC `app.bypass_rls` (`db/patches/harden_app_user_and_rls_bypass.sql:31-38`), which `app_user` sets with `set_config` on every connection open (`RlsConnectionInterceptor.cs:93-106`); `db/patches/app_user_role.sql:68-70` notes that no grant is needed for this. There is no second database role for platform or worker traffic, no `BYPASSRLS` separation, and the `app_admin` role created in `rls_proposal.sql:56-58` is `NOLOGIN` and used by nothing. Tenant isolation therefore rests on the application never executing attacker-influenced SQL: one injection, or one mis-wired code path (M3 grants it by URL prefix), is a full cross-tenant read and write. All raw SQL found is parameterized, so this is a design weakness rather than a live hole.

**H10. Document numbers and idempotency keys are unique across the whole platform, so tenant A blocks tenant B.**
`expenses.expense_number` (`database_scripts/07_bc7_finance_royalty.sql:150`) is generated as `EXP-{yyyyMMdd}-{count+1}` from a per-brand count (`ExpenseCommands.cs:205`); `royalty_invoices.invoice_number` (`07:260`) as `ROY-{date}-{n}` (`RoyaltyCommands.cs:125`); `warehouse_batches.batch_number` (`database_scripts/04_bc4_order_lifecycle.sql:604`) as `WB-{date}-{n}` (`CreateWarehouseBatch.cs:49`); `subscription_invoices.invoice_number` as `SI-{date}-{n}`. Each has a global `UNIQUE`, so the second tenant to create a document on any given day fails. `orders UNIQUE (order_number, created_at)` (`04:123`) embeds a store code that is unique only per brand; `pickup_requests.request_number` (`04:257`) and garment tag codes use a four-hex-character brand prefix plus a non-atomic count. Client-supplied idempotency keys on `payments`, `wallet_transactions`, `outbox_events` and `notifications_outbox` are also global, so one tenant's key collides with another's. Nullable-`brand_id` uniques on `roles`, `system_settings`, `feature_flags` and `platform_plans` are not enforced for platform rows (no `NULLS NOT DISTINCT`).

**H11. Two RLS policies re-introduced the raw-cast pattern that already caused one outage.**
`db/migrations/0001_customer_social_auth_and_pin.up.sql:50-54` (`customer_identities`) and `db/patches/phase4_salon_fulfillment_schema.sql:91-96` (four salon tables) compare `brand_id = current_setting('app.current_brand_id', true)::uuid` directly. The interceptor writes an empty string for a null brand, and `''::uuid` raises `22P02`. Because permissive policies are OR-combined and every branch is evaluated, any platform-admin or worker session touching those tables errors. `HANDOFF.md` §5 records that exactly this pattern took down the subscriptions module and that the house pattern (`kernel.current_brand_id()`, which `NULLIF`-guards the cast) is mandatory; nothing enforces the rule, so it regressed.

### Medium

**M1. Two `ICurrentTenant` implementations that drifted.** `HttpContextCurrentTenant.BrandId` (core/operations) ignores the platform-admin `brand_id_override`; `CommerceHostCurrentTenant.cs:59-61` honours it. The same admin action stamps `audit_logs.brand_id = NULL` in core/operations and the target brand in commerce (`AuditContext.cs:52-54`). C1 is the same root cause.

**M2. ABAC runs on a second data source with no tenant context.** `AbacServiceCollectionExtensions.cs:36-37` builds a plain `NpgsqlDataSource` without the RLS interceptor. Under `app_user`, `authz.policy` RLS (`0024:160-163`) hides brand policies, `brand_feature` reads are empty, resource attributes are hidden, and `authz.decision_log` inserts fail their `WITH CHECK`. The engine is shipped disabled; the day `Abac:Enabled=true, Mode=enforce` is set, every decision denies and the audit trail is empty. `NpgsqlAbacStore.cs:44-48` claims the read "bypasses RLS deliberately"; it does not.

**M3. RLS bypass by path prefix before authentication.** `core.WebApi/Program.cs:621-646` sets `bypass_rls=true` for any request under eleven prefixes (`/api/v1/auth/*`, `/api/v1/customer/auth/*`, `/api/v1/partner/auth/otp`, `/oauth`, `/api/v1/signup`) and for authenticated `/api/v1/auth/step-up` (`:556-558`). Any endpoint later added under those prefixes inherits full cross-tenant read/write.

**M4. Analytics materialized views are outside RLS.** `app_user` has plain SELECT on them (`harden_app_user_and_rls_bypass.sql:70-77`); every query in `commerce.Application/Analytics/Reporting/Queries/` filters by `RequireBrandId()` today. One missed `.Where` is a cross-tenant revenue and LTV leak with no database backstop.

**M5. Partner-supplied `BrandId` is written unvalidated, and the outbox insert now violates RLS.** `CreatePartnerBooking.cs:87,110` stores `req.BrandId` (never checked to exist) into `partner_bookings` and `kernel.outbox_events`; the comment at `:103-106` says the outbox is "RLS inert", but `db/migrations/0027:48` enabled it with the strict brand policy, and a partner session has no brand GUC. Chargeable partner bookings fail their `WITH CHECK`. This is the pattern behind C1, C2 and M2: code comments describing policies that migrations later changed.

**M6. One human, one brand.** `identity_access.users.phone_e164` and `email` are globally unique (`database_scripts/02_bc2_identity_access.sql:23-24`); `CompleteSignup` refuses an existing phone; `ScopeResolver` accepts a `requestedScopeType` that no caller passes, and the `X-Scope` header named in `TokenClaims.cs:12` is unimplemented. A franchise owner with two brands cannot sign up twice and cannot switch. `scope_nodes` also lists all of a user's memberships across brands (`ScopeResolver.cs:168-170`), so `IsWithinScope(brandId: B)` passes for a user active in A; only the RLS `WITH CHECK` saves the write.

**M7. Fail-open valves on tenancy-gating paths.** Token-version revocation (`TenantResolutionMiddleware.cs:51-71`), brand suspension (`BrandSuspensionMiddleware.cs:71-101`, `BrandStatusStore.cs:47-53`, 30 s cache) and the permission-to-feature catalog (`FeatureCatalog.cs:64-70`) all allow the request when the lookup fails. `CachingBehavior.cs:43-63` caches any `ICacheableRequest` under an author-supplied key with no tenant component (no implementers yet).

**M8. Entitlement enforcement is indirect and incomplete.** Zero endpoints declare a feature (`grep RequireFeature` finds none); gating happens only because a permission was stripped at token mint. Of 521 mapped endpoints, 340 carry a `permission:` policy and roughly 180 (customer, rider, partner, anonymous, webhook lanes) are not plan-gated at all. Five sellable features on the price list (`wallet`, `loyalty`, `online_payments`, `item_tracking`, `whatsapp_bot`) map to no module and gate nothing; migration `0019` only `RAISE WARNING`s about it. Because entitlement is baked into each user's JWT, a plan change bumps `perm_version` for every member and forces a tenant-wide re-login.

**M9. Configuration is not tenant-shaped where it matters.** `kernel.feature_flags` has a table, a `DbSet` and an enum, and no evaluation code anywhere (`PLATFORM_STRATEGY.md` §7 calls it the kill-switch). `identity_access.vertical_terms` is keyed by vertical, holds three term keys, and cannot be overridden per tenant despite the migration comment. `SettingsResolver.cs` walks store → franchise → brand → platform on every request with no cache.

**M10. Test and CI gaps that hide the above.** 146 occurrences of `if (!_fx.DockerAvailable) return;` make every RLS test pass silently on a runner without Docker, so a green pipeline does not prove the isolation gate ran. There is no `WebApplicationFactory` test proving a brand-A JWT gets 404 on a brand-B order. `pos-web` has no CI job, no Dockerfile and no compose service. `admin-web` and `pos-web` have no unit tests. CI never applies migrations to a database.

**M11. pos-web keeps access and refresh tokens in `localStorage`** (`pos-web/src/stores/authStore.ts:64-83`, no `partialize`), unlike admin-web which moved the refresh token to an HttpOnly cookie.

**M12. Documentation is a liability.** 202 Markdown files, about 2 MB, with an "authority order" that places SQL first while the SQL is not reproducible (C3). Concrete contradictions: `HANDOFF.md` says 9 services on 5050/5001-5008 (consolidated to 3 hosts on 5301-5303; `scripts/smoke.sh:76-127` still probes the old ports); `INDEX.md` says 109 tables (stale since June); `TESTING.md` says 8 test projects and no Testcontainers (3 projects, Testcontainers in use); `HANDOFF.md` §6 and `PRODUCTION_ENV.md` say the secrets provider is done (absent); `db/HANDOFF.md` says RLS is inert and the FK patches unapplied; `docs/GAP_ANALYSIS.md` §6 says `Entitlement:Enforced` "appears in no config file" (it is `true` in `core.WebApi/appsettings.json:13`); `PLATFORM_STRATEGY.md` §2 says the billing engine is "already done" (engine B, which has no invoice generation); `protocol/DEPLOYMENT.md` describes Redis, RabbitMQ, S3, Hangfire, Key Vault, ClamAV, blue/green and PITR, none of which exist.

**M13. A tenant cannot read its own brand row.** `tenancy_org.brands` carries `rls_admin_only USING (kernel.rls_bypass())` (`db/patches/rls_proposal.sql:326-341`), so a tenant session sees no brand at all. The codebase names this "THE BRANDS-RLS TRAP" (`0015:252-258`, `0021:49-52`) and has worked around it six times with `SECURITY DEFINER` functions (`0003:34`, `0009:27`, `0014:138`, `0015:268`, `0017:69`, `0021:63`) instead of changing the policy to `id = kernel.current_brand_id()` for SELECT.

**M14. The stated privilege model for tenant purge is not what the grants enforce.** **(verify live)** `db/migrations/0015_brand_cancellation.up.sql:248-250` revokes `kernel.purge_brand` from PUBLIC and states it is "NOT granted to app_user … the worker's job, running as a superuser"; `RetentionSweepService.cs:241` calls it through the ordinary `app_user` context. `harden_app_user_and_rls_bypass.sql:67` set `ALTER DEFAULT PRIVILEGES … GRANT EXECUTE ON FUNCTIONS TO app_user`, which a `REVOKE … FROM PUBLIC` does not undo, so in a database where that patch preceded the migration the app role can purge a tenant from any code path; where it did not, the retention sweep fails with permission denied. Either way the comment and the ACL disagree.

**M15. The audit log is writable and deletable by the application role.** `db/patches/audit_logs_partition_maintenance.sql:78` grants `SELECT, INSERT, UPDATE, DELETE ON identity_access.audit_logs TO app_user`; there is no append-only trigger, and the audit row is written inside the business transaction, so a rolled-back or compromised request leaves no trace. `authz.decision_log` got this right (`0024:180-182`).

**M16. Partitioning is time-only and its maintenance has drifted.** The six pg_partman parents (`orders`, `audit_logs`, `process_logs`, `notifications_log`, `rider_location_pings`, `authz.decision_log`) partition by timestamp with no tenant key, so per-tenant retention is a row delete, never a partition drop. `process_logs` was moved to `laundry_fulfillment` (`db/patches/phase1_slice_c_laundry_fulfillment.sql:56-66`) but `partman.part_config`, `db/tools/run_partman_maintenance.sh` and `db/build_from_scratch.sh` still name `order_lifecycle.process_logs`, so pre-creation of its partitions has stopped. The scheduler is an opt-in macOS launchd plist (`db/tools/com.laundryghar.partman.plist`). Partitions have run out twice already, per `rider_ping_partition_maintenance.sql:4-10` and `audit_logs_partition_maintenance.sql:6-9`.

**M17. No zero-downtime discipline in schema changes.** Across 33 migrations there are 26 `CREATE INDEX` and zero `CONCURRENTLY`, zero `NOT VALID … VALIDATE` constraint additions, `ALTER TABLE … SET SCHEMA` and `RENAME` under `ACCESS EXCLUSIVE` locks in the patches, and `0004_add_tiffin_vertical` rebuilds CHECK constraints on eight tables with full validation. Fine for one developer's database; on a shared production database each of these is a tenant-wide stall.

**M18. Customer-self isolation was removed from `customer_catalog`.** `db/patches/fix_legacy_customer_catalog_rls_policies.sql:19-25` replaced `rls_brand_or_customer` with plain brand policies ("handled at the application layer"), so at the database level a customer token can read every customer and address of its brand. The same customer-self arm still exists on the commerce tables, where C1 defeats it from the other side.

### Schema inventory (from a replay of the SQL sources; **verify live** against `pg_policies`)

| Metric | Count |
|---|---|
| Live logical tables (partition children excluded) | 160 = 92 baseline + 48 patches + 22 migrations, minus 2 dropped |
| Tables with `brand_id` | 126 |
| Tables with any tenant column (brand, franchise, store, warehouse, partner) | 130 |
| Tables with `ENABLE ROW LEVEL SECURITY` | 136 (all 130 tenant-column tables + `platforms`, `brands`, `users`, `user_permission_override`, `partners`, `authz.policy_condition`) |
| Tables with `FORCE ROW LEVEL SECURITY` | 5 (not needed: `app_user` does not own tables) |
| Tables with at least one policy | 144; about 190 policies in total, 178 `FOR ALL`, 31 targeting `PUBLIC` instead of `app_user` |
| Tenant column but RLS not enabled | 0 (migration 0027 guards this) |
| Policy present but RLS never enabled | 8, all in `identity_access` (see C6) |
| No tenant column | 30: 22 legitimately platform-level, 8 tenant data (C6 plus `user_permission_override`) |
| Patch files in `db/patches/` referenced by no script, test or CI | 85 of 142 (C3) |
| `CREATE INDEX … CONCURRENTLY` in migrations | 0 of 26 (M17) |

Other schema-level observations: there is no `platform` schema, so platform tables are spread over `tenancy_org`, `identity_access`, `kernel`, `finance_royalty` and `authz`; hot child tables (`order_items`, `order_status_history`, `wallet_transactions`, `loyalty_points_ledger`, `process_logs`, `rider_location_pings`) carry only the lone `brand_id` FK index, and `subscription_usage_ledger` / `subscription_billing_attempts` have no brand index at all although RLS filters on it; reference data is correctly per-tenant for catalog, price lists, templates and slots, and global with no override for modules, features, bundles, presets and `vertical_terms`.

---

## 4. Root causes: the mistakes behind the mistakes

The findings above cluster into six decisions, and fixing findings one at a time without addressing these will produce the next batch.

1. **Tenancy was retrofitted onto a finished app, one mechanism at a time.** Git history shows the laundry app "built and running" by 2026-06-12, then entitlements (06-21), multi-vertical (06-23), SaaS billing (06-28), sub-brand RLS (07-01), the PaaS wave (08-25) and ABAC (08-31), each added beside its predecessor instead of replacing it. The result is three billing engines, two entitlement stores (`platform_plans.features` and `brand_feature`), two `ICurrentTenant` adapters, two authorization engines (RBAC live, ABAC inert), two navigation seeds, and three schema mechanisms. Every duplicate is a place where one copy was fixed and the other was not (C1, M1, H3).

2. **The token lanes, the three hosts and the SQL policies have no shared contract or contract test.** Four principal types (staff, customer, partner, API key) × three hosts × twelve session GUCs × four policy families is the real interface of this system, and nothing asserts it end to end. So a policy was added that assumes a claim customers never carry (C2), an adapter was written that omits half the GUCs (C1), and comments describe policies that migrations later changed (M5, M2).

3. **The schema is managed as a journal of work sessions, not as migrations.** Patches are named after features and dates, applied by hand in an order that exists only in chat transcripts, and the migration tool arrived after ~120 of them. CI builds the code but never the database it runs against (C3, M10).

4. **"Done" was defined by the existence of tables and endpoints, not by a running path with a consumer and a test.** The handoff and gap documents mark signup, cancellation, white-label, billing and secrets as complete when the API has no client, the job is switched off, the config flag is unset, or the code does not exist (H1, H2, H4, H8, M12). Much of this documentation was generated at the pace of the code and never reconciled.

5. **Single-instance assumptions were baked into a platform that promises N tenants on one deployment.** In-memory limiter and caches, in-process workers, local disk, no queue (H6), and no tenant dimension in telemetry (H7).

6. **Safety valves default to open on exactly the paths that gate tenants** (M7), and one broad bypass is granted by URL prefix rather than per endpoint (M3).

---

## 5. Recommended path

Ordered by risk removed per unit of work. Effort tags are rough: S = days, M = 1-3 weeks, L = a month or more.

### Phase 0: stop the bleeding (S each)

1. Collapse the two tenant adapters into one. Make `CommerceHostCurrentTenant` delegate to `HttpContextCurrentTenant` for the HTTP lane and keep only the worker marker; add a test that opens a connection in each host with a customer token and asserts all twelve GUCs. (C1, M1)
2. Decide the customer/API-key contract for `scope_nodes`: either mint `brand:<id>` for those lanes or make `within_scope_cols` short-circuit on `token_use IN ('customer','api_key','partner')`. Add an HTTP-level customer-lane test through a real host before touching production. **Check production first** for zero-row order reads since 2026-09-05. (C2)
3. Gateway: validate the JWT with the Identity JWKS before partitioning by brand; ignore `X-Brand-Id` for unauthenticated traffic; use `UseForwardedHeaders` with `KnownProxies` instead of reading `X-Forwarded-For` by hand; enable forwarded headers on the services for the gateway network so the login limiter sees client IPs. (C4)
4. Key `NotificationSettingsCache` by the outbox row's `brand_id`, falling back to the platform row, then env. (C5)
5. Delete the `ALTER ROLE … PASSWORD 'app_user'` line and rotate the role; strip credential defaults from the ops scripts. (H8)
6. Enable RLS on the six identity tables with membership-resolved policies modelled on `0029` plus a self arm, and add them to the export/purge registry. (C6)
7. Rewrite the five raw-cast policies with the `kernel.*` helpers and add a database assertion (extend `authz.rls_drift`) that fails when any `pg_policies.qual` contains a bare `::uuid` cast of the brand setting. (H11)

### Phase 1: make the database reproducible (M)

8. Take a schema-only dump of the live database as `db/baseline/0000_baseline.sql`, archive `database_scripts/` and `db/patches/`, and make `migrate.sh` the only path forward. Add a CI job that builds baseline + migrations in Testcontainers, then runs the RLS suite with Docker mandatory (fail the job when Docker is absent instead of returning). (C3, M10)
9. Add an RLS coverage assertion test: every table in a tenant schema that has `brand_id` must have RLS enabled and a policy, with an explicit allow-list of exceptions. Add the lanes × hosts × GUCs contract test. (root cause 2)
10. Split the database role: `app_tenant` with no bypass branch in its policies, and `app_platform` for platform-admin and worker traffic on its own pool; retire the `app.bypass_rls` GUC from tenant policies. (H9)
11. Make every per-tenant document number and idempotency key unique on `(brand_id, …)`, backed by per-brand sequences like `order_number_sequences`; use `UNIQUE NULLS NOT DISTINCT` for the nullable-brand catalogs. (H10)
12. Fix the comment-versus-policy drift found in M5, M2 and M14, repoint `partman.part_config` for `process_logs`, revoke UPDATE/DELETE on `audit_logs`, and adopt `CONCURRENTLY` / `NOT VALID` for future migrations. (M14 to M17)

### Phase 2: close the commercial loop (M to L)

13. Keep engine C; delete engine B and its page, or repoint the page at `module_bundle`. Add `trialing → active` with a first invoice at period end, a status CHECK, invoice numbering, GST breakdown and a payment row per ADR-010, and turn `BrandPlatformBillingEnabled` on in the production contract. (H1, H3)
14. Add an owner-scoped billing surface (`GET /admin/my-plan`, pay-link, invoice list) gated on `settings.*` from the tenant context, and build the `/settings?tab=plan` page the 402 already points at. Add a platform-admin suspend/reinstate endpoint with an audit row. (H2, H1)
15. Meter orders, stores, users and notifications per period against the bundle; return `402 quota_exceeded` at create time. (H3)
16. Add explicit `RequireFeature("key")` endpoint metadata and an authorization handler that reads `ent_off`, then map the five orphan features or take them off the price list. (M8)
17. Build the provider-facing UI for signup, the onboarding wizard, cancellation/export and white-label settings; the APIs exist. (H4)

### Phase 3: white-label and operability (L)

18. Add YARP's `RequestHeaderOriginalHost` transform and `XForwardedHost` on the services; front the gateway with Caddy on-demand TLS or Cloudflare for SaaS (settle OQ-6); expose `GET /public/brand-by-host` for runtime theming; generate mobile `app.config` from `GetAppConfig` per EAS profile and fix the Firebase bundle ids. (H5)
19. Move rate limiting, output cache and distributed cache to Redis; split the worker into its own deployable with leader election or a queue; implement the S3 provider with presigned reads; add resource limits and a second replica in compose. (H6)
20. Add a logging scope with `brand_id`, `user_id`, `trace_id` after tenant resolution; propagate `traceparent` from the gateway; set an OTLP endpoint; tag Sentry with brand; add a database health check. (H7)
21. Implement the documented secrets provider or delete the documentation. (H8)

### Phase 4: documentation (S, then continuous)

22. Reduce to one handoff, the ADRs, and generated references. Adopt the rule that a "DONE" entry must cite a test or a running path, and delete the sections that describe infrastructure that does not exist.

---

## 6. Method and limits

- Read: `INDEX.md`, `HANDOFF.md`, `PLATFORM_STRATEGY.md`, `PRODUCTION_SPEC.md`, `RBAC_Navigation_PaaS_PostgreSQL.md`, `docs/SAAS_PLATFORM_ARCHITECTURE.md`, `docs/GAP_ANALYSIS*.md`, `docs/AUDIT_REPORT.md`, `docs/FIX_REPORT.md`, `docs/ABAC_AUDIT_2026-08-31.md`, `docs/TASKS.md`, ADRs 001-010, `protocol/DEPLOYMENT.md`, `backend/laundryghar/PRODUCTION_ENV.md`, `deploy/`, `ops/`, `.github/workflows`, and git history (89 commits from 2026-06-13).
- Traced in source: tenant resolution and the RLS interceptor, all three `ICurrentTenant` implementations, JWT minting and validation, the worker scope, output caching, the gateway, entitlement resolution and the 402 path, suspension middleware, the billing services, signup and onboarding commands, notification channels, storage, the four clients' API layers and build configs, and the migrations that define the policies each of these relies on.
- Schema counts come from replaying `database_scripts/*.sql`, then `db/patches/*.sql` in the order `build_from_scratch.sh` implies, then `db/migrations/*.up.sql`, with drops, renames and schema moves applied; because the real patch order was never recorded, live `pg_policies` may differ where a hand-applied patch was skipped or re-ordered.
- Not done: nothing was executed against a live database or a running stack, so counts of tables and policies come from the SQL files, and the customer-lane regression in C2 is inferred from code plus `docs/FIX_REPORT.md`, not observed.
- Four parallel read-only reviews (request pipeline, database schema, billing and lifecycle, clients and infrastructure) fed this document; every Critical and High finding was then re-verified by opening the cited files.
