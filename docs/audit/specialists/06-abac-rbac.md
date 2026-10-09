# 06 — ABAC and RBAC Security Specialist (`authz`)

## Scope and method

**Inspected (read, not grepped-and-guessed):**
- Request pipeline of every host: `laundryghar.Gateway/Program.cs`, `laundryghar.Gateway/RateLimitPartitioning.cs`, `core.WebApi/Program.cs` (auth L298–420, pipeline L530–646), `operations.WebApi/Program.cs` (L85–184), `commerce.WebApi/Program.cs` (L40–75, L340–395).
- Shared authz plumbing in `laundryghar.Utilities`: `Auth/PermissionHandler.cs`, `Auth/AnyPermissionRequirement.cs`, `Auth/PermissionPolicyProvider.cs`, `Auth/ApiAuthorizationResultHandler.cs`, `Auth/TokenClaims.cs`, `Auth/RiderOnlyRequirement.cs`, `Auth/CustomerOnlyRequirement.cs`, `Services/HttpContextCurrentUser.cs`, `Services/HttpContextCurrentTenant.cs`, `Middlewares/TenantResolutionMiddleware.cs`, `Middlewares/BrandSuspensionMiddleware.cs`, `Middlewares/ImpersonationGuardMiddleware.cs`, `Caching/OutputCaching.cs`, `Authorization/Abac/*` (options, service, handler).
- Token minting: `core.Application/Identity/Auth/Common/ScopeResolver.cs` (whole file), `core.Infrastructure/Auth/JwtTokenService.cs`, `RefreshTokenHandler.cs`, `PasswordLoginHandler.cs`, `CompleteSignup.cs`.
- RLS/tenant adapters: `SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs`, `SharedDataModel/Contracts/ICurrentTenant.cs`, `commerce.Infrastructure/Worker/{CommerceHostCurrentTenant,WorkerScope,WorkerCurrentTenant}.cs`, `TokenVersionStore.cs`, `BrandStatusStore.cs`.
- Access-control write handlers: `CreateUser`, `UpdateUser`, `DeactivateUser`, `SetUserType`, `SetPersonStatus`, `GrantMembership`, `InviteUser`, `SetRoleCells`, `SetUserPermissionOverride`, `SetBrandFeature`, `UpdateDispatchSettings`, `PermVersionBumper`; seeded grants in `core.Infrastructure/Seeders/IdentitySeeder.cs` L120–640.
- Customer, rider and partner lanes: `CustomerOrderEndpoints.cs` + `OrderQueries.cs` L153–232, commerce customer endpoints + handlers (payments, packages, subscriptions), `RiderSelfEndpoints.cs` + `RiderSelf/*` handlers, `PartnerBookingEndpoints.cs`.
- SQL: `db/migrations/0005` (brand_feature RLS), `0025` (`kernel.split_setting`, `current_scope_nodes`), `0027` (system_settings), `0029` (users RLS), `0031` (sub-brand RESTRICTIVE policy, whole file), `db/patches/rls_proposal.sql` L230–285 (customer policies).
- Prior docs, checked against code: `docs/ABAC_AUDIT_2026-08-31.md`, `docs/AUDIT_REPORT.md`, `docs/FIX_REPORT.md`, `docs/FIX_TASKS.md` (0031 section).

**Commands run:** read-only `cat/sed/grep/find`. Two throw-away Python scripts in my scratch directory to list every `Map{Verb}` call and the authorization metadata attached to it, for all three WebApi assemblies. I checked the counts by hand against the "NONE" rows. All of those turned out to be lambda-style endpoints that my first parser missed, and each one does carry explicit `RequireAuthorization`/`AllowAnonymous`.

**Not verified, and why:** I ran nothing. The container has no .NET SDK, no running PostgreSQL and no Docker. Every runtime claim below comes from reading the code. Findings that depend on live data, such as whether a given permission row has a `module_key` or whether a platform admin holds a brand membership, are marked **Partially Verified**. I did not deep-audit the payment-gateway webhooks, OAuth/MCP flows, OTP security or DB-role grants. Those belong to other specialists.

## Current-state summary

**How a request is authorized today (the traced path):**
`Client → YARP Gateway` (does **not** validate JWT. It only partitions rate limits from an unverified `X-Brand-Id`/JWT `brand_id`, `RateLimitPartitioning.cs:33-70`) `→ host`
`→ UseAuthentication` (RS256 pinned. Issuer, audience and lifetime are validated. Core uses an in-process key (`core.WebApi/Program.cs:301-317`), ops/commerce use JWKS (`operations.WebApi/Program.cs:88-110`))
`→ TenantResolutionMiddleware` (`user_type == platform_admin` ⇒ `bypass_rls = true` + honours `X-Brand-Id`. `perm_ver` check against `kernel.user_perm_version` with a 15 s per-process cache. `TenantResolutionMiddleware.cs:28-75`, `TokenVersionStore.cs:16-45`)
`→ ImpersonationGuardMiddleware` (DB re-validation, fails closed)
`→ BrandSuspensionMiddleware` (402 when the brand is suspended or cancelled, allow-list of paths, fails open, 30 s cache)
`→ UseAuthorization` (`PermissionPolicyProvider`: `permission:<code>` ⇒ `PermissionHandler` reads the space-separated `permissions` **claim**. `platform_admin` bypasses it. A step-up gate applies for high/critical codes. `AbacRequirement` is attached but inert)
`→ endpoint → handler` (hand-written ownership or `IsWithinScope` checks on *some* write paths)
`→ EF Core → RlsConnectionInterceptor` (sets 12 GUCs from **JWT claims**) `→ PostgreSQL RLS` (brand, customer, partner and user policies, plus the 0031 RESTRICTIVE franchise/store/warehouse policy).

**RBAC is real and data-driven at the role/permission level.** Roles, permissions, `role_permissions`, `user_scope_memberships` and `user_permission_overrides` are database rows. `ScopeResolver.BuildTokenClaimsAsync` (`ScopeResolver.cs:19-277`) resolves the ancestor-or-self memberships of the active scope node, then applies allow/deny with deny winning. If `Entitlement:Enforced=true` (it is set in `core.WebApi/appsettings.json:12-14`), it strips permissions whose owning module is not licensed. The result is baked into a 15-minute access token (`appsettings.json:21`). Permissions are therefore **resolved at login/refresh and cached in the JWT**. Live revocation works through a `perm_version` bump plus a check on each request. That check has a 15 s cache and fails open on lookup errors.

**Endpoint coverage is complete.** Every one of the 514 mapped endpoints carries explicit authorization metadata or an explicit `AllowAnonymous` (table below). No host sets a fallback policy, so that completeness depends on discipline rather than on a default-deny.

**The weak point is the write side of identity administration.** The read paths over users were fixed by F-1/F-2 (`UserBrandScope.ScopedToCallerBrand`, used by `GetUsers`/`GetUserById`). The *write* handlers were not. `CreateUser`, `UpdateUser`, `SetPersonStatus`, `DeactivateUser`, `GrantMembership`, `SetUserPermissionOverride` and `SetRoleCells` have none of the following:
- a target-rank guard
- a target-in-scope guard
- a permission ceiling
- a user-type ceiling

As a result, anyone holding `users.create` can mint a `platform_admin` account. Self-service signup gives `brand_admin`, and therefore `users.create`, to any anonymous person who can receive an OTP on their own phone. That makes this an **unauthenticated-to-platform-admin escalation path** (SA-AUTHZ-001).

**ABAC is built but inert, confirmed.** `AbacOptions.Enabled` defaults to `false` (`AbacOptions.cs:15`). No `Abac` key exists in any `appsettings*.json` or deploy file. `AbacAuthorizationService.EvaluateAsync` returns `Skipped` on its first line when disabled. `AbacAuthorizationHandler` succeeds when an endpoint declares no ABAC target, and **zero** endpoints declare one (`AbacResource`/`RequireAbac`/`IAbacAuthorizationService` have no matches outside the Utilities library). The attribute-like decisions that do exist are hand-coded in handlers and RLS:
- tenant via the JWT `brand_id` → GUC
- customer ownership via `sub`
- rider ownership via `riders.user_id`
- franchise/store via `scope_nodes` and 0031

All of these attributes come from **server-signed claims** (trusted). None come from client-controlled bodies or headers, with one exception: platform admins may set `X-Brand-Id`.

**A recent change appears to have broken two lanes.** Migration 0031 added a RESTRICTIVE policy that denies whenever `app.current_scope_nodes` is unresolved. Customer tokens carry no `scope_nodes`. The commerce host's tenant adapter never publishes `scope_nodes` (or `customer_id`) at all. Following the code, customers lose access to orders, stores, price lists, pickups and payments, and every non-platform principal on the commerce host loses access to payments and all finance tables (SA-AUTHZ-005, SA-AUTHZ-006). The 0031 live verification in `docs/FIX_TASKS.md` §A-6 covered only platform admin, brand admin, store admin, warehouse staff and rider on the operations/core hosts.

## Table 1 — Authorization architecture and enforcement locations

| # | Layer | Where (evidence) | What it decides | Attribute source | Status |
|---|---|---|---|---|---|
| 1 | Gateway (YARP) | `laundryghar.Gateway/Program.cs:274-315` | Routing, CORS, rate limit only. **No JWT validation**, pass-through of `Authorization`/`X-Brand-Id` | Unverified JWT payload / `X-Brand-Id` (rate-limit bucket only) | Verified |
| 2 | Authentication | core `Program.cs:301-368` (Bearer, `mcp`, ApiKey schemes); ops `Program.cs:88-110`; commerce `Program.cs:137-160` | RS256 signature, issuer, audience, lifetime (30 s skew) | Signed token | Verified |
| 3 | Tenant resolution | `TenantResolutionMiddleware.cs:28-75` | `platform_admin` ⇒ RLS bypass + `X-Brand-Id` override; stale `perm_ver` ⇒ 401 | `user_type` claim; DB perm_version (15 s cache, fail-open) | Verified |
| 4 | Impersonation guard | `ImpersonationGuardMiddleware.cs` (whole) | Grant still approved, subject/brand match, read-only ⇒ GET only | DB per request; fails closed | Verified |
| 5 | Suspension gate | `BrandSuspensionMiddleware.cs:43-152` | Suspended/cancelled brand ⇒ 402 except allow-listed paths | `brand_id` claim or override → `kernel.brand_status` (30 s cache, fail-open) | Verified |
| 6 | Policy evaluation (RBAC) | `PermissionPolicyProvider.cs:102-149`; `PermissionHandler.cs:17-56`; `AnyPermissionRequirement.cs:225-269` | Hold code in `permissions` claim (or be `platform_admin`); step-up for high/critical | JWT claims minted by `ScopeResolver` | Verified |
| 7 | Lane policies | `CustomerOnlyRequirement.cs`, `RiderOnlyRequirement.cs:15-28`, `PartnerOnlyRequirement.cs` | `token_use` / `user_type` lanes | JWT claims | Verified |
| 8 | Plan entitlement | `ScopeResolver.cs:185-241` (strip at mint); `ApiAuthorizationResultHandler.cs:90-115` (402 explanation) | Un-licensed module permissions removed from staff tokens | DB at mint; `ent_off` claim | Verified (staff lane only) |
| 9 | ABAC PEP/PDP | `PermissionPolicyProvider.cs:126`; `AbacAuthorizationHandler.cs:69-103`; `AbacAuthorizationService.cs` (EvaluateAsync first line) | Nothing today: `Enabled=false`, no endpoint declares a target | — | Verified inert |
| 10 | Handler checks | `IsWithinScope` 75 call sites (mutations), customer/rider self-filters, `IsPlatformAdmin` 28 sites in 17 files, `UserBrandScope` on 4 read queries | Ownership, sub-brand scope, platform-only ops | JWT claims | Partially Verified (sampled) |
| 11 | DB RLS | `RlsConnectionInterceptor.cs:57-121`; policies in `rls_proposal.sql`, `0027`, `0029`, `0031` | Brand, customer, partner, user-self, sub-brand (RESTRICTIVE) row filters | GUCs from JWT claims via `ICurrentTenant` | Partially Verified (SQL read, not executed) |
| 12 | Output cache | `Caching/OutputCaching.cs:44-94` | Shared responses keyed by brand/franchise/store claim + `X-Brand-Id` | Claims/header | Verified (no per-user endpoints cached) |
| 13 | Background workers | `CommerceHostCurrentTenant.cs:42-94`, `WorkerScope.cs:50-87` | Positive AsyncLocal marker ⇒ RLS bypass; no HttpContext and no marker ⇒ fail closed | In-process marker | Verified |
| 14 | Frontend | `admin-web/src/hooks/usePermissions.ts:14-69`, `pos-web/src/hooks/usePermissions.ts:42` | Menu/route hiding only | Decoded JWT | UI-only (backend is authoritative) |

**Endpoint authorization coverage (my count from the endpoint mapping code):**

| Host | Endpoints | Permission policy (`permission:*`) | Lane policy (Customer/Rider/Partner/PartnerAdmin, apiscope) | Authenticated-only | Anonymous (explicit) | No metadata |
|---|---|---|---|---|---|---|
| core.WebApi | 172 | ~131 | 6 | 3 (`/auth/step-up/verify`, `/auth/logout`, `/admin/navigator`) | 32 (auth/OTP/OAuth/signup/terminology/public CMS/jwks+openid-configuration/paylink webhook) | 0 |
| operations.WebApi | 232 | ~165 | 65 | 2 (`/fulfillment-config`, cached static config) | 0 | 0 |
| commerce.WebApi | 110 | 80 | 28 | 0 | 2 (Razorpay webhooks) | 0 |

These counts are approximate, ±a few, because some files use constants such as `Read`/`Manage` for policy names. That affects which column an endpoint lands in, not whether it has authorization. No host configures `FallbackPolicy`, so a future endpoint that someone forgets to annotate becomes anonymous.

## Table 2 — Mandatory scenario matrix

| # | Scenario | Verdict | Evidence / mechanism |
|---|---|---|---|
| 1 | Tenant A reads/updates/deletes/exports Tenant B records | **Allowed — vulnerable via identity-admin chain**. Otherwise blocked by RLS + claims | Direct data access is blocked by the brand GUC taken from the signed `brand_id` (`HttpContextCurrentTenant.cs:24`, `RlsConnectionInterceptor.cs:59,95`). The only path for a non-platform user to move brand is `X-Brand-Id`, which is honoured for platform admins only (`TenantResolutionMiddleware.cs:38-47`). **However**, a brand A admin can create a platform_admin (SA-AUTHZ-001), or attach a foreign user to brand A and then take that account over (SA-AUTHZ-002/003), which yields full access to brand B. |
| 2 | User changes org/tenant/branch/resource id (IDOR) | **Blocked** for cross-brand ids (RLS) and for customer/rider self-lanes (app predicates). **Partially allowed** within a brand on identity writes | Customer: `GetMyOrderByIdHandler` filters `o.CustomerId == q.CustomerId` (`OrderQueries.cs:187-189`). Rider: `a.RiderId == riderId` (`UpdateMyAssignmentStatus.cs:29-38`). Identity writes take a target id with no scope/rank check (`UpdateUser.cs:20`, `SetPersonStatus.cs:26`, `DeactivateUser.cs:16`). |
| 3 | Rider reads another rider's tasks | **Blocked by app predicate** | Every `RiderSelf` handler resolves the rider from `riders.user_id == sub && brand_id` and filters `RiderId == rider.Id` (`GetMyTasksToday.cs:42`, `UpdateMyTaskStatus.cs:51`, `VerifyTaskOtp.cs:30`, `UploadProofPhoto.cs:42-50`, `OfferActions.cs:45,127`). |
| 4 | Store manager → other store | **Reads: blocked by RLS 0031** on the 39 listed tables (operations/core hosts). **Writes: blocked by `IsWithinScope`** where called (75 sites). **Identity writes: allowed** (store admin can change other stores' staff and the brand admin, SA-AUTHZ-002). **Amplified** when the user holds memberships at two levels (SA-AUTHZ-007) | `0031_subbrand_scope_rls.up.sql:60-203`; `HttpContextCurrentUser.cs:87-124`. Tables not in the 0031 list (e.g. `order_status_history`, `order_addons`, customers) are brand-scoped only. |
| 5 | Role used after permission revoked | **Blocked within ≈15 s** for role/membership/override/user-type changes. **Not blocked for up to 15 min** after user suspension/deactivation. Fails open on lookup error | Bumps: `GrantMembership.cs:225`, `RevokeMembership.cs:89`, `SetRoleCells.cs:106`, `AssignPermission.cs:94`, `SetUserPermissionOverride.cs:99`, `SetUserType.cs:75`. No bump in `SetPersonStatus.cs:48-68` or `DeactivateUser.cs:14-23` (SA-AUTHZ-009). |
| 6 | Suspended tenant uses protected functionality | **Blocked (402)** for staff and customer tokens on all three hosts, within ≤30 s. **Not blocked** for partner tokens (no `brand_id`) or when the status lookup errors (fail-open) | `BrandSuspensionMiddleware.cs:63-152`, registered in core `Program.cs:571`, ops `:164`, commerce `:380`. |
| 7 | Subscription lacking entitlement calls protected API | **Staff lane: blocked** (permission stripped at mint; ≤15 min lag for non-brand-scoped members). **Customer/partner lanes: not gated** | `ScopeResolver.cs:185-241`. There are no runtime feature checks in operations/commerce application code (grep for `BrandFeatureGate`/`BrandFeatures` finds only core identity code) (SA-AUTHZ-011). |
| 8 | User accesses another vertical's features | **Allowed — not enforced server-side**, except indirectly where a vertical's module is feature-gated | Vertical gating exists only in `GetNavigator.cs:39-45` (menu). No `VerticalKey` checks in any endpoint or operations/commerce handler (SA-AUTHZ-012). |
| 9 | Background job processes another tenant's records | **Not determinable** for per-job tenant correctness. **Bypass grant itself is safe** | Workers bypass RLS for all tenants by positive marker only (`WorkerScope.cs:70-86`, `CommerceHostCurrentTenant.cs:80-91`). Whether each job keeps per-record brand context (gateway secrets, notifications) was not traced job by job. |
| 10 | Cached authz decisions surviving role/membership change | **Bounded**: permissions live in the JWT for ≤15 min, invalidated by `perm_ver` with a 15 s cache (fail-open); brand status 30 s; output cache keyed by tenant and authorization re-runs on hits; ABAC `PolicyCache` inert | `TokenVersionStore.cs:16-45`, `BrandStatusStore.cs:15-53`, `OutputCaching.cs:44-94`. |
| C | Customer A reads customer B's order by id | **Blocked by app predicate** (404). DB customer policy is a second layer on the operations host only. On commerce, wallet/loyalty/refund tables fall back to brand-only RLS (SA-AUTHZ-005) | `OrderQueries.cs:187-189`; `CustomerPaymentHandlers.cs:39-41,135-137`; `CustomerPackageHandlers.cs:84-87`; `CustomerSubscriptionHandlers.cs:232-235`. |

## Table 3 — Hardcoded authorization decisions

| # | Location | Hardcoded decision | Risk |
|---|---|---|---|
| H1 | `Utilities/Auth/PermissionHandler.cs:32-33`, `AnyPermissionRequirement.cs:238-243` | `user_type == "platform_admin"` ⇒ every permission granted | A single string column grants total authority (see SA-AUTHZ-001) |
| H2 | `Utilities/Middlewares/TenantResolutionMiddleware.cs:35-47` | `platform_admin` ⇒ `bypass_rls=true` and `X-Brand-Id` honoured | Same bit disables the DB layer |
| H3 | `Utilities/Middlewares/BrandSuspensionMiddleware.cs:132-134` | `platform_admin` exempt from suspension | By design |
| H4 | `Utilities/Services/HttpContextCurrentUser.cs:62-63,90` | `IsPlatformAdmin` (user_type) short-circuits `IsWithinScope` | By design; inherits H1 risk |
| H5 | `core.WebApi/Endpoints/Identity/AdminSettings.cs:181-185` | `UserType == "brand_admin"` gate on settings handlers (identity axis, not permission) | Already flagged in `FIX_REPORT.md` "Observed but not changed"; still present |
| H6 | `AdminSettings.cs:191-195` | Platform billing settings: `IsPlatformAdmin` only | Correct but hardcoded |
| H7 | `core.Application/Identity/Auth/Common/ScopeResolver.cs:185-186,248` | platform_admin exempt from entitlement stripping; gets the full step-up catalogue | By design |
| H8 | `GrantMembership.cs:51-56` | Role code `"platform_admin"` grantable only by platform admin | Correct but the *user type* path is unguarded (SA-AUTHZ-001) |
| H9 | `Users/Commands/SetUserType/SetUserType.cs:18-33` | Hardcoded user_type → priority table | Separate from `roles.priority` data; can drift |
| H10 | `Utilities/Auth/RiderOnlyRequirement.cs:22-25` | `user_type == "rider"` lane | By design |
| H11 | `operations.Application/Logistics/Riders/Commands/CreateRider/CreateRider.cs:42` | `UserType == Rider` precondition | Low |
| H12 | `GetAccessPeople.cs:52,147-148`, `GetAccessFranchises.cs:51` | Role code `"franchise_owner"` for visibility/tiering | Display/filter logic keyed on a role code |
| H13 | 28 `IsPlatformAdmin` sites in 17 files (e.g. `PlatformPlanCommands.cs:28,129,203,244`, `FranchiseSubscriptionCommands.cs:29,134`, `ManageRoles.cs:104,134`, `RoleEditGuard.cs:53`) | Platform-only operations decided by user_type in handlers | Inconsistent: `SetBrandFeature`/`ApplyBundleToBrand` have **no** such check (SA-AUTHZ-004) |
| H14 | `admin-web/src/hooks/usePermissions.ts:50,69`, `pos-web/src/hooks/usePermissions.ts:42`, `BrandSwitcher.tsx` | UI treats `platform_admin`/`brand_admin` specially | UI only |

## Findings

### SA-AUTHZ-001 — Any `users.create` holder can create a `platform_admin` account (unauthenticated → platform takeover via self-signup)
- Category: Privilege escalation / RBAC
- Severity: **Critical**
- Status: Verified (code read end-to-end; not executed)
- Evidence:
  - `core.Application/Identity/Users/Commands/CreateUser/CreateUser.cs:22-55`: `UserType` is taken from the request. The only check is `UserType.IsValid` (L31), and `platform_admin` is in `UserType.All` (`SharedDataModel/Enums/UserType.cs:12,31-34`). With a password supplied, `Status=Active` (L46). There is no actor-rank or user-type ceiling. Contrast `SetUserType.cs:43-59`, which does block this.
  - Endpoint: `core.WebApi/Endpoints/Identity/AdminUsers.cs:33` (`permission:users.create`). The same path is reachable through `AdminAccessControl.cs:39` → `InviteUser.cs:29-35`, which calls `CreateUserCommand` with the client's `UserType` before the membership grant.
  - `users.create` is held by `brand_admin`, `franchise_owner` and `store_admin` (`IdentitySeeder.cs:521,577,603`).
  - Self-service signup is anonymous and creates a `brand_admin` (`Signup.cs:30-32`, `CompleteSignup.cs:46,150,161-165`).
  - The DB allows it: `users.user_type` CHECK includes `platform_admin` (`database_scripts/02_bc2_identity_access.sql:31-32`), and the users INSERT RLS is `WITH CHECK (true)` (`0029_users_brand_rls.up.sql`).
  - The new account receives a full bypass: `PermissionHandler.cs:32-33`, `TenantResolutionMiddleware.cs:35-40`, `HttpContextCurrentUser.cs:62-63,90`.
- Observed behaviour: `POST /api/v1/admin/users {email:"x@attacker", password:"…", userType:"platform_admin"}` as a brand/franchise/store admin creates an active platform admin. `users.create` is `high` risk, but step-up OTP goes to the attacker's own verified identifier. Logging in as the new account yields a token with `user_type=platform_admin`, which passes every permission policy, bypasses RLS on all tables, and is exempt from suspension.
- Reproduction / verification method: code trace above. To confirm, run an integration test that calls `CreateUserCommandHandler` with `UserType="platform_admin"` as a brand-admin `ICurrentUser`, then `PasswordLoginHandler`, and asserts the token's `user_type`.
- Impact: Complete loss of multi-tenant isolation. Any internet user who can pass a phone OTP can read and modify every tenant's data, billing and configuration.
- Recommended remediation (smallest safe change): in `CreateUserCommandHandler`, reuse the `SetUserType` guard. Reject `platform_admin` unless `actor.IsPlatformAdmin`, and reject any type whose priority outranks the actor. Better still, derive `user_type` from the granted role (`UserType.ForPrimaryRole`) and stop accepting it from clients. Add a DB trigger or CHECK that only a bypass session can write `user_type='platform_admin'`.
- Regression tests required: brand/franchise/store admin create and invite with `platform_admin` and with `brand_admin` (from store_admin) are refused. A platform admin can still create one.
- Dependencies / priority: **P0**
- Prior-doc cross-ref: contradicts `ABAC_AUDIT_2026-08-31.md` §3 ("6 routes refused, including self-promotion to platform_admin") and §6 ("production-sound"). That test covered `set-type`, not create/invite.

### SA-AUTHZ-002 — Identity write handlers lack target-rank and target-scope guards (in-brand account takeover)
- Category: Privilege escalation / horizontal + vertical IDOR
- Severity: **High**
- Status: Verified (code read; not executed)
- Evidence:
  - `SetPersonStatus.cs:24-68`. `POST /admin/access-control/people/{id}/status` (`AdminAccessControl.cs:51`, `permission:users.update`, risk `normal` per `IdentitySeeder.cs:129`). Action `"activate"` sets `PasswordHash` to a caller-supplied password **for any non-deleted user regardless of current status** (L33-46).
  - `UpdateUser.cs:18-75` (`users.update`) rewrites `Email`/`PhoneE164` (L27-28) and bank/UPI/KYC fields (L54-63) of any target id.
  - `DeactivateUser.cs:14-23` (`users.deactivate`).
  - None of these handlers loads the actor's rank or calls `IsWithinScope`/`ScopedToCallerBrand`. The only boundary is users RLS (`0029`: same brand via any membership).
  - `store_admin` and `franchise_owner` hold `users.update`/`users.deactivate` (`IdentitySeeder.cs:577,603`).
  - `PasswordLoginHandler.cs:60-125` never enforces `MustChangePassword`.
- Observed behaviour: a store admin of store S1 can set the brand admin's password (or change their email and then use forgot-password), then log in as the brand admin with a full-permission token. They can also edit any other store's staff and their payout bank details. The dedicated `users.set_password` permission (critical, step-up) is bypassed by a `normal`-risk door.
- Reproduction / verification method: code trace. Integration test: store-admin actor → `SetPersonStatusCommand(brandAdminId, {action:"activate", password})` → login as the brand admin succeeds.
- Impact: Vertical escalation inside a tenant, account takeover, payout fraud (bank/UPI change on rider/staff profiles).
- Recommended remediation:
  - Add one `TargetUserGuard` (shared helper) to every identity write handler. It should require `ScopedToCallerBrand`, require the target's highest role priority to be ≥ the actor's, and require `IsWithinScope` on the target's memberships.
  - Restrict `"activate"` to `Invited`/`Locked` users and require `users.set_password`.
  - Move email/phone/bank changes behind a high-risk permission with step-up and bump `perm_version`.
- Regression tests required: store_admin → brand_admin activate/update/deactivate refused; store_admin → other store's staff refused; own-store junior allowed.
- Dependencies / priority: **P0**
- Prior-doc cross-ref: `ABAC_AUDIT_2026-08-31.md` §3 claims "password reset of another user" was refused. That covered `/set-password` only.

### SA-AUTHZ-003 — `GrantMembership` accepts any user id platform-wide (cross-tenant attachment → cross-tenant takeover)
- Category: Cross-tenant privilege escalation
- Severity: **High** (Critical if target UUIDs are obtainable)
- Status: Partially Verified (code read; UUID-discovery path not established)
- Evidence: `GrantMembership.cs:42-225`. Scope, brand and rank checks apply to the *target scope and role* (L144-180), but nothing checks that `cmd.Request.UserId` belongs to the actor's brand. `user_scope_memberships` has RLS **off** (`0029_users_brand_rls.up.sql:149` comment; no later migration enables it). The insert therefore succeeds for any existing user id. `IsPrimary=true` also flips the victim's existing primary memberships (L188-193). Once the membership exists, `identity_access.user_in_brand(victim, attackerBrand)` is true (`0029`), so the victim becomes visible and writable to the attacker's brand under users RLS. That enables SA-AUTHZ-002 against them, including a platform-admin account if it holds any brand membership. `0031_subbrand_scope_rls.up.sql:9-14` records a live platform_admin with `nodes=[platform brand]`.
- Observed behaviour: brand admin of self-signed-up brand A → `POST /admin/roles/memberships/grant {userId: <brand B admin or platform admin>, scopeType:"brand", roleId:<store_staff>}` succeeds. The attacker then uses `SetPersonStatus activate` or `UpdateUser` email on that user and logs in as them.
- Reproduction / verification method: code trace. Exploitation needs the victim's UUID. I found no DTO exposing other tenants' user ids, but did not exhaustively audit all responses.
- Impact: Cross-tenant account takeover. Can also unexpectedly re-home a victim's primary scope.
- Recommended remediation: in `GrantMembershipCommandHandler`, require the target user to be within the actor's brand (`ScopedToCallerBrand`) unless the actor is a platform admin. New users should only be attachable through the invite flow, which creates them. Enable RLS on `user_scope_memberships` with a brand-resolving policy (a separate audit, per the 0029 note).
- Regression tests required: granting a membership to a user with no membership in the actor's brand → 403/404.
- Dependencies / priority: **P0**

### SA-AUTHZ-004 — No permission ceiling on role edits and user overrides; platform-plane handlers rely on permission codes only (self-grant `saas.manage` → free entitlements / mark own platform invoice paid)
- Category: Privilege escalation / entitlement bypass
- Severity: **High**
- Status: Partially Verified (code verified; whether `saas.*` survives entitlement stripping depends on `permissions.module_key` data. `0026_authz_seed_from_role_permissions.up.sql:18` says `saas` has no single backing module, i.e. it is likely an orphan and therefore kept)
- Evidence:
  - `SetUserPermissionOverride.cs:37-99` (`permissions.assign`) accepts **any** permission code (L64-66) and any scope type including `platform`, with no check that the actor holds the code or that the scope is within the actor's.
  - `SetRoleCells.cs:43-106` enables any cell on an own-brand role without checking the actor's own permissions.
  - Platform-plane handlers `SetBrandFeature.cs:15-54` and `ApplyBundleToBrand.cs` (route `AdminEntitlements.cs:34-38`, `permission:saas.manage`) contain no `IsPlatformAdmin` check. Contrast `PlatformPlanCommands.cs:28,129,203,244`, which do.
  - `brand_feature` RLS allows own-brand writes (`0005_split_features_from_modules.up.sql:158-161`).
- Observed behaviour: a brand admin grants themselves `saas.manage` (step-up OTP to their own phone), refreshes, then `POST /admin/entitlements/brands/{ownBrand}/features {featureKey, enabled:true}` licenses premium features without paying. `SetInvoiceStatus` on their own platform invoice is likely possible the same way (not traced).
- Impact: Revenue loss and an entitlement-model bypass. It also undermines the RBAC model generally, since any brand admin can mint any code their brand can see.
- Recommended remediation: enforce a **grant ceiling**. An actor may only grant codes they themselves hold, and never codes flagged platform-scope (`permissions.requires_scope`/platform module). Add `IsPlatformAdmin` (or a platform-audience check) to every handler under `/admin/entitlements` and `/admin/brands`. Restrict override scope types to nodes within the actor's scope.
- Regression tests required: a brand admin granting `saas.manage`/`brands.create` via override or role cells → refused; `SetBrandFeature` as a non-platform caller → 403.
- Dependencies / priority: **P0/P1**

### SA-AUTHZ-005 — Commerce host never publishes `customer_id` (A0.6 fail-open persists) or the subject GUCs
- Category: Tenant/customer isolation (defence-in-depth) — Related area: TENANCY
- Severity: **Medium**
- Status: Verified (code read)
- Evidence: commerce registers `CommerceHostCurrentTenant` as `ICurrentTenant` (`commerce.WebApi/Program.cs:59`). That class (`CommerceHostCurrentTenant.cs:42-94`) implements Brand/Franchise/Store/User/Partner/Bypass only. `CustomerId`, `ScopeNodes`, `Permissions`, `Roles`, `UserType` and `TokenUse` fall back to the interface defaults of `null` (`ICurrentTenant.cs:31,39-54`). The interceptor therefore writes `app.current_customer_id=''` on every commerce request (`RlsConnectionInterceptor.cs:64,100`). The `rls_brand_or_customer` policies on `wallet_accounts`, `wallet_transactions`, `loyalty_points_ledger`, `payment_refunds`, `customer_packages`, `package_usage_ledger` and `coupon_redemptions` (`rls_proposal.sql:236-282`) degrade to brand equality, which is the A0.6 fail-open that `HttpContextCurrentTenant.cs:30-46` fixed for the core and operations hosts only. `UserId` also publishes the customer's `sub` as `app.current_user_id` (L69).
- Observed behaviour: customer isolation on the commerce host rests on handler predicates alone. The ones I sampled are correct (`CustomerPaymentHandlers.cs:39-41,135-137`, `CustomerPackageHandlers.cs:84-87`, `CustomerSubscriptionHandlers.cs:232-235`).
- Impact: one forgotten `CustomerId ==` predicate in any commerce customer handler becomes a cross-customer leak within a brand, with no DB backstop.
- Recommended remediation: make `CommerceHostCurrentTenant` delegate to `HttpContextCurrentTenant` for the HTTP lane (all claim-derived members). Keep the worker branch as is.
- Regression tests required: unit test that `CommerceHostCurrentTenant` exposes `CustomerId`/`ScopeNodes` for customer and staff principals. RLS test with the commerce adapter.
- Dependencies / priority: P1 (fix together with 006)
- Prior-doc cross-ref: A0.6 in `ICurrentTenant.cs:19-31` and `ABAC_AUDIT_2026-08-31.md` §4 are described as closed. They are closed only on core and operations.

### SA-AUTHZ-006 — 0031 RESTRICTIVE sub-brand policy denies every customer request and every non-platform commerce-host request on the tables it covers
- Category: Authorization correctness / availability regression — Related area: TENANCY
- Severity: **High**
- Status: Partially Verified (SQL, interceptor and adapters read end-to-end; not executed)
- Evidence:
  - `0031_subbrand_scope_rls.up.sql:76-83`: `IF v_nodes IS NULL THEN RETURN NULL` under `AS RESTRICTIVE … USING/WITH CHECK` (L196-200) on 39 tables, including `order_lifecycle.orders`, `pickup_requests`, `delivery_slots`, `delivery_slot_bookings`, `tenancy_org.stores`, `customer_catalog.price_lists`, `commerce.payments` and `finance_royalty.*` (L139-178).
  - `kernel.split_setting` returns NULL for `'?'` (`0025…up.sql:49-62`), and the interceptor writes `'?'` when `ScopeNodes` is null (`RlsConnectionInterceptor.cs:85-88`).
  - Customer tokens carry no `scope_nodes` (`JwtTokenService.cs:98-120`), so `HttpContextCurrentTenant.ScopeNodes` is null (L53).
  - Commerce's adapter has no `ScopeNodes` at all (SA-AUTHZ-005).
  - The project's own test `SubBrandScopeRlsTests.An_unresolved_scope_nodes_claim_denies_everything` (`tests/operations.IntegrationTests/Rbac/SubBrandScopeRlsTests.cs:264-272`) asserts exactly this denial. No customer-lane or commerce-host case exists.
- Observed behaviour (by trace):
  - Customers get empty order, pickup, store and price lists, and inserts (pickup request, parcel order, `commerce.payments` initiate) fail the WITH CHECK.
  - Brand and store staff get empty payments, cash books, expenses, royalty and franchise-subscription data, and writes fail, on the commerce host.
  - Only platform admins (bypass) and workers are unaffected. That matches the only commerce verification recorded (`FIX_TASKS.md` A-6: "platform admin, HTTP 200 each").
- Impact: customer app and finance/payments console broken for tenants. It fails closed (no leak), but it shows the sub-brand rule was shipped without a lane-aware design.
- Recommended remediation: give the customer and partner lanes an explicit arm in `within_scope_cols`, for example by having the interceptor publish `scope_nodes` = `brand:<brand_id>` for `token_use in (customer, customer_mcp)`, or by short-circuiting on `kernel.current_customer_id() IS NOT NULL` for customer-owned rows. Fix SA-AUTHZ-005 so commerce publishes claims.
- Regression tests required: RLS tests for a customer session (own orders visible, others not) and for a commerce-adapter brand-admin session on `payments`/`cash_books`.
- Dependencies / priority: **P0** (if 0031 is applied in any shared environment, which `FIX_REPORT.md:128` states)
- Prior-doc cross-ref: contradicts `FIX_REPORT.md` A-6 "every legitimate scope level unchanged".

### SA-AUTHZ-007 — Scope check is decoupled from permission source (permission union × node union = scope amplification)
- Category: RBAC/ABAC design flaw
- Severity: **Medium**
- Status: Verified (code read)
- Evidence: `ScopeResolver.cs:117-161` unions permissions from all ancestor-or-self memberships of the active node. `ScopeResolver.cs:168-170` emits **all** membership nodes as `scope_nodes`. `IsWithinScope` (`HttpContextCurrentUser.cs:107-120`) and `kernel.within_scope_cols` (`0031…up.sql:85-108`) pass if **any** node covers the resource.
- Observed behaviour: a user who holds `store_admin` at S1 and any low-privilege role at brand level (for example a read-only role granted from the person drawer) gets store_admin's write permissions with brand-wide reach.
- Impact: least privilege fails for multi-membership users. Each membership's permissions should be bounded by that membership's node.
- Recommended remediation: evaluate `(permission, node)` pairs. Emit per-node permission sets, or in `IsWithinScope` require that the node granting the specific permission covers the resource. The ABAC engine (`authz.within_scope`) is the natural home for this once enabled.
- Regression tests required: store role at S1 + brand read-only role → write to S2 refused.
- Dependencies / priority: P1

### SA-AUTHZ-008 — ABAC engine is inert; attribute-based rules are hand-coded per handler
- Category: ABAC readiness
- Severity: **Medium** (against the stated RBAC+ABAC target)
- Status: Verified
- Evidence:
  - `AbacOptions.cs:15` (`Enabled` default false).
  - No `Abac` key in any `appsettings*.json` or `deploy/` file (grep).
  - `AbacAuthorizationService.EvaluateAsync` returns `Skipped` when disabled.
  - `AbacAuthorizationHandler.cs:73-78` succeeds when the endpoint declares no target. No endpoint declares one (no `AbacResource`/`RequireAbac`/`IAbacAuthorizationService` usage in core/operations/commerce).
  - `AbacAuthorizationHandler.cs:97-103` succeeds on evaluator exceptions (fail-open by design for shadow).
- Observed behaviour: all ABAC-like decisions (ownership, store/franchise, rider assignment, resource state) are hand-written at roughly 75 `IsWithinScope` sites plus self-filters. The `requires_scope` flag (0027) projects only into inert policy rows.
- Impact: there is no central policy engine to enforce new ABAC rules. Each new resource depends on developers remembering checks, which is how SA-AUTHZ-002/003 arose.
- Recommended remediation: keep RBAC authoritative and turn on shadow mode for one module (commerce first, per the plan) to collect parity data. Change the handler's exception path to deny once in enforce mode.
- Regression tests required: an endpoint with `AbacResource` in enforce mode denies on a policy deny and on an evaluator exception.
- Dependencies / priority: P2
- Prior-doc cross-ref: confirms `AUDIT_REPORT.md` "ABAC status — built, inert" and `ABAC_AUDIT_2026-08-31.md` §1.

### SA-AUTHZ-009 — User suspension/deactivation does not revoke live sessions; revocation check fails open
- Category: Session/permission revocation
- Severity: **Medium**
- Status: Verified (code read)
- Evidence:
  - `SetPersonStatus.cs:48-68` and `DeactivateUser.cs:14-23` set `Status=Suspended` but neither bumps `PermVersion` nor revokes refresh tokens. `SetUserType.cs:75` shows the intended pattern.
  - Refresh does check status (`RefreshTokenHandler.cs:67-69`), so exposure equals the access-token lifetime: 15 min (`appsettings.json:21`).
  - `TokenVersionStore.cs:41-45` returns null on any error, and `TenantResolutionMiddleware.cs:61-71` treats null as pass (fail-open). Cache TTL is 15 s per process.
  - `EnforceTokenVersion` applies only to `token_use=user`. Partner and customer tokens have no live revocation.
- Impact: a fired or compromised employee keeps full access for up to 15 minutes after suspension. A DB hiccup silently disables revocation.
- Recommended remediation: bump `PermVersion` and revoke the refresh-token family in both suspend paths. Make the version check fail closed for high/critical permissions, or at least log and alert.
- Regression tests required: suspend user → next request with the old token → 401 within the TTL.
- Dependencies / priority: P1

### SA-AUTHZ-010 — Gateway rate-limit partition keyed on client-controlled `X-Brand-Id` / unverified JWT
- Category: Abuse resistance / tenant DoS — Related area: API/OPS
- Severity: **Medium**
- Status: Verified (code read)
- Evidence: `RateLimitPartitioning.cs:24-70`. The partition is `brand:<X-Brand-Id header>` when present, else the unverified `brand_id` from the JWT payload, with a 10× budget (`Program.cs:218`). The IP fallback trusts the leftmost `X-Forwarded-For` (L80-85).
- Observed behaviour: an anonymous client rotating a random GUID in `X-Brand-Id` gets a fresh 3000/min bucket per request, so the global limiter is effectively disabled. Sending a victim tenant's brand id exhausts that tenant's shared bucket and throttles all its users. The code comment claims "the worst a forged brand_id achieves is being counted against someone else's budget". That *is* the tenant-DoS case.
- Recommended remediation: only use the brand partition after validating the JWT at the gateway, or key on `(IP, brand)`. Ignore `X-Brand-Id` for partitioning. Trust `X-Forwarded-For` only from known proxies.
- Regression tests required: `RateLimitPartitioningTests` should assert that a header-only brand does not escape the IP bucket.
- Dependencies / priority: P1

### SA-AUTHZ-011 — Plan entitlements are enforced only on the staff lane (token stripping); customer/partner lanes and non-brand-scoped staff are not covered live
- Category: Subscription entitlement enforcement
- Severity: **Medium**
- Status: Verified (code read)
- Evidence: entitlement is applied only in `ScopeResolver.cs:185-241` (staff tokens). `CustomerOnly`/`PartnerOnly` policies carry no feature check (`PermissionPolicyProvider.cs:151-189`). No runtime feature checks exist in operations/commerce application code (grep for `BrandFeatureGate|BrandFeatures` hits only core identity files). `PermVersionBumper.BumpBrandMembersAsync` bumps only **brand-scoped** memberships (`PermVersionBumper.cs:38-44`), so franchise/store staff keep stale entitlements until token expiry.
- Impact: customers of a brand without, say, the wallet/loyalty/subscription feature can still call those customer APIs if data exists. A downgrade takes up to 15 min to bite for store staff.
- Recommended remediation: add a `RequireFeature("<key>")` endpoint filter backed by a cached brand-feature lookup, applied to customer/partner groups. Bump all members whose memberships resolve to the brand (reuse `user_in_brand`).
- Regression tests required: brand without feature X → customer endpoint for X → 402.
- Dependencies / priority: P1

### SA-AUTHZ-012 — Vertical (business-type) boundary is not enforced server-side
- Category: Multi-vertical authorization
- Severity: **Medium**
- Status: Verified (absence searched across endpoints and operations/commerce application code)
- Evidence: the only vertical gate is in the menu builder (`GetNavigator.cs:39-45`). No endpoint or handler reads `Brand.VerticalKey` to authorize. `CreateOrderCommand.cs:644-646` notes that vertical resolution "lands" later.
- Impact: a laundry tenant can call salon/logistics APIs (and vice versa) wherever the permission code is held and the feature is licensed or core. The target of exactly one primary vertical per tenant is a UI convention, not a server control.
- Recommended remediation: tag endpoint groups with a vertical and add a filter comparing it to the caller brand's `vertical_key` (cached), or fold the vertical into the entitlement feature map so licensing implies the vertical.
- Regression tests required: laundry brand → salon endpoint → 403/402.
- Dependencies / priority: P2

### SA-AUTHZ-013 — Identity-axis (`user_type`) gates where permission gates belong; platform-scoped dispatch settings reachable by brand admins
- Category: Hardcoded authorization
- Severity: **Low**
- Status: Verified (code read; DB block inferred from policy text)
- Evidence: `AdminSettings.cs:181-185` (`UserType == "brand_admin"`). `UpdateDispatchSettings.cs:38-39` upserts a **platform** row (`brandId: null`) and is reachable by brand admins with `settings.manage` (only `offer_accept` needs `dispatch.mode.manage`, L27). The write is stopped only by `system_settings` WITH CHECK (`0027…up.sql:28-30`), which yields an unhandled DB error rather than a 403.
- Impact: correctness depends on the DB backstop. The user_type gate already mis-fired once (A-2).
- Recommended remediation: replace `Forbidden()` with permission/`IsPlatformAdmin` checks. Make dispatch settings platform-only in the handler.
- Dependencies / priority: P3

### SA-AUTHZ-014 — Suspension gate fails open and skips the partner lane
- Category: Tenant lifecycle enforcement
- Severity: **Low**
- Status: Verified
- Evidence: `BrandStatusStore.cs:47-53` (errors → null → pass). `BrandSuspensionMiddleware.cs:71-83` (no brand → pass; partner tokens carry none, `TokenClaims.cs:265-287`). Cache TTL 30 s.
- Recommended remediation: fail closed for mutating methods when the lookup errors. Resolve the partner's linked brands if partner activity should freeze with the brand.
- Dependencies / priority: P3

### SA-AUTHZ-015 — Partner isolation is a single (RLS-only) layer
- Category: Defence-in-depth
- Severity: **Low**
- Status: Partially Verified
- Evidence: `GetPartnerBookingTrack.cs:31-33` and `GetMyPartnerBookingsQuery` filter by id only. Isolation is `rls_partner` (`db/patches/rls_partner.sql:54-67`, `rls_partner_dispatch.sql:38`).
- Recommended remediation: add explicit `PartnerId ==` predicates from `ICurrentUser`/claims.
- Dependencies / priority: P3

## Table 4 — Missing security tests

| # | Test that should exist | Guards against | Present today? |
|---|---|---|---|
| T1 | Non-platform actor creates/invites a user with `userType=platform_admin` (and a higher-rank type) → refused | SA-AUTHZ-001 | No (only `SetUserType` is guarded) |
| T2 | store_admin → `people/{brandAdmin}/status activate`, `PUT users/{brandAdmin}` email, `deactivate` → refused; other-store staff → refused | SA-AUTHZ-002 | No |
| T3 | Brand A admin grants a membership to a user with no brand-A membership → refused | SA-AUTHZ-003 | No |
| T4 | Brand admin grants `saas.manage`/`brands.create` via override or role cells → refused; `SetBrandFeature` as non-platform → 403 | SA-AUTHZ-004 | No (`RoleEditGuardTests` cover role ownership/rank only) |
| T5 | RLS: customer-token session reads own orders/stores/price lists under 0031; commerce-adapter brand admin reads payments/cash books | SA-AUTHZ-006 | No (`SubBrandScopeRlsTests` has no customer/commerce case) |
| T6 | `CommerceHostCurrentTenant` publishes `CustomerId`, `ScopeNodes`, etc. | SA-AUTHZ-005 | No tests reference the class |
| T7 | Multi-membership user (store role + brand read-only role) cannot write to a sibling store | SA-AUTHZ-007 | No (`ScopeBoundaryTests` covers single-node cases) |
| T8 | Suspend user → old access token rejected within TTL; `TokenVersionStore` error behaviour | SA-AUTHZ-009 | Partial (middleware tests exist; no suspend→revoke test) |
| T9 | Gateway: rotating `X-Brand-Id` does not escape the IP limit | SA-AUTHZ-010 | `RateLimitPartitioningTests` exists and appears to codify current behaviour (not read in full) |
| T10 | Customer endpoint for an unlicensed feature → 402; vertical mismatch → 403 | SA-AUTHZ-011/012 | No |
| T11 | Contract test: every mapped endpoint has authorization metadata (reflection over `EndpointDataSource`) or is on an explicit anonymous allow-list | Future anonymous endpoints (no FallbackPolicy) | No |
| T12 | `IsWithinScope` present on every mutating handler that takes a franchise/store/warehouse id (architecture test) | Missing call sites (e.g. A0.7 history) | No |

Note: Docker-backed integration tests return early and report "passed" when Docker is unavailable (`if (!_fx.DockerAvailable) return;`, 21 files). CI on GitHub runners presumably has Docker; locally they give false greens. Related area: QA.

## Positive controls verified
- **Token validation**: RS256 pinned, issuer/audience/lifetime validated on all three hosts (core `Program.cs:301-317`, ops `:88-110`, commerce `:137-160`).
- **Lane separation**: `PermissionHandler` requires `token_use=user` (`PermissionHandler.cs:23-25`). `CustomerOnly` requires `token_use=customer`. `RiderOnly` requires user + rider type. API keys are a separate scheme with `apiscope:` policies.
- **Tenant context comes only from signed claims**. `X-Brand-Id` is honoured for platform admins only (`TenantResolutionMiddleware.cs:38-47`; `HttpContextCurrentUser.cs:131-138`).
- **F-1/F-2 fix present**: `UserBrandScope.ScopedToCallerBrand` used by `GetUsers.cs:33`, `GetUserById.cs:25`, `GetPersonMemberships.cs:50`, `GetPersonPermissionOverrides.cs:48`.
- **Absent `scope_nodes` denies** (`HttpContextCurrentUser.cs:92-105`). The interceptor's three-state sentinel avoids pooled-connection GUC leakage (`RlsConnectionInterceptor.cs:67-90`).
- **Customer and rider self-service lanes** derive identity from `sub` and filter every read and write by it (sampled handlers listed in Table 2).
- **Live revocation** bump on membership, role-cell, override and user-type changes (Table 2 #5).
- **Impersonation** re-validated per request, fails closed, read-only enforced on method (`ImpersonationGuardMiddleware.cs`).
- **Worker RLS bypass** requires a positive marker. No HttpContext and no marker ⇒ fail closed (`CommerceHostCurrentTenant.cs:80-91`).
- **Output cache** keys on tenant claims plus `X-Brand-Id`. No per-user endpoint is cached (all 19 `CacheSharedOutput` uses reviewed).
- **GrantMembership** blocks the `platform_admin` *role*, out-of-scope target nodes, other-brand scopes and higher-rank roles (`GrantMembership.cs:51-183`). **RoleEditGuard** blocks editing system, foreign-brand or higher-rank roles (with tests).
- **Every endpoint has explicit authorization metadata** (Table 1 coverage).

## Open questions / not verified
- Whether 0031 is applied in staging/production and whether customer and commerce traffic is actually failing (SA-AUTHZ-006). This needs one live request per lane.
- `permissions.module_key` for `saas.*` in the live DB (decides whether SA-AUTHZ-004 survives entitlement stripping).
- Whether any API response exposes other tenants' user UUIDs (decides SA-AUTHZ-003's exploitability).
- Per-job tenant correctness of the 15 commerce workers (scenario 9).
- OAuth/MCP (`/oauth/*` runs with an anonymous RLS bypass, `core.WebApi/Program.cs:545-549,617-645`) and webhooks are left to the API/payments specialists.
- I could not run any test or the application.

## Verdict inputs
- **Q11 — RBAC consistently enforced on the backend: Partially Supported.** Every endpoint is permission- or lane-gated and checked server-side from signed claims. But identity-administration writes lack rank, scope and type ceilings (SA-AUTHZ-001/002/003/004), giving an unauthenticated-to-platform-admin path.
- **Q12 — ABAC real, trusted, server-side: Not Supported.** The engine is inert (SA-AUTHZ-008). The attribute checks that exist are hand-coded, use trusted claim sources, and are partially broken by 0031 lane gaps (SA-AUTHZ-006) and scope amplification (SA-AUTHZ-007).
- **Q13 — subscription entitlements enforced server-side** (interpreting Q13 as the plan/entitlement question): **Partially Supported.** The staff lane is enforced via token stripping. Customer and partner lanes are not, and entitlements can be self-granted through `saas.manage` (SA-AUTHZ-004/011).
- **Q2 / Q3 — tenant isolation, from the authz angle: Partially Supported.** Direct cross-tenant data access is blocked by signed-claim RLS plus handlers. Cross-tenant takeover is reachable through identity-admin chains (SA-AUTHZ-001/003). Commerce-host customer RLS is fail-open (SA-AUTHZ-005). The vertical boundary is not enforced (SA-AUTHZ-012).
