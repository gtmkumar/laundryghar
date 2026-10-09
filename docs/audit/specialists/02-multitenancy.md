# 02 — SaaS Multi-Tenancy Specialist

Agent key: `multitenancy` · Area code: `TEN` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw`

## Scope and method

**Inspected (read end-to-end, not grepped-and-guessed):**
- Tenant context pipeline: `laundryghar.SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs`, `laundryghar.SharedDataModel/DependencyInjection.cs`, `laundryghar.Utilities/Services/HttpContextCurrentTenant.cs`, `HttpContextCurrentUser.cs`, `commerce.Infrastructure/Worker/CommerceHostCurrentTenant.cs`, `WorkerScope.cs`, `laundryghar.Utilities/Middlewares/{TenantResolutionMiddleware,BrandSuspensionMiddleware}.cs`, the middleware order in `core.WebApi/Program.cs`, `operations.WebApi/Program.cs` and `commerce.WebApi/Program.cs`.
- Anonymous brand resolution: `core.Infrastructure/Services/BrandResolver.cs`, `core.Application/Identity/Auth/Common/CustomerBrandResolver.cs`, `core.WebApi/Endpoints/Engagement/PublicEngagement.cs`, and migrations 0002/0003.
- Tenant lifecycle: signup (`core.Application/Identity/Signup/Commands/CompleteSignup.cs`), brand CRUD (`core.WebApi/Endpoints/Identity/AdminBrands.cs` and its handlers), cancellation, withdraw and export (`AdminCancellation.cs`, `RequestBrandCancellation.cs`, `WithdrawBrandCancellation.cs`, `BrandExportService.cs`), dunning (`BrandPlatformBillingService.cs`, migration 0021), the purge (`RetentionSweepService.cs`, migration 0015) and the status store (`BrandStatusStore.cs`, migration 0009).
- Database isolation: `database_scripts/0*.sql` (tenant columns and unique constraints), `db/patches/{rls_proposal,app_user_role,harden_app_user_and_rls_bypass,rls_enable_engagement_cms}.sql`, and migrations 0024/0025/0027/0029/0031. I also wrote a Python scan of every `CREATE TABLE`, `ADD COLUMN brand_id` and `ENABLE ROW LEVEL SECURITY` statement (script: `scratchpad/multitenancy/scan.py`).
- Other paths: caches (`laundryghar.Utilities/Caching/OutputCaching.cs`, `GatewaySettingsCache`, `IMemoryCache` users), the gateway (`laundryghar.Gateway/Program.cs`, `RateLimitPartitioning.cs`), storage (`operations.Infrastructure/Storage/*`, `FileStorageKeyGenerator.cs`), workers (`commerce.Infrastructure/Worker/Services/*`), analytics (`commerce.Application/Analytics/Reporting/*`), webhooks (`RazorpayWebhookHandler.cs`), API keys (`ApiKeyAuthentication.cs`, migration 0016), and identity administration (`GrantMembership.cs`, `RevokeMembership.cs`, `UpdateUser`, `DeactivateUser`, `SetPassword`).
- Tests: everything under `backend/laundryghar/tests/**` that touches tenancy (listed in the test section below).
- Deploy: `deploy/docker-compose.yml`, `deploy/.env.example`, the `appsettings*.json` connection strings, and `.github/workflows/ci.yml`.

**Commands I ran:**
1. I started a private PostgreSQL 16 cluster on `127.0.0.1:55433` (data dir `/var/tmp/lg-mt02`). It could not live in the scratchpad because the `postgres` OS user cannot traverse `/tmp/claude-0`, which has mode 700. I did not touch the other agent's server on port 55432.
2. I loaded verbatim function and policy text from `rls_proposal.sql`, `harden_app_user_and_rls_bypass.sql`, migration 0025 L49-68 and the whole of migration 0031 into trimmed `commerce.payments` and `order_lifecycle.orders` tables.
3. As a non-superuser `app_user`, I ran the exact `set_config` values that `RlsConnectionInterceptor` emits for each `ICurrentTenant` implementation. Scripts: `scratchpad/multitenancy/repro_setup.sql`, `repro_scen.sql` and `repro_susp.sql`.

**Could NOT verify:**
- No .NET SDK, so I built nothing and ran no xUnit tests.
- No Docker, so no Testcontainers integration tests.
- I did not run the full DB build (`pg_partman` and `postgis` are not installed).
- I do not know which patches and migrations are actually applied in any deployed environment.
- I sent no HTTP traffic end to end.
- I did not execute YARP's Host-header behaviour.

Every runtime claim below is therefore either an SQL-level reproduction against verbatim DDL, or a static trace, and is labelled as such.

## Current-state summary

### What the tenant is
- **Primary tenant = brand** (`tenancy_org.brands`, `database_scripts/01_bc1_tenancy_org.sql:L42-79`).
- **Hierarchy:** `platforms` (single row) → `brands` → (`territories`) → `franchises` → `stores` / `warehouses` (`01_bc1_tenancy_org.sql:L20,42,87,178,236,299`). Franchises, stores and warehouses are *sub-tenant scopes inside a brand*. They are not separate tenants.
- **Second, parallel tenant type:** RaaS logistics **partners** (`logistics.partners`, `partner_id`). These are isolated by the `rls_partner` policies (`db/patches/rls_partner*.sql`).
- **Customers are per-brand.** `customer_catalog.customers` has `UNIQUE(brand_id, phone_e164)` (`03_bc3_customer_catalog.sql:L68`).
- **Staff users are global identities** (`identity_access.users` has no `brand_id` and has globally unique `phone_e164` and `email`, `02_bc2_identity_access.sql:L23-24`). They attach to brands only through `identity_access.user_scope_memberships(scope_type, scope_id, role_id)`.
- **Denormalised `brand_id`:** almost every tenant-owned table carries it. My static scan found 162 tables, of which only the 34 catalogue, identity and authz tables listed in `scan.py`'s output lack `brand_id`.

### How tenant context is resolved and propagated (traced path)
`Client → Gateway (YARP; no token validation; rate-limit bucket chosen from unvalidated X-Brand-Id/JWT) → service host → UseAuthentication (RS256 JWT) → [core only: pre-auth bypass for login/OTP/refresh/signup/oauth paths, Program.cs L543-561, L620-646] → TenantResolutionMiddleware → ImpersonationGuardMiddleware → BrandSuspensionMiddleware → UseAuthorization → UseOutputCache → endpoint → handler (ICurrentUser.RequireBrandId() + explicit .Where(BrandId==…)) → EF Core → RlsConnectionInterceptor.ConnectionOpened → SELECT set_config('app.current_brand_id', …, false) ×12 → PostgreSQL RLS policies (kernel.current_brand_id(), kernel.rls_bypass(), kernel.within_scope_cols()).`

1. **The brand comes from the signed JWT `brand_id` claim.** Staff, customer and API-key tokens all carry it; partner tokens do not (`core.Infrastructure/Auth/JwtTokenService.cs:L51-52, L109, L147-171`; `ApiKeyAuthentication.cs:L181`).
2. **Header override is admin-only.** `X-Brand-Id` is honoured on authenticated requests only for `user_type == platform_admin`. Those callers get `Items["bypass_rls"]=true` plus `Items["brand_id_override"]` (`TenantResolutionMiddleware.cs:L32-48`). A non-admin cannot spoof the brand through the header, because the claim always wins (`HttpContextCurrentUser.cs:L131-138` only consults the override item, and only the admin branch sets it). **Verified.**
3. **Anonymous endpoints resolve the brand themselves.** They use the Host header (custom domain) → `X-Brand-Id` → `?brandCode=` → `LG-MAIN` (`BrandResolver.cs:L67-96`). Customer-auth endpoints use `X-Brand-Id` → body `brandCode` → config default, and **never consult the Host** (`CustomerBrandResolver.cs:L20-52`).
4. **DB propagation.** `RlsConnectionInterceptor` is Scoped and runs on every EF connection open. It writes all 12 GUCs, writing `""` or `"?"` for nulls, so a pooled connection cannot carry over the previous request's values (`RlsConnectionInterceptor.cs:L57-121`; `DependencyInjection.cs:L48-64, L86-96`). The policies read `kernel.current_brand_id()` = `NULLIF(current_setting('app.current_brand_id', true), '')::uuid` (`rls_proposal.sql:L65-83`).
5. **There are no EF global tenant query filters.** The only `HasQueryFilter` calls are soft-delete filters (32 of them, e.g. `BrandConfiguration.cs:L60`). App-layer isolation is therefore the explicit `RequireBrandId()` predicate convention (159 files), backed by RLS.
6. **The runtime role is the RLS-subject `app_user`.** This holds in dev (`*/appsettings.Development.json`, `Username=app_user`) and is what `deploy/.env.example` documents ("MUST be a non-superuser role (app_user)"). Production gets it only through `DB_CONNECTION_STRING`; there is **no startup assertion** that the role is non-superuser and `NOBYPASSRLS`.
7. **Failure modes.**
   - No brand on an authenticated call: `RequireBrandId()` throws a 400 (`HttpContextCurrentUser.cs:L143-146`), and RLS sees `NULL` and returns no rows.
   - Anonymous call with no resolvable brand: 404.
   - The commerce host's context-less non-worker flows fail closed (`CommerceHostCurrentTenant.cs:L30-35, L80-90`). **Verified.**

### Tenant lifecycle (traced)

| Stage | Entry point | What actually happens | Evidence |
|---|---|---|---|
| Create (self-serve) | `POST /api/v1/signup/complete` (anonymous, core runs it with `bypass_rls`) | Verifies the OTP, refuses a reused phone, generates the brand code, creates the brand (`status='active'`), the owner user, a `brand_admin` membership, franchise `OWN`, a trial subscription, and provisions the template. | `CompleteSignup.cs:L63-217`; `core.WebApi/Program.cs:L641-646` |
| Create (platform) | `POST /api/v1/admin/brands` (`brands.create`) | Plain insert. | `AdminBrands.cs:L28, L47-52` |
| Suspend | Dunning worker → `kernel.set_brand_suspension(...,'nonpayment')`; or a manual `PUT /admin/brands/{id}` with `Status` | `brands.status='suspended'`. | `BrandPlatformBillingService.cs:L212-240`; `0021_brand_dunning.up.sql:L60-100`; `UpdateBrand.cs:L23` |
| What suspension blocks | `BrandSuspensionMiddleware` | Returns 402 for **authenticated, non-platform-admin** HTTP calls outside an allow-list. It does **not** gate anonymous traffic, background workers or deleted brands, and it fails open on lookup error. | `BrandSuspensionMiddleware.cs:L44-57, L63-101, L128-141`; `BrandStatusStore.cs:L26-53` |
| Cancel / withdraw | `POST /api/v1/admin/cancellation` and `/withdraw` (`settings.manage`) | `brands.status` cancelled ⇄ active through a SECURITY DEFINER function. **Bypass of suspension: see SA-TEN-003.** | `RequestBrandCancellation.cs:L60-94`; `WithdrawBrandCancellation.cs:L38-56`; `0015:L265-294` |
| Export | `GET /api/v1/admin/cancellation/export` | `kernel.export_brand(tenant.BrandId)` (SECURITY DEFINER), streamed as NDJSON. | `AdminCancellation.cs:L64-97`; `0015:L143-165` |
| Delete (soft) | `DELETE /api/v1/admin/brands/{id}` (`brands.delete`) | Sets `brands.deleted_at` only. Status stays `active`; logins and operations continue (see SA-TEN-008). This contradicts the comments saying "there is no delete endpoint". | `DeleteBrand.cs:L14-24`; `AdminCancellation.cs:L21`; `RetentionSweepService.cs:L198-200` |
| Purge (hard) | `RetentionSweepService` worker → `kernel.purge_brand` (not granted to `app_user`) | Fixpoint delete of every `brand_id` table and a tombstone on the brand. | `RetentionSweepService.cs:L213-265`; `0015:L175-249` |
| Reactivate | Dunning reinstates only `nonpayment` suspensions; withdrawal sets the status back to `active` | — | `0021:L92-100`; `WithdrawBrandCancellation.cs:L56` |

## Data access path × tenant filter mechanism

| Data access path | Tenant filter mechanism | Verified? | Gap |
|---|---|---|---|
| Staff HTTP, core/operations hosts (EF) | JWT `brand_id` → `RequireBrandId()` predicate + RLS `rls_brand` (+ 0031 `rls_subbrand_scope` RESTRICTIVE using `scope_nodes`) | Verified (code + SQL repro S4) | No composite FKs (SA-TEN-011). |
| Customer HTTP, core/operations hosts | `brand_id` claim + per-handler `CustomerId` predicate; RLS `rls_brand_or_customer` on payments, wallets etc.; **orders only `rls_brand`** | Partially (SQL repro S1) | Under 0031, customer tokens carry no `scope_nodes`, so **every 0031 table returns 0 rows and audit inserts fail** (SA-TEN-001). Before 0031, a customer sees the brand's other customers' orders at the RLS level. |
| Any HTTP request to the commerce host (payments, wallets, finance, analytics, partner billing) | `CommerceHostCurrentTenant`: brand + user + partner only; **no CustomerId, no ScopeNodes** | Verified (code + SQL repro S2/S3) | Pre-0031 the customer RLS arm fails open (SA-TEN-002); post-0031 everything that is not bypassed is denied (SA-TEN-001). |
| Platform-admin HTTP | Full `bypass_rls`; brand only through `X-Brand-Id` → `RequireBrandId()`. RLS `WITH CHECK` is not enforced. | Verified | Correctness depends entirely on handler predicates (SA-TEN-007). |
| Anonymous public CMS (`/api/v1/public/*`) | `IBrandResolver` + explicit `.Where(BrandId==…)`; **no bypass** | Partially (SQL repro S5) | Under enforced RLS the result is 0 rows (SA-TEN-006). Host resolution is not reachable behind the gateway (SA-TEN-009). |
| Anonymous auth / signup / OAuth (core) | Request-wide `bypass_rls` + per-query identifier predicates | Verified (code) | Customer auth accepts any `X-Brand-Id` and ignores Host and brand status. |
| Razorpay webhooks (commerce, core) | Exact-route `bypass_rls`; payment found by `gateway_order_id`; HMAC with **that brand's** secret, else the platform env secret | Verified (code) | If a brand has no secret, the platform-wide secret is accepted for its payments (Low; noted, not raised). |
| Partner (RaaS) HTTP | `partner_id` claim → `app.current_partner_id` → `rls_partner` | Partially (tests exist; not run) | — |
| Commerce workers (14 hosted services) | `WorkerScope` marker → full `bypass_rls`; brand taken from each record (e.g. `NotificationMappingService.cs:L211-260`) | Partially (sampled 2 services) | Workers ignore brand status (SA-TEN-008). Isolation is per-handler only. |
| Analytics MVs (`analytics.mv_*`) | App predicate `BrandId == RequireBrandId()` only (MVs cannot carry RLS; granted to `app_user`) | Verified (code) | A store filter is not scope-checked, and the refresh is global (SA-TEN-013). |
| SECURITY DEFINER functions (`export_brand`, `brand_status`, `set_brand_cancellation_state`, `brand_app_identity`, `brand_onboarding_facts`, `resolve_*`, `user_in_brand`, …) | Caller-supplied `p_brand_id`; **the function trusts it** | Verified (grep + read of 0015) | Isolation depends on the caller (SA-TEN-007). |
| `identity_access.user_scope_memberships`, `user_profiles`, `refresh_tokens`, `role_permissions`, `login_history` | **No RLS** (deliberate, `0029:L147-153`); app predicates only | Verified | Cross-brand membership writes (SA-TEN-004). |
| `identity_access.users` | RLS by `user_in_brand(id, current_brand)` (0029) | Verified (code) | A membership can be created for any user id, so the boundary is bypassable (SA-TEN-004). |
| File storage (local provider) | Key `{brandId}/area/...` + streaming endpoint that loads the owning entity by brand | Verified (code) | S3 and Azure are unwired seams. |
| Output cache | `VaryByValue(brand\|franchise\|store)` + `X-Brand-Id` + declared query keys | Verified (code) | Key ignores `scope_nodes` (Info, SA-TEN-014). The store is in-process. |
| `IMemoryCache` (brand status, domain, token version, ABAC) | Keys include `brandId`, host or user id | Verified (code) | — |
| Gateway rate limit | Partition `brand:{X-Brand-Id or unvalidated JWT brand_id}` | Verified (code) | Spoofable (SA-TEN-005). |
| Logs / traces | None | Verified absent (grep) | SA-TEN-014. |

## Existing automated isolation tests (what they really assert)

None were executed here: there is no SDK or Docker. CI runs them on GitHub (`.github/workflows/ci.yml:L31-36`).

- `operations.IntegrationTests/Rbac/RbacRlsFixture.cs` stands up a **hand-written, trimmed** spine schema (L140-280). It is not the real `database_scripts`. Pooling is off (L43-47). Its `SetRlsAsync` claims to be a "byte-for-byte mirror" of the interceptor (L86-98), but it sets **6 of the interceptor's 12 GUCs**: no `customer_id`, `user_type`, `token_use`, `scope_nodes`, `permissions` or `roles`.
- `RlsIsolationTests.cs` (9 tests):
  - cross-brand reads and inserts on `stores`;
  - bypass sees all brands;
  - self-membership;
  - admin-only users;
  - audit `WITH CHECK`;
  - `app_user` is not superuser and not `bypassrls` (asserted inside the container only).
- `UsersBrandRlsTests.cs` (14) applies **migration 0029 verbatim** and asserts brand-bounded user visibility and cross-tenant update/delete. `UserTenantIsolationTests.cs` (6) asserts the handler-level brand filter on `GetUsers` and `GetUserById` against real PostgreSQL.
- `SubBrandScopeRlsTests.cs` (15) applies **migration 0031 verbatim**. It asserts that store, franchise and warehouse boundaries hold and that unresolved or empty `scope_nodes` denies. **It does not model a customer, partner or API-key session, or the commerce host's tenant adapter.** Those are exactly the sessions that end up with an unresolved `scope_nodes` (SA-TEN-001).
- `Partner{,Dispatch,Invoice,Wallet}RlsTests.cs` (17) test partner isolation. `RolesGlobalVisibilityRlsTests.cs` (8) applies migration 0030.
- `BrandResolverHostTests.cs` (5) tests domain resolution at the SQL function level.
- `BrandCancellationTests.cs` (13) covers export and purge isolation (`An_export_never_leaks_another_tenants_data`). `The_cancellation_transition_refuses_every_other_status` checks only the *target* status, **not** a suspended *source* status, so the gap in SA-TEN-003 is untested.
- `operations.Tests/Auth/BrandSuspensionMiddlewareTests.cs` (8) is a middleware unit test.
- **Missing:**
  - an end-to-end test through `RlsConnectionInterceptor` with pooling on;
  - any test running the real DB build plus `app_user` against all policies;
  - a CI job asserting RLS coverage after migrations;
  - an HTTP-level cross-tenant test;
  - a test that a suspended brand cannot reactivate itself.

## Findings

### SA-TEN-001 — Migration 0031's restrictive scope policy denies ALL rows (and all audited writes) for customer, API-key and commerce-host sessions
- Category: Tenant isolation / availability (fail-closed outage)
- Severity: Critical
- Status: Partially Verified. SQL semantics are reproduced against the verbatim migration and the C# adapters were read; I did not run HTTP end to end, and I do not know whether 0031 is applied in any deployed environment.
- Evidence:
  - `db/migrations/0031_subbrand_scope_rls.up.sql:L82` makes `IF v_nodes IS NULL THEN RETURN NULL` the result for an unresolved `scope_nodes`. L198 creates the policy `AS RESTRICTIVE … FOR ALL`. L140-178 list 39 tables, including `commerce.payments`, `identity_access.audit_logs`, `kernel.system_settings`, `order_lifecycle.orders`, `order_items`, `pickup_requests`, `delivery_slots` and `tenancy_org.stores`.
  - `db/migrations/0025…up.sql:L49-68`: `kernel.split_setting` maps `'?'` to NULL.
  - `RlsConnectionInterceptor.cs:L85-90, L103` writes `"?"` when `ICurrentTenant.ScopeNodes` is null.
  - `JwtTokenService.cs:L98-119` (customer tokens) and `L122-145` (OAuth customer tokens) emit no `scope_nodes`; only staff tokens do (`L64`). `ApiKeyAuthentication.cs:L181-189` emits none either.
  - `CommerceHostCurrentTenant.cs:L42-94` does not implement `ScopeNodes`, so it always gets the interface default `null` (`ICurrentTenant.cs:L47`), **even for staff**.
  - The audit interceptor adds an `audit_logs` row on (almost) every `SaveChanges` (`AuditSaveChangesInterceptor.cs:L34-41, L83-102`), and it is registered in commerce (`commerce.WebApi/Program.cs:L58`).
- Observed behaviour: the SQL reproduction (`repro_scen.sql`, run after applying 0031 verbatim) gave:
  - S1, operations-host customer: orders 2→**0**, payments 1→**0**;
  - S2, commerce-host customer: payments 2→**0**;
  - S3, commerce-host brand admin: payments 2→**0**, orders 2→**0**;
  - S4, operations-host brand admin with `scope_nodes=brand:A`: unchanged at 2/2.

  The same NULL makes the `WITH CHECK` fail, so every audited insert or update by these principals raises a policy violation.
- Reproduction / verification method: `psql -h 127.0.0.1 -p 55433 -d mt_repro -f repro_setup.sql`, then `f0031.sql`, then `repro_scen.sql`. The GUC values were derived from the C# adapters by reading them.
- Impact: if 0031 is applied and services run as `app_user`, all of the following break:
  - customer order history, pickups and slots;
  - every commerce-host read and write for staff and customers (payments, wallets, finance, analytics, partner billing);
  - API-key integrations.

  Conversely, if these flows "work" somewhere, RLS is not actually enforced in that environment. Either way the documented isolation model and the runtime disagree. The existing test suite cannot detect this (see the test section above).
- Recommended remediation (smallest safe change):
  1. Make `CommerceHostCurrentTenant` delegate the subject slice (`ScopeNodes`, `Roles`, `Permissions`, `UserType`, `TokenUse`, `CustomerId`) to the same claim reads as `HttpContextCurrentTenant`. Better: compose it around `HttpContextCurrentTenant`.
  2. Give non-staff principals a defined scope semantics in the `within_scope_cols` predicate. For example, return `true` when `app.current_token_use` is in `customer`, `customer_mcp` or `api_key` (the brand and customer policies still confine them), or have the interceptor publish `""` plus a token-use-aware arm. Do not emit a fake `scope_nodes` claim.
- Regression tests required: add customer, API-key and commerce-adapter sessions to `SubBrandScopeRlsTests`, and an audit insert under each session type. Add a test asserting that `CommerceHostCurrentTenant` and `HttpContextCurrentTenant` publish identical GUCs for the same principal.
- Dependencies / priority: P0. Blocks any production use of 0031.
- Related area: DB, AUTHZ.

### SA-TEN-002 — Commerce host never sets `app.current_customer_id`, so customer-level RLS on payments, wallets, refunds and loyalty degrades to brand equality
- Category: Intra-tenant isolation (defense in depth)
- Severity: Medium
- Status: Verified (code read + SQL repro S2 before 0031)
- Evidence:
  - The A0.6 fix exists only in `HttpContextCurrentTenant.cs:L30-46`.
  - `CommerceHostCurrentTenant.cs:L69` publishes `UserId = sub` for every token, including customer tokens (whose `sub` is the customer id), and it has no `CustomerId` member, so the default `null` applies (`ICurrentTenant.cs:L31`).
  - Policy shape: `rls_proposal.sql:L272-279` (`current_customer_id() IS NULL OR customer_id = current_customer_id()`).
- Observed behaviour: in the S2 repro (before 0031) customer `c1` saw the payments of `c1` **and `c2`** at the RLS level.
- Reproduction: `repro_scen.sql` S2, before `f0031.sql`.
- Impact: in the very host that serves payments and wallets, customer isolation rests only on per-handler `CustomerId` predicates. This is the exact fail-open condition A0.6 documented, and it was re-introduced through the second adapter.
- Recommended remediation: same change as SA-TEN-001 step 1 (one shared claim-to-GUC mapping).
- Regression tests: RLS test with the commerce adapter's GUCs; customer A must not read customer B's payment.
- Dependencies / priority: P1. Fix together with SA-TEN-001.
- Prior-doc cross-ref: A0.6 (`HttpContextCurrentTenant.cs` comment; `docs/ABAC_IMPLEMENTATION_PLAN.md`).

### SA-TEN-003 — A suspended brand (including a ToS or manual suspension) can lift its own suspension through cancel → withdraw
- Category: Tenant lifecycle / server-side restriction bypass
- Severity: High
- Status: Verified. The DB function behaviour was reproduced (`repro_susp.sql`) and the HTTP path was traced statically.
- Evidence:
  - `BrandSuspensionMiddleware.cs:L54` allow-lists `/api/v1/admin/cancellation` while a brand is suspended.
  - `AdminCancellation.cs:L35-36`: `POST ""` and `POST "withdraw"` require only `settings.manage`, which `brand_admin` holds per `settings_permissions.sql:L8`.
  - `RequestBrandCancellation.cs:L60-67` rejects only `archived`, then at `L94` calls `SetCancellationStateAsync(...,"cancelled")`.
  - `WithdrawBrandCancellation.cs:L56` calls `SetCancellationStateAsync(...,"active")`.
  - `0015_brand_cancellation.up.sql:L277-289` rejects only `archived` and then sets the new status unconditionally. Its own comment (L282-283) says a suspended brand is "not this function's business", but no code enforces that.
  - Dunning only re-suspends `nonpayment` brands that have an outstanding past-due invoice (`BrandPlatformBillingService.cs:L219-227`), so a ToS or manual suspension is never re-applied.
- Observed behaviour: repro output was `request-cancel was=suspended`, then `withdraw was=cancelled`, leaving the final row `status=active, suspension_reason=tos`.
- Impact: suspension, the platform's main server-side tenant restriction, can be undone by the tenant itself in two requests. That defeats both ToS enforcement and non-payment enforcement (the latter until the next dunning tick).
- Recommended remediation:
  1. In `set_brand_cancellation_state`, refuse `p_status='cancelled'` when the current status is `suspended`. Alternatively, record the prior status in `brand_cancellations` and have withdrawal restore it rather than forcing `active`.
  2. Have `RequestBrandCancellation` reject suspended brands, or preserve the suspension through the wind-down.
- Regression tests: a migration test (suspended → cancelled must raise, or withdraw must restore `suspended`) and a handler test.
- Dependencies / priority: P0.
- Related area: SUBSCRIPTION / ONBOARDING.

### SA-TEN-004 — Global staff identities plus unscoped membership writes allow cross-tenant tampering and account takeover
- Category: Cross-tenant data integrity / account security
- Severity: High
- Status: Partially Verified (static trace; not executed)
- Evidence:
  - `identity_access.users` is global, with unique `phone_e164` and `email` (`02_bc2_identity_access.sql:L23-24`).
  - `user_scope_memberships` has RLS **off** by design (`0029_users_brand_rls.up.sql:L147-153`).
  - User visibility is granted by any membership in the caller's brand (`0029:L113-143`, `user_in_brand`).
  - `GrantMembership.cs:L44-215` validates the target *scope node* (L136-162) but **never checks that `cmd.Request.UserId` belongs to or is visible in the actor's brand** (L210 inserts it as given). With `IsPrimary=true` it clears the target user's primary flag on **all** their memberships, other brands included (L188-193; the table has no RLS).
  - `UpdateUser.cs` (L20-28) lets a holder of `users.update` change email and phone. `DeactivateUser.cs` (L16-22) sets the global `users.status`. Both rely only on users RLS. `brand_admin` holds `users.update` and `users.deactivate` (`IdentitySeeder.cs:L515-537`).
- Observed behaviour (by trace):
  1. Brand A's admin grants a brand-A membership to any user UUID they know.
  2. That user becomes visible under users RLS.
  3. Brand A's admin changes the user's phone or email, or deactivates them.
  4. The new contact detail is used to log in through OTP, which takes over the identity, including its brand-B memberships.

  For a legitimately shared user (a person in two brands), only steps 3-4 are needed.
- Impact: tenant A can modify or disable, and potentially take over, an identity that operates tenant B. The precondition is knowing a user UUID (v4, not enumerable through any path I found) or the user being legitimately shared.
- Recommended remediation:
  - `GrantMembership` must require that the target user is already visible in the actor's brand, or is being created in the same call (invite flow).
  - Scope the `IsPrimary` reset to memberships inside the actor's brand.
  - Forbid `UpdateUser` contact changes and `DeactivateUser` for users who hold memberships in other brands (platform-admin only).
  - Longer term, enable an RLS policy on `user_scope_memberships` keyed on the scope's brand.
- Regression tests: brand A cannot grant to a brand-B-only user; brand A cannot change contact details of a multi-brand user; an `IsPrimary` grant does not touch other brands' memberships.
- Dependencies / priority: P1.
- Related area: AUTHZ.

### SA-TEN-005 — Gateway per-tenant rate limiting is keyed on a client-controlled brand id
- Category: Tenant fairness / noisy neighbour / rate-limit bypass
- Severity: Medium
- Status: Verified (code read)
- Evidence:
  - `laundryghar.Gateway/RateLimitPartitioning.cs:L24-31, L34-70`: the partition is `X-Brand-Id`, or failing that the **unvalidated** JWT `brand_id`. The brand bucket is 10× the IP limit (`Program.cs:L213-250`).
- Observed behaviour:
  - Any client can rotate random UUIDs in `X-Brand-Id` to get a fresh 10× bucket on each request, which bypasses the per-IP limit.
  - Any client can send `X-Brand-Id: <victim brand>` to exhaust that tenant's bucket and throttle all of its real users.

  The code comment claims "the worst a forged brand_id achieves is being counted against someone else's budget". That is itself the cross-tenant denial-of-service.
- Impact: per-tenant rate limiting provides neither isolation nor abuse protection.
- Recommended remediation:
  - Partition on the IP **and** the brand: a brand bucket only for requests carrying a token whose signature the gateway validates against JWKS, or keep per-IP as the outer limiter.
  - Ignore `X-Brand-Id` for anonymous traffic.
- Regression tests: a unit test showing that random `X-Brand-Id` values do not create new buckets for the same IP.
- Dependencies / priority: P1.
- Related area: DEVOPS / SEC.

### SA-TEN-006 — Anonymous public tenant content (banners, app-config, onboarding slides) returns nothing under enforced RLS
- Category: Tenant branding delivery / RLS design
- Severity: Medium
- Status: Partially Verified. The SQL analogue was reproduced (S5) and the code read; I did not hit the HTTP endpoint.
- Evidence:
  - `PublicEngagement.cs:L29-45` explicitly assumes "RLS cannot be relied upon here" and adds `.Where(BrandId)` (e.g. `GetPublicBanners…:L20-27`).
  - Core sets `bypass_rls` only for the auth, signup and OAuth paths (`core.WebApi/Program.cs:L543-561, L620-646`). `/api/v1/public` is not among them.
  - `engagement_cms.app_banners`, `mobile_app_config` and `onboarding_slides` have RLS enabled with `rls_brand` (`rls_enable_engagement_cms.sql:L27-36`; `rls_proposal.sql:L218-219`).
- Observed behaviour: S5 (an anonymous session with an explicit `brand_id=A` predicate) returned **0** rows. An explicit predicate cannot widen RLS.
- Impact: a pre-login branded mobile experience (Q8) is empty whenever RLS is enforced. If it renders in some environment, that environment is not running as an RLS subject.
- Recommended remediation: add a narrowly scoped SECURITY DEFINER read function per public resource (the house pattern used by `brand_app_identity`), or add a `FOR SELECT` policy that permits `status='active' AND is_active` rows to anonymous sessions only when they carry a resolved brand GUC. In that case, publish the anonymous resolved brand into a dedicated GUC.
- Regression tests: an integration test calling the public endpoints as `app_user` with RLS on.
- Dependencies / priority: P1.
- Related area: VERTICALS / FRONTEND.

### SA-TEN-007 — The database layer trusts the application completely: self-settable bypass GUC, blanket platform-admin bypass, and SECURITY DEFINER functions taking caller-supplied brand ids
- Category: Isolation architecture
- Severity: Medium
- Status: Verified (SQL repro S6 + code and migration read)
- Evidence:
  - `kernel.rls_bypass()` reads `app.bypass_rls`, a plain GUC that `app_user` can set (`harden_app_user_and_rls_bypass.sql:L31-38`). S6: after `SET ROLE app_user; set_config('app.bypass_rls','true',false)` the session saw **all 3** rows across both brands.
  - Every platform-admin request runs with full bypass (`TenantResolutionMiddleware.cs:L38-47`), so RLS `WITH CHECK` never applies to those writes.
  - Since 0015/0019, more than ten SECURITY DEFINER functions are granted to `app_user` and trust their brand argument. `kernel.export_brand` (`0015:L143-165`) dumps an entire tenant for any `p_brand_id`; `set_brand_cancellation_state` (`0015:L265-294`) changes any brand's status.
  - The comments record the "brands-RLS trap" six times (`0021:L49`), caused by `tenancy_org.brands` being `rls_admin_only` instead of `id = current_brand_id()`.
  - `BrandExportService.cs:L24-26` opens the EF connection directly. That bypasses the interceptor, which is harmless today only because the function is SECURITY DEFINER.
- Impact: RLS is a guard against *forgotten predicates*, not against SQL injection or a single mis-wired handler. One raw-SQL injection or one wrong argument yields cross-tenant reads or writes. The steady growth of SECURITY DEFINER escape hatches widens that surface.
- Recommended remediation (incremental, no rewrite):
  1. Add a read policy on `brands` (`id = current_brand_id()`) so tenants can read their own row, then retire the read-only DEFINER helpers.
  2. Inside each remaining DEFINER function, assert `p_brand_id = kernel.current_brand_id() OR kernel.rls_bypass()`.
  3. Longer term, have workers and platform admins use a separate login role (the `app_admin` role already exists in `rls_proposal.sql:L54-57`) instead of a GUC the tenant role can set.
  4. Add a startup check that the runtime role is not superuser and not `BYPASSRLS`.
- Regression tests: calling each DEFINER function with a foreign brand id while `current_brand_id` is set must fail.
- Dependencies / priority: P2.
- Related area: DB / SEC.

### SA-TEN-008 — Suspension, cancellation and deletion are HTTP-only and partial gates
- Category: Tenant lifecycle
- Severity: Medium
- Status: Partially Verified (code read; workers sampled)
- Evidence:
  - `BrandSuspensionMiddleware.cs:L128-134` skips anonymous traffic and platform admins, and `L78-83` / `BrandStatusStore.cs:L47-53` fail open.
  - The worker services (`SubscriptionBillingService`, `AutoDispatchService`, `RoyaltyGenerationService`, `LoyaltyEarnService`, `NotificationMappingService`) contain no brand-status check: grep for `brand_status`/`BrandStatus` in `commerce.Infrastructure/Worker/Services` finds none.
  - `DeleteBrand.cs:L14-24` only stamps `deleted_at`. `kernel.brand_status` ignores `deleted_at` (`0009:L23-34`), and `ScopeResolver` does not check brand state (`core.Application/Identity/Auth/Common/ScopeResolver.cs:L51-100`).
- Observed behaviour (by trace):
  - A suspended or cancelled brand's customers keep being billed and its orders keep being auto-dispatched.
  - A "deleted" brand's users keep logging in and operating.
  - `AdminCancellation.cs:L21` and `RetentionSweepService.cs:L198-200` both state "there is no delete endpoint", but `DELETE /api/v1/admin/brands/{id}` exists (`AdminBrands.cs:L30, L62-66`).
- Impact: lifecycle states do not mean what the product says they mean. There is billing and legal exposure (charging end customers of a frozen tenant).
- Recommended remediation:
  - Add a shared `IBrandStatusStore` check (or SQL `JOIN brands … status='active'`) to every worker query that acts on behalf of a brand.
  - Make `DeleteBrand` set `status='archived'` (or remove the endpoint in favour of cancellation), and make `brand_status` return `archived` when `deleted_at` is set.
- Regression tests: worker tests with a suspended brand fixture; a login test for a deleted brand.
- Dependencies / priority: P1.
- Related area: SUBSCRIPTION.

### SA-TEN-009 — Custom-domain (Host) tenant resolution is unreachable in the shipped topology
- Category: White-label tenant resolution
- Severity: Medium
- Status: Suspected. Based on YARP's documented default behaviour; not executed.
- Evidence:
  - `BrandResolver.cs:L67-71` reads `context.Request.Host.Host`.
  - The gateway forwards to `http://core:8080` (`deploy/docker-compose.yml:L84-95`) without a `RequestHeaderOriginalHost` transform (grep finds none). YARP's default sends the destination host.
  - Services do not process `X-Forwarded-Host` (`laundryghar.ServiceDefaults/Extensions.cs:L275` handles only `XForwardedFor | XForwardedProto`, and compose keeps forwarded headers off for services at `L25`).
  - Clients call `PUBLIC_API_URL`, not the tenant's domain (`docker-compose.yml:L101-104`), and the customer app is single-brand at build time (`customer-mobile/src/api/engagement.ts:L31-35`).
  - Customer auth never consults Host (`CustomerBrandResolver.cs:L20-52`).
- Impact: white-label tier T2 (hostname is the tenant) does not take effect. Anonymous calls fall back to the unchecked `X-Brand-Id` or `brandCode`, or to `LG-MAIN`.
- Recommended remediation:
  - Add a YARP `RequestHeaderOriginalHost: true` transform, or forward `X-Forwarded-Host` and enable it in the services with known proxies.
  - Have `CustomerBrandResolver` prefer a verified Host.
- Regression tests: a gateway integration test asserting that the upstream sees the original host.
- Dependencies / priority: P2.
- Related area: VERTICALS / DEVOPS.

### SA-TEN-010 — Isolation test suite does not exercise the real runtime path
- Category: Test coverage
- Severity: Medium
- Status: Verified (read)
- Evidence:
  - `RbacRlsFixture.cs:L43-47` (pooling off), `L86-120` (6 of 12 GUCs set, under a "byte-for-byte mirror" claim) and `L140-280` (trimmed hand-written DDL).
  - `SubBrandScopeRlsTests.cs` has no customer, API-key or commerce-adapter case.
  - CI (`ci.yml:L31-36`) runs the tests, but no job builds the DB from `database_scripts` + `db/patches` + `db/migrations` and asserts RLS coverage.
- Impact: SA-TEN-001 and SA-TEN-002 shipped despite substantial RLS test volume.
- Recommended remediation:
  - Generate the fixture's GUC setter from `RlsConnectionInterceptor` itself, for example by instantiating the interceptor with a fake `ICurrentTenant`.
  - Add one smoke test per `ICurrentTenant` adapter × principal type.
  - Add a CI job that applies the real migration chain and re-runs the 0027 guard.
- Regression tests: the tests just described.
- Dependencies / priority: P1.

### SA-TEN-011 — No composite tenant foreign keys; cross-tenant references are prevented only in handlers
- Category: Data integrity
- Severity: Low
- Status: Partially Verified (DDL read; one handler sampled)
- Evidence:
  - FKs are single-column, e.g. `fk_patch_04_order_lifecycle.sql:L425-426` (`orders.customer_id`). Grep finds no `(brand_id, id)` unique or FK pairs. PostgreSQL FK checks are not subject to RLS.
  - Positive control: `CreateOrderCommand.cs:L98-115` verifies that both the store and the customer belong to the brand.
- Impact: a handler that forgets this check can link a brand-A row to a brand-B parent. RLS `WITH CHECK` only checks the row's own `brand_id`.
- Recommended remediation: add `UNIQUE(brand_id, id)` to the core parents (customers, stores, orders) and composite FKs on the hottest children, done incrementally.
- Regression tests: an insert of a cross-brand reference must fail at the DB.
- Dependencies / priority: P3.
- Related area: DB.

### SA-TEN-012 — Globally unique columns on tenant data leak existence and allow cross-tenant blocking
- Category: Data model
- Severity: Low
- Status: Partially Verified
- Evidence:
  - `04_bc4_order_lifecycle.sql:L409, L468` (`tag_code UNIQUE`).
  - `06_bc6_commerce.sql:L391, L497`, `00_kernel.sql:L197`, `payment_idempotency.sql` (global `idempotency_key`).
  - `0002_brand_domains.up.sql:L52-54` (global `UNIQUE(domain)` that also covers **unverified** rows).
  - `AdminWalletHandlers.cs:L95` looks up a client-supplied key by brand while uniqueness is global.
- Impact: brand A can pre-claim brand B's custom domain (unverified squatting), or collide on tag or idempotency values. The resulting unique-violation errors reveal that another tenant holds the value.
- Recommended remediation: make these keys `(brand_id, …)`; limit domain uniqueness to verified rows plus a pending-claim expiry.
- Regression tests: unique-index tests.
- Dependencies / priority: P3.

### SA-TEN-013 — Analytics: app-only isolation over materialized views, global refresh by any tenant
- Category: Reporting isolation / noisy neighbour
- Severity: Low
- Status: Verified (code read)
- Evidence:
  - `harden_app_user_and_rls_bypass.sql:L71-79` grants `SELECT` on all `analytics` MVs to `app_user`. MVs cannot carry RLS.
  - The handlers filter `BrandId == RequireBrandId()` (`GetDailyStoreRevenue.cs:L29-40` and similar), but `q.StoreId` is not scope-checked.
  - `RefreshMatviews.cs:L28-45` lets any holder of `analytics.refresh` (`AnalyticsEndpoints`: `MapPost(Refresh)`) refresh all tenants' MVs.
- Impact: there is a single line of defence for analytics. Store-scoped users can read other stores' revenue in their brand. Any tenant can trigger platform-wide refresh load.
- Recommended remediation: wrap the MVs in brand-filtered security-barrier views or SECURITY DEFINER functions keyed on `current_brand_id()`; add `IsWithinScope` on `StoreId`; restrict refresh to platform admins or the worker.
- Dependencies / priority: P3.
- Related area: DB.

### SA-TEN-014 — Observability and caches are not tenant-aware
- Category: Operations
- Severity: Informational
- Status: Verified (grep / code read)
- Evidence:
  - No `brand_id` log scope or trace tag in `laundryghar.ServiceDefaults` or `laundryghar.Utilities`: grep for `LogContext.PushProperty`, `SetTag("brand` and `Enrich` finds none.
  - The output cache key varies on brand, franchise and store claims but not on `scope_nodes` or the user (`OutputCaching.cs:L83-94`), and the store is in-process (`L27-29`).
- Impact: per-tenant incident triage, per-tenant SLOs and noisy-neighbour detection are not possible from telemetry. Users of one brand with different membership sets may share cached admin list responses.
- Recommended remediation: add a middleware that pushes `brand_id`, `franchise_id` and `token_use` into the logging scope and `Activity` tags; add `scope_nodes` to `TenantKey`.
- Dependencies / priority: P3.

### SA-TEN-015 — The runtime DB role password is hard-coded and re-applied by patches
- Category: Security / operations
- Severity: Medium
- Status: Verified (read)
- Evidence: `db/patches/app_user_role.sql:L34` and `db/patches/harden_app_user_and_rls_bypass.sql:L47` run `ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user'` unconditionally.
- Impact: re-running either "idempotent" patch against production silently resets the RLS-subject role to a publicly known password.
- Recommended remediation: remove the password from the patches and set it out of band (secrets manager), or use `\password` / `psql -v`.
- Dependencies / priority: P1.
- Related area: DB / DEVOPS.

## Positive controls verified
- **Pool-safe tenant context.** `RlsConnectionInterceptor` is Scoped, is resolved per DI scope, and writes all 12 GUCs on every EF connection open, including nulls (`RlsConnectionInterceptor.cs:L57-121`; `DependencyInjection.cs:L48-64, L86-96`). No cross-request bleed through pooled connections on EF paths. (Static verification only.)
- **No header spoofing for tenants.** `X-Brand-Id` is honoured on authenticated paths only for platform admins (`TenantResolutionMiddleware.cs:L38-47`), and the JWT claim is otherwise authoritative.
- **Commerce worker bypass is fail-closed.** It requires a positive AsyncLocal marker; context-less non-worker flows get no brand and no bypass (`CommerceHostCurrentTenant.cs:L80-90`; `WorkerScope.cs`).
- **Partner tokens are isolated by design.** They carry no `brand_id` and no permissions; `rls_partner` isolates them (`JwtTokenService.cs:L149-171`).
- **Cross-brand IDOR guard on order creation** for both store and customer (`CreateOrderCommand.cs:L98-115`).
- **Tenant export is bounded to the caller.** The export takes its brand from the caller's own tenant, never from the route (`AdminCancellation.cs:L64-97`). The purge function is not granted to `app_user` (`0015:L248-250`).
- **Coverage guard.** Migration 0027 aborts if any `brand_id` table lacks RLS (`0027…up.sql:L74-95`).
- **Users RLS** (0029) closed the earlier platform-wide user listing, and `UserTenantIsolationTests` and `UsersBrandRlsTests` cover it.
- **Self-signup is safe.** Brand codes are generated, never accepted, and the phone is OTP-proven before any write (`CompleteSignup.cs:L78-111, L289-296`).
- **Brand-namespaced storage.** Keys are `FileStorageKeyGenerator.Generate(brandId, …)` and objects are served only through authorized streaming endpoints (`LocalFileStorageProvider.cs:L43-47, L85`; `ItemImageCommands.cs:L39-45`).
- **Output cache keys include the tenant** (`OutputCaching.cs:L44-55, L83-94`).
- **Payment gateway secrets are cached per brand**, and webhooks verify the HMAC with the matched payment's brand secret (`GatewaySettingsCache.cs:L35-63`; `RazorpayWebhookHandler.cs:L94-115`).
- **Per-brand customer namespace** (`UNIQUE(brand_id, phone_e164)`, `UNIQUE(brand_id, customer_code)`) and per-brand codes for stores, franchises and catalogue (`01_bc1…:L108-335`; `03_bc3…:L68-551`).
- **Tenant-scoped config.** `kernel.system_settings` resolves store → franchise → brand → platform, and RLS lets tenants read platform rows but never write them (`0027…up.sql:L25-46`).

## Open questions / not verified
- Which DB state is deployed: is 0031 applied, are `rls_enable_*.sql` applied, and is the production `DB_CONNECTION_STRING` really `app_user`? This decides whether SA-TEN-001 and SA-TEN-006 are live outages or whether RLS is not enforced at all.
- YARP Host forwarding (SA-TEN-009): I did not execute it.
- Whether user UUIDs leak to other tenants through any API or export (this affects how exploitable SA-TEN-004 is).
- Per-plan resource limits and quotas (users, stores, orders): out of my scope; for the subscription agent.
- I did not trace the impersonation flow or the ABAC policy store's brand scoping (`NpgsqlAbacStore` intentionally reads every brand's policies).
- I sampled 2 of the 14 workers and the order-creation handler for app-layer predicates. I did not audit all 159 `RequireBrandId` handlers.
- Npgsql reset-on-close for production connection strings: dev strings are default; production is not in the repo.

## Verdict inputs
My interpretation of each question is in parentheses.

- **Q1** (is multi-tenancy genuinely implemented with real tenant isolation?): **Partially Supported.** There is a real shared-schema design with brand claims, app predicates, RLS on all `brand_id` tables and a session-GUC interceptor. But SA-TEN-001/002 show the RLS layer is mis-wired for customer, API-key and commerce sessions; SA-TEN-004 allows cross-tenant identity tampering; and nothing verifies end to end.
- **Q2** (tenant onboarding and lifecycle server-side): **Partially Supported.** Self-signup, cancellation, export and purge are implemented. Suspension is HTTP-only and self-reversible (SA-TEN-003), delete is a soft flag that changes nothing (SA-TEN-008), and workers ignore lifecycle state.
- **Q3** (tenant context resolution and propagation, unspoofable): **Partially Supported.** The JWT claim is authoritative and the platform-admin-only header override holds. But the commerce adapter diverges (SA-TEN-001/002), the gateway trusts unvalidated brand ids for rate limiting (SA-TEN-005), and Host-based resolution is not reachable (SA-TEN-009).
- **Q8** (per-tenant branding, from the isolation angle): **Partially Supported.** Branding data is brand-keyed (`brands.*`, `mobile_app_config`, `brand_domains`), but anonymous delivery returns nothing under enforced RLS (SA-TEN-006), custom-domain resolution does not take effect (SA-TEN-009), and tenants cannot read their own `brands` row without SECURITY DEFINER helpers (SA-TEN-007).
- **Q13** (tenant restrictions enforced server-side): **Partially Supported.** The suspension gate and entitlement-at-mint (`Entitlement:Enforced=true`, `core.WebApi/appsettings.json:L12-13`) are server-side, but they can be bypassed (SA-TEN-003) and are not applied to workers or anonymous paths (SA-TEN-008).
- **DB-Q8** (tenant context safe with pooled connections): **Fully Supported for EF-opened connections**, by static verification only (no runtime test with pooling on). One raw-open path, `BrandExportService`, skips the interceptor; it is safe only because its function is SECURITY DEFINER.
- **DB-Q10** (isolation at both app and DB layers): **Partially Supported.** Both layers exist, but the DB layer trusts a self-settable bypass GUC and brand-argument DEFINER functions (SA-TEN-007), excludes memberships and analytics, has no composite FKs, and, with 0031, fails closed for whole principal classes.

**Overall conclusion:** multi-tenancy is **partially implemented**. It is substantially more than "merely represented in the data model": shared schema, brand claim, explicit predicates, RLS with pool-safe session context, a partner tenant type, and lifecycle machinery all exist. But it is **not a verified guarantee**:

- **Verified:** claim-over-header precedence; interceptor pool safety (static); RLS policy semantics (by SQL reproduction).
- **Assumed and unproven:** that production runs as `app_user` with the full RLS chain applied; that the 159 handler predicates are complete.
- **Shown broken:** commerce and customer session context under the RLS policies; suspension enforcement.
