# 07 — Prioritized Remediation Roadmap

Audit date: 2026-10-09 · Inputs: [FINDINGS](../../FINDINGS.md) and [`findings-registry.json`](findings-registry.json) (canonical IDs, final severities, `phase`), the architect's dependency-ordered roadmap and root causes RC1–RC11 ([01b §3, §5](specialists/01b-architect-challenge-review.md)), the specialist remediations ([01](specialists/01-architecture.md)–[12](specialists/12-mobile-delivery-maps.md)), and the QA verification reports ([10a](specialists/10a-qa-verification-security.md), [10b](specialists/10b-qa-verification-platform.md), [10c](specialists/10c-qa-verification-db-mobile.md)). The test plan that backs every acceptance criterion here is [08 — Test Strategy](08-test-strategy.md).

## Summary

- The registry holds **158 canonical findings**: 3 Critical, 40 High, 79 Medium, 34 Low and 2 Informational, including the two consolidation IDs SA-ORC-001/002. This roadmap groups them into **45 tasks across six phases**. Every Critical, High and Medium finding, and every P0/P1 finding, maps to a task (see the [coverage check](#coverage-check)).
- **Phase 0 (12 tasks)** closes all 27 registry-P0 findings. The three Criticals are the anonymous-signup → `platform_admin` takeover (SA-AUTHZ-001), the migration-0031 lane outage (SA-TEN-001) and the platform-wide auth throttle (SA-API-001). Phase 0 also closes the cross-tenant notification credentials, the broken money paths, ephemeral uploads and the partition runway.
- **Production foundations come before billing**, not last. CI-gated releases, a reproducible schema, a worker host with claim locks and a concurrency model are prerequisites for the billing, vertical and white-label work. Only *scale-out* hardening stays in Phase 5. This differs from the order the user suggested, and the [phase table](#phases-and-deviations-from-the-suggested-names) explains why.
- **The first two weeks follow a fixed order.** Verify production state, then close the takeover chain, then fix the rate limiter and notification credentials, then the money paths. The 0031 customer-lane fix and the MOB-005 address-ownership fix ship **together**, last, because restoring the customer lane makes several latent defects live ([§ first two weeks](#2-phase-0-first-two-weeks-dependency-order)).
- **Estimates.** Complexity is a relative **S/M/L/XL** label. This roadmap gives no dates or effort numbers. Where the architect gave a cost, it is quoted as *architect estimate* ([01b §4](specialists/01b-architect-challenge-review.md)).
- **Verification limits.** The audit executed no HTTP requests and no .NET or Testcontainers tests (there was no .NET SDK and no Docker). SQL claims were reproduced on throwaway PostgreSQL 16 clusters. Phase 0 therefore **starts** with a production-state check (task 0.1) that decides which defects are live outages and which are latent release blockers.

---

## 1. How the roadmap was built

### Phases and deviations from the suggested names

| Phase | Name used here | User's suggested framing | Deviation and reason |
|---|---|---|---|
| 0 | **Verification & critical risks** | Phase 0, critical risks | Adds an explicit *verification* task (0.1). No HTTP or .NET path was executed, and the production patch, migration and `part_config` state is unknown ([01b open questions](specialists/01b-architect-challenge-review.md), [08b open questions](specialists/08b-database.md)). Phase 0 also carries the **data-loss** fixes (storage, partitions, backups) and freezes the schema bootstrap. These are production-hardening items, but they are time-bound or destructive. The partition runway documented in `db/HANDOFF.md` ends on 2026-12-01; that date was not measured ([SA-OPS-004](../../FINDINGS.md#sa-ops-004)). |
| 1 | **Production / SaaS foundation** | "Phase 1 SaaS foundation" | It includes the production foundations the user had placed in "Phase 5 production hardening": CI-gated release, a schema baseline with a CI schema build, a worker host with claim locks, a concurrency model, telemetry and the secrets path. The architect put these first because billing (Phase 2), vertical modules (Phase 3) and white-label (Phase 4) all depend on them ([01b §5](specialists/01b-architect-challenge-review.md) "Depends on" column). Examples: the renewal worker cannot be trusted without claim locks (SA-DB-009); vertical refactors cannot be verified without a reproducible schema (RC1). |
| 2 | **Subscription & entitlement correctness, plus onboarding prerequisites** | "Phase 2 Subscription & onboarding" | Splits onboarding. Its **backend prerequisites** stay here: plan choice, failing loudly on a missing bundle, GSTIN to invoices. The **experience** (signup UI, branding, domains, provisioning service) moves to Phase 4, because it depends on Phase 3's vertical seam and on Phase 2's subscription → entitlement projection. |
| 3 | **Modular verticals, and dispatch/location boundaries** | (verticals) | Adds the dispatch and location modules. They share the order spine and the `OrderTransitionService` with the vertical seam ([01b §4.6–4.7](specialists/01b-architect-challenge-review.md)). |
| 4 | **White-label experiences** | (white-label) | No deviation. |
| 5 | **New verticals & production hardening at scale** | "Phase 5 production hardening" | Keeps only the hardening that is **scale-driven** and should wait for measured need: multi-replica caches and limiters, PgBouncer-compatible tenant context, RLS predicate performance (SA-OPS-015, SA-DB-016, SA-DB-017). All other production hardening moved to Phases 0–1, as explained above. |

### Placement deviations from the registry `phase` field

The registry `phase` is kept for every finding except those below. Where a finding is split across phases, the coverage table lists both tasks.

| Finding | Registry phase | Placed in | Reason |
|---|---|---|---|
| SA-QB-003 (ForwardedHeaders guidance) | P1 | **0.4** | Its own priority says "P1, before the SA-API-001 fix", and SA-API-001 is P0. The two are one change. |
| SA-DB-007 (global business-number uniques) | P3 | **1.6** | It shares the fix (per-brand counter plus `(brand_id, number)` unique) with SA-QC-002 (P1). QC-002 becomes more frequent once the commerce lane is restored ([10c](specialists/10c-qa-verification-db-mobile.md) SA-QC-002). |
| SA-FE-004 (admin-web hard-coded laundry transitions) | P3 | **1.11** (client switch) + 3.2 | QA-B found it live today: parcel orders cannot be advanced past `picked_up` from admin-web ([10b](specialists/10b-qa-verification-platform.md)). The server already returns `allowedTransitions` (`OrderDtos.cs:131-137`) and POS uses it, so the client fix does not have to wait for 3.2. |
| SA-OPS-006, SA-QB-002, SA-ARCH-010 | P1 | **0.2** (partial) + 1.2 / 1.1 / 1.10 | Phase 0 regression tests only protect anything if (a) release depends on them, (b) they cannot silently pass without Docker, and (c) commerce has a test project to host the payment and notification tests (10a T14/T15 target a "new `commerce.Tests` project"). |
| SA-SOLID-010 (god-handlers untested) | P3 | **1.10** (tests) + 3.2 (refactor) | Its priority reads "P1 for tests, P2 for refactor". The architect requires parity tests before the order-transition consolidation touches the money path ([01b §4.6](specialists/01b-architect-challenge-review.md)). |
| SA-ARCH-013, SA-ARCH-014, SA-DB-002 | P0 | 0.3 / 0.7 / 0.11 + **1.3 / 1.3 / 1.1** | Their priorities split them: the P0 part (trigger and derivation; customer and commerce lanes; freezing the bootstrap) goes in Phase 0, and the P1 part (audience split; resolver consolidation; schema baseline) goes in Phase 1. |
| SA-SOLID-003 + SA-QB-001 (royalty) | P2 | 2.4, **pull into Phase 0 if royalty generation is enabled in production** | The registry priority is "P0 for franchise billing". QA-B notes that the royalty worker is off by default and that a manual override exists. Task 0.1 decides which applies. |

### Root causes by phase

| Root cause ([01b §3](specialists/01b-architect-challenge-review.md)) | Phase 0 (stop the bleeding) | Structural fix |
|---|---|---|
| RC1 No single schema source of truth | 0.11 freeze bootstrap | 1.1 baseline + CI schema build |
| RC2 Opt-in cross-cutting policy (dead pipeline) | — | 1.7 dispatcher pipeline; 2.2 `RequireFeature` metadata |
| RC3 Anemic model; duplicated invariants | 0.8 leg state machine | 3.2 `OrderTransitionService`; 3.4 dispatch module; 3.7 coupon policy |
| RC4 No concurrency model | 0.6 refund cap lock | 1.6 tokens and partial uniques |
| RC5 Workers co-hosted, no locks, no uniform scope | 0.5 brand-keyed credentials | 1.8 worker host; 2.1 billing worker scope |
| RC6 Identity plane trusts client attributes; platform power is one column | 0.3 takeover chain + DB trigger | 1.3 control-plane audience; 1.4 identity RLS |
| RC7 Tenant context per host and per lane | 0.4 rate limiter; 0.7 0031 lanes | 1.3 one resolver; 1.5 verified-claim rate keys |
| RC8 Subscription is not the source of entitlements | 0.3 (SA-AUTHZ-004 self-grant) | 2.1 + 2.2 projection |
| RC9 Vertical as metadata, not modules | 0.12 hide inoperable templates | 3.3 vertical seam; 5.1 salon module |
| RC10 Single-node / developer-machine infrastructure | 0.10 storage, partitions, backups | 1.8, 1.12; 5.3 scale-out |
| RC11 Documentation ahead of code | — | 1.2 (correct `PRODUCTION_SPEC.md`, `PRODUCTION_ENV.md`) |

RC1, RC6 and RC7 explain every Critical finding ([01b §3](specialists/01b-architect-challenge-review.md)). That is why the Phase 0 order below starts with RC6 (takeover) and ends with RC7 (lanes).

### Reuse the patterns that already work

The fixes below should copy controls that the audit verified, not invent new ones:
- **Idempotency.** The customer pickup flow: a partial unique index plus 23505 mapped to the existing row, and an atomic CHECK-guarded slot increment (`PickupCommands.cs:338-510`; [08b](specialists/08b-database.md)).
- **Locking.** The partner wallet's `FOR UPDATE` (`CommerceDbContext.cs:111-125`).
- **Event consumption.** The inbox consumer on `outbox_consumed_events` (`PartnerBookingDebitService.cs`).
- **Counters.** The atomic order-number counter (`order_lifecycle.next_order_number()`).
- **State transitions.** The fulfilment-strategy seam where it is used (`UpdateOrderStatusCommand.cs:53-68`).
- **Tenant context.** The pool-safe RLS interceptor, which writes all 12 GUCs on every open (`RlsConnectionInterceptor.cs:57-121`).
- **Worker trust.** The worker RLS bypass, granted only by a positive marker (`CommerceHostCurrentTenant.cs:80-90`).
- **Migrations.** The transactional migration tool with checksums (`db/tools/migrate.sh`). Its 0025–0033 down/up round trip restored identical policies ([08b](specialists/08b-database.md)).

### Task fields

Each task lists the finding IDs it closes (canonical, with duplicates named), code areas, dependencies, complexity, risk and priority, acceptance criteria, required tests (named in [08](08-test-strategy.md)), and rollback or migration notes. Backend paths are relative to `backend/laundryghar/` unless they start with a top-level repo folder (`db/`, `deploy/`, `admin-web/`, `.github/`, …). **Risk** is the risk of deferring the task; **change risk** is the regression risk of making it.

---

## 2. Phase 0 first two weeks (dependency order)

The five items requested, in dependency order, followed by the work that runs in parallel. **No dates are implied.** "Week 1" and "week 2" express ordering only.

| Step | Task | Why it sits here |
|---|---|---|
| 0 | **0.1 production-state verification** and **0.2 test/release harness** | 0.1 decides whether SA-TEN-001 is a live outage (is 0031 applied, and are customers live?), how much partition runway is left, whether `kernel.rls_bypass()` is the hardened version, and whether any unexpected `platform_admin` rows exist (forensics for SA-AUTHZ-001). 0.2 makes the regression tests of every later step binding. |
| 1 | **0.3 takeover chain** (SA-AUTHZ-001 + SA-QA-001 + the SA-ARCH-013 DB trigger first; then SA-AUTHZ-003 → 002, then 004) | This is the highest severity. While it is open, no isolation claim holds: an anonymous signup can become a platform admin who bypasses RLS everywhere ([01b §2.1](specialists/01b-architect-challenge-review.md), M9). It depends on nothing else. As an interim compensating control while it is in review, the operator may consider pausing anonymous self-signup (`Signup.cs:31-32`). That is a judgement call, not an audit finding. |
| 2 | **0.4 auth rate limiter** (SA-API-001 + SA-QB-003) | It is independent and small (S), so it can run in parallel with step 1. It must land **before step 5**: restoring the customer lane sends every customer OTP and login through the one 10-requests-per-minute bucket shared by all tenants. |
| 3 | **0.5 brand-keyed notification credentials** (SA-API-012) | It is independent and small. It must land **before step 5**: once customers can place orders again, order notifications start flowing, and today each one is sent with one arbitrary tenant's WhatsApp/SMS credentials (a cross-tenant data flow, [01b §2.2](specialists/01b-architect-challenge-review.md)). |
| 4 | **0.6 money paths** (SA-API-007, SA-API-008, SA-API-009/SA-DB-006, SA-QC-001) | Must land **before or with step 5**. Restoring the commerce lane makes the refund race and the refund-contract defect reachable by brand staff; today only platform admins reach them ([10c](specialists/10c-qa-verification-db-mobile.md) SA-DB-006 and SA-QC-001 rows). Restoring the customer lane lets customer cancellations queue refunds that are never executed (SA-API-008). |
| 5 | **0.7 0031 customer/commerce lanes + SA-MOB-005 address ownership, in one release** (with SA-TEN-002 and SA-DB-012) | It must ship as one unit. QA-C showed the address IDOR is blocked today **only** by the 0031 outage and "becomes live the moment 0031 is fixed" ([10c](specialists/10c-qa-verification-db-mobile.md) SA-MOB-005). The same adapter fix also closes SA-TEN-002, where a commerce-host customer saw another customer's wallet (T1b). |

**If 0.1 shows the 0031 outage is live in production with real customers**, compress the order: ship step 5 immediately after step 1, together with step 2. Keep the admin refund endpoint restricted to platform operators until step 4 lands. Do **not** ship step 5 without SA-MOB-005.

**Parallel tracks in the same two weeks** (different owners):
- **Ops track:** 0.10 durability (partition `part_config` fix → scheduler → storage → PITR), 0.11 bootstrap freeze, 0.12 admin-web image and template visibility.
- **Mobile/dispatch track:** 0.8 leg state machine. Then the rider offline-queue fix in 1.9, because the new 409 responses will poison today's queue (SA-MOB-008).
- **DB track:** 0.9 suspension self-lift and DEFINER checks.

---

## 3. Phase 0 — Verification & critical risks

**Goal:** stop takeover, cross-tenant data flow, outages and money or data loss. **Exit gate:** [08 §5, Phase 0](08-test-strategy.md#5-acceptance-criteria-per-roadmap-phase-exit-gates).

### 0.1 Production-state verification and forensics
- **Findings addressed:** none closed. This task decides whether SA-TEN-001, SA-DB-002, SA-DB-003, SA-DB-012, SA-OPS-004, SA-QC-003, SA-OPS-010 and SA-AUTHZ-001 are live in production or latent.
- **Checks** (read-only; drawn from the "not verified" lists in [01b](specialists/01b-architect-challenge-review.md), [08b](specialists/08b-database.md), [10a](specialists/10a-qa-verification-security.md), [10c](specialists/10c-qa-verification-db-mobile.md)):
  1. `public.schema_migrations`: is 0031 applied, and do customers use the deployment?
  2. The body of `kernel.rls_bypass()`: does it accept `'true'`?
  3. `has_function_privilege('app_user','kernel.purge_brand(uuid)','EXECUTE')`.
  4. `pg_policies` for `custident_tenant`.
  5. `partman.part_config` parent tables, and the actual partition runway.
  6. Whether `db/patches/payment_idempotency.sql` (the refund-cap trigger) is applied.
  7. Inventory `identity_access.users WHERE user_type='platform_admin'` against known operators, and review `login_history` for those accounts.
  8. Whether `brand_admin` holds `users.create` and `permissions.assign` in the deployed DB.
  9. Commerce replica count.
  10. Whether `Worker__BrandPlatformBillingEnabled` and royalty generation are on.
  11. Last successful backup and last test restore.
  12. Whether any salon or tiffin brands already exist.
- **Code areas:** none (database and deployment inspection).
- **Dependencies:** none. **Complexity:** S. **Risk / priority:** P0. Every later sequencing decision depends on it.
- **Acceptance criteria:** a written answer to each check, and each listed finding re-labelled *live* or *latent*. Any unexplained `platform_admin` row is escalated as a possible incident before 0.3 ships.
- **Required tests:** none (inspection only).
- **Rollback / migration:** read-only. The schema-only dump taken here is the candidate baseline for 1.1.

### 0.2 Make Phase 0 regression tests binding
- **Findings addressed:** partial **SA-OPS-006** (release gated on the backend and admin-web CI jobs), partial **SA-QB-002** (integration tests fail instead of passing when Docker is missing in CI), partial **SA-ARCH-010** (scaffold `tests/commerce.Tests`). Full closure is in 1.2, 1.1 and 1.10.
- **Code areas:** `.github/workflows/release.yml:7-10,20-81`, `.github/workflows/ci.yml`; the `if (!_dockerAvailable) return;` pattern in `tests/operations.IntegrationTests/**` (60 occurrences in 17 files per [10b](specialists/10b-qa-verification-platform.md)); new `tests/commerce.Tests/`.
- **Dependencies:** none. **Complexity:** S–M. **Risk / priority:** P0 enabler. Today every CI run on `main` failed or was cancelled while every Release run succeeded ([11](specialists/11-devops.md)).
- **Acceptance criteria:**
  - a commit whose backend tests fail produces no release image;
  - with `CI=true` and no Docker, the integration suite fails;
  - `commerce.Tests` builds and runs in CI.
- **Required tests:** "MIG-9 no-early-return meta-test" ([08 §3.13](08-test-strategy.md#313-db-migrations-and-rollback)).
- **Rollback / migration:** workflow-only and revertible. Mobile CI stays non-blocking until 1.2 fixes the two known failures (rider `npm ci` ERESOLVE; TS2882).

### 0.3 Close the platform-takeover chain
- **Findings addressed:**
  - **SA-AUTHZ-001** (Critical);
  - **SA-QA-001**;
  - **SA-ARCH-013**, P0 part: server-derived `user_type` plus a DB trigger that allows `platform_admin` writes only from a platform or bypass role;
  - **SA-AUTHZ-003** (closes SA-TEN-004);
  - **SA-AUTHZ-002**;
  - **SA-AUTHZ-004**.
- **Code areas:**
  - `core.Application/Identity/Users/Commands/CreateUser/CreateUser.cs:22-55`;
  - `core.Application/Identity/AccessControl/Commands/InviteUser/InviteUser.cs:29-35`;
  - `GrantMembership.cs:42-225`, `SetPersonStatus.cs:24-68`, `UpdateUser.cs:18-75`, `DeactivateUser.cs:14-23`;
  - `SetUserPermissionOverride.cs:37-99`, `SetRoleCells.cs:43-106`;
  - `core.Application/Identity/Entitlements/Commands/{SetBrandFeature,ApplyBundleToBrand,SetBrandPlatformInvoiceStatus,CancelBrandPlatformSubscription}.cs`;
  - `SharedDataModel/Enums/UserType.cs`;
  - a new migration in `db/migrations/` (trigger on `identity_access.users`; see `0029_users_brand_rls.up.sql:122`, `WITH CHECK (true)`).
- **Order inside the task:**
  1. AUTHZ-001, QA-001 and the trigger, which close the anonymous path;
  2. one shared `TargetUserGuard` for AUTHZ-003 and AUTHZ-002, which breaks the 003 → 002 cross-tenant chain ([01b §2.3](specialists/01b-architect-challenge-review.md));
  3. the grant ceiling plus `IsPlatformAdmin` checks on entitlement handlers (AUTHZ-004).
- **Dependencies:** 0.1 (forensics), 0.2. **Complexity:** M. Architect estimate for the Phase 0 guards: S; DB backstops: M.
- **Risk / priority:** Critical / P0. **Change risk:** existing invite flows change, because `user_type` becomes derived from the role, not taken from the client ([01b §4.4](specialists/01b-architect-challenge-review.md)).
- **Acceptance criteria:**
  - Brand, franchise and store admins cannot create or invite `platform_admin`, and a store admin cannot create `brand_admin`.
  - A rejected invite leaves no `users` row.
  - The S6 repro ([10a](specialists/10a-qa-verification-security.md)) now fails: an `app_user` INSERT of `user_type='platform_admin'` is refused.
  - `GrantMembership` on a user outside the actor's brand returns 403/404, and `IsPrimary` does not touch other brands.
  - A store admin cannot activate, update or deactivate a brand admin or another store's staff.
  - A brand admin cannot grant `saas.manage` or `brands.create`.
  - `SetBrandFeature`, `ApplyBundleToBrand` and `SetBrandPlatformInvoiceStatus` return 403 for non-platform callers.
  - A platform admin can still do all of the above.
- **Required tests:** RBAC-1…RBAC-5 and RBAC-7 ([08 §3.2](08-test-strategy.md#32-rolepermission-enforcement)), which are 10a T1–T5 and S6 as an RLS-SQL regression.
- **Rollback / migration:** the trigger migration has a down file. Apply it only **after** the 0.1 inventory, so legitimate operator accounts are known. Operator tooling that writes `platform_admin` must then use the bypass/platform role. If 0.1 finds suspicious rows or overrides granting platform codes, revoke them and bump `perm_version` as part of the release.

### 0.4 Per-client auth rate limiting behind the gateway
- **Findings addressed:** **SA-API-001** (Critical; closes SA-OPS-001) and **SA-QB-003** (moved from P1).
- **Code areas:**
  - `core.WebApi/Program.cs:198-217` (partition on `Connection.RemoteIpAddress`);
  - `laundryghar.ServiceDefaults/Extensions.cs:267-287` (`KnownProxies`/`KnownIPNetworks` are cleared when enabled);
  - `deploy/docker-compose.yml:25,83-84`;
  - `backend/laundryghar/PRODUCTION_ENV.md:135-150`, `deploy/README.md:55`;
  - the endpoint groups in `Auth.cs:56` (including `/refresh`), `CustomerAuth.cs:45`, `PartnerAuth.cs:33`, `Signup.cs:31-32` and `OAuth.cs`.
- **Change:** enable forwarded headers on the three services, with `KnownIPNetworks` set to the compose or cluster network (never cleared). Have the gateway overwrite `X-Forwarded-For`. Give `/refresh` its own per-user or per-family limit. Keep one documented setting.
- **Dependencies:** none. Must precede 0.7. **Complexity:** S. **Risk / priority:** Critical (availability) / P0.
- **Acceptance criteria:**
  - two clients behind the trusted proxy get independent partitions;
  - a spoofed `X-Forwarded-For` from an untrusted peer is ignored;
  - refresh is not throttled by login traffic;
  - `LoginHistory.IpAddress` records the client IP.
- **Required tests:** BYP-3 (10a T10) and the SA-QB-003 two-client YARP test ([08 §3.11](08-test-strategy.md#311-api-authz-bypass-attempts)).
- **Rollback / migration:** configuration only. A wrong network allow-list either reproduces today's single bucket or trusts spoofed IPs, so verify in a staging topology first. The gateway's own partitioning (SA-API-002) is fixed in 1.5.

### 0.5 Brand-keyed notification credentials
- **Findings addressed:** **SA-API-012** (closes SA-SOLID-007).
- **Code areas:** `commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:31-62` (singleton, `FirstOrDefault` with no brand filter, 60 s TTL per [10b](specialists/10b-qa-verification-platform.md)), `RoutingChannelSender.cs:98-158`, `commerce.WebApi/Program.cs:259`. Reference implementations: `RoutingOtpSender.cs` and `GatewaySettingsCache.cs`, which are brand-aware.
- **Dependencies:** 0.2 (commerce.Tests). Must precede 0.7. **Complexity:** S. **Risk / priority:** High / P0 for multi-tenant launch.
- **Acceptance criteria:** credentials are resolved per `request.BrandId`: the brand row, else the platform row, chosen deterministically. The cache is keyed per brand. No send ever uses another brand's row.
- **Required tests:** BJ-4 (10a T15).
- **Rollback / migration:** code only. Which row won at runtime is Not Tested, so the operator should decide whether past WhatsApp/SMS sends are treated as a cross-tenant data incident.

### 0.6 Money paths: capture, refunds, refund cap and refund contract
- **Findings addressed:**
  - **SA-API-007** (closes SA-SOLID-002);
  - **SA-API-008**;
  - **SA-API-009** (closes SA-DB-006);
  - **SA-QC-001**.
- **Code areas:**
  - `commerce.Application/.../RazorpayWebhookHandler.cs:186-283`;
  - `CustomerPaymentHandlers.cs:48-81,158-166`;
  - `AdminPaymentHandlers.cs:100-231`;
  - `commerce.Application/Commerce/Common/Dtos/CommerceDtos.cs:438-449`;
  - `RazorpayPaymentGateway.cs:117-150`;
  - `operations.Application/Orders/Common/OrderCancellationRefund.cs:44-93`;
  - `db/patches/payment_idempotency.sql:29-80` (trigger), to be re-issued as a numbered migration;
  - reference pattern: `PartnerBookingDebitService.cs`.
- **Change:**
  - one idempotent "on captured" routine, called by both verify and webhook, that accepts `captured` from `pending|failed`;
  - amount and order bound server-side from `amount_due`;
  - refund row inserted first as `processing` and committed, then the gateway called outside the transaction with a deterministic reference;
  - `FOR UPDATE` on the payment in both the handler and the trigger;
  - the refund API split into `RefundMethod` and `RefundType`;
  - an idempotent refund executor (a worker plus an inbox marker).
- **Dependencies:** 0.2. Must land before or with 0.7. **2.6 (customer payment UI) must not ship before this task.** QA-B rates SA-API-007 *Medium-latent*, because no shipped client calls initiate or verify today, and notes it becomes P0 the day the UI ships. The registry keeps High.
- **Complexity:** L. **Risk / priority:** High / P0 before taking online payments or enabling refunds for brand staff. **Change risk:** this is the money path. Run the CC-2 and BJ-3 tests before merging.
- **Acceptance criteria:**
  - the webhook sequence `failed` → `captured` ends captured, and the order's `amount_paid` is updated;
  - a verify/webhook race credits once;
  - two parallel refunds respect the cap (the T5 repro now fails at the second);
  - the gateway is called zero times when the row insert fails;
  - every documented refund method persists;
  - cancelling a paid order executes its refund exactly once.
- **Required tests:** CC-2, BJ-3, BYP-8 and REF-1 ([08](08-test-strategy.md)), which correspond to 10a T14/T21 and the 10c "Refund contract" and "Concurrency: money" rows.
- **Rollback / migration:**
  - **Before enabling the refund executor, reconcile existing `payment_refunds` rows in `pending` against Razorpay.** Some may already have been refunded by hand, and executing them would double-refund.
  - Ship the executor behind the existing `WorkerOptions` flag pattern.
  - The trigger migration must create the trigger if it is absent; 10a found no script that applies it.
  - The refund API change has no admin-web or pos-web caller ([10c](specialists/10c-qa-verification-db-mobile.md) grep).

### 0.7 Customer and commerce lanes under 0031, with address ownership (one release)
- **Findings addressed:**
  - **SA-TEN-001** (Critical; closes SA-AUTHZ-006 and SA-DB-001);
  - **SA-TEN-002** (closes SA-AUTHZ-005);
  - **SA-ARCH-014**, P0 part: the customer and commerce lanes;
  - **SA-DB-012**;
  - **SA-MOB-005**.
- **Code areas:**
  - `db/migrations/0031_subbrand_scope_rls.up.sql:60-110,198` (`kernel.within_scope_cols`), to be replaced by a new migration;
  - `commerce.Infrastructure/Worker/CommerceHostCurrentTenant.cs:42-94`;
  - `laundryghar.Utilities/Services/HttpContextCurrentTenant.cs`;
  - `core.Infrastructure/Auth/JwtTokenService.cs:59-64,98-110`;
  - `RlsConnectionInterceptor.cs`;
  - `db/migrations/0001_customer_social_auth_and_pin.up.sql:49-54` and `phase4_salon_fulfillment_schema.sql:90-95` (the raw `::uuid` cast), to be replaced by a new migration;
  - `operations.Application/Orders/Pickup/Commands/PickupCommands.cs:52-103,336-470`.
- **Design choice.** Three options are on record; the lane-matrix test decides between them:
  - **DB-side** (08b): `within_scope_cols` returns true when `kernel.current_customer_id()` is set. This is preferred because it does not depend on token rotation.
  - **Token-use arm** (02): a true arm for `customer`, `customer_mcp` and `api_key`. Do **not** emit a fake `scope_nodes` claim.
  - **Architect** (01b): emit a customer scope node, or exempt `token_use=customer`.

  In all three, `CommerceHostCurrentTenant` delegates the subject slice (`ScopeNodes`, `Roles`, `Permissions`, `UserType`, `TokenUse`, `CustomerId`) to the same claim reads as `HttpContextCurrentTenant`.
- **Dependencies:** 0.1, 0.3, 0.4, 0.5, 0.6 ([§2](#2-phase-0-first-two-weeks-dependency-order)). **Complexity:** M. **Risk / priority:** Critical (availability; it fails closed) / P0.
- **Acceptance criteria** (on a 0031-migrated schema, as `app_user`):
  - A customer reads their own orders, payments, stores and slots, and can insert orders, pickups and audited writes.
  - A customer cannot see other customers' orders, payments or `wallet_accounts`. The T1b repro now returns 1 wallet, not 2.
  - Brand staff on the commerce host see the same payments as on the operations host.
  - The customer-identity query under anonymous + bypass does not throw.
  - A foreign `addressId` returns 404 on both customer and admin pickup create.
  - The existing staff-boundary assertions in `SubBrandScopeRlsTests` still pass.
- **Required tests:** ISO-1…ISO-4, MOB-L4 and PAR-8 ([08](08-test-strategy.md)), which correspond to 10a T6–T8 and the 10c "RLS × principals / adapters / anonymous paths" rows.
- **Rollback / migration:**
  - The down migration restores the 0031 predicate. That re-creates an outage, not a leak.
  - Expense numbering by franchise-scoped staff on the commerce host becomes exposed to SA-QC-002 after this release ([10c](specialists/10c-qa-verification-db-mobile.md)), so schedule 1.6 early.
  - Optionally fold the SA-DB-017 inlinable-SQL rewrite into the same function change. Otherwise it stays in 5.3.

### 0.8 Delivery-leg state machine
- **Findings addressed:** **SA-MOB-001**.
- **Code areas:** `operations.Application/Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs:34-42,96,148-264`; `UpdateMyAssignmentStatus.cs:44` (the sibling endpoint, per [10c](specialists/10c-qa-verification-db-mobile.md)); `RiderLoad`; `IFulfillmentStrategy.EnsureTransition`.
- **Change:**
  - a per-leg transition table;
  - Conflict for illegal transitions and 200 no-op for same-status repeats;
  - load decremented only on first terminal entry;
  - before delivery completion, the order's strategy must allow `→ delivered`;
  - conditional `UPDATE … WHERE status=@expected`.
- **Dependencies:** 0.2. SA-MOB-002 (1.9) depends on its status vocabulary. **Complexity:** M (12 R1 = M). **Risk / priority:** High / P0 (phantom `delivered` plus a COD payment on a cancelled order).
- **Acceptance criteria:**
  - cancelled → completed returns 409, and so does completed → started;
  - a repeat `completed` changes load once;
  - completing the delivery leg of a cancelled order is rejected;
  - `/assignments/{id}/status` accepts only allow-listed values.
- **Required tests:** MOB-L1 ([08 §3.15](08-test-strategy.md#315-mobile-and-location)).
- **Rollback / migration:**
  - The change is code only.
  - Audit for orders that have a `cancelled` history row followed by `delivered`, and for COD payments on them.
  - **Ship the rider offline-queue fix (SA-MOB-008, 1.9) right after.** Today's queue treats any rejection as "offline" and blocks on the first rejected item, so the new 409s would wedge it.

### 0.9 Suspension self-lift and DEFINER function checks
- **Findings addressed:** **SA-TEN-003** and **SA-DB-003** (Medium after QA-C).
- **Code areas:** `db/migrations/0015_brand_cancellation.up.sql:143-294` (`set_brand_cancellation_state`, `export_brand`, `purge_brand`), to be changed by a new migration; `RequestBrandCancellation.cs:60-94`, `WithdrawBrandCancellation.cs:56`, `BrandSuspensionMiddleware.cs:54`; default privileges in `db/patches/harden_app_user_and_rls_bypass.sql:59-67`; `RetentionSweepService.cs:241`.
- **Change:**
  - Refuse `suspended` → `cancelled`, or restore the prior status on withdraw.
  - Inside each brand-taking SECURITY DEFINER function, raise unless bypass is set or the brand id equals `current_brand_id()`.
  - Then revoke `purge_brand` from `app_user`, and run the retention sweep under a dedicated maintenance role.
- **Dependencies:** 0.1. **Complexity:** S–M. **Risk / priority:** High (TEN-003) / P0. QA-C: no HTTP path passes a foreign brand id, and while SA-TEN-007 is open `app_user` can already self-set bypass. The DB-003 revoke therefore adds defence in depth, and its full value comes with 1.3.
- **Acceptance criteria:**
  - The S7 repro is refused: a suspended brand cannot become `active` through cancel and withdraw.
  - The S7b and T2 repros raise: a cross-brand DEFINER call from an `app_user` session fails.
  - The retention sweep still purges under its own role.
- **Required tests:** SUS-1 and ISO-5.
- **Rollback / migration:** the down migration restores the old function bodies. The revoke must be sequenced **after** the maintenance role exists, or `RetentionSweepService` breaks ([10c](specialists/10c-qa-verification-db-mobile.md) contradiction 5).

### 0.10 Data durability: uploads, partitions, backups
- **Findings addressed:**
  - **SA-OPS-003** (closes SA-API-017, storage part);
  - **SA-OPS-004** (closes SA-MOB-012);
  - **SA-QC-003**;
  - **SA-OPS-010**.
- **Code areas:**
  - `operations.Infrastructure/Storage/FileStorageProviderFactory.cs:16-37`, `LocalStorageOptions.cs:9-15`, `operations.Infrastructure/DependencyInjection.cs:32-47`;
  - `deploy/docker-compose.yml:48-58`;
  - `commerce.Infrastructure/Worker/Services/PartitionMaintenanceService.cs`;
  - `database_scripts/99_cross_cutting_schema_qualified.sql`, `db/patches/phase1_slice_c_laundry_fulfillment.sql:24-90`;
  - `db/tools/run_partman_maintenance.sh`, `db/tools/com.laundryghar.partman.plist:30`;
  - `ops/backup/{backup.sh,verify-backup.sh,restore.sh,README.md}`.
- **Order inside the task:**
  1. Mount a named volume at an explicit `Storage__Local__RootPath`, included in backups.
  2. Fix the stale `part_config` row **before** scheduling maintenance; otherwise every `run_maintenance_proc()` aborts (T12).
  3. Call maintenance from the worker under a single-runner advisory lock, per table, and alert on DEFAULT-partition rows.
  4. Build the S3/Blob provider at the existing seam.
  5. Make managed PITR primary, with encrypted dumps and a scheduled verify that checks row counts and RLS policies.
- **Dependencies:** 0.1 (runway, `part_config`, backup state). **Complexity:** M. **Risk / priority:** High / P0. The partition runway is time-bound (2026-12-01 documented, not measured). QA-B: raise it to Critical if a live DB shares that runway.
- **Acceptance criteria:**
  - uploads survive a redeploy;
  - after a maintenance run, future partitions exist N months ahead for orders, audit, process, notification and decision logs;
  - a rider-ping partition older than 14 days is dropped;
  - `run_maintenance_proc()` succeeds on the migrated schema;
  - a scheduled restore-verify passes and alerts on failure.
- **Required tests:** BJ-7, MIG-6 and STO-1.
- **Rollback / migration:**
  - Copy existing files out of `/tmp/laundryghar-uploads` **before** the first redeploy after this change; they are lost on every redeploy today. Keep the brand-prefixed keys.
  - If rows have already landed in DEFAULT partitions, move them out before creating children for the same range. PostgreSQL refuses to create a partition whose range overlaps rows held by the DEFAULT partition.

### 0.11 Freeze the schema bootstrap path
- **Findings addressed:** **SA-DB-002** (closes SA-ARCH-008, SA-SUB-020 and SA-VERT-010). The freeze is here; the baseline is in 1.1.
- **Code areas:** `db/build_from_scratch.sh:6-8,77-120` (re-runs `rls_proposal.sql`), `db/patches/rls_proposal.sql:85-87` (the `'on'`-only `rls_bypass`), `deploy/README.md:35`, `ops/backup/README.md:5`.
- **Change:**
  - Stop `rls_proposal.sql` redefining `kernel.rls_bypass()`.
  - Remove the "safe to re-run against an existing database" claim.
  - Document that the documented path fails at migration 0005. This was reproduced twice: by 08b and by QA-C C3.
  - Capture the production schema dump from 0.1.
- **Dependencies:** 0.1. **Complexity:** S. **Risk / priority:** High / P0. Re-running the bootstrap on production turns every platform-admin and worker bypass into zero rows.
- **Acceptance criteria:** re-running the bootstrap scripts cannot change `kernel.rls_bypass()`, and an assertion that `kernel.rls_bypass()` accepts `'true'` passes.
- **Required tests:** MIG-4.
- **Rollback / migration:** documentation and script change only.

### 0.12 Product-surface release blockers
- **Findings addressed:** **SA-VERT-002** (closes SA-ARCH-004) and **SA-FE-001**.
- **Code areas:**
  - a new migration setting `is_public=false` for salon and tiffin (see `db/migrations/0011_vertical_templates.up.sql:41` and `0012_recurring_fulfillment_mode.up.sql:155-156`), plus `GetSignupTemplates.cs:29` and `CompleteSignup.cs:73-75`;
  - `admin-web/Dockerfile:24-31`, `.github/workflows/release.yml:44-52`, `deploy/docker-compose.yml:102-109` and `admin-web/src/api/client.ts:26-34`.
- **Decision to take:** the logistics/courier template is public too ([10b](specialists/10b-qa-verification-platform.md) SA-VERT-004 row). The architect states "courier cannot launch" without coordinate capture and serviceability enforcement ([01b](specialists/01b-architect-challenge-review.md) M12 item 7; SA-MOB-004, SA-MOB-013). Decide whether it stays public before Phase 3.
- **Dependencies:** none. **Complexity:** S. **Risk / priority:** High / P0.
- **Acceptance criteria:**
  - `GetSignupTemplates` returns only templates whose fulfilment mode has a creation path;
  - the built admin-web bundle contains all 9 gateway prefixes (the reverse of QA-B command 11);
  - the dashboard and CMS smoke-test cleanly against compose.
- **Required tests:** BT-5 and the CI-1 bundle grep step ([08 §4](08-test-strategy.md#4-ci-gating-recommendations)).
- **Rollback / migration:** templates are re-published later through the operability gate (5.1). Brands already created from salon or tiffin templates (counted in 0.1) keep laundry-mode orders until 3.3 (SA-VERT-001).

---

## 4. Phase 1 — Production / SaaS foundation

**Goal:** reproducibility, safe concurrency, safe workers, quality gates, one tenant context. **Architect exit criteria:** a fresh environment built from scripts passes EF-model validation, two worker instances run each job once, and CI is green including mobile ([01b §5](specialists/01b-architect-challenge-review.md)).

### 1.1 Single schema source of truth and migration CI
- **Findings addressed:** **SA-DB-002** (completion), **SA-OPS-007**, **SA-QB-002**, **SA-TEN-010**, **SA-DB-018**.
- **Code areas:**
  - `db/migrations/0000_baseline.up.sql` (new, from the 0.1 dump);
  - `db/build_from_scratch.sh` and `db/patches/` (retired from the bootstrap);
  - `db/tools/migrate.sh:83-93,138-170` (add `pg_advisory_lock`; note that `no-transaction` files are not atomic);
  - `.github/workflows/ci.yml:88-111`;
  - `tests/operations.IntegrationTests/Rbac/RbacRlsFixture.cs:43-47,86-120,140-280`;
  - `tests/operations.IntegrationTests/RepoPaths.cs`.
- **Dependencies:** 0.1, 0.11. **Complexity:** L (RC1). **Risk / priority:** High / P1. Every later structural change needs this to be verifiable.
- **Acceptance criteria:**
  - CI builds PostgreSQL 18 with partman and postgis, then applies baseline + migrations, then runs `migrate.sh verify`;
  - `up → down → up` passes for every migration after the baseline;
  - EF mappings match `information_schema`;
  - the 0027 RLS-coverage guard passes on the full chain;
  - the RLS fixture's GUC setter is generated from the interceptor, with pooling on;
  - an FK-index catalog check runs.
- **Required tests:** MIG-1…MIG-8 and ISO-10.
- **Rollback / migration:** on existing databases, record the baseline as already applied in `schema_migrations`; never execute it against production. Adopt expand/contract for backward-compatible deploys ([11](specialists/11-devops.md)). Build FK indexes with `CREATE INDEX CONCURRENTLY` in `-- migrate: no-transaction` files. Drop redundant indexes only after checking production `pg_stat_user_indexes`.

### 1.2 Release, deploy and secrets pipeline
- **Findings addressed:**
  - **SA-OPS-006** (completion);
  - **SA-OPS-012**;
  - **SA-FE-011** (closes SA-MOB-017);
  - **SA-OPS-009**;
  - **SA-TEN-015**;
  - **SA-API-023**;
  - **SA-OPS-017**;
  - **SA-OPS-018** (closes SA-ARCH-012 and SA-VERT-011).
- **Code areas:**
  - `.github/workflows/{ci,release}.yml` and `deploy/docker-compose.yml:39,52,64,76,110` (GHCR image names, SHA tags);
  - `rider-mobile/package-lock.json:14602-14607` (react / react-dom);
  - `customer-mobile/.gitignore:5` and `expo-env.d.ts` (the TS2882 cause);
  - a pos-web Dockerfile and CI job (new);
  - `laundryghar.ServiceDefaults/Extensions.cs` (`AddKeyPerFile`);
  - `db/patches/app_user_role.sql:34` and `harden_app_user_and_rls_bypass.sql:47`;
  - `*/appsettings.Development.json` and `backend/laundryghar/.dockerignore`;
  - `PRODUCTION_SPEC.md:104-118`, `backend/laundryghar/PRODUCTION_ENV.md:85-134` and `HANDOFF.md:797-803`.
- **Dependencies:** 0.2. pos-web must not be deployed before the SA-FE-003 fix in 1.11. QA-B lowered SA-FE-003 to Medium only because pos-web is not shipped. **Complexity:** M. **Risk / priority:** High (OPS-006) / P1.
- **Acceptance criteria:**
  - CI is green on `main` including both mobile jobs and a pos-web job;
  - release runs only on CI success and deploys by SHA tag;
  - compose pulls GHCR images;
  - no DB password or OTP master code is committed for non-Development use;
  - Dev JSONs are excluded from images;
  - base images and actions are pinned;
  - the docs describe the DB-polled outbox and the key-per-file secrets as they actually exist.
- **Required tests:** CI-1…CI-8 ([08 §4](08-test-strategy.md#4-ci-gating-recommendations)).
- **Rollback / migration:** the `app_user` password was committed (`'app_user'`), so rotate it when it is removed from the patches. Keep branch protection on once CI is green.

### 1.3 One tenant-context resolver, control-plane audience and DB trust boundary
- **Findings addressed:**
  - **SA-ARCH-014** (consolidation);
  - **SA-ARCH-013** (separate platform token audience and endpoint group);
  - **SA-TEN-007** (closes SA-DB-015);
  - **SA-TEN-008** (closes SA-AUTHZ-014, SA-SUB-017 and SA-SUB-018);
  - **SA-DB-014**.
- **Code areas:**
  - `laundryghar.Utilities/Services/HttpContextCurrentTenant.cs`, `commerce.Infrastructure/Worker/{CommerceHostCurrentTenant,WorkerCurrentTenant}.cs`;
  - `laundryghar.Utilities/Middlewares/TenantResolutionMiddleware.cs:34-47`, `laundryghar.Utilities/Auth/PermissionHandler.cs:31-33`;
  - `db/patches/harden_app_user_and_rls_bypass.sql:31-38` (the self-settable `app.bypass_rls`);
  - `BrandSuspensionMiddleware.cs:78-83,128-134`, `BrandStatusStore.cs:47-53`;
  - the worker services in `commerce.Infrastructure/Worker/Services/*`;
  - `DeleteBrand.cs:14-24`;
  - `Utilities/Authorization/Abac/NpgsqlAbacStore.cs:33-48`, `DecisionLogWriter.cs:125-140`;
  - `core.WebApi/Endpoints/Identity/{AdminEntitlements,AdminBrands}.cs`.
- **Dependencies:** 0.3, 0.7, 1.1 (the lane matrix runs on the CI schema). **Complexity:** L. Architect estimates: control plane S–M; removing the bypass GUC with a distinct worker/control-plane role S–M. **Risk / priority:** High / P1.
- **Acceptance criteria:**
  - One `ICurrentTenant` resolver maps user, customer, partner, api_key and worker tokens to the same GUC set, and the lane × host × restrictive-table matrix is green.
  - The S5 and T3 repros fail: `app_user` cannot self-set bypass.
  - `/admin/entitlements`, `/admin/brands` and the platform invoice endpoints require the platform audience.
  - Workers skip suspended and cancelled brands; the partner and api-key lanes are gated; a deleted brand cannot log in.
  - ABAC store reads and decision-log writes work as `app_user`.
- **Required tests:** ISO-1, ISO-6, RBAC-9, SUS-2…SUS-4 and ABAC-3.
- **Rollback / migration:** roll out in stages. Introduce the new DB role(s) and a second connection string, move workers and the control plane onto them, then remove the GUC path. Operator tooling must move to the platform audience ([01b §4.1](specialists/01b-architect-challenge-review.md) risk).

### 1.4 Identity and session hardening
- **Findings addressed:** **SA-DB-005**, **SA-AUTHZ-007**, **SA-AUTHZ-009**, **SA-API-020**, **SA-AUTHZ-013**, **SA-ORC-002** (default-deny `FallbackPolicy`).
- **Code areas:**
  - `db/migrations/0029_users_brand_rls.up.sql:122,147-155`, followed by a new migration (RLS on `user_scope_memberships`, profiles, OTP, refresh and password-reset tables; restrict the `users` INSERT check);
  - `ScopeResolver.cs:105-170`, `HttpContextCurrentUser.cs:107-120`;
  - `SetPersonStatus.cs:48-68`, `DeactivateUser.cs:14-23`;
  - `TokenVersionStore.cs:41-45`, `TenantResolutionMiddleware.cs:61-71`;
  - `RefreshTokenHandler.cs:43-112`;
  - `AdminSettings.cs:181-185` and `UpdateDispatchSettings.cs:38-39`;
  - `laundryghar.Utilities/Auth/PermissionPolicyProvider.cs:32-33` and the `AddAuthorization` calls in the core, operations and commerce `Program.cs` (SA-ORC-002).
- **Dependencies:** 0.3, 1.1. **Complexity:** M. Architect estimate: RLS on memberships M; the policy "must be designed and not copied". **Risk / priority:** High (DB-005) / P1.
- **Acceptance criteria:**
  - the T4 repro fails: a brand-A session cannot insert a brand-B membership, and cannot read other brands' OTP or refresh rows;
  - a store role at S1 plus a brand read-only role cannot write to S2;
  - suspending a user rejects their old token within the TTL, and the version check fails closed for high-risk permissions;
  - a parallel refresh produces one success;
  - brand admins get 403, not a DB error, on platform dispatch settings;
  - every host sets an authenticated-user `FallbackPolicy`, and intended public endpoints are explicitly `AllowAnonymous`.
- **Required tests:** ISO-7, ABAC-1, RBAC-8, CC-8, RBAC-10 and RBAC-6 (the endpoint-metadata contract test, 06 T11).
- **Rollback / migration:** the access-control screens read other users' memberships, so exercise them against the new policy before release. The down migration disables RLS again.

### 1.5 Gateway rate limiting on verified identity
- **Findings addressed:** **SA-API-002** (closes SA-AUTHZ-010, SA-OPS-002 and SA-TEN-005), **SA-QA-002**, **SA-OPS-014**.
- **Code areas:** `laundryghar.Gateway/RateLimitPartitioning.cs:24-92`, `laundryghar.Gateway/Program.cs:213-258`, `ResilientForwarderHttpClientFactory.cs`, `tests/operations.Tests/Auth/RateLimitPartitioningTests.cs:27-37,63-77`, plus container resource limits in `deploy/docker-compose.yml`.
- **Dependencies:** 0.4. Plan-tier limits need 2.2. **Complexity:** M. **Risk / priority:** High / P1.
- **Acceptance criteria:**
  - an unauthenticated `X-Brand-Id` does not leave the IP bucket;
  - XFF from an untrusted hop is ignored;
  - brand partitions are keyed only on a validated token;
  - a per-brand concurrency partition exists;
  - the old tests that assert the vulnerable behaviour are replaced.
- **Required tests:** BYP-2 (10a T9).
- **Rollback / migration:** configuration and code only. Merge the test replacement in the same PR, so the "fixed" behaviour is not reverted to satisfy old tests (SA-QA-002).

### 1.6 Concurrency and idempotency model
- **Findings addressed:**
  - **SA-API-004** (closes SA-DB-011);
  - **SA-API-005** (closes SA-DB-008);
  - **SA-DB-010**;
  - **SA-SUB-016**;
  - **SA-API-018** (closes SA-MOB-018);
  - **SA-API-019**;
  - **SA-QC-002**;
  - **SA-DB-007** (closes SA-TEN-012; moved from P3).
- **Code areas:**
  - `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:71-94,292-311,685-689,757-803`;
  - `CustomerWalletHandlers.cs:149-212`, `AdminWalletHandlers.cs`, `AdminPaymentHandlers.cs:180-215`;
  - `CustomerCouponHandlers.cs:85-142`;
  - `ProcessPaylinkWebhook.cs`, `RazorpayWebhookHandler.cs:186-233`;
  - `customer-mobile/src/api/orders.ts:128-136`, `customer-mobile/app/(app)/booking/pay.tsx:390-404`;
  - `PickupCommands.cs:38-50,61-63,657-672`;
  - `ExpenseCommands.cs:204-205`, `CreateWarehouseBatch.cs:48-49`, `GenerateTags.cs:34-42`, `SaveCommercials.cs:53`, `CreateParcelOrderCommand.cs:163-166`;
  - the EF configurations under `laundryghar.SharedDataModel/Persistence/Configurations/**`.
- **Dependencies:** 0.6, 1.1. **Complexity:** L (RC4). **Risk / priority:** High / P1. DB-Q3 is Not Supported for money and dispatch paths ([01b §2.1](specialists/01b-architect-challenge-review.md)).
- **Acceptance criteria:**
  - parallel POS orders with the same key yield one order and one ledger debit;
  - after parallel wallet credits and debits, `balance == SUM(ledger)` (the T6 repro now fails);
  - parallel coupon redemption respects the caps;
  - two concurrent identical webhooks produce one outbox event;
  - the customer app sends `Idempotency-Key`;
  - an admin booking on a full slot is rejected, and the count stays consistent after a reject;
  - two warehouse- or franchise-scoped users, or two brands, can create batches and expenses on the same day;
  - concurrency conflicts map to 409.
- **Required tests:** CC-1, CC-3, CC-4, CC-5, CC-9, ENT-11 and ISO-12.
- **Rollback / migration:**
  - Before adding new unique indexes (order idempotency key, coupon redemption, `(brand_id, number)`), detect and resolve existing duplicates.
  - Create the indexes concurrently in no-transaction migrations.
  - Add the brand-scoped uniques before dropping the global ones.
  - Seed each per-brand counter from the current maximum.

### 1.7 Dispatcher validation pipeline and input bounds
- **Findings addressed:** **SA-API-003** (closes SA-ARCH-005 and SA-SOLID-005), **SA-API-016**, **SA-SOLID-014**.
- **Code areas:** `laundryghar.Utilities/CQRS/Extensions/ServiceCollectionExtensions.cs:14`, `CQRS/Dispatcher/Dispatcher.cs:15-45`, `CQRS/Registration/BehaviorRegistrar.cs:13-25`, `Validation/ValidationFilter.cs`, `laundryghar.Utilities/Common/PaginatedList.cs:11-42`, `PickupCommands.cs:81-93,196-202`.
- **Severity dissent:** QA-A keeps SA-API-003 High, because the upload MIME and size checks live only in dead validators. QA-B lowered it to Medium, because of DB CHECKs, `RequestSizeLimit` and attachment-only serving, and counts 39 dead validators rather than 40. The registry records Medium.
- **Dependencies:** 0.2. **Complexity:** S (architect). **Change risk:** enabling validation surfaces latent 400s. Roll it out per module behind tests ([01b §4.3](specialists/01b-architect-challenge-review.md)).
- **Acceptance criteria:**
  - every `AbstractValidator<T>` is reachable, enforced by a reflection test;
  - upload validators run;
  - `pageSize` is capped;
  - a pickup with an inflated `EstimatedAmount` against a server-priced cart below the minimum is rejected.
- **Required tests:** BYP-5, BYP-4 and BYP-7.
- **Rollback / migration:** validation can be enabled module by module.

### 1.8 Worker host and inbox consumers
- **Findings addressed:**
  - **SA-OPS-005** (closes SA-API-014 and SA-ARCH-007);
  - **SA-DB-009**;
  - **SA-API-015**;
  - **SA-ARCH-006**;
  - **SA-SOLID-008**.
- **Code areas:**
  - `commerce.WebApi/Program.cs:259-323` (14 hosted services, the `LoggingChannelSender` and `LoggingEventPublisher` registrations);
  - `OutboxEventRelayService.cs:74-200`, `NotificationDispatcherService.cs:105-225`;
  - `SubscriptionBillingService.cs:353-383`;
  - `NotificationMappingService.cs:119-178`, `LoyaltyEarnService.cs:90-115`;
  - `PartnerBookingDebitService.cs` (the reference pattern);
  - `Worker/Stubs/LoggingChannelSender.cs`.
- **Dependencies:** 0.5, 1.1, 1.3 (uniform worker scope contract). **Complexity:** M–L. Architect estimate for "modular monolith + separate worker host": M (2–4 eng-months including module carving); that figure covers 3.1 as well. **Risk / priority:** Medium; P0 before any multi-replica deployment.
- **Acceptance criteria:**
  - two worker instances process each outbox and notification row once;
  - a stale `publishing`/`sending` lease is reclaimed;
  - an event committed late is still consumed by the loyalty and notification consumers;
  - an unconfigured channel in Production records `suppressed`/`failed:not_configured`, not `sent`;
  - billing attempts are inserted before charging.
- **Required tests:** BJ-1, BJ-2, BJ-5 and BJ-10.
- **Rollback / migration:**
  - Add a separate `worker` compose service using the same image with a flag, pinned to one replica at first. Moving back to in-process hosting is a configuration change.
  - Seed the inbox (`outbox_consumed_events`) from the current watermarks so that switching consumers does not replay history.

### 1.9 Dispatch and rider-app integrity
- **Findings addressed:**
  - **SA-MOB-002** (closes SA-DB-022);
  - **SA-MOB-003**;
  - **SA-MOB-008**;
  - **SA-MOB-009** (Suspected);
  - **SA-MOB-010**;
  - **SA-MOB-011** (scope corrected by QA-C: the gap is `UpdateRider` suspend/terminate, not `DeactivateRider`);
  - **SA-MOB-020**;
  - **SA-MOB-021**;
  - **SA-MOB-007** (rider push on assign, auto-assign and leg cancel; the registry places it in phase 1).
- **Code areas:**
  - `PickupCommands.cs:204-279`, `DeliveryAssignmentCommands.cs:29-107`, `AutoDispatchService.cs`, `OfferActions.cs`;
  - `VerifyTaskOtp.cs:39-59`, `BatchLocationPing.cs:42-87`, `RiderSelfDtos.cs:6-16`, `RiderSelfEndpoints.cs:126-145`;
  - `UpdateRider.cs:53,87-89`, `laundryghar.Utilities/Auth/RiderOnlyRequirement.cs`;
  - `DeliverySlotQueries.cs:49-53`, `UpdateMaps.cs:52`, `GetAdminSettings.cs:44`;
  - `rider-mobile/app/(app)/tasks/[id].tsx:346-406`, `rider-mobile/src/hooks/useOfflineQueueFlush.ts:42-58`, `rider-mobile/src/lib/backgroundLocation.ts:30-52`, `rider-mobile/src/api/client.ts:136-186`;
  - push: `AutoDispatchService.cs:354-371`, `PickupCommands.cs:259-264`, `RiderPushToken.cs`, `ExpoPushChannelSender`, `rider-mobile/src/hooks/useRiderTasks.ts:57` (30 s polling today).
- **Dependencies:** 0.8 (leg vocabulary; MOB-008 should follow 0.8 promptly); 1.6 (unique-index and 23505 pattern); push needs FCM configured (1.2, SA-FE-011/SA-MOB-017) and the notification worker (1.8). OTP generation belongs in the strategy transition effect. **Complexity:** M (12 R2 M, R6 M, R7 S–M, R9 S, R14 S).
- **Risk / priority:** High (MOB-002/003) / P1. MOB-003 is "a missing control, not an exploitable one" ([01b](specialists/01b-architect-challenge-review.md)). Until OTP generation ships, remove the "OTP-verified delivery" claim (01b M12 item 5).
- **Acceptance criteria:**
  - Concurrent assignment leaves one live leg; assign-after-assign returns 409; assigning a cancelled pickup returns 409; accept and expire race safely.
  - An order at `out_for_delivery` has an OTP; completing without verifying returns 400; the 6th wrong attempt locks the leg.
  - The rider app does not queue a 4xx, skips a poison item, and replays `failed` with its reason; the background task never logs the rider out.
  - Pings are validated (ranges, batch ≤ 50, server-time clamp), and an off-duty or suspended rider's pings are ignored.
  - A suspended rider gets 403/404 on status calls.
  - Slots are brand-filtered in the handler, and map keys are masked for read-only admins.
  - Assigning, auto-assigning or cancelling a leg creates a notification row for the rider's push tokens.
- **Required tests:** CC-6, BJ-9 and MOB-L2, MOB-L3, MOB-L5…MOB-L8 ([08 §3.15](08-test-strategy.md#315-mobile-and-location)).
- **Rollback / migration:**
  - Before creating the partial unique index on live legs, find and resolve existing duplicate live assignments.
  - Do not backfill OTPs for legs already in progress: generate them on the next transition only.
  - Rider-app changes need an OTA or store release, which depends on 1.2.

### 1.10 Commerce test project and god-handler tests
- **Findings addressed:** **SA-ARCH-010** (completion) and **SA-SOLID-010** (the tests half; the refactor is in 3.2).
- **Code areas:** `tests/commerce.Tests/` (new; referencing `commerce.Application` and `commerce.Infrastructure`), `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:51-806`, `UpdateMyTaskStatus.cs:37-280`, `core.WebApi/.../OAuth.cs`; the InMemory pattern in `tests/operations.Tests/Catalog/Import/ImportTestSupport.cs:23-27`.
- **Dependencies:** 0.2. **Complexity:** M. **Risk / priority:** Medium / P1, before touching commerce structure.
- **Acceptance criteria:**
  - CreateOrder golden-total tests cover express, add-ons, coupon, loyalty, GST and an unregistered franchise;
  - rider-flow state tests exist;
  - webhook idempotency, wallet mutation and outbox consumer tests run in CI.
- **Required tests:** PAR-1, PAR-7 and BJ-2.
- **Rollback / migration:** none.

### 1.11 Web client session, data hygiene and server-driven transitions
- **Findings addressed:**
  - **SA-FE-002**, **SA-FE-003**, **SA-FE-005**, **SA-FE-010**;
  - **SA-FE-014**, **SA-FE-015**, **SA-FE-016**;
  - **SA-FE-004** (the client switch to `allowedTransitions`; moved from P3).
- **Code areas:**
  - admin-web: `src/stores/authStore.ts:75-85`, `src/components/layout/{Topbar,BrandSwitcher}.tsx`, `src/hooks/{useOrders,useAnalytics,useCatalog,useNavigator}.ts`, `src/App.tsx:46-52,123-125`, `src/lib/webmcp.ts`, `src/pages/orders/{orderStatus.ts,OrderDetailDrawer.tsx,OrdersPage.tsx}`, `src/types/api.ts`;
  - pos-web: `src/stores/{authStore,cartStore}.ts`, `src/api/client.ts:136-145`, `src/components/layout/Topbar.tsx`;
  - customer-mobile: `app/(app)/booking/items.tsx:158-184`;
  - rider-mobile: `src/store/offlineQueueStore.ts:16`;
  - server side: `core.WebApi/Endpoints/Identity/Auth.cs:39-51,185-210` (cookie path and logout).
- **Dependencies:** web unit-test infrastructure (Vitest, part of 1.2). **Complexity:** M. **Risk / priority:** High (FE-004, live for parcel orders) and Medium for the rest / P1.
- **Acceptance criteria:**
  - logout after a reload ends the session, and a later refresh returns 401;
  - the persisted `lg-pos-auth` JSON holds no `refreshToken`;
  - switching brand changes the query keys or clears the cache;
  - production builds show no demo garments and no WebMCP tools;
  - routers have an error boundary;
  - logout clears the cart, offline queue and query caches;
  - the admin order drawer offers only the server's `allowedTransitions`: a parcel order at `picked_up` offers `out_for_delivery`.
- **Required tests:** BR-7, VW-4, BYP-9 and WEB-1…WEB-4 ([08](08-test-strategy.md)).
- **Rollback / migration:** client releases only. The cookie-path change needs the server and admin-web shipped together.

### 1.12 Tenant-aware observability and health
- **Findings addressed:** **SA-OPS-008** (closes SA-TEN-014), **SA-OPS-011** (closes SA-API-024), **SA-API-025**.
- **Code areas:** `laundryghar.ServiceDefaults/Extensions.cs:119-203`, a new middleware after `TenantResolutionMiddleware`, `laundryghar.Utilities/CQRS/Context/CorrelationContext.cs:9-19`, `laundryghar.Gateway/HealthServicesEndpoint.cs:52-101`, `deploy/docker-compose.yml` (`OTEL_*`); the log call sites in `GoogleLoginHandler.cs:78`, `InviteEmailSender.cs:46`, `SetPersonStatus.cs:85` and `SettingsMailer.cs:58-70`.
- **Dependencies:** 1.3 (one resolver to tag from). **Complexity:** S–M. **Risk / priority:** Medium / P1.
- **Acceptance criteria:**
  - logs and spans carry `tenant.brand_id`, including in worker scopes;
  - OTLP is exported, with Npgsql instrumentation;
  - alerts exist for 5xx and 429 rates, outbox and notification dead letters, partition runway and backup age;
  - `/health` checks the DB, and `/health/services` is internal only;
  - emails are masked in logs.
- **Required tests:** OBS-1 (middleware unit test).
- **Rollback / migration:** none.

---

## 5. Phase 2 — Subscription & entitlement correctness, plus onboarding prerequisites

**Goal:** subscription state drives features on every lane. **Architect exit criteria:** an unpaid tenant loses paid features automatically, paying reinstates them, and non-staff lanes return 402/403 correctly ([01b §5](specialists/01b-architect-challenge-review.md)). **Depends on Phase 1** (worker claims, concurrency, the uniform worker scope).

### 2.1 Brand billing lifecycle (one release)
- **Findings addressed:**
  - **SA-SUB-004** (closes SA-API-010);
  - **SA-SUB-005**;
  - **SA-SUB-001**;
  - **SA-SUB-003**;
  - **SA-SUB-002** (closes SA-API-011);
  - **SA-SUB-014**;
  - **SA-SUB-015** (Suspected; mandate charging, "b" track).
- **Why one release:** SA-SUB-003 is masked today by SA-SUB-004 and "becomes live as soon as SUB-004 is fixed" ([10a](specialists/10a-qa-verification-security.md)). SA-SUB-001 needs 004 and 005 first, or the pass never runs.
- **Code areas:**
  - `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs:40-233` (the `CreateAsyncScope()` at `:63`);
  - `WorkerOptions.cs:140`;
  - `core.Application/Identity/Signup/Commands/CompleteSignup.cs:199-225`, `TemplateProvisioner.cs:66-77`;
  - `ProcessPaylinkWebhook.cs` (status check at L71-72 per 10a), `CollectBrandPlatformInvoice.cs:25-27`;
  - `RazorpayPaymentGateway.cs:226-282`, `SubscriptionBillingService.cs:392-436`, `GatewaySubscriptionCharger.cs`;
  - `backend/laundryghar/PRODUCTION_ENV.md`.
- **Dependencies:** 1.3, 1.6 (webhook dedupe), 1.8. **Complexity:** L. **Risk / priority:** High / the specialists rate it "P0 for commercial launch". It is placed after Phase 1 because a renewal worker without claim locks can double-charge across replicas (SA-DB-009).
- **Acceptance criteria:**
  - the renewal pass issues invoices under `app_user` with RLS on;
  - at trial end, a subscription converts with an invoice or moves to `past_due`;
  - `past_due` → paid → `active`, and the next renewal is invoiced;
  - a paylink for a `past_due` invoice can be created and paid;
  - every transition writes an audit row and an outbox notification;
  - pending mandate statuses are finalised by webhook (validated against the Razorpay sandbox).
- **Required tests:** ENT-6, ENT-7, ENT-9, ENT-10, ENT-12, SUS-5 and SUS-6.
- **Rollback / migration:**
  - **Enabling the worker on a database where trials have been perpetual will immediately process every expired trial.** Decide on grandfathering and tenant communication first, and run one dry-run pass that reports what it would do.
  - Default the flag on in Production only after that dry run.

### 2.2 Subscription → entitlement projection on every lane
- **Findings addressed:**
  - **SA-SUB-006**;
  - **SA-SUB-012**;
  - **SA-AUTHZ-011** (closes SA-SUB-010);
  - **SA-SUB-009**;
  - **SA-SUB-008**;
  - **SA-SUB-019** (Informational).
- **Code areas:**
  - `ScopeResolver.cs:185-241`;
  - `CancelBrandPlatformSubscription.cs:47-68`, `ApplyBundleToBrand.cs:54-143`;
  - `PermVersionBumper.cs:36-44`, `PermissionPolicyProvider.cs:151-189`;
  - `BrandStatusStore.cs:47-53`, `FeatureCatalog.cs:64-70`;
  - migrations `0005_split_features_from_modules.up.sql:66-83` and `0019_premium_feature_modules.up.sql:108-123` (WARNING → EXCEPTION);
  - `module_bundle` limits (`phase4_bundle_pricing.sql:25-29`), `FranchiseSubscriptionCommands.cs:64-68`;
  - the store, warehouse, user and rider create handlers.
- **Dependencies:** 2.1, 0.3 (AUTHZ-004 closed, so a self-grant cannot bypass the projection), 1.3. **Complexity:** M (architect: entitlement M). **Risk / priority:** High (SUB-006) / P1.
- **Acceptance criteria:**
  - cancelling at period end removes bundle features from the next token;
  - `past_due` beyond grace revokes them, and paying reinstates;
  - a trial upgrade issues no invoice, and a downgrade is scheduled;
  - a customer, partner or api-key endpoint for an unlicensed feature returns 402;
  - franchise and store staff lose a feature within the TTL;
  - every sellable feature gates at least one permission;
  - create-store at the limit returns 402 `plan_limit_reached`, and Starter can create exactly one store.
- **Required tests:** ENT-1…ENT-5 and ENT-8.
- **Rollback / migration:**
  - Classify existing `brand_feature` rows by source (`bundle` vs `manual`) and backfill `valid_until` before switching ScopeResolver to the projection.
  - Decide what happens to tenants who hold features today without a paid subscription (the product owner decides).
  - The `perm_version` bump on deploy forces a token refresh.

### 2.3 Tenant billing self-service and GST invoices
- **Findings addressed:** **SA-SUB-007** and **SA-SUB-013**.
- **Code areas:** `AdminEntitlements.cs:28-38`, a new brand-scoped `/api/v1/admin/billing/me` endpoint and `billing.read` permission, `BrandSuspensionMiddleware.cs:51`, `ApiAuthorizationResultHandler.cs:42`, `admin-web/src/pages/settings/SettingsPage.tsx`, `BrandPlatformInvoice.cs:9-31`, `phase4_brand_platform_subscription.sql:41-53`, and `db/patches/invoice_generation.sql` (precedent).
- **Dependencies:** 2.1, 0.3. **Complexity:** M. **Risk / priority:** High / P0 for commercial launch (specialist).
- **Acceptance criteria:**
  - an owner reads only their own invoices, and another brand's invoice id returns 404;
  - an owner can create a payment link while suspended;
  - invoice numbers are gap-free and unique under concurrency;
  - CGST+SGST vs IGST is selected correctly;
  - `paid_at` and the gateway payment id are populated.
- **Required tests:** ENT-13, ENT-14 and SUS-7.
- **Rollback / migration:** existing invoices need `invoice_number` values backfilled in a deterministic order.

### 2.4 Royalty correctness
- **Findings addressed:** **SA-SOLID-003** (High) and **SA-QB-001**.
- **Code areas:** `RoyaltyCommands.cs:98-107`, `RoyaltyGenerationService.cs:195-203`, `SharedDataModel/Enums/CommercePaymentStatus.cs`, `UpdateMyTaskStatus.cs:196-222` and `CustomerPaymentHandlers.cs:54-81` (no `FranchiseId`), plus a banned-literal analyzer rule.
- **Dependencies:** 0.6. Pull it into Phase 0 if 0.1 shows royalties are generated in production. **Complexity:** S–M. **Risk / priority:** High / "P0 for franchise billing".
- **Acceptance criteria:** royalty over seeded COD, online and offline payments equals their sum; `payments.franchise_id` is not null when `order_id` is set; no raw payment-status literals remain.
- **Required tests:** PAR-9.
- **Rollback / migration:** backfill `payments.franchise_id` from `orders`. Royalty periods already issued at zero need a decision on re-issuing.

### 2.5 Onboarding prerequisites
- **Findings addressed:** **SA-ONB-010**, **SA-ONB-007** and **SA-SUB-011** (a product decision: franchise SaaS subscriptions).
- **Code areas:** `CompleteSignup.cs:84-85,122,184-225,279-307`, `SignupDtos.cs:26-32`, `TemplateProvisioner.cs:56`, `GenerateInvoiceCommand.cs:80-87`, `FranchiseSubscriptionCommands.cs`, `docs/ADRs/ADR-010-recurring-billing-and-dunning.md`, and the admin-web `PlatformPlansPage`.
- **Dependencies:** 2.1. **Complexity:** S–M. **Risk / priority:** Medium / P1–P2.
- **Acceptance criteria:**
  - a public template without a resolvable bundle fails signup loudly;
  - an optional `planCode` is validated against the vertical's bundles;
  - a concurrent duplicate phone returns 409;
  - a signup with a GSTIN produces invoices showing it;
  - the franchise-subscription decision is recorded and ADR-010 is updated (deprecate or bill).
- **Required tests:** ONB-T1…ONB-T3.
- **Rollback / migration:** self-signed-up franchises have no `Gstin`, so backfill it from `brands.config.gstin`.

### 2.6 Customer online payments UI
- **Findings addressed:** **SA-FE-012** (Low).
- **Code areas:** `customer-mobile/app/(app)/booking/pay.tsx:586-594`, `customer-mobile/src/api/commerce.ts:149-167`, `customer-mobile/src/hooks/useCommerce.ts:51-65`, `customer-mobile/src/constants/config.ts:116`, `rider-mobile/app/(app)/notifications.tsx`.
- **Dependencies:** **0.6 (hard)** and 2.2 (`online_payments` must actually gate). **Complexity:** M. **Risk / priority:** Low / P2.
- **Acceptance criteria:** pay with UPI or card end to end against the sandbox, and the order is marked paid exactly once.
- **Required tests:** E2E-PAY-1 (device / sandbox).
- **Rollback / migration:** client release; keep it behind the existing config flag.

---

## 6. Phase 3 — Modular verticals, and dispatch/location boundaries

**Goal:** one owner per invariant. **Architect exit criteria:** adding a test vertical touches only its module plus registration, and the laundry parity suite is green. **Depends on Phases 1 and 2.**

### 3.1 Module ownership and architecture tests
- **Findings addressed:**
  - **SA-ARCH-001** (Medium after QA-B);
  - **SA-SOLID-009** (closes SA-ARCH-002);
  - **SA-ARCH-009**;
  - **SA-SOLID-012**;
  - **SA-SOLID-013**;
  - **SA-ARCH-011**;
  - **SA-API-022**.
- **Code areas:**
  - `laundryghar.SharedDataModel/Persistence/LaundryGharDbContext.cs:34-217`;
  - `operations.Application/Common/Interfaces/IOperationsDbContext.cs:78-111`;
  - `laundryghar.Utilities.csproj:9-32`;
  - the `IFormFile` uses in Application commands;
  - `SettingsFirstPaymentGateway.cs:113-127` and `RoutingChannelSender.cs:118,154`;
  - `core.WebApi/Program.cs:100-118,408-436` and `laundryghar.AppHost/AppHost.cs:56-62`;
  - `ExceptionHandler.cs`.
- **Dependencies:** 1.1, 1.10. **Complexity:** L. Architect estimate: M (2–4 eng-months including module carving, shared with the worker host). Do not split the database ([01b §4.1](specialists/01b-architect-challenge-review.md)). **Risk / priority:** Medium / P1 as a precondition for vertical modules.
- **Acceptance criteria:**
  - NetArchTest rules fail when Application references Infrastructure, `LaundryGharDbContext` or `Microsoft.AspNetCore.Http`, or writes a foreign context's sets;
  - each table has one owning module;
  - cross-module writes go through in-process module APIs;
  - providers get non-null loggers;
  - downstream URLs are set outside Development;
  - errors use ProblemDetails, and concurrency maps to 409.
- **Required tests:** ARCH-T1…ARCH-T3.
- **Rollback / migration:** incremental, one module at a time. The existing order-placement tests must keep coupon, loyalty and package atomicity.

### 3.2 `OrderTransitionService` as the only status writer
- **Findings addressed:** **SA-SOLID-001** (closes SA-API-006), **SA-MOB-016**, **SA-FE-004** (server side completed), **SA-SOLID-010** (the refactor).
- **Code areas:**
  - `UpdateOrderStatusCommand.cs:52-117`, `CancelOrderCommand.cs:48-101`, `CancelOrderByCustomerCommand.cs:35-113`;
  - `UpdateMyTaskStatus.cs:148-397`;
  - `NotificationChannelPreferencePolicy.cs:43-62`, `LoyaltyEarnService.cs`;
  - `CustomerPickupCommands.cs:395-407` (the leg-cancel pattern to reuse).
- **Dependencies:** 1.10 (parity tests first; this touches the money path), 0.8, 1.8. **Complexity:** M–L. **Risk / priority:** High / P1.
- **Acceptance criteria:**
  - Each of the 5 write paths produces exactly one history row with the correct `FromStatus` and one `order.status_changed` event, and the template resolves.
  - A rider completing a `disputed` order is rejected.
  - Loyalty is earned for POS-delivered and rider-delivered orders alike.
  - A customer cancel cancels the legs and decrements load.
- **Required tests:** VW-8, MOB-L9, PAR-3 and PAR-7.
- **Rollback / migration:** keep `delivery.completed` as an additional event, so existing consumers keep working.

### 3.3 Vertical module seam and server-side vertical gates
- **Findings addressed:**
  - **SA-VERT-001** (closes SA-ARCH-003, SA-ONB-004 and SA-SOLID-004);
  - **SA-AUTHZ-012** (closes SA-FE-006, SA-MOB-015, SA-ONB-003 and SA-VERT-005);
  - **SA-VERT-004**;
  - **SA-VERT-006**;
  - **SA-VERT-007**;
  - **SA-VERT-008**;
  - **SA-API-021**;
  - **SA-FE-013**.
- **Code areas:**
  - `CreateOrderCommand.cs:51-60,618-647`, `Order.cs:44,48`, `FulfillmentMode.DefaultFor`;
  - `GetNavigator.cs:38-45`;
  - `InvoiceTaxCalculator.cs:18`, `GenerateInvoiceCommand.cs:37-38,156`, `InvoicePdfRenderer.cs:44`;
  - `NotificationChannelPreferencePolicy.cs:43-60`, `db/patches/phase2_slice_j_notification_event_catalog.sql`;
  - `OrderDtos.cs`, `CatalogKind.cs:32-37`, `TemplateProvisioner.cs:146-152`, `ItemCommands.cs:38`;
  - `CompleteSignup.cs:115-120`, `LoyaltyEarnService.cs:94,109`;
  - `admin-web/src/hooks/usePermissions.ts:46-51`, `admin-web/src/lib/routePermissions.ts`;
  - the customer-mobile booking FAB.
- **Dependencies:** 3.2, 2.2 (vertical gates expressed as entitlement features, [01b §1.3](specialists/01b-architect-challenge-review.md)). **Complexity:** L. Architect estimate: M–L including the salon module; the blueprint's 49 person-day salon figure is unverified.
- **Risk / priority:** High / P1. SA-VERT-004 is already live for public logistics tenants: parcel orders invoice as "SAC 999712 — Laundry & Dry-Cleaning Services" ([10b](specialists/10b-qa-verification-platform.md)). Consider pulling the invoice part forward if a logistics tenant is live.
- **Acceptance criteria:**
  - a salon brand gets `appointment`/`booked`, a laundry brand gets `process_deliver`, and parcel on a laundry brand gets `point_to_point`;
  - a mode the vertical does not allow returns 422, and a DB check enforces `orders.vertical_key = brand vertical`;
  - a laundry brand calling a salon or parcel endpoint gets 403/402;
  - invoice SAC follows the mode;
  - templates come from the catalog;
  - a non-IN template works at signup;
  - loyalty is earned on salon completion;
  - admin `/settings` is gated on permission.
- **Required tests:** VW-1…VW-3, VW-5…VW-7, VW-9, BT-4 and PAR-2…PAR-5.
- **Rollback / migration:** existing orders of non-laundry brands carry `vertical_key='laundry'`. Decide whether to backfill them (requires the 0.1 count). Add the DB check `NOT VALID` first.

### 3.4 Dispatch module (operations-owned, single writer)
- **Findings addressed:** **SA-SOLID-011**, **SA-MOB-006**, **SA-MOB-014**, **SA-MOB-019**, **SA-ORC-001** (store-scoped rider track and live reads). Rider push (SA-MOB-007) lands earlier, in 1.9; this task moves it behind the `AssignmentService`.
- **Code areas:**
  - `commerce.Infrastructure/Worker/Services/AutoDispatchService.cs:301-445`;
  - `PickupCommands.cs:196-278`, `PickupCod.cs:17`, `RiderLoadHelper`;
  - `RiderTaskMapper.cs:11-12,120-130`, `RiderSelfEndpoints.cs:64-65,84`;
  - `OrderQueries.cs:207-232`;
  - `customer-mobile/app/(app)/orders/tracking/[id].tsx`;
  - `operations.Application/Logistics/RiderOps/Queries/GetRiderTrack/GetRiderTrack.cs:25-33`, `GetRidersLive.cs`, `RidersAdmin.cs` (SA-ORC-001).
- **Design note:** the architect **disagrees** with 12's "shared static helpers". AutoDispatch must call an operations-owned `AssignmentService` composed by the worker host ([01b §1.3](specialists/01b-architect-challenge-review.md)).
- **Dependencies:** 1.9, 3.2, 1.8; FCM configured (1.2). **Complexity:** M (architect: M for assignment and tracking consolidation). **Risk / priority:** Medium / P1–P2.
- **Acceptance criteria:**
  - manual and auto assignment produce identical leg fields and events;
  - offers are visible and actionable, or offer mode cannot be enabled;
  - pickup progress shows `rider_dispatched`/`arrived`;
  - past-task PII is masked;
  - store-A staff requesting the track of a store-B rider in the same franchise get 404, while a franchise owner is allowed.
- **Required tests:** MOB-L10…MOB-L13.
- **Rollback / migration:** delete `RiderLoadHelper` and the duplicate COD helper only after the parity test passes.

### 3.5 Location module: coordinates, geocoder port, serviceability
- **Findings addressed:** **SA-MOB-004** and **SA-MOB-013**.
- **Code areas:** customer address DTOs (`customer-mobile/src/types/api.ts:235-254`), the `CustomerAddress`, `Store` and `DeliveryAssignment` entities, `GetFareQuoteQuery.cs:57-59`, `GeofenceEvaluator.cs`, `SelfQueries.cs:89-116`, `customer-mobile/src/hooks/useCatalog.ts:172-179`, `PickupCommands.cs:52-120,336-470`, `RiderRanker`, and a new `IGeocoder` port.
- **Dependencies:** 3.4, 2.2 (capabilities as features). **Complexity:** L (12 R4 = L, R5 = M; architect: M location + S mobile).
- **Risk / priority:** High for logistics ("P0 for the logistics vertical"), P1 for laundry. Courier must not be sold until this lands ([01b](specialists/01b-architect-challenge-review.md) M12 item 7).
- **Acceptance criteria:**
  - an address saved with coordinates (from the device or the geocoder) lets the quote succeed;
  - a leg copies the geo;
  - the geofence fires within 150 m;
  - an unserviceable pincode returns 422;
  - slots are filtered to the resolved store;
  - the dispatch radius is configurable.
- **Required tests:** MOB-L14 and MOB-L15.
- **Rollback / migration:**
  - Geocoding sends customer addresses to a third party, so add the provider to the DPDP processor register.
  - Cache geocodes per address, never per request.
  - Existing addresses have no coordinates: geocode them lazily on next use.

### 3.6 DB tenant backstops
- **Findings addressed:**
  - **SA-DB-004** (closes SA-TEN-011);
  - **SA-DB-013** (closes SA-TEN-013);
  - **SA-DB-019**;
  - **SA-DB-020** (a product decision);
  - **SA-AUTHZ-015**.
- **Code areas:** new migrations (parents get `UNIQUE (brand_id, id)`; composite FKs on orders ↔ customers/stores/franchises, payments ↔ orders, order_items ↔ orders and pickup_requests ↔ stores/customers; `security_barrier` views over the 7 `analytics` MVs, with direct SELECT revoked; partial uniques where reuse is required); `GetDailyStoreRevenue.cs`, `GetDashboard.cs`; `UserConfiguration.cs:47-48`; `GetPartnerBookingTrack.cs:31-33`.
- **Dependencies:** 1.1, 1.6. **Complexity:** M. **Risk / priority:** Medium / P1 (defence in depth; QA-C notes parent RLS still blocks cross-tenant reads).
- **Acceptance criteria:**
  - a cross-brand FK insert fails (the T7a repro now fails);
  - a brand-A session sees only A rows through the analytics views (the T9 repro now returns 0 B rows);
  - partner queries carry an explicit `PartnerId` predicate.
- **Required tests:** ISO-8, ISO-9 and BYP-10.
- **Rollback / migration:** add the FKs `NOT VALID`, then `VALIDATE`. Find and repair existing cross-brand references first (T7a shows they can exist).

### 3.7 Coupon and promotion policy
- **Findings addressed:** **SA-SOLID-006**.
- **Code areas:** `CreateOrderCommand.cs:268-338`, `CustomerCouponHandlers.cs:59-150`, `CustomerPickupCommands.cs:46-100`, `CouponHandlers.cs`.
- **Dependencies:** 1.6 (guarded counters), 3.1 (commerce owns coupons). **Complexity:** M. **Risk / priority:** Medium / P2.
- **Acceptance criteria:** one pure `CouponEligibilityPolicy` is used by all three paths; first-order and eligibility rules are enforced; validate-apply loads the order from the DB.
- **Required tests:** BYP-6 and COUP-1.
- **Rollback / migration:** rounding unification may change totals by a paisa. Communicate it.

---

## 7. Phase 4 — White-label experiences

**Goal:** a new tenant self-onboards to a branded web and app experience without code changes (architect exit). **Depends on Phases 2 and 3.**

### 4.1 Unified provisioning service
- **Findings addressed:** **SA-ONB-005** (closes SA-VERT-009) and **SA-ONB-009**.
- **Code areas:** `core.Application/Identity/TenancyOrg/Brands/Commands/CreateBrand/CreateBrand.cs:17-49`, `Dtos/BrandDtos.cs:9-18`, `CompleteSignup.cs:97-235`, `TemplateProvisioner.cs`, `db/patches/phase0_multi_vertical.sql:75-99`.
- **Dependencies:** 2.1, 2.2, 3.3. **Complexity:** S–M (architect). **Risk / priority:** Medium / P2.
- **Acceptance criteria:**
  - self-signup and back-office creation share one `BrandProvisioner`;
  - an admin-created brand gets the template's vertical, features, own franchise and trial;
  - a request with no template returns 422;
  - a platform-only `ChangeBrandVertical` works before the first order and returns 409 after it.
- **Required tests:** ONB-T4, BT-2, BT-3 and BT-6.
- **Rollback / migration:** brands created by `CreateBrand` before this change are unprovisioned, so run an idempotent re-provision for them.

### 4.2 Branding and terminology
- **Findings addressed:** **SA-ONB-002**, **SA-API-013** (closes SA-ONB-006), **SA-FE-009**.
- **Code areas:**
  - `database_scripts/01_bc1_tenancy_org.sql:42-70`, `BrandDtos.cs:20-27`, `UpdateBrand.cs:15-35` (FindAsync under `rls_admin_only`);
  - `IFileStorageProvider` (logo);
  - `NotificationMappingService.cs:423-437`, `EmailTemplates.cs:14-49`, `SettingsMailer.cs:82-97`, `RazorpayPaymentGateway.cs:183`;
  - the `src/lib/terminology.ts` files in customer-mobile, rider-mobile and pos-web.
- **Dependencies:** 0.10 (object storage for logos), 3.3. **Complexity:** M. **Risk / priority:** High (ONB-002) / P1.
- **Acceptance criteria:**
  - an owner updates their own brand's branding and cannot update another's;
  - hex colours are validated;
  - public branding resolves per brand;
  - fallback messages use the brand's name and currency;
  - rendering with a salon pack shows no "garment" or "wash".
- **Required tests:** BR-1, BR-2, BR-4, BR-5 and BR-8.
- **Rollback / migration:** none.

### 4.3 Custom domains, go-live and public content
- **Findings addressed:**
  - **SA-ONB-001** (closes SA-OPS-013 and SA-TEN-009). Registry Medium; QA-B argued High; the orchestrator says raise it to High when Phase 4 starts.
  - **SA-ONB-011**.
  - **SA-TEN-006**.
- **Code areas:** `laundryghar.Gateway/Program.cs:45-55,283-316` (add `RequestHeaderOriginalHost`), `ServiceDefaults/Extensions.cs:267-283` (`XForwardedHost` with known proxies), `core.Infrastructure/Services/BrandResolver.cs:40,67-100`, dynamic CORS, `OnboardingCommands.cs:148-159`, `AddBrandDomain.cs:60-73`, `PublicEngagement.cs:29-45`, and a SECURITY DEFINER read function per public resource.
- **Dependencies:** 0.4 and 1.5 (forwarded headers done safely), 4.2, and a decision on OQ-6 (TLS automation). **Complexity:** M. **Risk / priority:** Medium (High at Phase 4 start) / P1.
- **Acceptance criteria:**
  - a request on a verified custom domain resolves to its brand upstream;
  - CORS allows verified domains;
  - go-live requires location and catalogue facts plus a priced item;
  - adding an unverified primary never demotes a verified one;
  - anonymous banners, app-config and slides return brand rows under RLS;
  - the behaviour for an unknown Host is pinned by a test. QA-B observed fall-through to the default `LG-MAIN`, and a product decision is needed.
- **Required tests:** BR-3, BR-6 and ONB-T5.
- **Rollback / migration:** gateway transform toggle.

### 4.4 Signup and wizard UI, white-label mobile pipeline
- **Findings addressed:** **SA-ONB-008** (closes SA-FE-007 and SA-FE-008), **SA-OPS-016**, **SA-ONB-012**.
- **Code areas:** `admin-web/src/App.tsx:57-116` (signup and wizard routes), `customer-mobile/app.config.ts:4-30,56`, `rider-mobile/app.config.ts:6`, `customer-mobile/eas.json:48-59`, `core.Application/Identity/WhiteLabel/Queries/GetAppConfig.cs:89-114`, `tests/core.Tests/WhiteLabel/AppIdentifierTests.cs:23-41`, and new EAS workflows.
- **Dependencies:** 1.2, 4.1, 4.2. Confirm current App Store and Play policy on per-tenant template apps before committing ([01b §1.3](specialists/01b-architect-challenge-review.md); the architect suggests a single host app as the default and per-tenant builds as a premium add-on). **Complexity:** L. **Risk / priority:** Medium / P2.
- **Acceptance criteria:**
  - e2e: signup → OTP → login → the wizard shows steps;
  - `app.config.ts` is driven by a JSON produced from `GetAppConfig`;
  - EAS project ids are real and OTA works;
  - two colliding brand codes get distinct app identifiers.
- **Required tests:** ONB-T6 and ONB-T7.
- **Rollback / migration:** persist `app_identifier` once per brand. **Do not** change identifiers of apps already published.

---

## 8. Phase 5 — New verticals & production hardening at scale

**Goal:** prove multi-vertical operation, and scale only on measured need. **Architect exit criteria:** a salon tenant books, serves and bills appointments end to end, and laundry is unaffected (parity suite).

### 5.1 Salon module
- **Findings addressed:** **SA-VERT-003** and **SA-DB-021**. The salon template is re-published only through the SA-VERT-002 operability gate.
- **Code areas:** `db/patches/phase4_salon_fulfillment_schema.sql:52-95` (now a migration), `database_scripts/04_bc4_order_lifecycle.sql:347-365`, `Service.cs` (`duration_minutes`), `operations.Application/Fulfillment/Salon/*`, a new availability service and appointment booking command.
- **Dependencies:** 3.3, 4.1. **Complexity:** XL. **Risk / priority:** High (target capability) / P2, only when salon is pursued.
- **Acceptance criteria:**
  - two transactions booking the same staff member for overlapping ranges: one fails (`EXCLUDE USING gist`, which needs `btree_gist`);
  - operating-hours and holiday rejections;
  - `app_user` has USAGE on `salon_fulfillment`;
  - an end-to-end booking → service → invoice works;
  - the laundry parity suite stays green.
- **Required tests:** CC-7 and VW-S1…VW-S3.
- **Rollback / migration:** additive schema.

### 5.2 Tiffin generator, courier GA, customer live tracking
- **Findings addressed:** none new. This task builds on the tiffin facet of SA-VERT-002 (SA-ARCH-004), courier GA on 3.5, offer mode on 3.4, and the "rider approaching" view (12 R13, part of SA-MOB-014).
- **Dependencies:** 3.4, 3.5, 4.1. **Complexity:** L. **Acceptance:** a `DeliverySchedule` generator writes the occurrence ledger idempotently; a courier quote and dispatch work on real coordinates; the customer sees a coarse rider location only during their own active leg.
- **Required tests:** VW-T1 and MOB-L16.

### 5.3 Scale-out hardening (on measured need)
- **Findings addressed:** **SA-OPS-015**, **SA-DB-016** and **SA-DB-017**.
- **Code areas:** `OutputCaching.cs:27-29`, `TokenVersionStore.cs:16`, `BrandStatusStore.cs:15`, `PolicyCache.cs:36`, `FeatureCatalog.cs:21`, `SharedDataModel/DependencyInjection.cs:60-96`, `RlsConnectionInterceptor.cs`, `0031_subbrand_scope_rls.up.sql:60-110`.
- **Dependencies:** 1.8, 1.12 (measurement). **Complexity:** M. **Risk / priority:** Medium / P2.
- **Acceptance criteria:** pool size per host and a documented connection budget; Redis output cache and rate limits before running more than one replica; "session pooling only" documented, or `set_config(...,true)` inside explicit transactions; a plan test shows an InitPlan, not a per-row plpgsql call.
- **Required tests:** ISO-10 (pool reuse) and PERF-1 (plan test).

### 5.4 ABAC activation decision
- **Findings addressed:** **SA-AUTHZ-008**.
- **Code areas:** `AbacOptions.cs:15`, `AbacAuthorizationHandler.cs:73-103`, `AbacAuthorizationService`.
- **Dependencies:** 1.3 (SA-DB-014 fixed). **Complexity:** M. Per the architect, activate ABAC only if declarative brand rules are still needed after the Phase 0–3 guards; the exploitable gaps were missing guards, not a missing engine ([01b §4.4](specialists/01b-architect-challenge-review.md)).
- **Acceptance criteria:** shadow mode for one module (commerce) reports parity with RBAC; in enforce mode, an evaluator exception denies.
- **Required tests:** ABAC-4 and ABAC-5.

---

## Coverage check

All Critical, High and Medium canonical findings, with their registry phase and the task(s) that close them. A † marks a placement that differs from the registry phase (see [§1](#placement-deviations-from-the-registry-phase-field)). Generated from the registry; 122 rows.

| Finding | Severity | Registry phase | Roadmap task(s) | Duplicates closed | Title |
|---|---|---|---|---|---|
| [SA-API-001](../../FINDINGS.md#sa-api-001) | Critical | P0 | 0.4 | SA-OPS-001 | Production auth rate limiter collapses into one global bucket for all tenants (gateway IP) |
| [SA-AUTHZ-001](../../FINDINGS.md#sa-authz-001) | Critical | P0 | 0.3 | — | Any `users.create` holder can create a `platform_admin` account (unauthenticated → platform… |
| [SA-TEN-001](../../FINDINGS.md#sa-ten-001) | Critical | P0 | 0.7 | SA-AUTHZ-006, SA-DB-001 | Migration 0031's restrictive scope policy denies ALL rows (and all audited writes) for custo… |
| [SA-API-007](../../FINDINGS.md#sa-api-007) | High | P0 | 0.6 | SA-SOLID-002 | Customer online payment lifecycle is broken end to end (captured money can be ignored; order… |
| [SA-API-008](../../FINDINGS.md#sa-api-008) | High | P0 | 0.6 | — | Cancellation refunds are queued but never executed |
| [SA-API-009](../../FINDINGS.md#sa-api-009) | High | P0 | 0.6 | SA-DB-006 | Admin refund calls Razorpay inside a retried DB transaction; cumulative cap is racy |
| [SA-API-012](../../FINDINGS.md#sa-api-012) | High | P0 | 0.5 | SA-SOLID-007 | Notification worker sends every tenant's WhatsApp/SMS with one arbitrary tenant's credentials |
| [SA-ARCH-013](../../FINDINGS.md#sa-arch-013) | High | P0 | 0.3 + 1.3 | — | Platform control plane is not separated from the tenant plane; global authority is a single… |
| [SA-ARCH-014](../../FINDINGS.md#sa-arch-014) | High | P0 | 0.7 + 1.3 | — | Tenant context is implemented three times and per lane, and the implementations diverge |
| [SA-AUTHZ-002](../../FINDINGS.md#sa-authz-002) | High | P0 | 0.3 | — | Identity write handlers lack target-rank and target-scope guards (in-brand account takeover) |
| [SA-AUTHZ-003](../../FINDINGS.md#sa-authz-003) | High | P0 | 0.3 | SA-TEN-004 | `GrantMembership` accepts any user id platform-wide (cross-tenant attachment → cross-tenant… |
| [SA-AUTHZ-004](../../FINDINGS.md#sa-authz-004) | High | P0 | 0.3 | — | No permission ceiling on role edits and user overrides; platform-plane handlers rely on perm… |
| [SA-DB-002](../../FINDINGS.md#sa-db-002) | High | P0 | 0.11 + 1.1 | SA-ARCH-008, SA-SUB-020, SA-VERT-010 | The documented fresh-build path cannot reproduce the production schema; re-running it silent… |
| [SA-DB-012](../../FINDINGS.md#sa-db-012) | High | P0 | 0.7 | — | Customer-identity and salon RLS policies use a raw uuid cast that throws on an empty brand G… |
| [SA-FE-001](../../FINDINGS.md#sa-fe-001) | High | P0 | 0.12 | — | admin-web production image bakes only 3 of its 9 API base URLs |
| [SA-MOB-001](../../FINDINGS.md#sa-mob-001) | High | P0 | 0.8 | — | Rider task status endpoint has no state machine: any leg can be moved to any status, revivin… |
| [SA-OPS-003](../../FINDINGS.md#sa-ops-003) | High | P0 | 0.10 | SA-API-017 | Uploaded files live in the container's `/tmp` and are lost on every redeploy |
| [SA-OPS-004](../../FINDINGS.md#sa-ops-004) | High | P0 | 0.10 | SA-MOB-012 | pg_partman maintenance for orders/audit/process/notification/decision logs is scheduled only… |
| [SA-QC-001](../../FINDINGS.md#sa-qc-001) | High | P0 | 0.6 | — | Admin refund API contract ("gateway"/"wallet") violates the `refund_type` CHECK; the Razorpa… |
| [SA-TEN-002](../../FINDINGS.md#sa-ten-002) | High | P0 | 0.7 | SA-AUTHZ-005 | Commerce host never sets `app.current_customer_id`, so customer-level RLS on payments, walle… |
| [SA-TEN-003](../../FINDINGS.md#sa-ten-003) | High | P0 | 0.9 | — | A suspended brand (including a ToS or manual suspension) can lift its own suspension through… |
| [SA-VERT-002](../../FINDINGS.md#sa-vert-002) | High | P0 | 0.12 (+ 5.1 re-publish) | SA-ARCH-004 | Salon and tiffin templates are publicly sellable at signup, but neither vertical is operable |
| [SA-API-002](../../FINDINGS.md#sa-api-002) | High | P1 | 1.5 | SA-AUTHZ-010, SA-OPS-002, SA-TEN-005 | Gateway rate-limit partition is attacker-controlled (bypass and targeted tenant throttling) |
| [SA-API-004](../../FINDINGS.md#sa-api-004) | High | P1 | 1.6 | SA-DB-011 | POS CreateOrder idempotency is check-then-act on jsonb with no unique constraint (duplicate… |
| [SA-API-005](../../FINDINGS.md#sa-api-005) | High | P1 | 1.6 | SA-DB-008 | No optimistic concurrency anywhere; balances and counters are lost-update prone |
| [SA-DB-005](../../FINDINGS.md#sa-db-005) | High | P1 | 1.4 | — | Identity tables without RLS: cross-tenant role grants and PII/token reads are possible at th… |
| [SA-MOB-002](../../FINDINGS.md#sa-mob-002) | High | P1 | 1.9 | SA-DB-022 | One job can be held by two riders: no uniqueness, locking or status check on assignment crea… |
| [SA-MOB-003](../../FINDINGS.md#sa-mob-003) | High | P1 | 1.9 | — | Proof-of-delivery OTP is never generated, so the OTP gate is inert; verification has no atte… |
| [SA-OPS-006](../../FINDINGS.md#sa-ops-006) | High | P1 | 0.2 † (partial) + 1.2 | — | Release images ship from a red `main`; no deploy, migration or approval stage |
| [SA-SOLID-003](../../FINDINGS.md#sa-solid-003) | High | P2 | 2.4 (Phase 0 if royalties run in prod) | — | Payment status is an unconstrained string. Royalty revenue filters on `"completed"`, which t… |
| [SA-SUB-001](../../FINDINGS.md#sa-sub-001) | High | P2 | 2.1 | — | Self-serve trials never end: `trialing` brand subscriptions are never converted, invoiced or… |
| [SA-SUB-002](../../FINDINGS.md#sa-sub-002) | High | P2 | 2.1 | SA-API-011 | Paying an overdue (`past_due`) brand invoice via Razorpay is ignored, and a payment link can… |
| [SA-SUB-003](../../FINDINGS.md#sa-sub-003) | High | P2 | 2.1 | — | A brand subscription never returns from `past_due` to `active`, so after one late payment re… |
| [SA-SUB-004](../../FINDINGS.md#sa-sub-004) | High | P2 | 2.1 | SA-API-010 | The brand renewal pass runs outside a trusted worker scope, so RLS hides all subscriptions a… |
| [SA-SUB-006](../../FINDINGS.md#sa-sub-006) | High | P2 | 2.2 | — | Subscription state and entitlements are disconnected: features are granted without payment a… |
| [SA-SUB-007](../../FINDINGS.md#sa-sub-007) | High | P2 | 2.3 | — | No tenant-facing billing: owners cannot see their plan or invoices, cannot pay or upgrade, a… |
| [SA-FE-004](../../FINDINGS.md#sa-fe-004) | High | P3 | 1.11 † (client) + 3.2 | — | Admin order management hardcodes the laundry state machine and ignores the server's `allowed… |
| [SA-MOB-004](../../FINDINGS.md#sa-mob-004) | High | P3 | 3.5 | — | No geocoding or coordinate capture anywhere: geofence, distance-aware dispatch, coordinate n… |
| [SA-SOLID-001](../../FINDINGS.md#sa-solid-001) | High | P3 | 3.2 | SA-API-006 | Order status transitions are implemented separately in 5 write paths and have diverged (miss… |
| [SA-VERT-001](../../FINDINGS.md#sa-vert-001) | High | P3 | 3.3 | SA-ARCH-003, SA-ONB-004, SA-SOLID-004 | Order creation ignores the brand's vertical; every non-parcel order is a laundry `process_de… |
| [SA-VERT-004](../../FINDINGS.md#sa-vert-004) | High | P3 | 3.3 | — | Invoices hardcode laundry tax identity (SAC 999712, "Laundry & Dry-Cleaning Services") and l… |
| [SA-ONB-002](../../FINDINGS.md#sa-onb-002) | High | P4 | 4.2 | — | Tenant branding (logo, colours, theme) has no usable write path and no client consumes it |
| [SA-VERT-003](../../FINDINGS.md#sa-vert-003) | High | P5 | 5.1 | — | No appointment-grade scheduling: no staff availability, service duration, operating-hours en… |
| [SA-DB-003](../../FINDINGS.md#sa-db-003) | Medium | P0 | 0.9 | — | SECURITY DEFINER brand-lifecycle functions are callable by `app_user` with any brand id; `pu… |
| [SA-MOB-005](../../FINDINGS.md#sa-mob-005) | Medium | P0 | 0.7 | — | Customer can attach another customer's address to a pickup request (IDOR); the rider is disp… |
| [SA-OPS-010](../../FINDINGS.md#sa-ops-010) | Medium | P0 | 0.10 | — | Backup/DR is daily logical dumps only, with no PITR, no encryption and no proven schedule |
| [SA-QA-001](../../FINDINGS.md#sa-qa-001) | Medium | P0 | 0.3 | — | InviteUser is not atomic: the user is committed before the membership guards run |
| [SA-QC-003](../../FINDINGS.md#sa-qc-003) | Medium | P0 | 0.10 | — | Stale pg_partman config for `order_lifecycle.process_logs` makes `partman.run_maintenance_pr… |
| [SA-API-003](../../FINDINGS.md#sa-api-003) | Medium | P1 | 1.7 | SA-ARCH-005, SA-SOLID-005 | Validation pipeline not wired: 40 FluentValidation validators never execute; CQRS behaviors… |
| [SA-API-015](../../FINDINGS.md#sa-api-015) | Medium | P1 | 1.8 | — | Watermark cursors can skip events permanently (notifications, loyalty earn) |
| [SA-API-016](../../FINDINGS.md#sa-api-016) | Medium | P1 | 1.7 | — | Unbounded `pageSize` on list endpoints |
| [SA-API-018](../../FINDINGS.md#sa-api-018) | Medium | P1 | 1.6 | SA-MOB-018 | Customer app does not send an idempotency key for booking, so the server guard is unused |
| [SA-API-019](../../FINDINGS.md#sa-api-019) | Medium | P1 | 1.6 | — | Admin/POS-created pickups ignore slot capacity, but rejection releases capacity |
| [SA-API-020](../../FINDINGS.md#sa-api-020) | Medium | P1 | 1.4 | — | Refresh-token rotation is not atomic |
| [SA-ARCH-006](../../FINDINGS.md#sa-arch-006) | Medium | P1 | 1.8 | — | Outbox is a DB-polling integration with no broker, no typed contracts, and inconsistent cons… |
| [SA-ARCH-010](../../FINDINGS.md#sa-arch-010) | Medium | P1 | 0.2 † (scaffold) + 1.10 | — | Commerce (payments, wallets, subscriptions, billing workers) has no test project |
| [SA-AUTHZ-007](../../FINDINGS.md#sa-authz-007) | Medium | P1 | 1.4 | — | Scope check is decoupled from permission source (permission union × node union = scope ampli… |
| [SA-AUTHZ-009](../../FINDINGS.md#sa-authz-009) | Medium | P1 | 1.4 | — | User suspension/deactivation does not revoke live sessions; revocation check fails open |
| [SA-DB-009](../../FINDINGS.md#sa-db-009) | Medium | P1 | 1.8 | — | Background workers claim rows without a guarded update or SKIP LOCKED: duplicate publish/cha… |
| [SA-DB-010](../../FINDINGS.md#sa-db-010) | Medium | P1 | 1.6 | — | Coupon usage limits enforced only in the application |
| [SA-DB-014](../../FINDINGS.md#sa-db-014) | Medium | P1 | 1.3 | — | The ABAC store's raw connections run without tenant GUCs: brand policies, roles and entitlem… |
| [SA-DB-018](../../FINDINGS.md#sa-db-018) | Medium | P1 | 1.1 | — | 69 FKs without supporting indexes; 55 redundant indexes |
| [SA-FE-002](../../FINDINGS.md#sa-fe-002) | Medium | P1 | 1.11 | — | admin-web logout does not end the session after any page reload |
| [SA-FE-003](../../FINDINGS.md#sa-fe-003) | Medium | P1 | 1.11 | — | pos-web stores the refresh token in localStorage, and its refresh path ignores the HttpOnly… |
| [SA-FE-005](../../FINDINGS.md#sa-fe-005) | Medium | P1 | 1.11 | — | Brand switching and logout do not scope or clear the client cache, so data from one brand sh… |
| [SA-FE-010](../../FINDINGS.md#sa-fe-010) | Medium | P1 | 1.11 | — | Customer booking falls back to hardcoded demo garments and prices in production |
| [SA-FE-011](../../FINDINGS.md#sa-fe-011) | Medium | P1 | 1.2 | SA-MOB-017 | Client quality gates: mobile CI is red, pos-web is outside CI/CD, web apps have no unit tests |
| [SA-MOB-007](../../FINDINGS.md#sa-mob-007) | Medium | P1 | 1.9 (+ 3.4) | — | Riders get no push notification for new or changed assignments; the app learns of work only… |
| [SA-MOB-008](../../FINDINGS.md#sa-mob-008) | Medium | P1 | 1.9 | — | Rider offline queue treats server rejections as "offline", poisons itself, and drops failure… |
| [SA-MOB-009](../../FINDINGS.md#sa-mob-009) | Medium | P1 | 1.9 | — | Background location task may run without hydrated auth and trigger `logout()`, wiping stored… |
| [SA-MOB-010](../../FINDINGS.md#sa-mob-010) | Medium | P1 | 1.9 | — | Location ping ingestion is unvalidated and not gated by duty/assignment; client timestamps d… |
| [SA-MOB-011](../../FINDINGS.md#sa-mob-011) | Medium | P1 | 1.9 | — | Deactivating a rider does not stop tracking, task access or open legs |
| [SA-OPS-005](../../FINDINGS.md#sa-ops-005) | Medium | P1 | 1.8 | SA-API-014, SA-ARCH-007 | Background jobs assume a single commerce instance; two notification lanes can double-send or… |
| [SA-OPS-007](../../FINDINGS.md#sa-ops-007) | Medium | P1 | 1.1 | — | Migrations are a manual step; CI never applies or rolls them back |
| [SA-OPS-008](../../FINDINGS.md#sa-ops-008) | Medium | P1 | 1.12 | SA-TEN-014 | Logs, traces and metrics are not tenant-aware, and production exports nothing |
| [SA-OPS-009](../../FINDINGS.md#sa-ops-009) | Medium | P1 | 1.2 | — | Secrets-provider abstraction is documented as done but absent from code |
| [SA-OPS-012](../../FINDINGS.md#sa-ops-012) | Medium | P1 | 1.2 | — | Compose cannot pull the CI-built images, and pos-web has no deploy path |
| [SA-OPS-014](../../FINDINGS.md#sa-ops-014) | Medium | P1 | 1.5 | — | Noisy-neighbour controls are global, not per tenant or plan |
| [SA-QB-002](../../FINDINGS.md#sa-qb-002) | Medium | P1 | 0.2 † (partial) + 1.1 | — | Integration tests report "Passed" when Docker is missing, and migration and rollback coverag… |
| [SA-QC-002](../../FINDINGS.md#sa-qc-002) | Medium | P1 | 1.6 | — | Sub-brand RLS (0031) makes the per-brand `COUNT(*)+1` number generators scope-blind: routine… |
| [SA-SOLID-008](../../FINDINGS.md#sa-solid-008) | Medium | P1 | 1.8 | — | The `LoggingChannelSender` null object breaks the `IChannelSender` contract in production, s… |
| [SA-TEN-007](../../FINDINGS.md#sa-ten-007) | Medium | P1 | 1.3 | SA-DB-015 | The database layer trusts the application completely: self-settable bypass GUC, blanket plat… |
| [SA-TEN-008](../../FINDINGS.md#sa-ten-008) | Medium | P1 | 1.3 | SA-AUTHZ-014, SA-SUB-017, SA-SUB-018 | Suspension, cancellation and deletion are HTTP-only and partial gates |
| [SA-TEN-010](../../FINDINGS.md#sa-ten-010) | Medium | P1 | 1.1 | — | Isolation test suite does not exercise the real runtime path |
| [SA-TEN-015](../../FINDINGS.md#sa-ten-015) | Medium | P1 | 1.2 | — | The runtime DB role password is hard-coded and re-applied by patches |
| [SA-AUTHZ-011](../../FINDINGS.md#sa-authz-011) | Medium | P2 | 2.2 | SA-SUB-010 | Plan entitlements are enforced only on the staff lane (token stripping); customer/partner la… |
| [SA-ONB-007](../../FINDINGS.md#sa-onb-007) | Medium | P2 | 2.5 | — | GSTIN captured at signup never reaches invoices |
| [SA-ONB-010](../../FINDINGS.md#sa-onb-010) | Medium | P2 | 2.5 | — | Signup has no plan choice, silently tolerates a missing plan, and has no handler or integrat… |
| [SA-QB-001](../../FINDINGS.md#sa-qb-001) | Medium | P2 | 2.4 (with SA-SOLID-003) | — | COD and online payment rows carry no `franchise_id`, so royalty under-counts even after SA-S… |
| [SA-SUB-005](../../FINDINGS.md#sa-sub-005) | Medium | P2 | 2.1 | — | Brand platform billing worker is disabled by default and undocumented |
| [SA-SUB-008](../../FINDINGS.md#sa-sub-008) | Medium | P2 | 2.2 | — | Plan limits / quotas do not exist (a-brand) or are not enforced (a-franchise); Starter "1 lo… |
| [SA-SUB-009](../../FINDINGS.md#sa-sub-009) | Medium | P2 | 2.2 | — | Five sellable features gate nothing (`wallet`, `loyalty`, `online_payments`, `item_tracking`… |
| [SA-SUB-011](../../FINDINGS.md#sa-sub-011) | Medium | P2 | 2.5 | — | Franchise SaaS subscriptions (ADR-010 "module B") are data model + CRUD only, never billed o… |
| [SA-SUB-012](../../FINDINGS.md#sa-sub-012) | Medium | P2 | 2.2 | — | Plan-change billing defects: changing tier during a trial bills the trial window at full pri… |
| [SA-SUB-013](../../FINDINGS.md#sa-sub-013) | Medium | P2 | 2.3 | — | Brand platform invoices are not GST invoices and carry no payment record |
| [SA-SUB-014](../../FINDINGS.md#sa-sub-014) | Medium | P2 | 2.1 | — | Brand dunning makes no charge attempts, sends no notices, and its state changes are not audited |
| [SA-SUB-015](../../FINDINGS.md#sa-sub-015) | Medium | P2 | 2.1 | — | (b) Recurring mandate charge integration is suspect: likely non-existent Razorpay endpoint/h… |
| [SA-API-021](../../FINDINGS.md#sa-api-021) | Medium | P3 | 3.3 | — | Single-region and laundry-only assumptions baked into the API |
| [SA-ARCH-001](../../FINDINGS.md#sa-arch-001) | Medium | P3 | 3.1 | — | Three "services" share one EF model, one database role and overlapping table ownership (dist… |
| [SA-AUTHZ-012](../../FINDINGS.md#sa-authz-012) | Medium | P3 | 3.3 | SA-FE-006, SA-MOB-015, SA-ONB-003, SA-VERT-005 | Vertical (business-type) boundary is not enforced server-side |
| [SA-DB-004](../../FINDINGS.md#sa-db-004) | Medium | P3 | 3.6 | SA-TEN-011 | No composite tenant foreign keys: rows can reference another tenant's parents |
| [SA-DB-007](../../FINDINGS.md#sa-db-007) | Medium | P3 | 1.6 † | SA-TEN-012 | Globally unique business numbers generated per tenant: cross-tenant unique violations and an… |
| [SA-DB-013](../../FINDINGS.md#sa-db-013) | Medium | P3 | 3.6 | SA-TEN-013 | Tables with tenant data that RLS cannot protect: analytics materialized views |
| [SA-MOB-006](../../FINDINGS.md#sa-mob-006) | Medium | P3 | 3.4 | — | Offer→accept dispatch mode is not wired end-to-end (no rider UI, offers hidden from the task… |
| [SA-MOB-013](../../FINDINGS.md#sa-mob-013) | Medium | P3 | 3.5 | — | Serviceability, zones and service areas are not enforced anywhere in the booking or dispatch… |
| [SA-MOB-014](../../FINDINGS.md#sa-mob-014) | Medium | P3 | 3.4 (+ 5.2) | — | Customer tracking is a status timeline only, and pickup progress never reflects rider start… |
| [SA-MOB-016](../../FINDINGS.md#sa-mob-016) | Medium | P3 | 3.2 | — | Customer order cancellation does not cancel or release the order's delivery legs |
| [SA-SOLID-006](../../FINDINGS.md#sa-solid-006) | Medium | P3 | 3.7 | — | Coupon rules are implemented three times and have drifted. First-order and eligibility rules… |
| [SA-SOLID-009](../../FINDINGS.md#sa-solid-009) | Medium | P3 | 3.1 | SA-ARCH-002 | Anemic, fully mutable shared data model. Bounded contexts write each other's tables, and Dom… |
| [SA-SOLID-010](../../FINDINGS.md#sa-solid-010) | Medium | P3 | 1.10 † (tests) + 3.2 (refactor) | — | God-handlers with no direct tests: `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `OAuth` |
| [SA-VERT-006](../../FINDINGS.md#sa-vert-006) | Medium | P3 | 3.3 | — | Notification templates are a hardcoded laundry/logistics status switch; the vertical-tagged… |
| [SA-VERT-007](../../FINDINGS.md#sa-vert-007) | Medium | P3 | 3.3 | — | Clients are laundry-shaped and only superficially vertical-aware; the order DTO does not exp… |
| [SA-API-013](../../FINDINGS.md#sa-api-013) | Medium | P4 | 4.2 | SA-ONB-006 | Hard-coded "Laundry Ghar" brand identity in customer-facing messages and emails |
| [SA-FE-009](../../FINDINGS.md#sa-fe-009) | Medium | P4 | 4.2 | — | Server terminology is wired only in admin-web; customer, rider and POS ship hardcoded laundr… |
| [SA-ONB-001](../../FINDINGS.md#sa-onb-001) | Medium | P4 | 4.3 | SA-OPS-013, SA-TEN-009 | "Go live" and custom domains are database rows that no request path can reach |
| [SA-ONB-005](../../FINDINGS.md#sa-onb-005) | Medium | P4 | 4.1 | SA-VERT-009 | Back-office `CreateBrand` creates an unprovisioned tenant with an implicit vertical |
| [SA-ONB-008](../../FINDINGS.md#sa-onb-008) | Medium | P4 | 4.4 | SA-FE-007, SA-FE-008 | No client implements signup, the provider wizard, branding or the white-label app config; mo… |
| [SA-OPS-016](../../FINDINGS.md#sa-ops-016) | Medium | P4 | 4.4 | — | Mobile release pipeline is not operational (OTA, CI, white-label) |
| [SA-TEN-006](../../FINDINGS.md#sa-ten-006) | Medium | P4 | 4.3 | — | Anonymous public tenant content (banners, app-config, onboarding slides) returns nothing und… |
| [SA-AUTHZ-008](../../FINDINGS.md#sa-authz-008) | Medium | P5 | 5.4 | — | ABAC engine is inert; attribute-based rules are hand-coded per handler |
| [SA-DB-017](../../FINDINGS.md#sa-db-017) | Medium | P5 | 5.3 | — | The per-row plpgsql scope predicate makes RLS scans about 8× slower |
| [SA-OPS-015](../../FINDINGS.md#sa-ops-015) | Medium | P5 | 5.3 | — | Horizontal-scaling blockers (DB-Q8 infrastructure view) |

### Low and Informational findings (backlog grouping)

The registry P1 Lows are inside Phase 1 tasks. The rest form a backlog that is picked up by the task in the same code area.

| Finding | Severity | Registry phase | Roadmap task(s) | Duplicates closed | Title |
|---|---|---|---|---|---|
| [SA-API-023](../../FINDINGS.md#sa-api-023) | Low | P1 | 1.2 | — | Development credentials and OTP master codes committed to appsettings |
| [SA-API-025](../../FINDINGS.md#sa-api-025) | Low | P1 | 1.12 | — | Plaintext email addresses in logs |
| [SA-AUTHZ-013](../../FINDINGS.md#sa-authz-013) | Low | P1 | 1.4 | — | Identity-axis (`user_type`) gates where permission gates belong; platform-scoped dispatch se… |
| [SA-FE-014](../../FINDINGS.md#sa-fe-014) | Low | P1 | 1.11 | — | WebMCP exposes customer search and order-status mutation to in-browser AI agents in producti… |
| [SA-FE-015](../../FINDINGS.md#sa-fe-015) | Low | P1 | 1.11 | — | Web routers have no error boundary |
| [SA-FE-016](../../FINDINGS.md#sa-fe-016) | Low | P1 | 1.11 | — | Shared-device residue after logout (POS cart PII, rider offline queue, mobile query caches) |
| [SA-MOB-020](../../FINDINGS.md#sa-mob-020) | Low | P1 | 1.9 | — | Customer slot listing has no in-handler brand predicate (RLS-only), contrary to its comment |
| [SA-MOB-021](../../FINDINGS.md#sa-mob-021) | Low | P1 | 1.9 | — | Map provider keys stored unencrypted and echoed to every settings reader |
| [SA-OPS-011](../../FINDINGS.md#sa-ops-011) | Low | P1 | 1.12 | SA-API-024 | Health checks never check the database |
| [SA-OPS-017](../../FINDINGS.md#sa-ops-017) | Low | P1 | 1.2 | — | Container and supply-chain hygiene |
| [SA-OPS-018](../../FINDINGS.md#sa-ops-018) | Informational | P1 | 1.2 | SA-ARCH-012, SA-VERT-011 | Spec claims infrastructure that does not exist (broker, Redis, Hangfire, Serilog, S3) |
| [SA-ORC-002](../../FINDINGS.md#sa-orc-002) | Low | P1 | 1.4 | — | No default-deny authorization FallbackPolicy on any host; endpoint coverage depends on per-e… |
| [SA-QA-002](../../FINDINGS.md#sa-qa-002) | Low | P1 | 1.5 | — | Gateway rate-limit unit tests assert the vulnerable behaviour |
| [SA-QB-003](../../FINDINGS.md#sa-qb-003) | Low | P1 | 0.4 † | — | Production guidance for ForwardedHeaders contradicts itself, and both options are unsafe as… |
| [SA-SOLID-014](../../FINDINGS.md#sa-solid-014) | Low | P1 | 1.7 | — | The pickup flow enforces minimum order value and sets the expected COD from client-supplied… |
| [SA-SUB-016](../../FINDINGS.md#sa-sub-016) | Low | P1 | 1.6 | — | Webhook idempotency is check-then-act on status only: no event dedupe, no lock or concurrenc… |
| [SA-FE-012](../../FINDINGS.md#sa-fe-012) | Low | P2 | 2.6 | — | Customer payments, wallet top-up and packages are not implemented in the UI |
| [SA-SUB-019](../../FINDINGS.md#sa-sub-019) | Informational | P2 | 2.2 | — | Entitlement change propagation and fail-open behaviour (informational) |
| [SA-API-022](../../FINDINGS.md#sa-api-022) | Low | P3 | 3.1 | — | Inconsistent error contract; no API versioning |
| [SA-ARCH-009](../../FINDINGS.md#sa-arch-009) | Low | P3 | 3.1 | — | Layering is nominal: `Utilities` is a cross-cutting god-library; composition roots are copy-… |
| [SA-ARCH-011](../../FINDINGS.md#sa-arch-011) | Low | P3 | 3.1 | — | MCP downstream URLs are not wired for AppHost or compose; synchronous core→operations coupling |
| [SA-AUTHZ-015](../../FINDINGS.md#sa-authz-015) | Low | P3 | 3.6 | — | Partner isolation is a single (RLS-only) layer |
| [SA-DB-019](../../FINDINGS.md#sa-db-019) | Low | P3 | 3.6 | — | Soft-delete tables: no partial unique constraints |
| [SA-DB-020](../../FINDINGS.md#sa-db-020) | Low | P3 | 3.6 | — | Staff identity is globally unique by email and phone |
| [SA-FE-013](../../FINDINGS.md#sa-fe-013) | Low | P3 | 3.3 | — | admin-web `/settings` gate drifts from server authorization; the route map is hand-synced |
| [SA-MOB-019](../../FINDINGS.md#sa-mob-019) | Low | P3 | 3.4 | — | Riders retain indefinite access to customer PII for historical tasks |
| [SA-ORC-001](../../FINDINGS.md#sa-orc-001) | Low | P3 | 3.4 | — | Admin rider-track and live-location reads are franchise-scoped, not store-scoped |
| [SA-SOLID-011](../../FINDINGS.md#sa-solid-011) | Low | P3 | 3.4 | — | Dispatch and assignment logic is duplicated across bounded contexts and has drifted |
| [SA-SOLID-012](../../FINDINGS.md#sa-solid-012) | Low | P3 | 3.1 | — | Dependency inversion holds by convention only. Application reaches ASP.NET Core and Npgsql t… |
| [SA-SOLID-013](../../FINDINGS.md#sa-solid-013) | Low | P3 | 3.1 | — | Settings-resolved providers lose their logger (`_logger as ILogger<OtherType>` always evalua… |
| [SA-VERT-008](../../FINDINGS.md#sa-vert-008) | Low | P3 | 3.3 | — | Catalog discriminator is inert, and the service model is laundry-shaped |
| [SA-ONB-009](../../FINDINGS.md#sa-onb-009) | Low | P4 | 4.1 | — | The vertical cannot be changed through any governed path, and a raw change would not re-prov… |
| [SA-ONB-011](../../FINDINGS.md#sa-onb-011) | Low | P4 | 4.3 | — | `go-live` ignores wizard prerequisites, and adding a primary custom domain can strand the br… |
| [SA-ONB-012](../../FINDINGS.md#sa-onb-012) | Low | P4 | 4.4 | — | White-label app identifiers can collide between brands |
| [SA-DB-016](../../FINDINGS.md#sa-db-016) | Low | P5 | 5.3 | — | Pooled-connection tenant context is safe for EF, but rests on session-level GUCs |
| [SA-DB-021](../../FINDINGS.md#sa-db-021) | Low | P5 | 5.1 | — | No DB-level booking overlap prevention; the salon schema is inaccessible to app_user |

---

## Disagreements carried into this roadmap

| Topic | Positions | Effect on the roadmap |
|---|---|---|
| SA-API-003 severity | QA-A High (dead upload MIME/size validators); QA-B and registry Medium (DB CHECKs, `RequestSizeLimit`, attachment serving; 39 not 40 validators) | Kept in Phase 1 (1.7); upload validators are among its acceptance criteria. |
| SA-API-007 / SA-SOLID-002 | Registry High; QA-B Medium-latent (no client calls initiate or verify) | Kept in Phase 0 (0.6) because 0.7 and 2.6 make it live. 2.6 has a hard dependency on 0.6. |
| SA-ONB-001 severity | Registry Medium (Partially Verified, no tenant on custom domains); QA-B High | Phase 4; re-rate when 4.3 starts. |
| SA-DB-003 severity and fix order | 08b High; QA-C Medium (no foreign-id HTTP path; bypass GUC already self-settable) | Phase 0 brand check + later revoke (0.9). Full value only with 1.3 (SA-TEN-007). |
| SA-TEN-001 fix shape | 08b: DB-side customer arm; 02: token-use arm, no fake claim; 01b: customer scope node or exemption | Decided in 0.7 by the lane-matrix test. |
| DB-Q8 | 02 Fully (EF path); 08b, 11, QA-A, QA-C and 01b Partially | Phase 5 (5.3) for pooler compatibility; 1.3 for raw-connection context. |
| SA-MOB-011 scope | 12: deactivate leaves access; QA-C: deactivate is a false positive (soft delete + query filter), the gap is `UpdateRider` suspend/terminate | 1.9 targets `UpdateRider`. |
| SA-OPS-005 severity | 11 High; QA-B and registry Medium (single replica documented) | 1.8; must land before any multi-replica deployment. |
| SA-FE-003 severity | 09 High; QA-B Medium while pos-web is not deployed | 1.11 must precede the pos-web deploy in 1.2. |

## Observations for registry triage

These are not new findings. They are inconsistencies noticed while mapping the registry to tasks.
1. The registry `priority` and `phase` fields disagree for several findings. The specialist urgency was kept in `priority`; the orchestrator's dependency placement is in `phase`. Examples:
   - SA-DB-002: phase 0, priority "P1";
   - SA-SUB-001…004: phase 2, priority "P0";
   - SA-MOB-002 and SA-OPS-006: phase 1, priority "P0";
   - SA-QB-003: phase 1, priority "before the SA-API-001 fix";
   - SA-FE-004: phase 3, priority "P1".

   This roadmap follows `phase` except where the [deviation table](#placement-deviations-from-the-registry-phase-field) says otherwise.
2. SA-ONB-011's `status` field holds a fragment ("- Prerequisite bypass: Verified (code) - Primary-demotion collision: Suspected …") rather than a single label. That fragment also appears in the FINDINGS index row.
3. SA-ARCH-014's `priority` field ends with a stray "---".
