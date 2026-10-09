# 08 — Test Strategy and Acceptance Criteria

Audit date: 2026-10-09 · Companion to [07 — Remediation Roadmap](07-remediation-roadmap.md) · Sources: [FINDINGS](../../FINDINGS.md), [`findings-registry.json`](findings-registry.json), the specialist reports in [`specialists/`](specialists/), and the verification reports [10a](specialists/10a-qa-verification-security.md), [10b](specialists/10b-qa-verification-platform.md) and [10c](specialists/10c-qa-verification-db-mobile.md).

## Summary

- **What was executed in this audit** (§1; every item is labelled **EXECUTED**):
  - **Backend:** GitHub CI on `274b7af` passed core.Tests 137/137, operations.Tests 426/426 and operations.IntegrationTests 284/284.
  - **Web clients**, run locally on scratch copies: admin-web lint and build passed; pos-web `tsc` and lint passed.
  - **Mobile:** customer-mobile jest 170/170 with a **failing typecheck**. rider-mobile `npm ci` failed with **ERESOLVE**; after `--legacy-peer-deps`, jest passed 91/91 with a **failing typecheck**.
  - **SQL:** three independent sets of SQL reproductions on throwaway PostgreSQL 16 clusters, by the DB agent, QA-A (S1–S7) and QA-C (T1–T12).
- **What was not executed:** no .NET build or test, no Docker or Testcontainers, no HTTP request and no device run happened inside the audit container. Backend results come from CI logs.
- **Blind spots that matter most** (§2):
  - The integration suite reports "Passed" without Docker (SA-QB-002).
  - Only 13 of 33 migrations are exercised, each on a trimmed fixture.
  - The isolation suite sets 6 of 12 GUCs on hand-written DDL (SA-TEN-010).
  - Several tests **assert the vulnerable behaviour**: the gateway rate-limit tests (SA-QA-002) and `SubBrandScopeRlsTests`, which asserts that an unresolved `scope_nodes` denies everything, the exact mechanism of the Critical SA-TEN-001.
  - Commerce has no test project (SA-ARCH-010).
- **What this report proposes** (§3; every item is labelled **PROPOSED**): tests in 16 areas. Each names the findings it guards, its level, a target file (reusing the 10a T1–T21, 10b and 10c missing-test lists), and a pass criterion.
- **CI gates** (§4) and per-phase **exit gates** (§5) turn these tests into release conditions for the roadmap phases.

Test names such as `ISO-1` are labels for this plan only; they are not finding IDs. `10a-T1` refers to the 10a missing-test list, `06-T11` to Table 4 of [06](specialists/06-abac-rbac.md), and `QA-C T5` to a QA-C SQL reproduction.

---

## 1. Tests already run in this audit (EXECUTED)

### 1.1 Backend CI on `274b7af` (EXECUTED by GitHub Actions; read by QA-B and DevOps)

CI run 36294076412 (2026-09-27) tested SHA `274b7af`. Later commits on the audit branch touch only `docs/` ([10b](specialists/10b-qa-verification-platform.md) header; [11](specialists/11-devops.md) scope).

| Job | Result | What it proves | Source |
|---|---|---|---|
| Backend build + tests (job 108549585458) | **Passed.** core.Tests **137/137**, operations.Tests **426/426**, operations.IntegrationTests **284/284** in 2 min 39 s (Docker present on the runner) | The existing suites are green, including Testcontainers, so the early-return path was not taken on GitHub. It does **not** prove the findings are covered (see §2). | [10b](specialists/10b-qa-verification-platform.md) cmd 9; [11](specialists/11-devops.md) |
| admin-web lint + build (job 108549585404) | Passed | — | [11](specialists/11-devops.md) |
| Migration lint | Passed | Checks only that up/down files are paired; applies no migrations | [10a](specialists/10a-qa-verification-security.md) §3; `ci.yml:88-111` |
| rider-mobile (job 108549585491) | **Failed** at `npm ci` (ERESOLVE: `react-dom@19.2.8` needs `react ^19.2.8`; the lockfile has 19.2.3) | SA-FE-011 / SA-MOB-017 | [10b](specialists/10b-qa-verification-platform.md), [11](specialists/11-devops.md), [10c](specialists/10c-qa-verification-db-mobile.md) |
| customer-mobile (job 108549585512) | **Failed** at `tsc`: `app/_layout.tsx(11,8) TS2882 '../global.css'` | SA-FE-011 | same |
| Overall run | **Failed**. Every CI run on `main` failed or was cancelled. Release run 36294076432 **succeeded** on the same SHA | SA-OPS-006 (release not gated on CI) | [10b](specialists/10b-qa-verification-platform.md) cmd 8; [11](specialists/11-devops.md) |

### 1.2 Client suites run locally on scratch copies (EXECUTED by QA-B; Node 22.22.0, npm 10.9.4)

| Suite | Commands | Result | Source |
|---|---|---|---|
| admin-web | `npm ci`, `npm run lint`, `npm run build` | `ci` ok; lint **0 errors**, 12 warnings; build ok. **No unit tests exist.** The `e2e` script needs a live stack and was Not Tested. | [10b](specialists/10b-qa-verification-platform.md) test inventory |
| admin-web production bundle | `vite build` with only the 3 URLs the Dockerfile, compose and release bake in, then a grep for each gateway prefix | identity 2, catalog 1, orders 1; **commerce, finance, analytics, warehouse, logistics and engagement all 0**. This reproduces SA-FE-001. | [10b](specialists/10b-qa-verification-platform.md) cmd 11 |
| pos-web | `npm ci`, `npx tsc -b`, `npm run lint` | `ci` ok; tsc ok; lint **0 errors**, 2 warnings. **No tests; not in CI.** | [10b](specialists/10b-qa-verification-platform.md) |
| customer-mobile | `npm ci`, `npm run typecheck`, `npx jest --ci` | `ci` ok; **typecheck FAIL** (TS2882, same as CI); jest 11 suites, **170/170 passed** (re-run to confirm) | [10b](specialists/10b-qa-verification-platform.md); also [12](specialists/12-mobile-delivery-maps.md), [09](specialists/09-frontend-mobile.md) |
| rider-mobile | `npm ci` | **FAIL, ERESOLVE** (same as CI); passes only with `--legacy-peer-deps` | [10b](specialists/10b-qa-verification-platform.md) |
| rider-mobile (after `--legacy-peer-deps`) | `npm run typecheck`, `npx jest --ci` | **typecheck FAIL** (exit 2, same TS2882; CI never reaches this step); jest 8 suites, **91/91 passed** | [10b](specialists/10b-qa-verification-platform.md) |

QA-C did not run `npm ci`, `tsc` or jest itself ([10c](specialists/10c-qa-verification-db-mobile.md) "Not verified").

### 1.3 SQL reproductions (EXECUTED on throwaway PostgreSQL 16 clusters)

Every security and concurrency repro ran **as `app_user`** (NOSUPERUSER, NOBYPASSRLS), with GUCs set the way `RlsConnectionInterceptor` sets them. Each cluster was deleted afterwards.

**Recipe differences between the three sets:**
- **DB agent and QA-C** built the repository schema with `pg_partman` and `postgis` installed at OS level. They used the same two workarounds: data-assertion `DO $verify$` blocks were removed from two patches, demo seeds were skipped, and `harden_app_user_and_rls_bypass.sql` was re-applied.
- **QA-A** loaded verbatim extracts of migrations and patches into trimmed tables, so no extensions were needed.

**DB agent** ([08b](specialists/08b-database.md); PG 16.15, port 55432):

| Repro | Result | What it proved |
|---|---|---|
| `db/build_from_scratch.sh` | exit 0: 542 FKs, 61 `updated_at` triggers, 92 inert policies | The bootstrap runs, but applies only a fraction of the schema |
| `db/tools/migrate.sh up` | **failed at 0005**: `relation "identity_access.modules" does not exist` | SA-DB-002: the documented build path cannot reproduce the schema |
| Patches in git first-commit order, multiple passes; then migrations 0005–0033 | 135 patches applied; 29 migrations applied; `rls_proposal.sql` overwrote the hardened `kernel.rls_bypass()` | SA-DB-002 (bypass revert). Final schema: 162 tables, 136 with RLS, 190 policies, 1,649 indexes |
| Brand A/B isolation, SELECT/INSERT/UPDATE/DELETE | Held | **Positive control**: brand RLS is real for 126/126 brand tables |
| Customer GUCs (`scope_nodes` unresolved) | orders **0** (superuser sees 7), payments 0, stores 0; INSERT order/pickup → `rls_subbrand_scope` violation | SA-DB-001 → SA-TEN-001 |
| `kernel.export_brand('B')` and `purge_brand('B')` from a brand-A session | Export returned B's rows (7 tables); purge deleted every B row (rolled back); 22 DEFINER functions executable by `app_user` | SA-DB-003 |
| Cross-brand FK inserts | `INSERT orders(... B franchise/store/customer)` and `payments(order_id=B order)` succeeded | SA-DB-004 |
| Membership insert granting brand B's role from an A session | Succeeded | SA-DB-005 |
| Two concurrent 60.00 refunds on a 100.00 payment | Both committed; sum **120.00** | SA-DB-006 → SA-API-009 |
| Same-day expense number in brands A and B | B got `duplicate key … expenses_expense_number_key` | SA-DB-007 |
| Read-modify-write wallet race (+50 / −30 on 100) | Final **150**; correct value is 120 | SA-DB-008 → SA-API-005 |
| Two sessions claiming the same outbox event | Both `UPDATE 1` | SA-DB-009 |
| `customer_identities` with brand `''` and bypass `'true'` | `invalid input syntax for type uuid: ""` | SA-DB-012 |
| Brand-A session on `mv_daily_store_revenue` | 76 A rows and 76 B rows | SA-DB-013 |
| `EXPLAIN ANALYZE`, synthetic data | `count(*)` over 30k orders: 384 ms with RLS, 48 ms with bypass | SA-DB-017 (synthetic, relative evidence only) |
| Migration down-9/up round trip (0025–0033) | Restored identical policies, RLS state and index count | **Positive control**: `migrate.sh` is sound |

**QA-A** ([10a](specialists/10a-qa-verification-security.md) §1; port 55434):

| Scenario | Principal | Result | What it proved |
|---|---|---|---|
| S1 | Core/ops host, customer token (`scope_nodes='?'`) | orders 0, payments 0, stores 0; INSERT denied by `rls_subbrand_scope` | SA-TEN-001 |
| S2 | Commerce host, customer | payments 0 | SA-TEN-001 |
| S3 | Commerce host, brand admin (adapter has no `ScopeNodes`) | payments 0, orders 0 | SA-TEN-001 (commerce staff lane) |
| S4 | Ops host, brand admin, `scope_nodes=brand:A` | payments 2, orders 2 | Control: staff on core/ops unaffected |
| S5 | Brand-B session sets `app.bypass_rls=true` itself | orders 0 → 2 (brand A's rows) | SA-TEN-007 / SA-DB-015 |
| S6 | Brand-B session inserts `user_type='platform_admin'` | `INSERT 0 1` | SA-AUTHZ-001: no DB backstop |
| S7 | Suspended brand (`tos`) calls the cancellation-state function: cancelled, then active | Final `status=active, suspension_reason=tos` | SA-TEN-003 |
| S7b | Brand-A session calls the same function with brand B's id | B becomes `cancelled` | SA-TEN-007, SA-DB-003 (DB-only) |
| S2b | Pre-0031 state, commerce customer with no `customer_id` GUC | payments visible 2 (c1 and c2); with the GUC, 1 | SA-TEN-002 |

QA-A also ran a **validator reachability** comparison: 130 validated types vs 92 endpoint-filtered types, leaving 40 never invoked (SA-API-003). QA-B refined this to **39** truly dead validators, because `PartnerBookingLocation` runs through `SetValidator` ([10b](specialists/10b-qa-verification-platform.md) cmd 1).

**QA-C** ([10c](specialists/10c-qa-verification-db-mobile.md); PG 16.15, port 55435, full repository build):

| Repro | Result | What it proved |
|---|---|---|
| C2–C3 | `build_from_scratch.sh` exit 0; `migrate.sh up` **exit 3 at 0005** | SA-DB-002 reproduced independently |
| C4–C7 | Patches need 3 passes (no deterministic order); `rls_bypass()` overwritten to accept only `'on'` | SA-DB-002 second half |
| C8 | DEFINER functions executable by `app_user` through **default privileges alone** | SA-DB-003 mechanism |
| T1 | Customer GUCs: orders, payments and stores 0; INSERT orders/pickups denied; `customer_addresses` returns 2 (c2's address visible, because addresses are brand-only) | SA-TEN-001 |
| T1b | Commerce-host customer: `wallet_accounts` visible = **2** (c1 and c2). Brand-A staff via commerce host: payments 0; via core/ops: 2 | SA-TEN-002 (cross-customer, reproduced); SA-TEN-001 also blocks commerce staff |
| T2 | `export_brand(B)` returned 8 B row groups; `purge_brand(B)` deleted all B rows (rolled back) | SA-DB-003 |
| T3 | Self-set bypass, then `DELETE` of B's payment → `DELETE 1` | SA-TEN-007 gives the same power as purge/export |
| T4 | A session inserts a B-role membership; `otp_codes` and `refresh_tokens` are readable; RLS off on 5 identity tables | SA-DB-005 |
| T5 | Two concurrent 60.00 refunds on 100.00 → refunded 120.00 | SA-DB-006 → SA-API-009 |
| T6 | Wallet race → final 70.00 (correct: 120) | SA-DB-008 → SA-API-005 |
| T7 | (a) cross-brand FK inserts succeed; (b) pickup at another customer's or another brand's address → `INSERT 0 1`; (c) uuid cast error; (d) `refund_type` `'wallet'`/`'gateway'` → CHECK violation | SA-DB-004; SA-MOB-005 at the DB layer; SA-DB-012; SA-QC-001 |
| T8 | A warehouse-W2-scoped user sees 0 batches, generates `WB-…-0001`, and gets a duplicate key | SA-QC-002 |
| T9 | Brand-A session reads B rows from `mv_customer_ltv` | SA-DB-013 |
| T10 | `COPY authz.decision_log` → "COPY FROM not supported with row-level security"; `authz.policy` under `app_user` shows 0 brand rows | SA-DB-014 |
| T11 | `delivery_assignments` has only a PK unique, no transition guard | SA-MOB-001 / SA-MOB-002 (no DB backstop) |
| T12 | `CALL partman.run_maintenance_proc()` → `parent table not found: order_lifecycle.process_logs`, and the old ping partition survives; per-table `run_maintenance('logistics.rider_location_pings')` drops it | SA-QC-003; corrects SA-MOB-012 (retention is configured; the scheduler and the stale row break it) |

### 1.4 What could not be executed

These are **Not Tested** in this audit ([10a](specialists/10a-qa-verification-security.md), [10b](specialists/10b-qa-verification-platform.md), [10c](specialists/10c-qa-verification-db-mobile.md) "Not verified"):
- any HTTP request, including the SA-AUTHZ-001 login → token → bypass chain (static, except the S6 DB step);
- .NET unit and integration tests in the container;
- Razorpay and WhatsApp sandboxes;
- device behaviour (SA-MOB-009, background location, push);
- the admin-web e2e script;
- production database state.

---

## 2. Existing test inventory and its blind spots

### 2.1 Inventory

| Suite | Size | Notable coverage | Source |
|---|---|---|---|
| `backend/laundryghar/tests/core.Tests` | 11 files; 137 tests | `Configuration/EntitlementConfigTests.cs` (config pin); `WhiteLabel/AppIdentifierTests.cs` | [10b](specialists/10b-qa-verification-platform.md), [03](specialists/03-subscription.md), [05](specialists/05-onboarding-whitelabel.md) |
| `backend/laundryghar/tests/operations.Tests` | 35 files; 426 tests | `Auth/{BrandSuspensionMiddlewareTests,ScopeBoundaryTests,RateLimitPartitioningTests}.cs`; `Fulfillment/FulfillmentStrategyParityTests.cs` (27); salon (11) and recurring (8) strategy tests; `DeliveryCadenceTests` (12); `Logistics/GetRidersLiveTests.cs` | [04](specialists/04-verticals.md), [06](specialists/06-abac-rbac.md), [10c](specialists/10c-qa-verification-db-mobile.md) |
| `backend/laundryghar/tests/operations.IntegrationTests` (Testcontainers, `postgres:16-alpine`) | 44 files; 284 tests | `Rbac/RlsIsolationTests` (9), `UsersBrandRlsTests` (14, applies 0029 verbatim), `UserTenantIsolationTests` (6), `SubBrandScopeRlsTests` (15, applies 0031 verbatim), partner RLS tests (17), `RolesGlobalVisibilityRlsTests` (8), `BrandResolverHostTests` (5), `BrandCancellationTests` (13), `EntitlementEnforcementTests` (9), `BrandDunningTests`, `PlanChangeTests`, `RoleEditGuardTests`, `ScopeResolverTests`; `Phase1SqlMigrationTests`, `Phase1EfModelTests`, `Phase2*`/`Phase4*` schema tests | [02](specialists/02-multitenancy.md), [03](specialists/03-subscription.md), [06](specialists/06-abac-rbac.md) |
| commerce (`commerce.Application`, `commerce.Infrastructure`: 96 source files) | **No test project.** `operations.Tests/Commerce/QuotaUnitTests.cs` touches only SharedDataModel enums | — | SA-ARCH-010 ([01](specialists/01-architecture.md), [10b](specialists/10b-qa-verification-platform.md)) |
| admin-web, pos-web | **No unit tests.** admin-web has an `e2e` script that needs a live stack | — | SA-FE-011 |
| customer-mobile | 11 jest suites, 170 tests (`src/__tests__/*.test.ts`) | Stores, API client, terminology, version gate, min-order | [10b](specialists/10b-qa-verification-platform.md) |
| rider-mobile | 8 jest suites, 91 tests (`src/__tests__/*.test.ts`) | Includes `offlineQueueStore.test.ts` and `apiClient.test.ts` | [10b](specialists/10b-qa-verification-platform.md) |

### 2.2 Blind spots

| # | Blind spot | Effect | Findings |
|---|---|---|---|
| B1 | **Silent pass without Docker.** `catch (Exception) { _dockerAvailable = false; }` followed by `if (!_dockerAvailable) return;`. 10b counts this pattern 60 times across 17 files; 06 counts 21 files with `if (!_fx.DockerAvailable) return;`. | On any runner without Docker, the whole integration suite goes green without asserting anything. On GitHub it did run. | SA-QB-002 |
| B2 | **Thin migration coverage.** Only 13 of the 33 `.up.sql` migrations are applied by any test, each onto a minimal fixture. No test runs a `.down.sql` or the documented bootstrap. Tests use PG 16 while production compose uses PG 18. | The bootstrap failure at 0005 (SA-DB-002) and the `rls_bypass` revert reached the docs uncaught. | SA-QB-002, SA-OPS-007, SA-DB-002 |
| B3 | **Isolation tests do not exercise the runtime path.** `RbacRlsFixture.cs` uses hand-written trimmed DDL (L140-280), turns pooling off (L43-47) and sets **6 of the interceptor's 12 GUCs** (no `customer_id`, `user_type`, `token_use`, `scope_nodes`, `permissions` or `roles`) under a "byte-for-byte mirror" claim. | Exactly the GUCs that broke the customer and commerce lanes are never set. There is no pooling-on test and no HTTP cross-tenant test. | SA-TEN-010 |
| B4 | **Tests assert the defect.** `SubBrandScopeRlsTests.cs:264-273` asserts that an unresolved `scope_nodes` "denies everything", and no test models a customer, API-key or commerce-host principal, which is the population that always produces the unresolved value. `RateLimitPartitioningTests.cs:27-37,63-69,71-77` assert that `X-Brand-Id` takes precedence and that the leftmost XFF is trusted. | A correct fix turns these tests red, which invites someone to "fix the fix" back. CI passed **with** the Critical SA-TEN-001 present. | SA-TEN-001, SA-QA-002 |
| B5 | **Partial assertions.** `BrandCancellationTests.The_cancellation_transition_refuses_every_other_status` checks the target status, not a suspended *source* status. `PlanChangeTests` assert features only, not billing. `AppIdentifierTests` assert the collision-prone mapping (`LG-MAIN → lgmain`). | SA-TEN-003, SA-SUB-012 and SA-ONB-012 are untested or locked in. | SA-TEN-003, SA-SUB-012, SA-ONB-012 |
| B6 | **No commerce test project.** No tests for `RazorpayWebhookHandler`, wallets, `SubscriptionBillingService`, `BrandPlatformBillingService` (worker scope), `NotificationSettingsCache` or royalty. | SA-API-007/008/009/012, SA-SUB-001/003/004 and SA-SOLID-003 were all invisible to CI. | SA-ARCH-010 |
| B7 | **God-handlers untested.** No tests for `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `CancelOrder*` or OAuth. | The order-status writers diverged (SA-SOLID-001), and the rider path has no state machine (SA-MOB-001). | SA-SOLID-010 |
| B8 | **No concurrency tests outside matview and RLS.** A grep for `WhenAll`/`concurren` finds none for money, booking or dispatch. | Over-refund, lost update, duplicate POS orders and double assignment are unguarded. | SA-API-004/005/009, SA-MOB-002 |
| B9 | **No identity-write or escalation tests.** Only `SetUserType` is guarded and tested; create, invite, grant, status and override are not. | SA-AUTHZ-001 to SA-AUTHZ-004. | 06 Table 4 |
| B10 | **Clients.** No web unit tests; both mobile typechecks red; pos-web outside CI. | Client-side state machines and caches drift from the server unseen. | SA-FE-011, SA-FE-004, SA-FE-005 |

What existing tests **do** give, and must keep passing (positive controls):
- brand-level RLS isolation;
- users RLS (0029);
- staff sub-brand boundaries;
- partner RLS;
- the strategy transition tables and parity tests;
- entitlement stripping on staff tokens (9 integration tests);
- the suspension middleware;
- `BrandDunningTests`, for the nonpayment-only reinstatement rule;
- the export isolation test (`An_export_never_leaks_another_tenants_data`).

---

## 3. Proposed tests (PROPOSED)

Every test in this section is **PROPOSED**. None exists yet unless the location says "extend".

**Levels:**
- **unit:** xUnit or jest/Vitest, no DB.
- **integration-TC:** xUnit with Testcontainers, on the real migrated schema from roadmap task 1.1.
- **RLS-SQL:** SQL run as `app_user` with interceptor-identical GUCs, the QA-C recipe ([10c](specialists/10c-qa-verification-db-mobile.md) method), hosted in the integration project.
- **E2E:** HTTP against a composed stack.
- **device:** simulator or physical device.

**Paths:** backend paths are relative to `backend/laundryghar/tests/`. "(new)" means a new file.

### 3.1 Tenant isolation

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| ISO-1 Lane matrix under 0031 | SA-TEN-001, SA-ARCH-014 | RLS-SQL + integration-TC | `operations.IntegrationTests/Rbac/SubBrandScopeRlsTests.cs` (extend; 10a-T6, 06-T5) | For customer, `customer_mcp`, api_key, partner, worker, core/ops staff and commerce staff on all 39 restrictive tables, plus an audited insert: each principal sees its own rows only, and every audit insert succeeds. The existing staff-boundary and fail-closed staff cases still pass. | 0.7, 1.3 |
| ISO-2 Adapter GUC parity | SA-TEN-001, SA-TEN-002 | unit | `operations.Tests/Auth/CurrentTenantAdapterParityTests.cs` (new; 10a-T7, 06-T6) | `CommerceHostCurrentTenant` and `HttpContextCurrentTenant`, run through the real `RlsConnectionInterceptor`, publish identical GUCs for the same staff and customer principal | 0.7 |
| ISO-3 Customer-vs-customer on commerce tables | SA-TEN-002 | RLS-SQL | `operations.IntegrationTests/Rbac/RlsIsolationTests.cs` (extend; 10a-T8) | Customer c1 cannot read c2's `wallet_accounts`, `payments`, `payment_refunds` or loyalty rows (reverses QA-C T1b) | 0.7 |
| ISO-4 Anonymous + bypass paths | SA-DB-012 | RLS-SQL | `operations.IntegrationTests/Rbac/AnonymousBypassRlsTests.cs` (new; 10c list) | With brand `''` and bypass `true`, every table touched by auth paths (incl. `customer_identities` and the salon tables) queries without error | 0.7 |
| ISO-5 DEFINER and privilege guard | SA-DB-003, SA-TEN-007 | RLS-SQL | `operations.IntegrationTests/Rbac/BrandCancellationTests.cs` (extend; 10c list) | `has_function_privilege('app_user','kernel.purge_brand(uuid)','EXECUTE')` is false. Each brand-taking DEFINER raises for a foreign brand id (reverses S7b and T2). | 0.9 |
| ISO-6 Bypass not self-settable | SA-TEN-007 | RLS-SQL | `operations.IntegrationTests/Rbac/RlsIsolationTests.cs` (extend) | An `app_user` session that sets `app.bypass_rls=true` still sees 0 foreign rows (reverses S5 and T3) | 1.3 |
| ISO-7 Identity-table RLS | SA-DB-005, SA-AUTHZ-003 | RLS-SQL | `operations.IntegrationTests/Rbac/UsersBrandRlsTests.cs` (extend; 10c list) | An A session cannot insert a membership for a B user, role or scope, and cannot read B's `otp_codes`, `refresh_tokens` or profiles (reverses T4) | 1.4 |
| ISO-8 Composite tenant FKs | SA-DB-004 | RLS-SQL | `operations.IntegrationTests/Rbac/TenantForeignKeyTests.cs` (new) | Cross-brand `orders`→store/customer/franchise and `payments`→order inserts fail (reverses T7a) | 3.6 |
| ISO-9 Analytics views | SA-DB-013 | RLS-SQL | `operations.IntegrationTests/Phase2MatviewRegistryTests.cs` (extend) | An A session reads only A rows through the security-barrier views, and direct MV SELECT is revoked (reverses T9) | 3.6 |
| ISO-10 Real-path fixture and pool reuse | SA-TEN-010, SA-DB-016 | integration-TC | `operations.IntegrationTests/Rbac/RbacRlsFixture.cs` (rewrite) | The fixture's GUC setter is produced by `RlsConnectionInterceptor` with a fake `ICurrentTenant`; pooling is on; two tenants alternate on one pooled connection with no bleed | 1.1, 5.3 |
| ISO-11 HTTP cross-tenant smoke | SA-TEN-010 (02 "Missing") | E2E | `operations.IntegrationTests/Http/CrossTenantSmokeTests.cs` (new, `WebApplicationFactory`) | Brand-A tokens get 404/403 on B ids for orders, customers, payments and riders on all three hosts | 1.3 |
| ISO-12 Number generators | SA-DB-007, SA-QC-002 | integration-TC | `operations.IntegrationTests/NumberGeneratorTests.cs` (new; 10c list) | Two brands, and two warehouse- or franchise-scoped users in one brand, create their first expense, batch and pickup on the same day with no unique violation; concurrent creates in one brand produce distinct numbers | 1.6 |

### 3.2 Role/permission enforcement

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| RBAC-1 User-creation escalation | SA-AUTHZ-001, SA-QA-001 | integration-TC | `operations.IntegrationTests/Rbac/UserCreationEscalationTests.cs` (new; 10a-T1) | Brand, franchise and store admins calling `CreateUser`/`InviteUser` with `platform_admin`, and a store admin asking for `brand_admin`, are refused with **no `users` row written**; a platform admin succeeds | 0.3 |
| RBAC-2 Token mint without platform membership | SA-AUTHZ-001, SA-ARCH-013 | integration-TC | `operations.IntegrationTests/Rbac/ScopeResolverTests.cs` (extend; 10a-T2) | A `platform_admin`-typed user without a platform-scoped membership gets no bypass and no all-permissions | 0.3 |
| RBAC-3 Membership grant target | SA-AUTHZ-003 | integration-TC | `operations.IntegrationTests/Rbac/GrantMembershipTargetTests.cs` (new; 10a-T3) | A grant to a user with no membership in the actor's brand returns 403/404; `IsPrimary` leaves other brands' memberships untouched | 0.3 |
| RBAC-4 Identity-write target guard | SA-AUTHZ-002 | integration-TC | `operations.IntegrationTests/Rbac/IdentityWriteTargetGuardTests.cs` (new; 10a-T4) | A store admin's `activate`, email/bank update or deactivate on a brand admin or another store's staff is refused; an own-store junior is allowed | 0.3 |
| RBAC-5 Grant ceiling and platform plane | SA-AUTHZ-004 | integration-TC | `operations.IntegrationTests/Rbac/EntitlementEnforcementTests.cs` (extend; 10a-T5) | Granting `saas.manage`/`brands.create` through an override or role cells is refused; `SetBrandFeature`, `ApplyBundleToBrand` and `SetBrandPlatformInvoiceStatus` return 403 for non-platform callers | 0.3 |
| RBAC-6 Endpoint metadata contract | SA-ORC-002 (06-T11) | unit | `core.Tests/Configuration/EndpointAuthorizationContractTests.cs` (new) | Every mapped endpoint on every host has an authorization policy or an explicit `AllowAnonymous` on an allow-list; a `FallbackPolicy` is configured | 1.4 |
| RBAC-7 DB backstop for platform type | SA-ARCH-013, SA-AUTHZ-001 | RLS-SQL | `operations.IntegrationTests/Rbac/UsersBrandRlsTests.cs` (extend) | A non-bypass session inserting or updating `user_type='platform_admin'` is rejected (reverses S6) | 0.3 |
| RBAC-8 Revocation on suspend | SA-AUTHZ-009 | integration-TC | `operations.Tests/Auth/TokenVersionRevocationTests.cs` (new; 10a-T18, 06-T8) | After suspend or deactivate, the old access token gets 401 within the version TTL; on a version-store error, high-risk permissions fail closed | 1.4 |
| RBAC-9 Platform audience | SA-ARCH-013 | integration-TC | `core.Tests/Auth/PlatformAudienceTests.cs` (new) | `/admin/entitlements`, `/admin/brands` and the platform invoice endpoints reject tenant-audience tokens | 1.3 |
| RBAC-10 Identity-axis gates | SA-AUTHZ-013 | integration-TC | `operations.IntegrationTests/Rbac/DispatchSettingsAuthTests.cs` (new) | A brand admin updating platform dispatch settings gets 403, not a DB error | 1.4 |

### 3.3 ABAC policy evaluation

ABAC is inert today (SA-AUTHZ-008), and the architect recommends hand-written resource guards plus DB backstops first ([01b §4.4](specialists/01b-architect-challenge-review.md)). These tests therefore cover the scope and attribute rules that exist as code, and gate any later engine activation.

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| ABAC-1 Scope amplification | SA-AUTHZ-007 | unit + integration-TC | `operations.Tests/Auth/ScopeBoundaryTests.cs` (extend; 10a-T19, 06-T7) | A user with a store role at S1 plus a brand read-only role cannot write to S2 | 1.4 |
| ABAC-2 `IsWithinScope` coverage | 06-T12 (A0.7 history) | unit (architecture) | `operations.Tests/Architecture/ScopeCallSiteTests.cs` (new) | Every mutating handler that takes a franchise, store or warehouse id calls `IsWithinScope` | 1.4 |
| ABAC-3 ABAC store under `app_user` | SA-DB-014 | integration-TC | `operations.IntegrationTests/Rbac/AbacStoreTests.cs` (new) | Against `app_user` (not superuser), brand policies, roles and entitlements are visible, and decision-log writes succeed (reverses T10) | 1.3 |
| ABAC-4 Enforce-mode deny | SA-AUTHZ-008 | integration-TC | `operations.IntegrationTests/Rbac/AbacEnforceTests.cs` (new) | An endpoint with `AbacResource` in enforce mode denies on a policy deny **and** on an evaluator exception | 5.4 |
| ABAC-5 Shadow parity | SA-AUTHZ-008 | integration-TC | same | In shadow mode for commerce, ABAC decisions equal RBAC decisions across the scenario matrix in [06](specialists/06-abac-rbac.md) Table 2 | 5.4 |
| ABAC-6 Resource guards | SA-MOB-005, SA-MOB-010, SA-MOB-011 | integration-TC | see MOB-L4, MOB-L5, MOB-L6 | Address ownership, and rider active + on-duty, are checked server-side | 0.7, 1.9 |

### 3.4 Subscription entitlements

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| ENT-1 Non-staff lanes and propagation | SA-AUTHZ-011, SA-SUB-019 | integration-TC | `operations.IntegrationTests/Rbac/EntitlementEnforcementTests.cs` (extend; 10a-T16, 06-T10) | A customer, partner or api-key endpoint for an unlicensed feature returns 402; franchise and store staff lose the feature within the TTL after a downgrade | 2.2 |
| ENT-2 Subscription → entitlement projection | SA-SUB-006 | integration-TC | `operations.IntegrationTests/Rbac/SubscriptionEntitlementTests.cs` (new) | Cancel at period end → the next token lacks bundle features; `past_due` beyond grace → revoked; pay → reinstated; `ApplyBundle` on a cancelled subscription follows the decided rule | 2.2 |
| ENT-3 Sellable features gate something | SA-SUB-009 | RLS-SQL (catalog) | `operations.IntegrationTests/Phase2SubscriptionInclusionsTests.cs` (extend) | Every `is_sellable` feature is referenced by an active module that owns at least one permission; the 0019 check raises instead of warning | 2.2 |
| ENT-4 Plan limits | SA-SUB-008 | integration-TC | `operations.IntegrationTests/Rbac/PlanLimitTests.cs` (new) | `CreateStore` at the limit returns 402 `plan_limit_reached`; Starter can create exactly one store | 2.2 |
| ENT-5 Plan-change billing | SA-SUB-012 | integration-TC | `operations.IntegrationTests/Rbac/PlanChangeTests.cs` (extend) | A trial upgrade issues no invoice and stays `trialing`; a downgrade keeps features until `current_period_end` | 2.2 |
| ENT-6 Billing worker lifecycle | SA-SUB-004, SA-SUB-001, SA-SUB-003 | integration-TC | `commerce.Tests/Billing/BrandPlatformBillingServiceTests.cs` (new; 10a-T12) | Under `app_user` with RLS on, the renewal pass issues an invoice; trial end → invoice or `past_due`; `past_due` → paid → `active` → next renewal invoiced | 2.1 |
| ENT-7 Paylink on `past_due` | SA-SUB-002 | unit + integration-TC | `core.Tests/Identity/ProcessPaylinkWebhookTests.cs` (new; 10a-T13) | `payment_link.paid` for a `past_due` invoice marks it paid, and the next pass reinstates; a link can be created for `past_due` | 2.1 |
| ENT-8 Self-grant bypass closed | SA-AUTHZ-004 | — | covered by RBAC-5 | — | 0.3 |
| ENT-9 Worker flag pinned | SA-SUB-005 | unit | `core.Tests/Configuration/EntitlementConfigTests.cs` pattern, new `WorkerOptionsConfigTests.cs` | The Production value of `BrandPlatformBillingEnabled` is pinned, and startup warns when it is off | 2.1 |
| ENT-10 Dunning is audited and notified | SA-SUB-014 | integration-TC | `commerce.Tests/Billing/DunningAuditTests.cs` (new) | Each transition writes one `audit_logs` row and one outbox notification | 2.1 |
| ENT-11 Webhook dedupe | SA-SUB-016 | integration-TC | `commerce.Tests/Payments/WebhookConcurrencyTests.cs` (new) | Two concurrent identical webhooks produce one outbox event; paylink `amount_paid` is verified | 1.6 |
| ENT-12 Mandate charge contract | SA-SUB-015 | integration (recorded sandbox) | `commerce.Tests/Gateway/RazorpayContractTests.cs` (new) | Recorded sandbox responses: pending statuses stay pending and are finalised by webhook | 2.1 |
| ENT-13 GST invoices | SA-SUB-013 | integration-TC | `commerce.Tests/Billing/PlatformInvoiceTests.cs` (new) | Invoice numbers are gap-free and unique under concurrency; CGST+SGST vs IGST is selected correctly; `paid_at` and the payment id are set | 2.3 |
| ENT-14 Tenant billing self-service | SA-SUB-007 | integration-TC | `core.Tests/Identity/BillingSelfServiceTests.cs` (new) | An owner reads only their own invoices; another brand's invoice id returns 404; a pay link can be created while suspended | 2.3 |

### 3.5 Single-primary-business-type enforcement

The schema already enforces one immutable vertical. These tests **pin that positive control** and close the gaps around it.

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| BT-1 One template at signup | positive control ([05](specialists/05-onboarding-whitelabel.md)) | unit | `core.Tests/Signup/SignupTemplateTests.cs` (new) | Signup accepts exactly one public `TemplateKey`; unknown or non-public keys are rejected; `brands.vertical_key` is set once | 2.5 |
| BT-2 Immutable once trading | SA-ONB-009 | RLS-SQL | `operations.IntegrationTests/Phase2BundleVerticalTests.cs` (extend) | Changing `brands.vertical_key` after the first order raises (`trg_brand_vertical_immutable`) | 4.1 |
| BT-3 Back-office creation | SA-ONB-005 | integration-TC | `core.Tests/TenancyOrg/CreateBrandProvisioningTests.cs` (new) | `CreateBrand` without a template returns 422; with one, it gets that template's vertical, features, own franchise and trial | 4.1 |
| BT-4 Orders follow the brand vertical | SA-VERT-001 | unit + RLS-SQL | `operations.Tests/Orders/CreateOrderVerticalTests.cs` (new) | Salon brand → `appointment`/`booked`; laundry → `process_deliver`; parcel on laundry → `point_to_point` with `vertical_key='laundry'`; a disallowed mode → 422; the DB check rejects `orders.vertical_key ≠ brand vertical` | 3.3 |
| BT-5 Only operable templates are offered | SA-VERT-002 | unit | `core.Tests/Signup/GetSignupTemplatesTests.cs` (new) | `GetSignupTemplates` returns only templates whose mode has a creation path; salon and tiffin are hidden until the operability gate passes | 0.12 |
| BT-6 Governed vertical change | SA-ONB-009 | integration-TC | `core.Tests/TenancyOrg/ChangeBrandVerticalTests.cs` (new) | Platform-only; 409 after orders exist; before orders it re-expands features and writes an audit row | 4.1 |

### 3.6 Onboarding and provisioning

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| ONB-T1 Missing bundle and duplicate phone | SA-ONB-010 | integration-TC | `core.Tests/Signup/CompleteSignupTests.cs` (new; 05 T-16 list) | A public template with no resolvable bundle fails loudly; a concurrent duplicate phone returns 409; `planCode` is validated | 2.5 |
| ONB-T2 GSTIN reaches invoices | SA-ONB-007 | integration-TC | same + `operations.Tests/Orders/InvoiceTests.cs` (new) | A signup with a GSTIN yields invoices that print it | 2.5 |
| ONB-T3 Trial created at signup | SA-SUB-001 | integration-TC | `core.Tests/Signup/CompleteSignupTests.cs` | Signup creates a `trialing` subscription with a trial end, and bundle rows carry `valid_until` (or the projection covers them) | 2.5 |
| ONB-T4 One provisioning path | SA-ONB-005 | integration-TC | `core.Tests/TenancyOrg/CreateBrandProvisioningTests.cs` | Self-signup and back-office creation produce identical feature sets, franchise and trial for the same template; re-running is idempotent | 4.1 |
| ONB-T5 Go-live prerequisites and domains | SA-ONB-011 | integration-TC | `core.Tests/Onboarding/GoLiveTests.cs` (new) | Go-live with no location or no priced item returns 422; adding an unverified primary keeps the verified primary | 4.3 |
| ONB-T6 Signup journey | SA-ONB-008 | E2E | `admin-web/e2e/signup.spec.ts` (new; Playwright) | signup → OTP → login → the wizard shows steps | 4.4 |
| ONB-T7 App identifiers unique | SA-ONB-012 | unit | `core.Tests/WhiteLabel/AppIdentifierTests.cs` (rewrite) | `LG-MAIN`/`LGMAIN` and `7`/`B7` get distinct, persisted identifiers | 4.4 |

### 3.7 Branding isolation

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| BR-1 Brand-self branding write | SA-ONB-002 | integration-TC | `core.Tests/TenancyOrg/BrandBrandingTests.cs` (new) | An owner updates their own brand's name, logo, favicon and colours, and cannot update another brand's; invalid hex is rejected | 4.2 |
| BR-2 Public branding resolution | SA-ONB-002 | integration-TC | same | `GET /public/branding` resolves per Host or brand code, and never returns another brand's branding | 4.2 |
| BR-3 Host survives the gateway | SA-ONB-001 | E2E | `operations.IntegrationTests/Http/GatewayHostForwardingTests.cs` (new) | A request on a verified custom domain resolves its brand upstream; CORS allows it. The test also pins the decided behaviour for unknown hosts: QA-B observed fall-through to the default `LG-MAIN` | 4.3 |
| BR-4 No hard-coded brand | SA-API-013 | unit | `commerce.Tests/Notifications/FallbackTemplateTests.cs` (new) | A fallback body for brand "X" contains "X" and its currency, and not "Laundry Ghar" | 4.2 |
| BR-5 Terminology per vertical | SA-FE-009 | unit (jest/Vitest) | `customer-mobile/src/__tests__/terminology.test.ts`, `rider-mobile/src/__tests__/terminology.test.ts` (extend); pos-web (new) | Rendering with a salon pack shows no "garment" or "wash" | 4.2 |
| BR-6 Anonymous public content | SA-TEN-006 | integration-TC | `operations.IntegrationTests/Rbac/PublicContentRlsTests.cs` (new) | As `app_user` with RLS on, banners, app-config and onboarding slides return the resolved brand's active rows only | 4.3 |
| BR-7 Client cache per brand | SA-FE-005 | unit (Vitest) + E2E | `admin-web/src/hooks/__tests__/brandScopedKeys.test.ts` (new) | Switching brand changes query keys or clears the cache; the order list refetches with the new header | 1.11 |
| BR-8 Logo storage key | SA-ONB-002, SA-OPS-003 | unit | `operations.Tests/Storage/FileStorageContractTests.cs` (new) | Logo keys are brand-prefixed; reads through the public endpoint never cross brands | 4.2 |

### 3.8 Vertical-specific workflows

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| VW-1 Order creation per vertical | SA-VERT-001 | unit | see BT-4 | — | 3.3 |
| VW-2 Invoice tax identity | SA-VERT-004 | unit | `operations.Tests/Orders/InvoiceTaxProfileTests.cs` (new) | Laundry → SAC 999712; point-to-point → the configured courier SAC; salon `completed` is billable | 3.3 |
| VW-3 Notification templates from the catalog | SA-VERT-006 | unit + integration-TC | `operations.IntegrationTests/Phase2NotificationCatalogTests.cs` (extend) | Template resolution per mode is driven by `notification_event_catalog` rows, with the switch only as a fallback | 3.3 |
| VW-4 Admin uses server transitions | SA-FE-004, SA-VERT-007 | unit (Vitest + RTL) | `admin-web/src/pages/orders/__tests__/OrderDetailDrawer.test.tsx` (new) | A parcel order at `picked_up` offers `out_for_delivery`; a salon `booked` order offers Confirm/Cancel; only `allowedTransitions` are rendered | 1.11 |
| VW-5 Catalog kind per vertical | SA-VERT-008 | unit | `operations.Tests/Catalog/CatalogKindTests.cs` (new) | `DefaultFor` covers all four verticals; item creation on a salon brand yields `service` | 3.3 |
| VW-6 Vertical-neutral completion | SA-API-021 | integration-TC | `commerce.Tests/Loyalty/LoyaltyEarnTests.cs` (new) | Loyalty is earned on a salon completion; a non-IN template signup keeps its currency, country and locale | 3.3 |
| VW-7 Vertical endpoint gate | SA-AUTHZ-012 | integration-TC | `operations.Tests/Auth/ScopeBoundaryTests.cs` (extend; 10a-T17) | A laundry brand calling a salon or parcel endpoint gets 403/402 | 3.3 |
| VW-8 One transition writer | SA-SOLID-001 | integration-TC | `operations.IntegrationTests/Orders/OrderTransitionServiceTests.cs` (new) | Each of the 5 paths (admin PATCH, admin cancel, customer cancel, rider pickup collect, rider delivery complete) writes one history row with the correct `FromStatus` and one `order.status_changed`; the template resolves; a rider completing a `disputed` order is rejected | 3.2 |
| VW-9 Customer app mode-driven | SA-VERT-007 | unit (jest) | `customer-mobile/src/__tests__/verticalOptions.test.ts` (new) | A logistics brand shows no laundry option | 3.3 |
| VW-S1…S3 Salon end to end | SA-VERT-003 | integration-TC + E2E | `operations.IntegrationTests/Salon/*` (new) | S1 availability (operating hours, holidays, shifts); S2 appointment booking writes order + appointment in one transaction; S3 booking → service → invoice | 5.1 |
| VW-T1 Tiffin generator | SA-VERT-002 (tiffin facet) | integration-TC | `operations.IntegrationTests/Recurring/DeliveryScheduleGeneratorTests.cs` (new) | Occurrences are generated idempotently into `delivery_schedule_occurrences` | 5.2 |

### 3.9 Booking conflicts and concurrency

All of these need two or more real connections (integration-TC). The two races that were reproduced in SQL (QA-C T5, T6; 08b) become the regression oracles.

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| CC-1 POS order idempotency | SA-API-004 | integration-TC | `operations.IntegrationTests/Orders/CreateOrderIdempotencyTests.cs` (new; 10a-T21) | Parallel requests with the same key produce one order, one coupon redemption and one package debit | 1.6 |
| CC-2 Refund cap | SA-API-009, SA-DB-006 | integration-TC | `commerce.Tests/Payments/RefundConcurrencyTests.cs` (new; 10a-T21) | Two parallel 60.00 refunds on 100.00: one succeeds and one fails (reverses T5); a simulated transient failure makes exactly one gateway call | 0.6 |
| CC-3 Wallet balance | SA-API-005 | integration-TC | `commerce.Tests/Wallets/WalletConcurrencyTests.cs` (new) | After parallel credits and debits, `balance == SUM(ledger)` (reverses T6); a conflict maps to 409 | 1.6 |
| CC-4 Coupon caps | SA-DB-010 | integration-TC | `commerce.Tests/Coupons/CouponRedemptionConcurrencyTests.cs` (new) | Parallel redemption never exceeds the global, per-customer or per-order limits | 1.6 |
| CC-5 Slot capacity | SA-API-019 | integration-TC | `operations.IntegrationTests/Orders/SlotCapacityTests.cs` (new; 10b list) | Parallel customer pickups at capacity → exactly capacity bookings; an admin booking on a full slot is rejected; `booked_count` stays consistent after a reject | 1.6 |
| CC-6 Single live assignment | SA-MOB-002 | integration-TC | `operations.IntegrationTests/Logistics/AssignmentConcurrencyTests.cs` (new; 10c list) | Concurrent manual and auto assignment → one live leg and a 409; accept vs expire → one winner; assigning a cancelled pickup → 409 | 1.9 |
| CC-7 Appointment overlap | SA-VERT-003, SA-DB-021 | integration-TC | `operations.IntegrationTests/Salon/AppointmentOverlapTests.cs` (new) | Two transactions, same staff member, overlapping range: one fails on the exclusion constraint | 5.1 |
| CC-8 Refresh rotation | SA-API-020 | integration-TC | `core.Tests/Auth/RefreshRotationTests.cs` (new) | A parallel refresh with the same token produces exactly one success | 1.4 |
| CC-9 Client idempotency key | SA-API-018 | unit (jest) | `customer-mobile/src/__tests__/bookingIdempotency.test.ts` (new) | The checkout request carries an `Idempotency-Key` that stays stable across retries of one attempt | 1.6 |

### 3.10 Background jobs and events

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| BJ-1 Two workers, one execution | SA-OPS-005, SA-DB-009 | integration-TC | `commerce.Tests/Workers/ClaimConcurrencyTests.cs` (new) | Two relays or dispatchers process each row once; a stale `publishing`/`sending` lease is reclaimed; a row already claimed is not published (QA-C's additional defect) | 1.8 |
| BJ-2 No skipped events | SA-API-015, SA-ARCH-006 | integration-TC | `commerce.Tests/Workers/InboxConsumerTests.cs` (new) | With inverted commit order, a late-committed event is still processed by the loyalty and notification consumers | 1.8 |
| BJ-3 Refund executor | SA-API-008 | integration-TC | `commerce.Tests/Payments/RefundExecutorTests.cs` (new) | Cancelling a paid order executes its refund exactly once, through the gateway or the wallet | 0.6 |
| BJ-4 Brand-keyed credentials | SA-API-012 | unit + integration-TC | `commerce.Tests/Notifications/NotificationSettingsCacheTests.cs` (new; 10a-T15) | Two brands with distinct WhatsApp/SMS credentials: each send uses its own brand's | 0.5 |
| BJ-5 Unconfigured channel is not "sent" | SA-SOLID-008 | unit | `commerce.Tests/Notifications/ChannelSenderTests.cs` (new) | In Production with no credentials, the row is `suppressed`/`failed:not_configured`, not `sent` | 1.8 |
| BJ-6 Workers respect the lifecycle | SA-TEN-008 | integration-TC | `commerce.Tests/Workers/BrandLifecycleTests.cs` (new) | Billing, dispatch, royalty, loyalty and notification workers skip suspended or cancelled brands | 1.3 |
| BJ-7 Partition maintenance and retention | SA-OPS-004, SA-QC-003 | integration-TC (PG with partman) | `operations.IntegrationTests/PartitionMaintenanceTests.cs` (new) | `run_maintenance_proc()` succeeds; future partitions exist N months ahead for all partman tables; a ping partition older than 14 days is dropped (reverses T12); the DEFAULT partition is empty | 0.10 |
| BJ-8 Renewal under `app_user` | SA-SUB-004 | integration-TC | see ENT-6 | — | 2.1 |
| BJ-9 Rider push | SA-MOB-007 | integration-TC | `commerce.Tests/Notifications/RiderPushTests.cs` (new) | Assign, auto-assign and leg cancel each create a notification row for the rider's push tokens | 1.9 |
| BJ-10 Event contracts | SA-ARCH-006 | unit | `commerce.Tests/Contracts/OutboxEventTypesTests.cs` (new) | Every produced event type is in the typed catalogue, and every consumer subscribes to a catalogued type | 1.8 |

### 3.11 API authz bypass attempts

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| BYP-1 Header tenant override | positive control ([02](specialists/02-multitenancy.md)) | integration-TC | `operations.Tests/Auth/TenantResolutionMiddlewareTests.cs` (new) | `X-Brand-Id` from a non-platform token is ignored | 0.3 |
| BYP-2 Gateway partition spoofing | SA-API-002, SA-QA-002 | unit | `operations.Tests/Auth/RateLimitPartitioningTests.cs` (**replace** L27-37, L63-77; 10a-T9) | A header-only `X-Brand-Id` does not leave the IP bucket; XFF from an untrusted hop is ignored; only a validated token keys the brand partition | 1.5 |
| BYP-3 Auth limiter behind a proxy | SA-API-001, SA-QB-003 | integration (`WebApplicationFactory` + YARP hop) | `core.Tests/Auth/AuthRateLimitPartitionTests.cs` (new; 10a-T10) | Two clients through a trusted proxy get independent buckets; a spoofed XFF from an untrusted peer is ignored; `/refresh` is not throttled by logins | 0.4 |
| BYP-4 Page size clamp | SA-API-016 | integration-TC | `operations.Tests/Common/PaginationTests.cs` (new) | `pageSize=10000` returns at most the cap | 1.7 |
| BYP-5 Validators reachable | SA-API-003 | unit (reflection) | `core.Tests/Configuration/ValidatorReachabilityTests.cs` (new; 10a-T20) | Every `AbstractValidator<T>` is reachable through the pipeline or `ValidationFilter<T>`; upload MIME and size violations return 400 | 1.7 |
| BYP-6 Coupon validate-apply | SA-SOLID-006 | integration-TC | `commerce.Tests/Coupons/ValidateApplyTests.cs` (new) | A foreign or mismatched order is rejected; totals come from the DB, not the client | 3.7 |
| BYP-7 Client-priced pickup | SA-SOLID-014 | integration-TC | `operations.Tests/Orders/PickupPricingTests.cs` (new) | An inflated `EstimatedAmount` against a server-priced cart below the minimum is rejected | 1.7 |
| BYP-8 Payment amount binding | SA-API-007 | integration-TC | `commerce.Tests/Payments/InitiatePaymentTests.cs` (new) | A client-supplied `Amount`/`OrderId` not matching the caller's order `amount_due` is rejected | 0.6 |
| BYP-9 WebMCP absent in production | SA-FE-014 | unit (Vitest) | `admin-web/src/lib/__tests__/webmcp.test.ts` (new) | With `import.meta.env.PROD`, no WebMCP tools are registered | 1.11 |
| BYP-10 Partner predicate | SA-AUTHZ-015 | integration-TC | `operations.IntegrationTests/Rbac/PartnerRlsTests.cs` (extend) | Partner queries filter by `PartnerId` from claims even with RLS bypassed in the test | 3.6 |

### 3.12 Tenant suspension and subscription expiry

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| SUS-1 No self-lift | SA-TEN-003 | RLS-SQL + integration-TC | `operations.IntegrationTests/Rbac/BrandCancellationTests.cs` (extend; 10a-T11) | A `tos`, manual or nonpayment suspended brand cannot reach `active` through cancel → withdraw (reverses S7); withdraw restores `suspended` or cancel is refused | 0.9 |
| SUS-2 All lanes gated | SA-TEN-008 | integration-TC | `operations.Tests/Auth/BrandSuspensionMiddlewareTests.cs` (extend) | Partner and api-key principals of a suspended brand are blocked | 1.3 |
| SUS-3 Lookup failure | SA-TEN-008 | unit | same | The decided behaviour on a status-lookup error is pinned (today it fails open) | 1.3 |
| SUS-4 Deleted brand | SA-TEN-008 | integration-TC | `core.Tests/TenancyOrg/DeletedBrandLoginTests.cs` (new) | A deleted (archived) brand's users cannot log in, and `brand_status` reports `archived` | 1.3 |
| SUS-5 Trial expiry | SA-SUB-001 | integration-TC | `commerce.Tests/Billing/BrandPlatformBillingServiceTests.cs` | A trial that does not convert moves to `past_due`, and the brand's next token loses the paid features | 2.1 |
| SUS-6 Past-due recovery | SA-SUB-003, SA-SUB-002 | integration-TC | same | `past_due` → paid → subscription `active` → the next renewal is invoiced | 2.1 |
| SUS-7 Suspended owner can pay | SA-SUB-007 | integration-TC | see ENT-14 | — | 2.3 |

### 3.13 DB migrations and rollback

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| MIG-1 Fresh bootstrap | SA-DB-002, SA-OPS-007 | integration (CI Postgres service, **PG 18** with partman + postgis) | `.github/workflows/ci.yml` job `schema` (new) | Baseline + all migrations apply from empty, and `migrate.sh verify` passes | 1.1 |
| MIG-2 Up → down → up | SA-OPS-007, SA-QB-002 | same | same | Every migration after the baseline round-trips. The schema dump (policies, RLS flags, indexes) is identical before and after, as in the 08b 0025–0033 round trip. | 1.1 |
| MIG-3 EF model vs schema | SA-QB-002, SA-DB-002 | integration-TC | `operations.IntegrationTests/Phase1EfModelTests.cs` (extend) | Every mapped entity, column and type exists in `information_schema` on the CI-built schema | 1.1 |
| MIG-4 Bypass function intact | SA-DB-002 | RLS-SQL | `operations.IntegrationTests/Rbac/RlsIsolationTests.cs` (extend) | `kernel.rls_bypass()` returns true for `'true'` after the full build and after re-running any bootstrap script | 0.11 |
| MIG-5 RLS coverage | SA-TEN-010 | RLS-SQL | CI `schema` job | The 0027 guard passes on the full chain: every `brand_id` table has RLS and at least one policy | 1.1 |
| MIG-6 Partman config resolves | SA-QC-003 | RLS-SQL | CI `schema` job | Every `partman.part_config.parent_table` resolves to an existing table | 0.10 |
| MIG-7 Migration runner safety | SA-OPS-007 | integration (CI) | CI `schema` job | Two concurrent `migrate.sh up` runs serialise on the advisory lock; `no-transaction` files are flagged and idempotent | 1.1 |
| MIG-8 Catalog invariants | SA-DB-018, SA-SUB-009 | RLS-SQL (catalog) | CI `schema` job | No new unindexed FK on listed high-churn tables; the sellable-feature check raises | 1.1 |
| MIG-9 No early return in CI | SA-QB-002 | unit (meta) | `operations.IntegrationTests/Meta/NoSilentSkipTests.cs` (new) | With `CI=true` and Docker unavailable, the suite fails (or the tests are reported as Skipped, never as Passed) | 0.2 |
| MIG-10 Version parity | SA-OPS-007 | config | `operations.IntegrationTests` container image | Testcontainers use the same Postgres major version as production compose (today 16-alpine vs 18) | 1.1 |

### 3.14 Regression across existing LaundryGhar functionality (laundry parity suite)

Run this suite before and after every Phase 3 change. It must stay green to pass the architect's Phase 3 exit criterion ("laundry parity suite green", [01b §5](specialists/01b-architect-challenge-review.md)).

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| PAR-1 CreateOrder golden totals | SA-SOLID-010 | unit (InMemory) | `operations.Tests/Orders/CreateOrderGoldenTotalsTests.cs` (new) | Fixed totals for express, add-ons, coupon, loyalty, GST and an unregistered franchise | 1.10 |
| PAR-2 Laundry order shape | SA-VERT-001 | unit | see BT-4 | A laundry brand's orders are `process_deliver` / `laundry` | 3.3 |
| PAR-3 Laundry ladder unchanged | SA-SOLID-001 | unit | `operations.Tests/Fulfillment/FulfillmentStrategyParityTests.cs` (existing, keep) | Existing 27 parity cases still pass through `OrderTransitionService` | 3.2 |
| PAR-4 Laundry invoice identity | SA-VERT-004 | unit | see VW-2 | Laundry stays SAC 999712 | 3.3 |
| PAR-5 Laundry notifications | SA-VERT-006 | unit | see VW-3 | Existing laundry templates resolve unchanged | 3.3 |
| PAR-6 Pickup reference pattern | positive control ([08b](specialists/08b-database.md)) | integration-TC | `operations.IntegrationTests/Orders/PickupIdempotencyTests.cs` (new) | Customer pickup with the same key returns the winner, and slot capacity rolls back on collision | 1.6 |
| PAR-7 Loyalty on all delivery paths | SA-SOLID-001 | integration-TC | `commerce.Tests/Loyalty/LoyaltyEarnTests.cs` | Loyalty is earned for POS-delivered and rider-delivered orders | 1.10, 3.2 |
| PAR-8 Customer HTTP smoke on a migrated DB | SA-TEN-001 | E2E | `operations.IntegrationTests/Http/CustomerJourneySmokeTests.cs` (new; 08b DB-001 tests) | Customer order create/list, pickup scheduling and slot listing succeed on a 0031-migrated schema as `app_user` | 0.7 |
| PAR-9 Royalty sum | SA-SOLID-003, SA-QB-001 | integration-TC | `operations.IntegrationTests/Finance/RoyaltyTests.cs` (new) | Royalty over seeded COD, online and offline payments equals their sum; `payments.franchise_id` is set whenever `order_id` is | 2.4 |
| PAR-10 admin-web deployed smoke | SA-FE-001 | E2E | `admin-web/e2e/dashboard.spec.ts` (extend the existing e2e script) | Dashboard, CMS, analytics, finance, warehouse and logistics pages load against compose | 0.12 |

### 3.15 Mobile and location

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| MOB-L1 Leg state machine | SA-MOB-001 | unit + integration-TC | `operations.Tests/Logistics/LegTransitionTests.cs`, `operations.IntegrationTests/Logistics/UpdateMyTaskStatusTests.cs` (new; 10c list) | cancelled→completed returns 409; completed→started returns 409; a double `completed` decrements load once; completing a delivery leg of a cancelled order returns 409; `/assignments/{id}/status` accepts only allow-listed values | 0.8 |
| MOB-L2 OTP | SA-MOB-003 | integration-TC | `operations.IntegrationTests/Logistics/DeliveryOtpTests.cs` (new) | `out_for_delivery` has an OTP; completing without verifying returns 400; the 6th wrong attempt locks the leg | 1.9 |
| MOB-L3 Double assignment | SA-MOB-002 | see CC-6 | — | — | 1.9 |
| MOB-L4 Address IDOR | SA-MOB-005 | integration-TC + RLS-SQL | `operations.IntegrationTests/Orders/PickupAddressOwnershipTests.cs` (new) | A foreign `addressId` (another customer, or another brand) returns 404 on customer and admin pickup create; an own address returns 201 (reverses T7b) | 0.7 |
| MOB-L5 Ping validation | SA-MOB-010 | unit + integration-TC | `operations.Tests/Logistics/LocationPingValidatorTests.cs` (new) | Lat/lng ranges, batch ≤ 50, `PingedAt` outside [now−15 m, now+2 m] clamped, accuracy filter; an off-duty ping is ignored (204) | 1.9 |
| MOB-L6 Suspended rider | SA-MOB-011 | integration-TC | `operations.IntegrationTests/Logistics/RiderStatusAccessTests.cs` (new; 10c location authz) | A rider suspended or terminated through `UpdateRider` gets 403/404 on ping and status | 1.9 |
| MOB-L7 Offline queue | SA-MOB-008 | unit (jest) | `rider-mobile/src/__tests__/offlineQueueStore.test.ts` (extend) | A 400 is not enqueued; a poison item is skipped; a `failed` replay carries its reason | 1.9 |
| MOB-L8 Headless auth | SA-MOB-009 (Suspected) | unit (jest) + device | `rider-mobile/src/__tests__/backgroundLocation.test.ts` (new) | With an unhydrated store, the task reads SecureStore, and a 401 never triggers `logout()`; confirm on a device | 1.9 |
| MOB-L9 Customer cancel releases legs | SA-MOB-016 | integration-TC | `operations.IntegrationTests/Orders/CancelOrderLegsTests.cs` (new) | Cancelling the order cancels its active legs and decrements load | 3.2 |
| MOB-L10 Location authz | 10c location-authz table, SA-ORC-001 | integration-TC | `operations.Tests/Logistics/GetRidersLiveTests.cs` (extend) | Rider X cannot read or PATCH rider Y's leg (404); customer endpoints never return rider coordinates; a store-scoped admin cannot read another store's rider track | 3.4 |
| MOB-L11 Pickup progress | SA-MOB-014 | integration-TC | `operations.IntegrationTests/Logistics/PickupProgressTests.cs` (new) | A `started` leg moves the pickup to `rider_dispatched`, and `arrived` to `arrived` | 3.4 |
| MOB-L12 Offer mode | SA-MOB-006 | integration-TC + device | `operations.IntegrationTests/Logistics/OfferFlowTests.cs` (new) | The rider sees the offer; accept → the task appears; decline → re-offered | 3.4 |
| MOB-L13 Historical PII | SA-MOB-019 | unit | `operations.Tests/Logistics/RiderTaskMapperTests.cs` (new) | Past-date task DTOs have a masked phone and address | 3.4 |
| MOB-L14 Coordinates and quote | SA-MOB-004 | integration-TC | `operations.IntegrationTests/Location/AddressGeoTests.cs` (new) | An address saved with coordinates (device or fake geocoder) → the quote succeeds; a leg copies the geo; the geofence flips within 150 m | 3.5 |
| MOB-L15 Serviceability | SA-MOB-013 | integration-TC | `operations.IntegrationTests/Location/ServiceabilityTests.cs` (new) | An unserviceable pincode returns 422; slots are filtered by the resolved store | 3.5 |
| MOB-L16 Customer live view | SA-MOB-014 (12 R13) | integration-TC | `operations.IntegrationTests/Location/RiderApproachingTests.cs` (new) | A coarse location is shown only for the caller's own `started`/`arrived` leg, and stops at terminal states | 5.2 |

### 3.16 Other tests referenced by the roadmap

| Test | Guards | Level | Location | Pass criterion | Roadmap |
|---|---|---|---|---|---|
| REF-1 Refund API contract | SA-QC-001 | unit + integration-TC | `commerce.Tests/Payments/IssueRefundTests.cs` (new; 10c list) | Each documented refund method persists; with a fake gateway, zero calls are made when the row insert fails | 0.6 |
| STO-1 Object storage provider | SA-OPS-003 | integration | `operations.Tests/Storage/FileStorageContractTests.cs` (new) | Save, read and delete with brand-prefixed keys; files survive a container restart in the compose smoke | 0.10 |
| WEB-1 admin-web logout | SA-FE-002 | E2E (Playwright) + integration-TC | `admin-web/e2e/logout.spec.ts` (new) | login, reload, logout, go to `/`: the app lands on `/login`, and a later cookie refresh returns 401 | 1.11 |
| WEB-2 pos-web token storage | SA-FE-003 | unit (Vitest) | `pos-web/src/stores/__tests__/authStore.test.ts` (new) | The persisted `lg-pos-auth` has no `refreshToken`; a hard reload still refreshes through the cookie | 1.11 |
| WEB-3 No demo data in production | SA-FE-010 | unit (jest) | `customer-mobile/src/__tests__/bookingItems.test.ts` (new) | An empty price list in production shows the empty state with no demo items | 1.11 |
| WEB-4 Logout residue and error boundary | SA-FE-016, SA-FE-015 | unit (Vitest/jest) | the client `__tests__` folders | Every logout path clears the cart, offline queue and query cache; the root route has an `errorElement` | 1.11 |
| OBS-1 Tenant log scope | SA-OPS-008 | unit | `operations.Tests/Observability/TenantScopeMiddlewareTests.cs` (new) | The middleware opens a `{brand_id,user_id}` scope and tags `Activity.Current` | 1.12 |
| ARCH-T1…T3 Architecture rules | SA-ARCH-001, SA-SOLID-009, SA-SOLID-012, SA-ARCH-009 | unit (NetArchTest) | `operations.Tests/Architecture/*` (new) | T1: Application does not depend on Infrastructure, `LaundryGharDbContext` or `Microsoft.AspNetCore.Http`. T2: no `.Add`/`.Update` on a foreign module's sets. T3: middleware order per host | 3.1 |
| COUP-1 Coupon policy | SA-SOLID-006 | unit | `commerce.Tests/Coupons/CouponEligibilityPolicyTests.cs` (new) | Rounding, cap, minimum, first-order and eligibility cases | 3.7 |
| PERF-1 RLS predicate plan | SA-DB-017 | RLS-SQL | `operations.IntegrationTests/Rbac/SubBrandScopeRlsTests.cs` (extend) | The plan shows an InitPlan, not a per-row plpgsql call | 5.3 |
| E2E-PAY-1 Online payment | SA-FE-012, SA-API-007 | device + sandbox | customer-mobile manual or Detox-style script | UPI/card payment end to end; the order is marked paid exactly once | 2.6 |

---

## 4. CI gating recommendations

| # | Gate | Today | Recommendation | Findings |
|---|---|---|---|---|
| CI-1 | **Release depends on CI** | `release.yml` runs on every push to `main` with no `needs`/`workflow_run`. Release succeeded on SHAs where CI failed. | Trigger release with `workflow_run: CI, conclusion: success`, or merge the jobs with `needs:`. Deploy by SHA tag, never `latest`. Enable branch protection requiring CI. Phase 0 (0.2) does this for the backend and admin-web jobs first. Add a step that builds the admin-web image and greps `dist/assets/*.js` for each gateway prefix. | SA-OPS-006, SA-FE-001 |
| CI-2 | **Mobile jobs green and required** | rider `npm ci` ERESOLVE; customer and rider `tsc` TS2882 | Regenerate the rider lockfile or align `react`/`react-dom`; commit a stub `expo-env.d.ts` or a `*.css` module declaration; then make the mobile matrix a required check. The rider typecheck is currently masked behind the `npm ci` failure ([10b](specialists/10b-qa-verification-platform.md)), so expect it to appear once `npm ci` passes. | SA-FE-011 (SA-MOB-017) |
| CI-3 | **pos-web in CI** | No job, no Dockerfile, no release entry | Add `npm ci`, `tsc -b`, `lint` and `build` (QA-B ran these clean locally), plus Vitest once WEB-2 exists. Add it to the release matrix only after SA-FE-003 is fixed. | SA-FE-011, SA-OPS-012 |
| CI-4 | **Schema build in CI** | The migration job only lints up/down pairing | A `schema` job on a PG 18 service with partman and postgis: baseline + migrations → `verify` → MIG-2 round trip → MIG-3 EF check → MIG-4/5/6/8 assertions → the ISO-1 lane matrix and RLS-SQL suites against this schema | SA-DB-002, SA-OPS-007, SA-TEN-010 |
| CI-5 | **Testcontainers fail, not pass, when Docker is missing** | 60 early returns in 17 files report Passed | Use `SkippableFact`/`Assert.Skip`, or throw when `CI=true`; add MIG-9; fail the job if any integration test is Skipped in CI | SA-QB-002 |
| CI-6 | **Commerce tests in the backend job** | No project | Add `tests/commerce.Tests` to `laundryghar.slnx` so `dotnet test` runs it | SA-ARCH-010 |
| CI-7 | **Tests that encode defects are replaced, not deleted** | `RateLimitPartitioningTests` and `SubBrandScopeRlsTests` assert vulnerable behaviour | Replace them in the same PR as the fix (BYP-2, ISO-1); keep the staff fail-closed assertions | SA-QA-002, SA-TEN-001 |
| CI-8 | **Supply chain** | Floating base-image tags; actions pinned to majors; no scan | Pin digests and SHAs; add an image scan (Trivy/Grype) and an SBOM to release; exclude `appsettings.Development.json` from images | SA-OPS-017 |
| CI-9 | **Web unit tests** | None | Vitest + React Testing Library job for admin-web and pos-web, starting with `orderStatus`/`allowedTransitions`, `routePermissions` and the interceptors | SA-FE-011 |
| CI-10 | **PG version parity** | Tests on `postgres:16-alpine`; production compose on `postgres:18` | Run Testcontainers and the schema job on the production major | SA-OPS-007 |

---

## 5. Acceptance criteria per roadmap phase (exit gates)

A phase is complete only when all of its gate items pass in CI. Items that cannot run in CI (device checks, production inspection) need a recorded manual sign-off. The architect's exit criteria ([01b §5](specialists/01b-architect-challenge-review.md)) are included verbatim in meaning and extended with the tests above.

### Phase 0 — Verification & critical risks
1. **Production state recorded.** Roadmap 0.1 is complete, and every finding it lists is labelled live or latent.
2. **No platform takeover.** RBAC-1, RBAC-2, RBAC-3, RBAC-4, RBAC-5 and RBAC-7 pass. Architect: "anonymous signup cannot obtain platform rights."
3. **Lanes restored and isolated.** ISO-1, ISO-2, ISO-3, ISO-4, MOB-L4 and PAR-8 pass on a 0031-migrated schema. Architect: "customer booking works on a 0031-migrated DB."
4. **No cross-tenant credentials.** BJ-4 passes. Architect: "one tenant's messages never use another's credentials."
5. **Auth not globally throttled.** BYP-3 passes.
6. **Money paths correct.** CC-2, BJ-3, BYP-8 and REF-1 pass. The pending refunds have been reconciled before the executor is enabled.
7. **Leg integrity.** MOB-L1 passes.
8. **Lifecycle and DB privileges.** SUS-1 and ISO-5 pass.
9. **Data durability.** BJ-7, MIG-6 and STO-1 pass. A scheduled restore-verify has succeeded at least once.
10. **Bootstrap frozen.** MIG-4 passes.
11. **Product surface.** BT-5, the CI-1 bundle grep and PAR-10 pass.
12. **Tests are binding.** CI-1 (backend and admin-web), MIG-9 and CI-6 (scaffold) are in place.

### Phase 1 — Production / SaaS foundation
1. A fresh environment built from baseline + migrations passes `verify`, the MIG-2 round trip and the MIG-3 EF-model check (architect). CI-4 and CI-10 are in place.
2. Two worker instances run each job once (architect): BJ-1, BJ-2, BJ-5 and BJ-10.
3. CI is green on `main`, including mobile (architect), with CI-2, CI-3, CI-5, CI-7, CI-8 and CI-9 required.
4. One tenant context: ISO-1 (all lanes and hosts), ISO-6, ISO-10, ISO-11, RBAC-9, ABAC-3 and SUS-2…SUS-4.
5. Identity hardening: ISO-7, ABAC-1, ABAC-2, RBAC-6, RBAC-8, RBAC-10 and CC-8.
6. Concurrency model: CC-1, CC-3, CC-4, CC-5, CC-9, ENT-11, ISO-12 and PAR-6.
7. Validation: BYP-4, BYP-5 and BYP-7. Rate limiting: BYP-2.
8. Dispatch and rider app: CC-6, MOB-L2, MOB-L5…MOB-L8 and BJ-9.
9. Commerce coverage: PAR-1 and PAR-7. Web hygiene: BR-7, VW-4, BYP-9 and WEB-1…WEB-4. Observability: OBS-1, with alerts defined.

### Phase 2 — Subscription & entitlement correctness, plus onboarding prerequisites
1. An unpaid tenant loses paid features automatically; paying reinstates them; non-staff lanes return 402/403 correctly (architect). The tests are ENT-1, ENT-2, ENT-6, ENT-7, SUS-5 and SUS-6.
2. Catalogue integrity: ENT-3, ENT-4 and ENT-5.
3. Billing operations: ENT-9, ENT-10, ENT-12, ENT-13, ENT-14 and SUS-7.
4. Royalty: PAR-9.
5. Onboarding prerequisites: BT-1, ONB-T1, ONB-T2 and ONB-T3.
6. Customer payments (2.6): E2E-PAY-1 passes, and it ships only after the Phase 0 money gate.

### Phase 3 — Modular verticals, and dispatch/location boundaries
1. Adding a test vertical touches only its module plus registration (architect). This is shown by ARCH-T1…T3 and by a throwaway module PR.
2. The **laundry parity suite is green** (architect): PAR-1…PAR-7 and PAR-9.
3. One status writer and vertical correctness: VW-1…VW-3, VW-5…VW-9, BT-4 and MOB-L9.
4. Dispatch and location: MOB-L10…MOB-L15.
5. DB backstops: ISO-8, ISO-9 and BYP-10. Coupons: BYP-6 and COUP-1.

### Phase 4 — White-label experiences
1. A new tenant self-onboards to a branded web and app experience without code changes (architect). The tests are ONB-T4…ONB-T7.
2. Branding isolation: BR-1…BR-6 and BR-8.
3. Provisioning and vertical governance: BT-2, BT-3 and BT-6.
4. Raise SA-ONB-001 to High when this phase starts (orchestrator note in the registry); BR-3 is its gate.

### Phase 5 — New verticals & production hardening at scale
1. A salon tenant books, serves and bills appointments end to end, and laundry is unaffected (architect). The tests are VW-S1…VW-S3, CC-7 and the full PAR suite.
2. Tiffin and courier: VW-T1, MOB-L14 and MOB-L16.
3. Scale-out, only on measured need: PERF-1 and ISO-10 (pool reuse), with the connection budget and Redis-backed caches and limiters in place before more than one replica.
4. ABAC (if activated): ABAC-4 and ABAC-5.
