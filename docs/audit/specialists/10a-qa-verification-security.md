# 10A — QA, Testing and Independent Verification (security, tenancy, billing, payments)

Agent key: `qa-a` · Area code: `QA` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw` (HEAD `4f54b2c`)

## Scope and method

**What this report does.** I re-checked the security, tenancy, subscription and payment claims in four specialist reports: `02-multitenancy.md` (SA-TEN-*), `03-subscription.md` (SA-SUB-*), `06-abac-rbac.md` (SA-AUTHZ-*) and the security, payment and concurrency findings in `08-backend-api.md` (SA-API-*). I also grepped `01`, `04`, `05`, `07`, `08b`, `09` and `11` for overlapping IDs, to build dedupe groups and spot contradictions.

**Coverage.**
- Every Critical and High finding in the four reports: 26 items.
- 18 Medium findings: TEN 5/8, SUB 4/10, AUTHZ 6/7, API 3/9. I picked the ones most likely to change a verdict.

**How each item was checked.** For each one I opened the cited files myself and traced the path:

> gateway route → endpoint mapping and authorization metadata → middleware → handler → EF / RLS interceptor → SQL policy, constraint or trigger

At each step I looked for a mitigating control the specialist might have missed:
- endpoint filters and validators;
- the CQRS pipeline;
- DB CHECKs and triggers;
- later migrations;
- RLS policies;
- seed sources;
- callers.

**Limitations.**
- **No .NET SDK and no Docker.** I ran no HTTP traffic, no xUnit and no Testcontainers. Every runtime claim below is either a static trace or an SQL-level reproduction, and is labelled as such.
- **The SQL repro is not the full schema.** It loads **verbatim extracts** of the migrations and patches into trimmed tables, so `pg_partman` and `postgis` are not needed.
- **CI was read, not re-run.** I read CI state through the GitHub API (read-only).

## Commands run & results

### 1. Throwaway PostgreSQL 16 cluster
Port 55434, data dir `/var/tmp/lg-qa-a`, run as the `postgres` OS user. It was stopped and deleted at the end (`pg_ctl stop -m fast; rm -rf /var/tmp/lg-qa-a`). The SQL files and output are archived in `scratchpad/qa-a/`.

```
initdb -D /var/tmp/lg-qa-a/data -A trust -U postgres
pg_ctl -D /var/tmp/lg-qa-a/data -o '-p 55434 -k /var/tmp/lg-qa-a -c listen_addresses=127.0.0.1' start
createdb qa
psql -f 00_base.sql      # roles app_user (NOBYPASSRLS, NOSUPERUSER), trimmed tables, users CHECK copied from 02_bc2_identity_access.sql:31-33
psql -f 01_kernel.sql    # verbatim rls_proposal.sql:65-83 + harden_app_user_and_rls_bypass.sql:31-38
psql -f 02_0025.sql      # verbatim 0025…up.sql:49-68 (split_setting, current_scope_nodes)
psql -f 03_policies.sql  # rls_brand (orders, stores) and rls_brand_or_customer (payments), text from rls_proposal.sql:268-279
psql -f 04_0031.sql      # migration 0031 verbatim (whole file)
psql -f 05_0015fn.sql    # verbatim 0015…up.sql:265-294 (set_brand_cancellation_state)
psql -f 06_0029.sql      # migration 0029 verbatim (whole file)
psql -f 07_data.sql      # brand A (status=suspended, reason=tos), brand B, 2 orders, 2 payments (customers c1, c2)
psql -f 08_scen.sql      # scenarios below; GUC values copied from RlsConnectionInterceptor.BuildSetConfigCommand
```

**Results.** Output is verbatim in `scratchpad/qa-a/scen_out.txt`. Every session ran as `app_user` with `rolbypassrls = f`.

| Scen | Principal emulated (GUCs) | Result | Supports |
|---|---|---|---|
| S1 | Core/ops host, **customer** token: `scope_nodes='?'`, `customer_id=c1` | orders **0**, payments **0**, stores **0**. INSERT order → `ERROR: new row violates row-level security policy "rls_subbrand_scope"` | SA-TEN-001, SA-AUTHZ-006, SA-DB-001 |
| S2 | Commerce host, customer (no `customer_id`, `scope_nodes='?'`) | payments **0** | SA-TEN-001 |
| S3 | Commerce host, **brand admin** (adapter has no `ScopeNodes`) | payments **0**, orders **0** | SA-TEN-001, SA-AUTHZ-006 |
| S4 | Ops host, brand admin, `scope_nodes=brand:A` | payments **2**, orders **2** | control (staff unaffected) |
| S5 | Brand-B session runs `set_config('app.bypass_rls','true')` itself | orders 0 → **2**, i.e. brand A's rows | SA-TEN-007, SA-DB-015 |
| S6 | Brand-B session: `INSERT INTO identity_access.users(…, user_type) VALUES (…,'platform_admin')` | `INSERT 0 1`. Neither the CHECK constraint nor the 0029 `WITH CHECK (true)` blocks it | SA-AUTHZ-001 (DB has no backstop) |
| S7 | Brand A (suspended, reason `tos`) calls `set_brand_cancellation_state(A,'cancelled')`, then `(A,'active')` | returns `suspended`, then `cancelled`. Final row: `status=active, suspension_reason=tos` | SA-TEN-003 |
| S7b | A brand-A session calls the same function with brand **B**'s id | `foreign_brand_was=active`, and B becomes `cancelled` | SA-TEN-007, SA-DB-003 (the HTTP handler passes the caller's own brand, so this is DB-only) |
| S2b | 0031 policy dropped (pre-0031 state); commerce customer c1 with no `customer_id` GUC | payments visible **2** (c1 and c2). With the GUC set as the core/ops adapter does: **1** | SA-TEN-002, SA-AUTHZ-005 |

### 2. Validator reachability
Script: `grep -rhoE "AbstractValidator<…>"` versus `ValidationFilter<…>` across `backend/laundryghar`, then `comm`.

- 130 validated types, 92 endpoint-filtered types.
- **40 validator types are never invoked.** The list matches SA-API-003 exactly; it includes `CreateOrderCommand`, `UploadInspectionPhotoCommand`, `UploadRiderDocumentCommand`, `InitiatePaymentRequest`, `WalletTopUpRequest` and `RefreshTokenCommand`.
- **Two filters point at types with no validator:** `CreateOrderRequest` and `UpsertSettingRequest`.

Files: `scratchpad/qa-a/{validated_types,filtered_types}.txt`.

### 3. CI (GitHub API, read-only)
- The latest CI run on `main` is run `36294076412` (2026-09-27, SHA `274b7af`, an ancestor of HEAD). Its "Backend build + tests" job **succeeded**. The overall run **failed** on the mobile jobs.
- Migration 0031 was added in `04d4bde`, which is an ancestor of `274b7af`. So `SubBrandScopeRlsTests` (which *asserts* the denial for unresolved `scope_nodes`) passes in CI. No test covers the customer or commerce lanes.
- The "Migration lint" job only checks that each up file has a matching down file. CI never applies migrations.

### 4. Git
I used only `git log` and `git merge-base`. No state changes.

## Verification table

Severity abbreviations: C = Critical, H = High, M = Medium. Status abbreviations: V = Verified, PV = Partially Verified, S = Suspected.

### Critical and High findings (all 26)

| ID | Specialist sev/status | QA verdict | Corrected sev/status | Evidence QA read | Notes |
|---|---|---|---|---|---|
| **SA-AUTHZ-001** | C / V | **Confirmed** | C / V (static chain; DB step reproduced in S6) | See the trace below this table. | **Chain holds end to end.** Two points the specialist did not make: (1) the account needs **no membership at all**, because `user_type` alone grants everything, which contradicts the premise in 0029 L41-44; (2) InviteUser commits the user *before* the GrantMembership guards run (see SA-QA-001). |
| **SA-TEN-001** | C / PV | **Confirmed** | C / V (SQL reproduced; HTTP not run) | 0031 L77-83 (NULL → deny), L138-179 (39 tables), L196-200 (RESTRICTIVE, FOR ALL); 0025 L49-68 (`'?'`→NULL); `RlsConnectionInterceptor.cs` (`ScopeNodes ?? "?"`); `HttpContextCurrentTenant.cs:53` (claim only); `JwtTokenService.cs:98-120` (no `scope_nodes` on customer tokens); `CommerceHostCurrentTenant.cs:42-94` (no `ScopeNodes`); `ICurrentTenant.cs` (`ScopeNodes => null`); 0032/0033 bodies (no policy change); `deploy/README.md:35,58` | **Not superseded.** 0032 only updates `users.user_type`; 0033 only touches the notification `recipient_type` CHECK. Grep finds `within_scope_cols`/`rls_subbrand_scope` only in 0025 and 0031. **0031 is on the standard path:** `migrate.sh up` applies all pending migrations, as `deploy/README.md` instructs, and CI's backend tests pass with it. **Correction to the impact text:** wallets, loyalty, refunds, packages, analytics and partner billing tables are *not* in the 0031 list. Reads there are not denied; they stay brand-equal (the SA-TEN-002 fail-open). Audited writes on the commerce host still fail, because `identity_access.audit_logs` *is* in the list. |
| **SA-AUTHZ-006** | H / PV | **Duplicate of SA-TEN-001** | → **C** / V | Same as SA-TEN-001 | The severity mismatch is resolved upward: it is an outage of the whole customer lane and the commerce staff lane in the standard apply path. It fails **closed** (no data exposure). |
| **SA-TEN-003** | H / V | **Confirmed** | H / V (S7 reproduced) | `BrandSuspensionMiddleware.cs:44-57` (`/api/v1/admin/cancellation` allow-listed); `AdminCancellation.cs:35-36` (`settings.manage`); `RequestBrandCancellation.cs:60-67,94` (only `archived` refused); `WithdrawBrandCancellation.cs` (`SetCancellationStateAsync(…,"active")`); 0015 L265-294 | The brand id comes from the caller's tenant, so this is self-only. Dunning reinstates only `nonpayment`, and the dunning worker is off by default (SA-SUB-005). A `tos` or manual suspension is therefore permanently lifted. |
| **SA-TEN-004** | H / PV | **Duplicate of SA-AUTHZ-003** (membership grant) **+ SA-AUTHZ-002** (UpdateUser / Deactivate) | H / PV | `GrantMembership.cs:42-225` | Its extra angle, a *legitimately shared* multi-brand user, is valid but is not a separate root cause. |
| **SA-AUTHZ-002** | H / V | **Confirmed** | H / V (static) | `SetPersonStatus.cs:24-68` (`activate` sets the password for any non-deleted user, with no rank or scope check); `UpdateUser.cs:18-29`; `DeactivateUser.cs:14-23`; `AdminAccessControl.cs:51` (`users.update`); `IdentitySeeder.cs:129` (`users.update` = normal risk), `:577,603`; grep `MustChangePassword` (set but never checked at login) | The temporary password is also emailed to the victim. That makes the takeover detectable, but it does not prevent it. |
| **SA-AUTHZ-003** | H / PV | **Confirmed** | H / PV | `GrantMembership.cs:47-185` (checks the target *role* and *scope*; never `cmd.Request.UserId`), `:188-205` (resets primary on all of the victim's memberships), `:207-222`; `AdminRoles.cs:33`; 0029 L149-153 (memberships RLS off) | I found no DTO that exposes foreign user ids (`RoleDto` has no `CreatedBy`). The UUID-discovery precondition stands, so severity stays High. Canonical for the TEN-004 group. |
| **SA-AUTHZ-004** | H / PV | **Confirmed – status corrected** | H / **V** | `SetUserPermissionOverride.cs:37-99` (any code, any scope); `ScopeResolver.cs:134-160` (global allow overrides apply everywhere); `SetBrandFeature.cs:20-54`, `SetBrandPlatformInvoiceStatus.cs:23-39`, `ApplyBundleToBrand.cs`, `CancelBrandPlatformSubscription.cs` (grep `IsPlatformAdmin` in `Entitlements/Commands` = **0 hits**); `AdminEntitlements.cs:33-38`; `IdentitySeeder.cs:221-222` (module `saas`); no `saas` module row anywhere (grep) → orphan → kept by `ScopeResolver.cs:236-240`; `phase4_brand_platform_subscription.sql:62-75` (own-brand RLS) | Traced the part the specialist left open. With a self-granted `saas.manage`, a brand admin can mark **its own platform invoice `paid`**. `/api/v1/admin/entitlements` is on the suspension allow-list, so this works even while suspended, and dunning then auto-reinstates (`BrandPlatformBillingService.cs:164-182`). |
| **SA-SUB-001** | H / V | **Confirmed** | H / V | `CompleteSignup.cs:199-225`; `TemplateProvisioner.cs:66-77` (no `ValidUntil`); `BrandPlatformBillingService.cs:67-69` (renewal only for `active`), `:201-207`; grep `trialing` | |
| **SA-SUB-002** | H / V | **Confirmed** | H / V | `ProcessPaylinkWebhook.cs:69-77`; `CollectBrandPlatformInvoice.cs` (link only for `issued`); `BrandPlatformBillingService.cs:186-195` | **Line citation error:** 03 cites `ProcessPaylinkWebhook.cs:L109-L115`, but the check is at **L71-72** (08's citation is correct). The substance is correct. |
| **SA-API-011** | H / V | **Duplicate of SA-SUB-002** | H | Same | |
| **SA-SUB-003** | H / V | **Confirmed** | H / V | `BrandPlatformBillingService.cs:176-182` (reinstate touches only `brands`), `:201-207`, `:67-69`; `ApplyBundleToBrand.cs:107` | It is masked today by SA-SUB-004 (no renewals run at all), but it becomes live as soon as SUB-004 is fixed. |
| **SA-SUB-004** | H / PV | **Confirmed** | H / PV (code plus .NET async `ExecutionContext` restore semantics; not executed) | `BrandPlatformBillingService.cs:60-69` (plain `CreateAsyncScope()` at L63), `:156` (worker scope only inside `RunDunningAsync`); `WorkerScope.cs:26-49` (`AsyncLocal` set synchronously inside the callee); `CommerceHostCurrentTenant.cs:80-90`; `phase4_brand_platform_subscription.sql:62-75` | An `AsyncLocal` written inside an `async` method is reverted when that method returns to its caller, so `RunCycleAsync` runs with `IsWorkerScope=false` → `bypass=false`, brand NULL → 0 rows. |
| **SA-API-010** | H / PV | **Duplicate of SA-SUB-004** | H | Same | |
| **SA-SUB-006** | H / V | **Confirmed** | H / V | `CancelBrandPlatformSubscription.cs:8,22-24`; `ApplyBundleToBrand.cs:100-107` (forces `active`); `ScopeResolver.cs:197-202` (`brand_feature` only) | |
| **SA-SUB-007** | H / V | **Confirmed** | H / V | `AdminEntitlements.cs:28-38`; `IdentitySeeder.cs:515-571` (no `saas.*` for `brand_admin`); `ApiAuthorizationResultHandler.cs:42` (`/settings?tab=plan`); `admin-web/src/pages/settings/SettingsPage.tsx:21-70` (no `plan` tab; `platform-payments` is `platformOnly`) | Cross-check with SA-AUTHZ-004: the only way an owner "reaches" billing today is the self-grant bypass. |
| **SA-API-001** | C / PV | **Confirmed** | C / PV (config and code; not run) | `core.WebApi/Program.cs:198-217` (partition on `Connection.RemoteIpAddress`), `:507,515`; `ServiceDefaults/Extensions.cs:267-287` (no-op unless `ForwardedHeaders:Enabled`); `deploy/docker-compose.yml:25,83-84` (off on services, only commented on the gateway); no `ForwardedHeaders` in any json or yml (grep); `Auth.cs:56` (whole group, **including `/refresh`** at L157) | In the shipped compose topology every auth call (login, OTP, refresh, signup) across all tenants shares one 10/min bucket. There is no alternative deployment manifest. Severity: I accept Critical as an availability launch-blocker. SA-OPS-001 rates the same defect High. |
| **SA-API-002** | H / V | **Confirmed** | H / V | `Gateway/RateLimitPartitioning.cs:24-31,34-38,80-86`; `Gateway/Program.cs:213-258` (`GlobalLimiter`), `:46-55` (only a `PathPattern` transform), `:290` | Brand ids are not secret: every customer token carries `brand_id`, so the targeted tenant DoS is practical. The existing unit tests **assert** the vulnerable behaviour (SA-QA-002). |
| **SA-API-003** | H / V | **Confirmed** | H / V (type-set diff reproduced) | `CQRS/Extensions/ServiceCollectionExtensions.cs:14`; `CQRS/Dispatcher/Dispatcher.cs:15-45` (no behaviours); `CommandDispatcher.cs` (has a behaviour chain but is never registered); `UploadInspectionPhoto.cs:53-65,89-112`; `UploadRiderDocument.cs:44-78,109-120` (MIME and size rules exist only in the dead validators) | The upload MIME and size checks are security-relevant, so I keep High. SA-ARCH-005 and SA-SOLID-005 rate it Medium. |
| **SA-API-004** | H / V | **Confirmed** | H / V (race not run) | `CreateOrderCommand.cs:71-94` (lookup outside the transaction), `:685-689` (string-interpolated JSON), `:757`; no unique index on order idempotency (grep `database_scripts`, `db/*`); no advisory, serializable or `FOR UPDATE` lock in `CreateOrderCommand` (grep) | SA-DB-011 rates the same defect Medium. |
| **SA-API-005** | H / V | **Confirmed** | H / V | grep `IsConcurrencyToken\|IsRowVersion\|ConcurrencyCheck\|xmin\|DbUpdateConcurrencyException` = 0 hits outside tests; `CustomerWalletHandlers.cs:180-185`; non-timestamp DB triggers: only `trg_brand_vertical_immutable` and `trg_check_refund_cap` | `Version++` is written but is not a concurrency token. |
| **SA-API-006** | H / V | **Confirmed** | H / V | `UpdateMyTaskStatus.cs:88-100` (leg status set with no transition check), `:148-176` (`delivered` with a hard-coded `FromStatus`, gated only on `DeliveredAt == null`); no order-status guard at L50-88 | |
| **SA-API-007** | H / V | **Confirmed** | H / V | `RazorpayWebhookHandler.cs:190-205` (`captured` only from `pending`), `:263-283` (`failed` is terminal); grep `AmountPaid`/`PaymentStatus` writes (only offline, COD, partner and royalty paths); no consumer updates the order on `payment.captured` (only `NotificationMappingService.cs:113,432`) | |
| **SA-API-008** | H / PV | **Confirmed – status corrected** | H / **V** | `OrderCancellationRefund.cs:57-93`; grep `PaymentRefunds` and `payment_refunds` across .cs and SQL: no pending-refund executor | The absence is established across all hosts and SQL. |
| **SA-API-009** | H / V | **Confirmed** | H / V | `AdminPaymentHandlers.cs:100-231` (cap computed outside the transaction; `InitiateRefundAsync` inside the lambda **before** the refund INSERT at L229-230); `CommerceDbContext.cs:103-115` (retrying strategy re-runs the lambda); `db/patches/payment_idempotency.sql:29-80` | **Mitigating control the specialist did not mention:** the DB trigger `commerce.check_refund_cap`. It (a) is racy under READ COMMITTED (reproduced by SA-DB-006), (b) fires *after* the Razorpay call, so a rejection rolls back the DB but not the money, and (c) is applied by **no** script (grep for `payment_idempotency` in `*.sh`, `README` and `yml`: none). The verdict stands. |
| **SA-API-012** | H / V | **Confirmed** | H / V | `NotificationSettingsCache.cs:30-62` (singleton; `FirstOrDefault` over all brands' rows with no ordering; the comment claims a platform preference the code does not implement); `RoutingChannelSender.cs:98-130` | |

**SA-AUTHZ-001 evidence trace** (all files read by QA):
- Endpoints and permissions: `AdminUsers.cs:33` (`permission:users.create`), `AdminAccessControl.cs:39` (`/invite`, `permission:users.create`), `InviteUser.cs:29-35`.
- Handler: `CreateUser.cs:31-33` (only `UserType.IsValid`), `:45-46` (type from the request, `Active` when a password is given), `:72` (committed). `UserType.cs:12,30-36` includes `platform_admin`.
- No validation in the pipeline: `Dispatcher.cs:15-29` and `ServiceCollectionExtensions.cs:14` (no behaviours, no validator); no `AbstractValidator<CreateUserRequest|InviteUserRequest>` exists.
- DB allows it: `02_bc2_identity_access.sql:31-33` (CHECK admits `platform_admin`); 0029 L121-122 (`WITH CHECK (true)`); no trigger on users (grep).
- Login and token: `PasswordLoginHandler.cs:60-111` (no membership requirement); `ScopeResolver.cs:27-49` (zero memberships is allowed), `:185-186` (platform_admin exempt from entitlement), `:260` (`user_type` claim).
- Runtime powers: `PermissionHandler.cs:32-33` (all permissions); `TenantResolutionMiddleware.cs:34-47` (`bypass_rls` + `X-Brand-Id`); `HttpContextCurrentTenant.cs:28` (feeds the interceptor).
- Anonymous entry: `Signup.cs:31-32` (anonymous); `CompleteSignup.cs:46,150,163-172` (`brand_admin` membership); `IdentitySeeder.cs:128` (`users.create` = high), `:521` (`brand_admin` holds it); `core.WebApi/Program.cs:470` (seeder runs on `--seed` or in Development).

### Medium findings checked (18)

| ID | Specialist sev/status | QA verdict | Corrected sev/status | Evidence QA read | Notes |
|---|---|---|---|---|---|
| SA-TEN-002 | M / V | **Confirmed** | M / V (S2b) | `CommerceHostCurrentTenant.cs:67-70` (`UserId=sub` for customer tokens, no `CustomerId`); `HttpContextCurrentTenant.cs:45-46` | **Stays live after 0031** on `wallet_accounts`, `wallet_transactions`, `loyalty_points_ledger`, `payment_refunds`, `customer_packages`, `package_usage_ledger` and `coupon_redemptions`, none of which is in the 0031 list. Canonical for the group. |
| SA-AUTHZ-005 | M / V | **Duplicate of SA-TEN-002** | M | same | |
| SA-TEN-005 | M / V | **Duplicate of SA-API-002** | → **H** | `RateLimitPartitioning.cs` | |
| SA-AUTHZ-010 | M / V | **Duplicate of SA-API-002** | → **H** | same | |
| SA-TEN-007 | M / V | **Confirmed** | M / V (S5, S7b) | `harden_app_user_and_rls_bypass.sql:31-38`; 0015 L265-294 | Overlaps SA-DB-015 (bypass GUC) and SA-DB-003 (DEFINER, High). |
| SA-TEN-009 | M / S | **Confirmed** (static) | M / PV | `Gateway/Program.cs:46-55` (only `PathPattern`; no `RequestHeaderOriginalHost`); `ServiceDefaults/Extensions.cs:273-276` (no `XForwardedHost`); compose L87-96 | Relies on YARP's documented default of sending the destination host. |
| SA-TEN-015 | M / V | **Confirmed** | M / V | `app_user_role.sql:23,34`; `harden_app_user_and_rls_bypass.sql:44,47` | |
| SA-SUB-005 | M / V | **Confirmed – minor correction** | M / V | `WorkerOptions.cs:138-140`; `docs/SAAS_PLATFORM_ARCHITECTURE.md:324` | "Undocumented" is overstated. The flag is named (opt-in) in the architecture doc, but not in the production env runbook. |
| SA-SUB-010 | M / V | **Duplicate of SA-AUTHZ-011** | M | grep `BrandFeatureGate\|RequireFeature`: only identity access-control files use it | |
| SA-AUTHZ-011 | M / V | **Confirmed** (canonical) | M / V | `PermVersionBumper.cs:36-44` (brand-scoped only); grep above | |
| SA-SUB-012 | M / V | **Confirmed** | M / V | `ApplyBundleToBrand.cs:108-140` | During a trial there is no invoice for the current period, so a full-price invoice for the trial window is issued, plus proration. |
| SA-SUB-020 | M / PV | **Confirmed** | M / V | `0008…up.sql:35`, `0021…up.sql:38-45` reference `brand_platform_*`; `db/build_from_scratch.sh` and `deploy/README.md` never call `apply_saas_billing_patches.sh` | Same group as SA-DB-002 and SA-ARCH-008. A fresh `migrate.sh up` would fail at 0008 or 0021 before reaching 0031. |
| SA-AUTHZ-007 | M / V | **Confirmed** | M / V | `ScopeResolver.cs:105-131` (permissions from ancestor-or-self memberships), `:168-170` (all membership nodes → `scope_nodes`) | |
| SA-AUTHZ-009 | M / V | **Confirmed** | M / V | `SetPersonStatus.cs:48-68` and `DeactivateUser.cs:14-23` (no `PermVersion` bump); `RefreshTokenHandler.cs:68`; `TenantResolutionMiddleware.cs:54-71` (fail-open); `appsettings.json` (`EnforceTokenVersion: true`) | |
| SA-API-013 | M / V | **Confirmed** | M / V | `NotificationMappingService.cs:432-433` | Duplicate group with SA-ONB-006. |
| SA-API-016 | M / V | **Confirmed** | M / V | `AdminUsers.cs:45-50` (`pageSize` has no upper bound) | Sampled one endpoint. |
| SA-API-020 | M / V | **Confirmed** | M / V (race not run) | `RefreshTokenHandler.cs:40-75` (read → check `RevokedAt` → set, with no conditional update) | |

### QA verdict counts (44 items)

| Verdict | Count |
|---|---|
| Confirmed | 36. Three of these had their status corrected (AUTHZ-004 and API-008 to Verified, TEN-001 to Verified at SQL level). Two had minor text corrections (SUB-002 line citation, SUB-005 "undocumented"). |
| Confirmed – severity corrected | 0 standalone. Severity corrections were applied through duplicates: SA-AUTHZ-006 → Critical; SA-TEN-005 and SA-AUTHZ-010 → High |
| Duplicate | 8 (TEN-004, AUTHZ-006, API-010, API-011, AUTHZ-005, TEN-005, AUTHZ-010, SUB-010) |
| Not reproduced | 0 |
| Rejected – false positive | 0 |

Every duplicate was also substantively confirmed.

## Dedupe groups

Canonical ID first. Severity is the QA recommendation.

| # | Group | Canonical | Members (report severity) | QA severity |
|---|---|---|---|---|
| G1 | Anonymous signup → `users.create` → platform_admin | **SA-AUTHZ-001** | (02's positive control "Self-signup is safe" contradicts it) | Critical |
| G2 | 0031 RESTRICTIVE policy denies customer, commerce and API-key lanes | **SA-TEN-001** | SA-AUTHZ-006 (H), SA-DB-001 (C) | Critical |
| G3 | Commerce adapter omits `customer_id` (and other subject GUCs) | **SA-TEN-002** | SA-AUTHZ-005 (M) | Medium |
| G4 | Auth limiter collapses to the gateway IP | **SA-API-001** | SA-OPS-001 (H) | Critical (OPS says High) |
| G5 | Gateway partition keyed on client-controlled brand / XFF | **SA-API-002** | SA-OPS-002 (H), SA-TEN-005 (M), SA-AUTHZ-010 (M), SA-OPS-014 (M, partly) | High |
| G6 | Membership grant to foreign users and identity-write takeover | **SA-AUTHZ-003** + **SA-AUTHZ-002** | SA-TEN-004 (H), SA-DB-005 (H, the RLS-off membership-table aspect), SA-DB-020 (L, global identity) | High |
| G7 | Self-grant / no platform check on platform-plane handlers | **SA-AUTHZ-004** | (SA-SUB-007 is a consequence) | High |
| G8 | Renewal pass outside the worker scope | **SA-SUB-004** | SA-API-010 (H) | High |
| G9 | `past_due` invoice cannot be paid by link or webhook | **SA-SUB-002** | SA-API-011 (H) | High |
| G10 | Entitlements not applied on customer, partner, API-key and worker lanes | **SA-AUTHZ-011** | SA-SUB-010 (M) | Medium |
| G11 | Notification worker uses another tenant's credentials | **SA-API-012** | SA-SOLID-007 (H) | High |
| G12 | Online capture never marks the order paid; failed → captured dropped | **SA-API-007** | SA-SOLID-002 (H) | High |
| G13 | Validators and pipeline dead | **SA-API-003** | SA-ARCH-005 (M), SA-SOLID-005 (M) | High |
| G14 | Refund cap race and gateway call inside the retried transaction | **SA-API-009** | SA-DB-006 (H) | High |
| G15 | No optimistic concurrency on balances and counters | **SA-API-005** | SA-DB-008 (H), SA-DB-010 (coupon) | High |
| G16 | POS order idempotency check-then-act | **SA-API-004** | SA-DB-011 (M) | High |
| G17 | Worker claims, leader election, multi-replica | **SA-API-014** | SA-DB-009 (M), SA-OPS-005 (H), SA-ARCH-007 (M), SA-OPS-015 | High before scale-out, otherwise Medium |
| G18 | Self-settable bypass GUC and trusting DEFINER functions | **SA-TEN-007** | SA-DB-015 (M), SA-DB-003 (H) | Medium (DB-003's purge/export surface could justify High) |
| G19 | Custom-domain resolution lost at the gateway | **SA-TEN-009** | SA-OPS-013 (M), SA-ONB-001 (H) | Medium |
| G20 | Schema not reproducible / SaaS billing patches off-path | **SA-DB-002** | SA-ARCH-008 (H), SA-SUB-020 (M), SA-VERT-010 (M), SA-OPS-007 (M) | High |
| G21 | Lifecycle gates are HTTP-only (workers, partner, fail-open) | **SA-TEN-008** | SA-SUB-018 (L), SA-SUB-017 (L), SA-AUTHZ-014 (L) | Medium |
| G22 | Vertical boundary not enforced at the API | **SA-AUTHZ-012** | SA-VERT-005 (M), SA-ONB-003 (H), SA-FE-006 (M) | Medium |
| G23 | Hard-coded "Laundry Ghar" in customer messages | **SA-API-013** | SA-ONB-006 (M) | Medium |
| G24 | Composite tenant FKs / global uniques | **SA-DB-004** / **SA-DB-007** | SA-TEN-011 (L), SA-TEN-012 (L) | Low–Medium |
| G25 | Analytics MVs app-only | **SA-DB-013** | SA-TEN-013 (L) | Low |
| G26 | Isolation tests miss the real runtime path | **SA-TEN-010** | SA-ARCH-010 (no commerce test project), SA-QA-002 | Medium |
| G27 | Billing lifecycle: trials never end / `past_due` never returns to active / worker off by default | SA-SUB-001, 003, 005 stay **distinct** | Different root causes; fix them together. | — |

## Contradictions

1. **Self-signup safety.** `02` lists "Self-signup is safe" as a positive control. `06` SA-AUTHZ-001 shows that self-signup gives an anonymous party `brand_admin` with `users.create`, and from there a platform takeover.
   - QA: the signup handler itself is safe (generated code, OTP-first). The capability it grants is not. 02's positive control should be scoped down.
2. **Severity of the 0031 outage.** SA-TEN-001 and SA-DB-001 say Critical; SA-AUTHZ-006 says High. QA: Critical.
3. **Severity of the auth-limiter collapse.** SA-API-001 says Critical; SA-OPS-001 says High.
4. **Severity of the gateway partition.** SA-API-002 and SA-OPS-002 say High; SA-TEN-005 and SA-AUTHZ-010 say Medium. QA: High.
5. **Severity of dead validation.** SA-API-003 says High; SA-ARCH-005 and SA-SOLID-005 say Medium.
6. **Other severity mismatches:**
   - Vertical boundary: SA-ONB-003 High vs Medium elsewhere (G22).
   - Custom domain: SA-ONB-001 High vs Medium (G19).
   - Workers: SA-OPS-005 High vs Medium (G17).
   - Order idempotency: SA-API-004 High vs SA-DB-011 Medium.
   - Workers ignoring suspension: SA-TEN-008 Medium vs SA-SUB-018 Low.
7. **`08` Q9: "Tenant context, RLS wiring … are solid".** This contradicts SA-TEN-001/002 (commerce adapter mis-wired; reproduced) and SA-DB-001.
8. **DB-Q8 verdict.** `02` says "Fully Supported for EF-opened connections"; `08b` and `11` say "Partially Supported". QA sides with Partially (see the verdict challenges).
9. **Line citation.** `03` SA-SUB-002 cites `ProcessPaylinkWebhook.cs:L109-L115`; the status check is at L71-72. `08` SA-API-011 is correct.
10. **SA-SUB-005 "undocumented"** versus `docs/SAAS_PLATFORM_ARCHITECTURE.md:324`, which names the opt-in flag.
11. **SA-TEN-001 impact text** lists wallets, analytics and partner billing as denied. Those tables are not in 0031's list (see the SA-TEN-001 row).
12. **Prior docs contradicted by code:**
    - `docs/ABAC_AUDIT_2026-08-31.md` §3 says self-promotion was refused. That covered `set-type` only; create and invite are unguarded.
    - `docs/FIX_REPORT.md` A-6 says "every legitimate scope level unchanged". S1-S3 contradict it.
    - Migration 0029's header says "a users row grants no authority on its own". `user_type` alone grants total authority.
    - `docs/GAP_ANALYSIS.md` E2 marks brand subscription "Done".
13. **Question numbering drifts between reports.**
    - Q2: "onboarding/lifecycle" in 02, "tenant isolation" in 06.
    - Q3: "context resolution" in 02, "isolation" in 06.
    - Q7: "new verticals without modification" in 01/07, "one vertical cannot reach another" in 04.
    - Q8: "branding" in 02/09, "exactly one business type" in 05.
    - Q13: "tenant restrictions" in 02, "entitlements" in 03/06, "restrictions client-side?" in 09.

    The registry must map verdicts by meaning, not by number.

## New findings

### SA-QA-001 — InviteUser is not atomic: the user is committed before the membership guards run
- Category: Privilege escalation / workflow integrity. Related area: AUTHZ.
- Severity: Medium (High while SA-AUTHZ-001 is open)
- Status: Verified (code read; not executed)
- Evidence:
  - `core.Application/Identity/AccessControl/Commands/InviteUser/InviteUser.cs:29-35` dispatches `CreateUserCommand`, then `GrantMembershipCommand`.
  - `CreateUser.cs:72` calls `SaveChangesAsync` itself.
  - `laundryghar.Utilities/CQRS/Dispatcher/Dispatcher.cs:15-29` has no transaction behaviour, and `TransactionBehavior` is never registered (`ServiceCollectionExtensions.cs:14`).
  - `GrantMembership.cs:144-185` throws on scope or rank violations only *after* the user row exists.
- Observed behaviour: `POST /api/v1/admin/access-control/invite` with a role or scope the actor may not grant returns 403. The account with the requested `user_type` and password (`Status=Active`) is nonetheless already committed. Because `user_type=platform_admin` needs no membership to receive full authority (`PermissionHandler.cs:32-33`, `ScopeResolver.cs:27-49`), GrantMembership's H2a, H2b and H2c guards give the invite path no protection at all. In the benign case, failed invites leave orphan accounts.
- Reproduction: integration test on `RbacEfFixture`. A store admin invites with `RoleId` set to a brand-level role and `UserType=platform_admin`; expect 403; assert no `users` row exists. Today, by trace, one exists.
- Impact: it widens SA-AUTHZ-001. It also means "the membership guard protects user creation" is not true anywhere.
- Recommended remediation: wrap InviteUser in `ExecuteInTransactionAsync` (or validate the grant before creating), and apply the SA-AUTHZ-001 type ceiling inside `CreateUserCommandHandler`.
- Regression tests: an invite rejected by the grant guard leaves no user row; an invite with a forbidden `user_type` is refused before any write.
- Dependencies / priority: P0 together with SA-AUTHZ-001.

### SA-QA-002 — Gateway rate-limit unit tests assert the vulnerable behaviour
- Category: Test quality / regression lock-in. Related area: OPS, TEN.
- Severity: Low
- Status: Verified (read)
- Evidence: `backend/laundryghar/tests/operations.Tests/Auth/RateLimitPartitioningTests.cs`:
  - `:63-69` `the_x_brand_id_header_takes_precedence`
  - `:71-77` `the_forwarded_client_ip_is_used_when_present` (leftmost XFF trusted)
  - `:27-37` `two_brands_on_the_same_ip_do_not_share_a_budget`
- Observed behaviour: the suite encodes exactly the bypass and tenant-DoS properties in SA-API-002. A correct fix will turn these tests red, which invites someone to "fix the fix" back.
- Impact: false assurance; the regression is locked in.
- Remediation: replace them with negative tests (see the next section, T9). Mark the old ones as documenting the defect until the fix lands.
- Dependencies / priority: P1, with SA-API-002.

(I considered two more candidates and did not raise them: the refund-cap trigger's apply path is covered by SA-API-009, SA-DB-006 and SA-DB-002; the residual fail-open on wallet and loyalty tables after 0031 is a correction to SA-TEN-001/002, not a new root cause.)

## Missing negative tests

Paths are relative to `backend/laundryghar/tests/`. "(new)" means a new file.

| # | Negative test | Guards | Where it should live |
|---|---|---|---|
| T1 | brand_admin, franchise_owner or store_admin calls `CreateUser` or `InviteUser` with `userType=platform_admin` (and a store_admin with `brand_admin`) → refused, no row written | SA-AUTHZ-001, SA-QA-001 | `operations.IntegrationTests/Rbac/UserCreationEscalationTests.cs` (new, on `RbacEfFixture`) |
| T2 | Token mint for a `user_type=platform_admin` user whose membership is not platform-scoped → no bypass, no all-permissions | SA-AUTHZ-001 defence in depth | `operations.IntegrationTests/Rbac/ScopeResolverTests.cs` |
| T3 | `GrantMembership` targeting a user with no membership in the actor's brand → 403; `IsPrimary` does not touch other brands' memberships | SA-AUTHZ-003 | `operations.IntegrationTests/Rbac/GrantMembershipTargetTests.cs` (new) |
| T4 | store_admin calls `SetPersonStatus activate`, `UpdateUser` email/bank, or `Deactivate` on brand_admin or another store's staff → refused | SA-AUTHZ-002 | same fixture, `IdentityWriteTargetGuardTests.cs` (new) |
| T5 | Override granting a code the actor lacks (`saas.manage`, `brands.create`) → refused. `SetBrandFeature`, `ApplyBundle`, `SetBrandPlatformInvoiceStatus` as a non-platform caller → 403 | SA-AUTHZ-004 | `operations.IntegrationTests/Rbac/EntitlementEnforcementTests.cs` |
| T6 | 0031 with customer, `customer_mcp`, `api_key` and commerce-adapter sessions: own rows visible, others not; audit insert succeeds | SA-TEN-001 | `operations.IntegrationTests/Rbac/SubBrandScopeRlsTests.cs` |
| T7 | `CommerceHostCurrentTenant` and `HttpContextCurrentTenant` publish identical GUCs for the same principal (run through the real `RlsConnectionInterceptor`) | SA-TEN-001/002 | `operations.Tests/Auth/CurrentTenantAdapterParityTests.cs` (new) |
| T8 | Commerce customer c1 cannot read c2's `wallet_accounts`/`payments` at the RLS level | SA-TEN-002 | `operations.IntegrationTests/Rbac/RlsIsolationTests.cs` |
| T9 | A header-only `X-Brand-Id` does not leave the IP bucket; XFF is ignored from an untrusted hop | SA-API-002 | `operations.Tests/Auth/RateLimitPartitioningTests.cs` (replace L63-77) |
| T10 | Two clients behind a trusted proxy get independent auth partitions; `/refresh` is not throttled by logins | SA-API-001 | no core WebApi test host exists; `core.Tests/Auth/AuthRateLimitPartitionTests.cs` (new, `WebApplicationFactory`) |
| T11 | A suspended (`tos`/`manual`/`nonpayment`) brand cannot cancel then withdraw back to `active` | SA-TEN-003 | `operations.IntegrationTests/Rbac/BrandCancellationTests.cs` |
| T12 | Renewal pass under `app_user` issues an invoice; trial → conversion or `past_due`; `past_due` → paid → subscription `active` | SA-SUB-001/003/004 | `operations.IntegrationTests/Rbac/BrandDunningTests.cs`, or a new commerce test project (SA-ARCH-010) |
| T13 | `payment_link.paid` for a `past_due` invoice → paid, then reinstated on the next pass | SA-SUB-002 | `core.Tests/Identity/ProcessPaylinkWebhookTests.cs` (new) |
| T14 | Razorpay `failed` → `captured` ends captured, and the order `amount_paid` is updated | SA-API-007 | new `commerce.Tests` project |
| T15 | Two brands with distinct WhatsApp/SMS credentials → each send uses its own brand's credentials | SA-API-012 | new `commerce.Tests` project |
| T16 | Customer endpoint for an unlicensed feature → 402; franchise/store staff lose a feature within the TTL after a downgrade | SA-AUTHZ-011 | `operations.IntegrationTests/Rbac/EntitlementEnforcementTests.cs` |
| T17 | Laundry brand calls a salon or parcel endpoint → 403/402 | SA-AUTHZ-012 | `operations.Tests/Auth/ScopeBoundaryTests.cs` |
| T18 | Suspended user's existing access token → 401 within the version TTL | SA-AUTHZ-009 | `operations.Tests/Auth/` (new `TokenVersionRevocationTests.cs`) |
| T19 | store role at S1 + brand read-only role → write to S2 refused | SA-AUTHZ-007 | `operations.Tests/Auth/ScopeBoundaryTests.cs` |
| T20 | Architecture test: every `AbstractValidator<T>` is reachable (pipeline or `ValidationFilter<T>`) | SA-API-003 | `core.Tests/Configuration/` (new `ValidatorReachabilityTests.cs`) |
| T21 | Parallel POS CreateOrder with the same key → one order; parallel refunds respect the cap | SA-API-004/009 | `operations.IntegrationTests` (Testcontainers) |

## Verdict challenges

| Q (by meaning) | Specialists | QA position |
|---|---|---|
| **Q1** Genuine multi-tenancy with real isolation | 02: Partially; 01: Partially | **Partially agree on mechanism, disagree on the security property.** Brand-claim + RLS + predicate isolation is real (S4 control). But an anonymous party can become platform admin (SA-AUTHZ-001, chain traced, DB step reproduced) and bypass all of it. Isolation as a guarantee is **Not Supported** until G1 and G6 are fixed. |
| **Q2** Tenant onboarding and lifecycle enforced server-side (02's meaning) | 02: Partially | **Agree.** Signup provisioning is real. Suspension is self-reversible (reproduced, S7), and the billing lifecycle is broken (G8, G9, G27). |
| **Q3** Tenant context resolution unspoofable | 02, 06: Partially | **Agree, with a caveat.** The `X-Brand-Id` override is gated on `user_type=platform_admin`, which SA-AUTHZ-001 lets an attacker obtain. So "unspoofable" holds only while G1 is closed. The commerce adapter divergence is reproduced (S2, S2b, S3). |
| **Q4** Plans, billing lifecycle and entitlements | 03: Partially | **Billing lifecycle: Not Supported**, which is stronger than 03. Under `app_user` no renewal is ever issued (SUB-004), trials never end (SUB-001), `past_due` cannot be paid (SUB-002), and the worker is off by default (SUB-005). Every recurring revenue path is non-functional. **Entitlement enforcement: Partially**: staff tokens yes, other lanes no, and self-grantable (G7). |
| **Q11** RBAC consistently enforced on the backend | 06: Partially | **Agree, Partially** as a description, but it is a P0 blocker. Every endpoint is permission-gated (verified for AdminUsers, AdminAccessControl, AdminEntitlements and AdminCancellation), yet three identity-write handlers lack ceilings (G1, G6, G7). |
| **Q12** ABAC real and server-side | 06: Not Supported | **Agree.** Not re-verified in depth. The engine is default-off per 06, and the hand-coded scope checks are amplified (SA-AUTHZ-007, confirmed). |
| **Q13** Entitlements and restrictions enforced server-side | 02, 03, 06: Partially; 09: "partially client-only" | **Agree, Partially.** Staff-lane token stripping is server-side. Customer, partner, API-key and worker lanes are not (G10). Suspension is self-reversible and HTTP-only (G21). The vertical boundary is client-only (G22). |
| **DB-Q7** Can the app role bypass RLS? | 08b: Partially (bypass possible) | **Agree, reproduced.** S5: `app_user` sets `app.bypass_rls=true` itself and reads another brand's rows. S7b: a DEFINER function accepts a foreign brand id. |
| **DB-Q8** Tenant context safe with pooled connections | 02: Fully (EF); 08b, 11: Partially | **Side with Partially.** Static only: the interceptor writes all 12 GUCs on each open (`RlsConnectionInterceptor.cs`). Pooling-on is never tested (02's own note: the fixture turns pooling off). Raw connections (ABAC store per SA-DB-014; `BrandExportService`) skip the interceptor, and the design rules out transaction-mode pooling. |
| **DB-Q10** Isolation at both the app and DB layers | 02, 08b: Partially | **Agree.** Both layers exist for brand tables, but: `user_scope_memberships` has RLS off (exploited by G6); customer-vs-customer RLS on commerce is fail-open (S2b) and survives 0031 on wallets and loyalty; the sub-brand layer fails closed for whole principal classes (S1-S3). |

## Not verified
- No HTTP-level execution of any chain, including AUTHZ-001's login, step-up and token issuance. That chain is static only, except its DB insert step (S6).
- Step-up OTP delivery on `users.create` and `permissions.assign` was not exercised. I assume the OTP goes to the actor's own verified phone, per 06.
- Whether 0031, `payment_idempotency.sql` or `apply_saas_billing_patches.sh` are applied in any deployed environment, and whether production uses `app_user`.
- Concurrency races (SA-API-004/005/009/020) were not run here. SA-DB-006 reports a reproduced over-refund, which I did not repeat.
- YARP default host and forwarded-header behaviour (SA-TEN-009) was taken from documented defaults, not executed.
- Medium findings not sampled: SA-TEN-006, 008, 010; SA-SUB-008, 009, 011, 013, 014, 015; SA-AUTHZ-008, 012; SA-API-014, 015, 017, 018, 019, 021.
- Production role-permission data: the brand_admin grants come from `IdentitySeeder` (run on `--seed` or in Development). I did not confirm a deployed DB has `users.create` / `permissions.assign` on `brand_admin`, though no SQL patch revokes them.
