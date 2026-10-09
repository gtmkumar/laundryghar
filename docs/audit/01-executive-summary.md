# Report 1 — Executive Audit Summary

**Subject:** whether LaundryGhar is ready to become a production-grade, multi-tenant, multi-vertical SaaS platform.
**Date:** 2026-10-09.
**Code baseline:** commit `274b7af` on `main` (later commits on the branch change only `docs/`).
**Method:** twelve specialist agents audited independently, three QA agents re-verified them, and the Principal Architect ran a challenge review. All findings are in [`FINDINGS.md`](../../FINDINGS.md); all verdicts are in [11-final-verdict.md](11-final-verdict.md).

## Bottom line

LaundryGhar is **not ready for commercial multi-tenant SaaS operation** (Q15: Not Supported, unanimous), but it is much further along than a single-tenant app with a `tenant_id` column. The foundations are real:
- brand-as-tenant with signed claims;
- explicit handler predicates;
- PostgreSQL RLS on all 126 brand tables;
- a pool-safe tenant-context interceptor;
- transactional self-signup;
- a feature-entitlement catalogue enforced on the staff API;
- a fulfilment-strategy seam for verticals.

What blocks it falls into three groups:
1. **Verified security failures that break isolation as a guarantee.** Above all, an anonymous self-signup can mint a platform administrator who bypasses every tenant boundary ([SA-AUTHZ-001](../../FINDINGS.md#sa-authz-001), Critical).
2. **Wiring defects that make whole lanes fail.**
   - A recent RLS migration (0031) denies customers and commerce-host staff every order, payment and pickup ([SA-TEN-001](../../FINDINGS.md#sa-ten-001), Critical).
   - The login rate limiter collapses all tenants into one 10-per-minute bucket behind the gateway ([SA-API-001](../../FINDINGS.md#sa-api-001), Critical).
   - The subscription billing lifecycle does not run.
3. **Single-node and developer-machine operational assumptions:**
   - a non-reproducible schema;
   - uploads stored in `/tmp`;
   - partition maintenance scheduled from a Mac;
   - release not gated on CI.

Multi-vertical support is **scaffolding, not product**. Salon and tiffin templates are publicly sellable, yet every order runs the laundry workflow and is invoiced as laundry.

**The recommended direction is evolutionary.** Keep the modular monolith on one PostgreSQL database with RLS, and add four things: a separate worker host, a separate platform control plane, explicit vertical modules, and a Location/Dispatch module. **Microservices, database-per-tenant and per-tenant codebases are not justified by the evidence** ([06-target-architecture.md](06-target-architecture.md)).

## Findings at a glance

| | Count |
|---|---|
| Finding IDs raised by all agents | 211 |
| Canonical findings after de-duplication | 156 (55 duplicates preserved and cross-referenced) |
| Critical / High / Medium / Low / Info (canonical) | 3 / 40 / 79 / 32 / 2 |
| Scheduled in Phase 0 (verified critical risks) | 27 |
| QA outcome on re-verified findings (122 rows: 44 in 10a, 50 in 10b, 28 in 10c) | 0 rejected outright; 1 partial false positive (SA-MOB-011, scope corrected); 14 severity corrections (10 lowered, 4 raised to match a duplicate); 25 duplicate identifications. The orchestrator then raised SA-TEN-002 to High on QA-C's reproduction. |

## Capability-by-capability readiness

| Capability | Verdict | One-line reason |
|---|---|---|
| Overall architecture | Partially Supported | A clean project graph, CQRS slices and a strategy seam. But it is one application split into three processes that share one EF model and database. The CQRS pipeline is dead, the domain model is anemic, and workers are co-hosted without locks ([01](specialists/01-architecture.md), [01b](specialists/01b-architect-challenge-review.md)). |
| SaaS and multi-tenancy | Partially Supported (mechanism) / **Not Supported** (guarantee) | Brand claim, predicates and RLS work for direct access. Takeover chains, cross-tenant notification credentials, commerce-host customer RLS fail-open and the 0031 lane outage break the guarantee ([03](03-saas-readiness-matrix.md) §1). |
| Subscription and billing | Partially Supported (catalogue, staff gating) / **Not Supported** (lifecycle) | Trials never end. Renewals see zero rows under RLS. Paid overdue invoices are ignored. Entitlements are not tied to payment and can be self-granted. |
| Business-vertical extensibility | **Not Supported** | Order creation ignores the brand's vertical. Invoices, notifications and the admin state machine are laundry-coded. Salon and tiffin cannot run. |
| Onboarding and white-label | Partially Supported (backend signup) / **Not Supported** (white-label) | Signup provisions a tenant server-side, but no client calls it. Branding has no write path. Custom domains don't survive the gateway. Mobile apps are one build-time brand. |
| ABAC / RBAC | RBAC **Partially Supported**; ABAC **Not Supported** (inert) | All 514 endpoints are gated, but identity administration lacks rank, scope and type ceilings. The ABAC engine is disabled ([04](04-abac-rbac-security-audit.md)). |
| OOP / SOLID | Partially Supported | Sound ports and layering by convention. Business rules are duplicated across 3–5 handlers, which has already produced live defects (royalty always zero, divergent order-status writers) ([05](05-oop-solid-compliance.md)). |
| Database integrity, indexing, idempotency and RLS | RLS support **Fully**; configuration **Partially**; duplicate prevention **Not Supported** | Over-refund and wallet lost update were reproduced. There are no composite tenant FKs. The app role can self-enable the RLS bypass ([09](09-database-audit.md)). |
| Customer and delivery-partner mobile apps, maps | Partially Supported (apps) / **Not Supported** (maps, zones, consistent dispatch) | Both apps are real and API-backed. There is no geocoding or serviceability, no leg state machine, and one job can be held by two riders ([10](10-mobile-delivery-maps.md)). |
| Production readiness | **Not Supported** | See the top risks below. |

## Top verified risks (act on these first)

| # | Risk | IDs | Severity | Status |
|---|---|---|---|---|
| 1 | **Platform takeover from anonymous signup.** Signup grants `users.create`. `CreateUser` accepts `userType=platform_admin` from the client. A platform admin gets every permission and the RLS bypass. | SA-AUTHZ-001 (+ root cause [SA-ARCH-013](../../FINDINGS.md#sa-arch-013), [SA-QA-001](../../FINDINGS.md#sa-qa-001)) | Critical | Verified: static chain traced by 3 agents; DB step reproduced |
| 2 | **Customer and commerce lanes denied under migration 0031.** Customers can't list orders or slots or book a pickup. Commerce-host staff see 0 payments. | SA-TEN-001 (dups SA-DB-001, SA-AUTHZ-006); root cause [SA-ARCH-014](../../FINDINGS.md#sa-arch-014) | Critical | Verified at SQL level; HTTP not run. Live outage only if production applied 0031 (unknown) |
| 3 | **Platform-wide login throttling.** Behind the gateway, every tenant's login, OTP, refresh and signup share one 10/min bucket. | SA-API-001 (dup SA-OPS-001) | Critical | Partially Verified: config and code read |
| 4 | **Cross-tenant takeover and leakage.** Membership grants to foreign users plus target-less identity writes. Another tenant's WhatsApp/SMS account sends your customers' messages. Commerce-host customers see each other's wallets. | [SA-AUTHZ-003](../../FINDINGS.md#sa-authz-003), [SA-AUTHZ-002](../../FINDINGS.md#sa-authz-002), [SA-API-012](../../FINDINGS.md#sa-api-012), [SA-TEN-002](../../FINDINGS.md#sa-ten-002) | High | Verified; TEN-002 reproduced |
| 5 | **Money paths.** Captured online payments are ignored or dropped. Cancellation refunds are never executed. The admin refund contract violates a DB CHECK after Razorpay has already refunded. Over-refund race reproduced. Royalty is always zero. | [SA-API-007](../../FINDINGS.md#sa-api-007), [SA-API-008](../../FINDINGS.md#sa-api-008), [SA-QC-001](../../FINDINGS.md#sa-qc-001), [SA-API-009](../../FINDINGS.md#sa-api-009), [SA-SOLID-003](../../FINDINGS.md#sa-solid-003) | High | Verified / reproduced |
| 6 | **Entitlement and billing integrity.** Brand admins can self-grant `saas.manage` and mark their own invoice paid. Trials never end. Suspended brands self-unsuspend. | [SA-AUTHZ-004](../../FINDINGS.md#sa-authz-004), [SA-SUB-001](../../FINDINGS.md#sa-sub-001), [SA-TEN-003](../../FINDINGS.md#sa-ten-003) | High | Verified; TEN-003 reproduced |
| 7 | **Data-loss operations.** Uploads live in container `/tmp`. Partition maintenance runs only on a developer Mac (runway 2026-12-01 per `db/HANDOFF.md`) and aborts on a stale config. | [SA-OPS-003](../../FINDINGS.md#sa-ops-003), [SA-OPS-004](../../FINDINGS.md#sa-ops-004), [SA-QC-003](../../FINDINGS.md#sa-qc-003) | High | Verified (config; QC-003 reproduced) |
| 8 | **Non-reproducible schema.** The documented bootstrap fails at migration 0005, and re-running it reverts the RLS-bypass hardening. | [SA-DB-002](../../FINDINGS.md#sa-db-002) | High | Reproduced twice |
| 9 | **Delivery consistency.** The rider status endpoint has no state machine, so a cancelled order can become "delivered" with a COD record. One job can be assigned to two riders. | [SA-MOB-001](../../FINDINGS.md#sa-mob-001), [SA-MOB-002](../../FINDINGS.md#sa-mob-002) | High | Verified |
| 10 | **Selling inoperable verticals.** The salon and tiffin templates are public, orders ignore the vertical, and parcel invoices carry the laundry SAC. | [SA-VERT-002](../../FINDINGS.md#sa-vert-002), [SA-VERT-001](../../FINDINGS.md#sa-vert-001), [SA-VERT-004](../../FINDINGS.md#sa-vert-004) | High | Verified |
| 11 | **Broken production web build and ungated releases.** The admin-web image omits 6 of 9 API URLs (reproduced). Release ships from a red `main`. | [SA-FE-001](../../FINDINGS.md#sa-fe-001), [SA-OPS-006](../../FINDINGS.md#sa-ops-006) | High | Verified / reproduced |

## Root causes

From [01b](specialists/01b-architect-challenge-review.md) §3. **RC1, RC6 and RC7 together explain every Critical.**

| # | Root cause |
|---|---|
| RC1 | No single schema source of truth |
| RC2 | Cross-cutting policy is opt-in per endpoint (the CQRS pipeline is not wired) |
| RC3 | Anemic shared model, with invariants duplicated across handlers |
| RC4 | No concurrency model |
| RC5 | Workers co-hosted without locks or a tenant-scope contract |
| RC6 | The identity plane trusts client-supplied attributes, and platform power is one mutable column |
| RC7 | Tenant context is composed per host and per lane |
| RC8 | Subscription state is not the source of entitlements |
| RC9 | Verticals are modelled as metadata, not modules |
| RC10 | Single-node infrastructure assumptions |
| RC11 | Documentation runs ahead of code and has been used as evidence |

## What genuinely works (balanced view)

| Area | Positive controls |
|---|---|
| Tokens and tenant context | RS256 JWT validation on every host. Tenant comes only from signed claims; the header override is platform-admin only. Every endpoint carries explicit authorization metadata. |
| Database | RLS on 126/126 brand tables, with A/B isolation proven live for all four operations. `app_user` is NOSUPERUSER/NOBYPASSRLS and owns no tables. The RLS interceptor rewrites every GUC on each connection open. |
| Entitlements and signup | Staff-lane entitlement stripping with a distinct 402. Live permission revocation via `perm_version`. OTP-first transactional signup with server-generated brand codes. |
| Payments and booking | Webhook HMAC is constant-time and fails closed. Customer pickup and slot booking is truly idempotent when a key is sent. Atomic order-number counter. The partner-wallet money path is correctly locked and idempotent. |
| Operations | The migration tool is transactional with checksum drift detection and verified round-trips. Backup scripts include a restore verifier. Containers run as non-root, with security headers and circuit breakers at the gateway. |
| Tests | Backend tests pass in CI: 137 + 426 + 284 (job 108549585458). Mobile unit tests pass locally: customer 170/170, rider 91/91. |

## Major uncertainties and untested areas

- **Production state is unknown:**
  - applied patches and migrations;
  - the connection role;
  - partition runway;
  - whether staging or production environments exist.

  This decides whether SA-TEN-001 is a live outage today or a release blocker.
- **Nothing ran at HTTP or .NET runtime level** (no SDK or Docker in the audit container). SQL claims were reproduced on throwaway PostgreSQL 16 clusters built from the repo, using documented workarounds.
- **Not run:** device behaviour (background GPS, push, headless auth), external providers (Razorpay, WhatsApp, maps) and performance at production scale (EXPLAIN on synthetic data only).
- **QA coverage was partial below High.** QA re-verified every Critical and High finding and about 40–50% of Mediums; the rest rest on specialist evidence, as labelled in the registry.

## Recommended next steps

1. **Confirm production state this week** (read-only):
   - Which migrations and patches are applied: 0031, `phase*`, `apply_saas_billing_patches.sh`.
   - The connection role.
   - `part_config` and partition runway.
   - Whether `brand_admin` holds `users.create` in the live DB.

   The checks are listed in [09-database-audit.md](09-database-audit.md) and [11-final-verdict.md](11-final-verdict.md) §E.
2. **Phase 0**, before any commercial tenant ([07-remediation-roadmap.md](07-remediation-roadmap.md)):
   - Close the takeover: server-derived `user_type`, a DB trigger on `user_type`, target guards, a grant ceiling.
   - Fix the 0031 customer and commerce lanes **together with** the pickup address-ownership check.
   - Use brand-keyed notification credentials.
   - Fix the trusted client IP for rate limiting.
   - Fix the refund, capture and royalty money paths.
   - Move uploads to object storage.
   - Schedule partman and repair the stale config.
   - Hide inoperable vertical templates.
   - Fix the admin-web image URLs.
   - Freeze a reproducible schema baseline.
3. **Phase 1:**
   - CI green and gating releases, including a schema build;
   - a worker host with locks;
   - concurrency tokens and partial unique indexes;
   - the validation pipeline;
   - a unified tenant context.
4. **Phase 2:** subscription-driven entitlements on all lanes, and a working billing lifecycle.
5. **Phases 3–5:**
   - vertical modules and the order-transition service;
   - Dispatch and Location modules;
   - white-label;
   - new verticals.

## Report index

See [README.md](README.md) for the full index of reports, specialist evidence and verification reports.
