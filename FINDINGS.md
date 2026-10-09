# FINDINGS — Unified findings registry (LaundryGhar SaaS audit, 2026-10-09)

This registry consolidates every finding from the multi-agent audit in [`docs/audit/`](docs/audit/README.md). It was generated from the specialist reports in `docs/audit/specialists/` after independent verification by three QA agents (`10a`, `10b`, `10c`) and the Principal Architect's challenge review (`01b`). The machine-readable copy is [`docs/audit/findings-registry.json`](docs/audit/findings-registry.json).

**ID convention.** `SA-<AREA>-NNN` (SaaS Audit). It does not collide with earlier IDs in `docs/AUDIT_REPORT.md` (A-n, F-n), `docs/ABAC_AUDIT_2026-08-31.md` (F-n) or `docs/QA-BUG-REPORT-2026-07-18.md` (BUG-n). No earlier root `FINDINGS.md` existed. Every ID raised by any agent is preserved. Where two agents reported the same defect, one ID is **canonical** and the others are listed as **duplicates** pointing to it. Distinct issues that share a theme are cross-referenced as *related*, not merged.

**Severity** is the consolidated value after QA. Where it differs from the specialist's original rating, both are shown with the reason. **Status** uses Verified / Partially Verified / Suspected / Not Tested. "Verified" means the code path was read end to end and, where stated, reproduced; no HTTP-level or .NET test execution was possible in the audit environment (no .NET SDK, no Docker). SQL-level claims were reproduced on throwaway PostgreSQL 16 clusters built from the repository. **Phase** is the consolidated roadmap phase and takes precedence over the specialist's original priority note (shown verbatim in each entry; they can differ, e.g. where QA raised severity or the architect re-sequenced dependencies). It refers to the remediation roadmap in [`docs/audit/07-remediation-roadmap.md`](docs/audit/07-remediation-roadmap.md) (0 = verified critical risks … 5 = new verticals / hardening at scale).

**Totals.** 211 IDs raised; 156 canonical findings after de-duplication (55 duplicates). Canonical by severity: Critical 3, High 40, Medium 79, Low 32, Informational 2. Canonical by phase: P0 27, P1 58, P2 21, P3 33, P4 11, P5 6.

## 1. Index of canonical findings

| ID | Severity | Status | Phase | Area | Title | Duplicates |
|---|---|---|---|---|---|---|
| [SA-API-001](#sa-api-001) | Critical | Partially Verified | P0 | Backend/API | Production auth rate limiter collapses into one global bucket for all tenants (gateway IP) | SA-OPS-001 |
| [SA-AUTHZ-001](#sa-authz-001) | Critical | Verified | P0 | Authorization (RBAC/ABAC) | Any `users.create` holder can create a `platform_admin` account (unauthenticated → platform takeover via … |  |
| [SA-TEN-001](#sa-ten-001) | Critical | Verified | P0 | Multi-tenancy | Migration 0031's restrictive scope policy denies ALL rows (and all audited writes) for customer, API-key and … | SA-AUTHZ-006, SA-DB-001 |
| [SA-API-007](#sa-api-007) | High | Verified | P0 | Backend/API | Customer online payment lifecycle is broken end to end (captured money can be ignored; orders never marked … | SA-SOLID-002 |
| [SA-API-008](#sa-api-008) | High | Verified | P0 | Backend/API | Cancellation refunds are queued but never executed |  |
| [SA-API-009](#sa-api-009) | High | Verified | P0 | Backend/API | Admin refund calls Razorpay inside a retried DB transaction; cumulative cap is racy | SA-DB-006 |
| [SA-API-012](#sa-api-012) | High | Verified | P0 | Backend/API | Notification worker sends every tenant's WhatsApp/SMS with one arbitrary tenant's credentials | SA-SOLID-007 |
| [SA-ARCH-013](#sa-arch-013) | High | Verified | P0 | Architecture | Platform control plane is not separated from the tenant plane; global authority is a single mutable column |  |
| [SA-ARCH-014](#sa-arch-014) | High | Verified | P0 | Architecture | Tenant context is implemented three times and per lane, and the implementations diverge |  |
| [SA-AUTHZ-002](#sa-authz-002) | High | Verified | P0 | Authorization (RBAC/ABAC) | Identity write handlers lack target-rank and target-scope guards (in-brand account takeover) |  |
| [SA-AUTHZ-003](#sa-authz-003) | High | Partially Verified | P0 | Authorization (RBAC/ABAC) | `GrantMembership` accepts any user id platform-wide (cross-tenant attachment → cross-tenant takeover) | SA-TEN-004 |
| [SA-AUTHZ-004](#sa-authz-004) | High | Verified | P0 | Authorization (RBAC/ABAC) | No permission ceiling on role edits and user overrides; platform-plane handlers rely on permission codes only … |  |
| [SA-DB-002](#sa-db-002) | High | Verified | P0 | Database (index/idempotency/RLS) | The documented fresh-build path cannot reproduce the production schema; re-running it silently reverts the … | SA-ARCH-008, SA-SUB-020, SA-VERT-010 |
| [SA-DB-012](#sa-db-012) | High | Verified | P0 | Database (index/idempotency/RLS) | Customer-identity and salon RLS policies use a raw uuid cast that throws on an empty brand GUC, even under … |  |
| [SA-FE-001](#sa-fe-001) | High | Verified | P0 | Frontend/clients | admin-web production image bakes only 3 of its 9 API base URLs |  |
| [SA-MOB-001](#sa-mob-001) | High | Verified | P0 | Mobile/delivery/maps | Rider task status endpoint has no state machine: any leg can be moved to any status, reviving cancelled legs … |  |
| [SA-OPS-003](#sa-ops-003) | High | Verified | P0 | DevOps/production | Uploaded files live in the container's `/tmp` and are lost on every redeploy | SA-API-017 |
| [SA-OPS-004](#sa-ops-004) | High | Verified | P0 | DevOps/production | pg_partman maintenance for orders/audit/process/notification/decision logs is scheduled only on a developer … | SA-MOB-012 |
| [SA-QC-001](#sa-qc-001) | High | Partially Verified | P0 | QA (DB/mobile) | Admin refund API contract ("gateway"/"wallet") violates the `refund_type` CHECK; the Razorpay refund is … |  |
| [SA-TEN-002](#sa-ten-002) | High | Verified | P0 | Multi-tenancy | Commerce host never sets `app.current_customer_id`, so customer-level RLS on payments, wallets, refunds and … | SA-AUTHZ-005 |
| [SA-TEN-003](#sa-ten-003) | High | Verified | P0 | Multi-tenancy | A suspended brand (including a ToS or manual suspension) can lift its own suspension through cancel → withdraw |  |
| [SA-VERT-002](#sa-vert-002) | High | Verified | P0 | Verticals/domain | Salon and tiffin templates are publicly sellable at signup, but neither vertical is operable | SA-ARCH-004 |
| [SA-API-002](#sa-api-002) | High | Verified | P1 | Backend/API | Gateway rate-limit partition is attacker-controlled (bypass and targeted tenant throttling) | SA-AUTHZ-010, SA-OPS-002, SA-TEN-005 |
| [SA-API-004](#sa-api-004) | High | Verified | P1 | Backend/API | POS CreateOrder idempotency is check-then-act on jsonb with no unique constraint (duplicate orders and double … | SA-DB-011 |
| [SA-API-005](#sa-api-005) | High | Verified | P1 | Backend/API | No optimistic concurrency anywhere; balances and counters are lost-update prone | SA-DB-008 |
| [SA-DB-005](#sa-db-005) | High | Verified | P1 | Database (index/idempotency/RLS) | Identity tables without RLS: cross-tenant role grants and PII/token reads are possible at the DB layer |  |
| [SA-MOB-002](#sa-mob-002) | High | Verified | P1 | Mobile/delivery/maps | One job can be held by two riders: no uniqueness, locking or status check on assignment creation; no … | SA-DB-022 |
| [SA-MOB-003](#sa-mob-003) | High | Verified | P1 | Mobile/delivery/maps | Proof-of-delivery OTP is never generated, so the OTP gate is inert; verification has no attempt limit |  |
| [SA-OPS-006](#sa-ops-006) | High | Verified | P1 | DevOps/production | Release images ship from a red `main`; no deploy, migration or approval stage |  |
| [SA-SOLID-003](#sa-solid-003) | High | Verified | P2 | OOP/SOLID | Payment status is an unconstrained string. Royalty revenue filters on `"completed"`, which the DB never … |  |
| [SA-SUB-001](#sa-sub-001) | High | Verified | P2 | Subscription/billing | Self-serve trials never end: `trialing` brand subscriptions are never converted, invoiced or expired, and … |  |
| [SA-SUB-002](#sa-sub-002) | High | Verified | P2 | Subscription/billing | Paying an overdue (`past_due`) brand invoice via Razorpay is ignored, and a payment link cannot be created … | SA-API-011 |
| [SA-SUB-003](#sa-sub-003) | High | Verified | P2 | Subscription/billing | A brand subscription never returns from `past_due` to `active`, so after one late payment renewals stop for … |  |
| [SA-SUB-004](#sa-sub-004) | High | Partially Verified | P2 | Subscription/billing | The brand renewal pass runs outside a trusted worker scope, so RLS hides all subscriptions and no renewal … | SA-API-010 |
| [SA-SUB-006](#sa-sub-006) | High | Verified | P2 | Subscription/billing | Subscription state and entitlements are disconnected: features are granted without payment and kept after … |  |
| [SA-SUB-007](#sa-sub-007) | High | Verified | P2 | Subscription/billing | No tenant-facing billing: owners cannot see their plan or invoices, cannot pay or upgrade, and are given no … |  |
| [SA-FE-004](#sa-fe-004) | High | Verified | P3 | Frontend/clients | Admin order management hardcodes the laundry state machine and ignores the server's `allowedTransitions` |  |
| [SA-MOB-004](#sa-mob-004) | High | Verified | P3 | Mobile/delivery/maps | No geocoding or coordinate capture anywhere: geofence, distance-aware dispatch, coordinate navigation and … |  |
| [SA-SOLID-001](#sa-solid-001) | High | Verified | P3 | OOP/SOLID | Order status transitions are implemented separately in 5 write paths and have diverged (missed notifications, … | SA-API-006 |
| [SA-VERT-001](#sa-vert-001) | High | Verified | P3 | Verticals/domain | Order creation ignores the brand's vertical; every non-parcel order is a laundry `process_deliver` order … | SA-ARCH-003, SA-ONB-004, SA-SOLID-004 |
| [SA-VERT-004](#sa-vert-004) | High | Verified | P3 | Verticals/domain | Invoices hardcode laundry tax identity (SAC 999712, "Laundry & Dry-Cleaning Services") and laundry billable … |  |
| [SA-ONB-002](#sa-onb-002) | High | Partially Verified | P4 | Onboarding/white-label | Tenant branding (logo, colours, theme) has no usable write path and no client consumes it |  |
| [SA-VERT-003](#sa-vert-003) | High | Verified | P5 | Verticals/domain | No appointment-grade scheduling: no staff availability, service duration, operating-hours enforcement or … |  |
| [SA-DB-003](#sa-db-003) | Medium | Verified | P0 | Database (index/idempotency/RLS) | SECURITY DEFINER brand-lifecycle functions are callable by `app_user` with any brand id; `purge_brand` was … |  |
| [SA-MOB-005](#sa-mob-005) | Medium | Verified | P0 | Mobile/delivery/maps | Customer can attach another customer's address to a pickup request (IDOR); the rider is dispatched there with … |  |
| [SA-OPS-010](#sa-ops-010) | Medium | Partially Verified | P0 | DevOps/production | Backup/DR is daily logical dumps only, with no PITR, no encryption and no proven schedule |  |
| [SA-QA-001](#sa-qa-001) | Medium | Verified | P0 | QA (security) | InviteUser is not atomic: the user is committed before the membership guards run |  |
| [SA-QC-003](#sa-qc-003) | Medium | Verified | P0 | QA (DB/mobile) | Stale pg_partman config for `order_lifecycle.process_logs` makes `partman.run_maintenance_proc()` abort, so … |  |
| [SA-API-003](#sa-api-003) | Medium | Verified | P1 | Backend/API | Validation pipeline not wired: 40 FluentValidation validators never execute; CQRS behaviors are dead code | SA-ARCH-005, SA-SOLID-005 |
| [SA-API-015](#sa-api-015) | Medium | Verified | P1 | Backend/API | Watermark cursors can skip events permanently (notifications, loyalty earn) |  |
| [SA-API-016](#sa-api-016) | Medium | Verified | P1 | Backend/API | Unbounded `pageSize` on list endpoints |  |
| [SA-API-018](#sa-api-018) | Medium | Verified | P1 | Backend/API | Customer app does not send an idempotency key for booking, so the server guard is unused | SA-MOB-018 |
| [SA-API-019](#sa-api-019) | Medium | Verified | P1 | Backend/API | Admin/POS-created pickups ignore slot capacity, but rejection releases capacity |  |
| [SA-API-020](#sa-api-020) | Medium | Verified | P1 | Backend/API | Refresh-token rotation is not atomic |  |
| [SA-ARCH-006](#sa-arch-006) | Medium | Partially Verified | P1 | Architecture | Outbox is a DB-polling integration with no broker, no typed contracts, and inconsistent consumer semantics |  |
| [SA-ARCH-010](#sa-arch-010) | Medium | Verified | P1 | Architecture | Commerce (payments, wallets, subscriptions, billing workers) has no test project |  |
| [SA-AUTHZ-007](#sa-authz-007) | Medium | Verified | P1 | Authorization (RBAC/ABAC) | Scope check is decoupled from permission source (permission union × node union = scope amplification) |  |
| [SA-AUTHZ-009](#sa-authz-009) | Medium | Verified | P1 | Authorization (RBAC/ABAC) | User suspension/deactivation does not revoke live sessions; revocation check fails open |  |
| [SA-DB-009](#sa-db-009) | Medium | Verified | P1 | Database (index/idempotency/RLS) | Background workers claim rows without a guarded update or SKIP LOCKED: duplicate publish/charge across … |  |
| [SA-DB-010](#sa-db-010) | Medium | Partially Verified | P1 | Database (index/idempotency/RLS) | Coupon usage limits enforced only in the application |  |
| [SA-DB-014](#sa-db-014) | Medium | Verified | P1 | Database (index/idempotency/RLS) | The ABAC store's raw connections run without tenant GUCs: brand policies, roles and entitlements are … |  |
| [SA-DB-018](#sa-db-018) | Medium | Verified | P1 | Database (index/idempotency/RLS) | 69 FKs without supporting indexes; 55 redundant indexes |  |
| [SA-FE-002](#sa-fe-002) | Medium | Partially Verified | P1 | Frontend/clients | admin-web logout does not end the session after any page reload |  |
| [SA-FE-003](#sa-fe-003) | Medium | Verified | P1 | Frontend/clients | pos-web stores the refresh token in localStorage, and its refresh path ignores the HttpOnly cookie |  |
| [SA-FE-005](#sa-fe-005) | Medium | Verified | P1 | Frontend/clients | Brand switching and logout do not scope or clear the client cache, so data from one brand shows under another |  |
| [SA-FE-010](#sa-fe-010) | Medium | Verified | P1 | Frontend/clients | Customer booking falls back to hardcoded demo garments and prices in production |  |
| [SA-FE-011](#sa-fe-011) | Medium | Verified | P1 | Frontend/clients | Client quality gates: mobile CI is red, pos-web is outside CI/CD, web apps have no unit tests | SA-MOB-017 |
| [SA-MOB-008](#sa-mob-008) | Medium | Verified | P1 | Mobile/delivery/maps | Rider offline queue treats server rejections as "offline", poisons itself, and drops failure reasons; … |  |
| [SA-MOB-009](#sa-mob-009) | Medium | Suspected | P1 | Mobile/delivery/maps | Background location task may run without hydrated auth and trigger `logout()`, wiping stored tokens |  |
| [SA-MOB-010](#sa-mob-010) | Medium | Verified | P1 | Mobile/delivery/maps | Location ping ingestion is unvalidated and not gated by duty/assignment; client timestamps drive staleness … |  |
| [SA-MOB-011](#sa-mob-011) | Medium | Verified | P1 | Mobile/delivery/maps | Deactivating a rider does not stop tracking, task access or open legs |  |
| [SA-OPS-005](#sa-ops-005) | Medium | Partially Verified | P1 | DevOps/production | Background jobs assume a single commerce instance; two notification lanes can double-send or wedge | SA-API-014, SA-ARCH-007 |
| [SA-OPS-007](#sa-ops-007) | Medium | Verified | P1 | DevOps/production | Migrations are a manual step; CI never applies or rolls them back |  |
| [SA-OPS-008](#sa-ops-008) | Medium | Verified | P1 | DevOps/production | Logs, traces and metrics are not tenant-aware, and production exports nothing | SA-TEN-014 |
| [SA-OPS-009](#sa-ops-009) | Medium | Verified | P1 | DevOps/production | Secrets-provider abstraction is documented as done but absent from code |  |
| [SA-OPS-012](#sa-ops-012) | Medium | Verified | P1 | DevOps/production | Compose cannot pull the CI-built images, and pos-web has no deploy path |  |
| [SA-OPS-014](#sa-ops-014) | Medium | Verified | P1 | DevOps/production | Noisy-neighbour controls are global, not per tenant or plan |  |
| [SA-QB-002](#sa-qb-002) | Medium | Verified | P1 | QA (platform) | Integration tests report "Passed" when Docker is missing, and migration and rollback coverage is thin |  |
| [SA-QC-002](#sa-qc-002) | Medium | Verified | P1 | QA (DB/mobile) | Sub-brand RLS (0031) makes the per-brand `COUNT(*)+1` number generators scope-blind: routine intra-tenant … |  |
| [SA-SOLID-008](#sa-solid-008) | Medium | Verified | P1 | OOP/SOLID | The `LoggingChannelSender` null object breaks the `IChannelSender` contract in production, so undelivered … |  |
| [SA-TEN-007](#sa-ten-007) | Medium | Verified | P1 | Multi-tenancy | The database layer trusts the application completely: self-settable bypass GUC, blanket platform-admin … | SA-DB-015 |
| [SA-TEN-008](#sa-ten-008) | Medium | Partially Verified | P1 | Multi-tenancy | Suspension, cancellation and deletion are HTTP-only and partial gates | SA-AUTHZ-014, SA-SUB-017, SA-SUB-018 |
| [SA-TEN-010](#sa-ten-010) | Medium | Verified | P1 | Multi-tenancy | Isolation test suite does not exercise the real runtime path |  |
| [SA-TEN-015](#sa-ten-015) | Medium | Verified | P1 | Multi-tenancy | The runtime DB role password is hard-coded and re-applied by patches |  |
| [SA-AUTHZ-011](#sa-authz-011) | Medium | Verified | P2 | Authorization (RBAC/ABAC) | Plan entitlements are enforced only on the staff lane (token stripping); customer/partner lanes and … | SA-SUB-010 |
| [SA-ONB-007](#sa-onb-007) | Medium | Verified | P2 | Onboarding/white-label | GSTIN captured at signup never reaches invoices |  |
| [SA-ONB-010](#sa-onb-010) | Medium | Verified | P2 | Onboarding/white-label | Signup has no plan choice, silently tolerates a missing plan, and has no handler or integration tests |  |
| [SA-QB-001](#sa-qb-001) | Medium | Verified | P2 | QA (platform) | COD and online payment rows carry no `franchise_id`, so royalty under-counts even after SA-SOLID-003 is fixed |  |
| [SA-SUB-005](#sa-sub-005) | Medium | Verified | P2 | Subscription/billing | Brand platform billing worker is disabled by default and undocumented |  |
| [SA-SUB-008](#sa-sub-008) | Medium | Partially Verified | P2 | Subscription/billing | Plan limits / quotas do not exist (a-brand) or are not enforced (a-franchise); Starter "1 location" is … |  |
| [SA-SUB-009](#sa-sub-009) | Medium | Partially Verified | P2 | Subscription/billing | Five sellable features gate nothing (`wallet`, `loyalty`, `online_payments`, `item_tracking`, `whatsapp_bot`) |  |
| [SA-SUB-011](#sa-sub-011) | Medium | Verified | P2 | Subscription/billing | Franchise SaaS subscriptions (ADR-010 "module B") are data model + CRUD only, never billed or enforced, which … |  |
| [SA-SUB-012](#sa-sub-012) | Medium | Verified | P2 | Subscription/billing | Plan-change billing defects: changing tier during a trial bills the trial window at full price (plus … |  |
| [SA-SUB-013](#sa-sub-013) | Medium | Verified | P2 | Subscription/billing | Brand platform invoices are not GST invoices and carry no payment record |  |
| [SA-SUB-014](#sa-sub-014) | Medium | Verified | P2 | Subscription/billing | Brand dunning makes no charge attempts, sends no notices, and its state changes are not audited |  |
| [SA-SUB-015](#sa-sub-015) | Medium | Suspected | P2 | Subscription/billing | (b) Recurring mandate charge integration is suspect: likely non-existent Razorpay endpoint/header, … |  |
| [SA-API-021](#sa-api-021) | Medium | Partially Verified | P3 | Backend/API | Single-region and laundry-only assumptions baked into the API |  |
| [SA-ARCH-001](#sa-arch-001) | Medium | Verified | P3 | Architecture | Three "services" share one EF model, one database role and overlapping table ownership (distributed monolith) |  |
| [SA-AUTHZ-012](#sa-authz-012) | Medium | Verified | P3 | Authorization (RBAC/ABAC) | Vertical (business-type) boundary is not enforced server-side | SA-FE-006, SA-MOB-015, SA-ONB-003, SA-VERT-005 |
| [SA-DB-004](#sa-db-004) | Medium | Verified | P3 | Database (index/idempotency/RLS) | No composite tenant foreign keys: rows can reference another tenant's parents | SA-TEN-011 |
| [SA-DB-007](#sa-db-007) | Medium | Verified | P3 | Database (index/idempotency/RLS) | Globally unique business numbers generated per tenant: cross-tenant unique violations and an existence oracle | SA-TEN-012 |
| [SA-DB-013](#sa-db-013) | Medium | Verified | P3 | Database (index/idempotency/RLS) | Tables with tenant data that RLS cannot protect: analytics materialized views | SA-TEN-013 |
| [SA-MOB-006](#sa-mob-006) | Medium | Verified | P3 | Mobile/delivery/maps | Offer→accept dispatch mode is not wired end-to-end (no rider UI, offers hidden from the task list, no … |  |
| [SA-MOB-007](#sa-mob-007) | Medium | Verified | P3 | Mobile/delivery/maps | Riders get no push notification for new or changed assignments; the app learns of work only by 30 s polling … |  |
| [SA-MOB-013](#sa-mob-013) | Medium | Verified | P3 | Mobile/delivery/maps | Serviceability, zones and service areas are not enforced anywhere in the booking or dispatch path |  |
| [SA-MOB-014](#sa-mob-014) | Medium | Verified | P3 | Mobile/delivery/maps | Customer tracking is a status timeline only, and pickup progress never reflects rider start or arrival |  |
| [SA-MOB-016](#sa-mob-016) | Medium | Verified | P3 | Mobile/delivery/maps | Customer order cancellation does not cancel or release the order's delivery legs |  |
| [SA-SOLID-006](#sa-solid-006) | Medium | Verified | P3 | OOP/SOLID | Coupon rules are implemented three times and have drifted. First-order and eligibility rules are never … |  |
| [SA-SOLID-009](#sa-solid-009) | Medium | Verified | P3 | OOP/SOLID | Anemic, fully mutable shared data model. Bounded contexts write each other's tables, and Domain projects are … | SA-ARCH-002 |
| [SA-SOLID-010](#sa-solid-010) | Medium | Verified | P3 | OOP/SOLID | God-handlers with no direct tests: `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `OAuth` |  |
| [SA-VERT-006](#sa-vert-006) | Medium | Verified | P3 | Verticals/domain | Notification templates are a hardcoded laundry/logistics status switch; the vertical-tagged event catalog is … |  |
| [SA-VERT-007](#sa-vert-007) | Medium | Verified | P3 | Verticals/domain | Clients are laundry-shaped and only superficially vertical-aware; the order DTO does not expose the mode |  |
| [SA-API-013](#sa-api-013) | Medium | Verified | P4 | Backend/API | Hard-coded "Laundry Ghar" brand identity in customer-facing messages and emails | SA-ONB-006 |
| [SA-FE-009](#sa-fe-009) | Medium | Verified | P4 | Frontend/clients | Server terminology is wired only in admin-web; customer, rider and POS ship hardcoded laundry copy |  |
| [SA-ONB-001](#sa-onb-001) | Medium | Partially Verified | P4 | Onboarding/white-label | "Go live" and custom domains are database rows that no request path can reach | SA-OPS-013, SA-TEN-009 |
| [SA-ONB-005](#sa-onb-005) | Medium | Verified | P4 | Onboarding/white-label | Back-office `CreateBrand` creates an unprovisioned tenant with an implicit vertical | SA-VERT-009 |
| [SA-ONB-008](#sa-onb-008) | Medium | Verified | P4 | Onboarding/white-label | No client implements signup, the provider wizard, branding or the white-label app config; mobile is one … | SA-FE-007, SA-FE-008 |
| [SA-OPS-016](#sa-ops-016) | Medium | Verified | P4 | DevOps/production | Mobile release pipeline is not operational (OTA, CI, white-label) |  |
| [SA-TEN-006](#sa-ten-006) | Medium | Partially Verified | P4 | Multi-tenancy | Anonymous public tenant content (banners, app-config, onboarding slides) returns nothing under enforced RLS |  |
| [SA-AUTHZ-008](#sa-authz-008) | Medium | Verified | P5 | Authorization (RBAC/ABAC) | ABAC engine is inert; attribute-based rules are hand-coded per handler |  |
| [SA-DB-017](#sa-db-017) | Medium | Verified | P5 | Database (index/idempotency/RLS) | The per-row plpgsql scope predicate makes RLS scans about 8× slower |  |
| [SA-OPS-015](#sa-ops-015) | Medium | Verified | P5 | DevOps/production | Horizontal-scaling blockers (DB-Q8 infrastructure view) |  |
| [SA-API-023](#sa-api-023) | Low | Verified | P1 | Backend/API | Development credentials and OTP master codes committed to appsettings |  |
| [SA-API-025](#sa-api-025) | Low | Verified | P1 | Backend/API | Plaintext email addresses in logs |  |
| [SA-AUTHZ-013](#sa-authz-013) | Low | Verified | P1 | Authorization (RBAC/ABAC) | Identity-axis (`user_type`) gates where permission gates belong; platform-scoped dispatch settings reachable … |  |
| [SA-FE-014](#sa-fe-014) | Low | Verified | P1 | Frontend/clients | WebMCP exposes customer search and order-status mutation to in-browser AI agents in production builds |  |
| [SA-FE-015](#sa-fe-015) | Low | Verified | P1 | Frontend/clients | Web routers have no error boundary |  |
| [SA-FE-016](#sa-fe-016) | Low | Verified | P1 | Frontend/clients | Shared-device residue after logout (POS cart PII, rider offline queue, mobile query caches) |  |
| [SA-MOB-020](#sa-mob-020) | Low | Partially Verified | P1 | Mobile/delivery/maps | Customer slot listing has no in-handler brand predicate (RLS-only), contrary to its comment |  |
| [SA-MOB-021](#sa-mob-021) | Low | Verified | P1 | Mobile/delivery/maps | Map provider keys stored unencrypted and echoed to every settings reader |  |
| [SA-OPS-011](#sa-ops-011) | Low | Verified | P1 | DevOps/production | Health checks never check the database | SA-API-024 |
| [SA-OPS-017](#sa-ops-017) | Low | Verified | P1 | DevOps/production | Container and supply-chain hygiene |  |
| [SA-QA-002](#sa-qa-002) | Low | Verified | P1 | QA (security) | Gateway rate-limit unit tests assert the vulnerable behaviour |  |
| [SA-QB-003](#sa-qb-003) | Low | Partially Verified | P1 | QA (platform) | Production guidance for ForwardedHeaders contradicts itself, and both options are unsafe as coded |  |
| [SA-SOLID-014](#sa-solid-014) | Low | Verified | P1 | OOP/SOLID | The pickup flow enforces minimum order value and sets the expected COD from client-supplied prices |  |
| [SA-SUB-016](#sa-sub-016) | Low | Verified | P1 | Subscription/billing | Webhook idempotency is check-then-act on status only: no event dedupe, no lock or concurrency token, no … |  |
| [SA-FE-012](#sa-fe-012) | Low | Verified | P2 | Frontend/clients | Customer payments, wallet top-up and packages are not implemented in the UI |  |
| [SA-API-022](#sa-api-022) | Low | Verified | P3 | Backend/API | Inconsistent error contract; no API versioning |  |
| [SA-ARCH-009](#sa-arch-009) | Low | Verified | P3 | Architecture | Layering is nominal: `Utilities` is a cross-cutting god-library; composition roots are copy-pasted per host |  |
| [SA-ARCH-011](#sa-arch-011) | Low | Partially Verified | P3 | Architecture | MCP downstream URLs are not wired for AppHost or compose; synchronous core→operations coupling |  |
| [SA-AUTHZ-015](#sa-authz-015) | Low | Partially Verified | P3 | Authorization (RBAC/ABAC) | Partner isolation is a single (RLS-only) layer |  |
| [SA-DB-019](#sa-db-019) | Low | Verified | P3 | Database (index/idempotency/RLS) | Soft-delete tables: no partial unique constraints |  |
| [SA-DB-020](#sa-db-020) | Low | Verified | P3 | Database (index/idempotency/RLS) | Staff identity is globally unique by email and phone |  |
| [SA-FE-013](#sa-fe-013) | Low | Verified | P3 | Frontend/clients | admin-web `/settings` gate drifts from server authorization; the route map is hand-synced |  |
| [SA-MOB-019](#sa-mob-019) | Low | Verified | P3 | Mobile/delivery/maps | Riders retain indefinite access to customer PII for historical tasks |  |
| [SA-SOLID-011](#sa-solid-011) | Low | Verified | P3 | OOP/SOLID | Dispatch and assignment logic is duplicated across bounded contexts and has drifted |  |
| [SA-SOLID-012](#sa-solid-012) | Low | Verified | P3 | OOP/SOLID | Dependency inversion holds by convention only. Application reaches ASP.NET Core and Npgsql through a … |  |
| [SA-SOLID-013](#sa-solid-013) | Low | Verified | P3 | OOP/SOLID | Settings-resolved providers lose their logger (`_logger as ILogger<OtherType>` always evaluates to null) |  |
| [SA-VERT-008](#sa-vert-008) | Low | Verified | P3 | Verticals/domain | Catalog discriminator is inert, and the service model is laundry-shaped |  |
| [SA-ONB-009](#sa-onb-009) | Low | Verified | P4 | Onboarding/white-label | The vertical cannot be changed through any governed path, and a raw change would not re-provision |  |
| [SA-ONB-011](#sa-onb-011) | Low | Partially Verified | P4 | Onboarding/white-label | `go-live` ignores wizard prerequisites, and adding a primary custom domain can strand the brand off its … |  |
| [SA-ONB-012](#sa-onb-012) | Low | Verified | P4 | Onboarding/white-label | White-label app identifiers can collide between brands |  |
| [SA-DB-016](#sa-db-016) | Low | Partially Verified | P5 | Database (index/idempotency/RLS) | Pooled-connection tenant context is safe for EF, but rests on session-level GUCs |  |
| [SA-DB-021](#sa-db-021) | Low | Verified | P5 | Database (index/idempotency/RLS) | No DB-level booking overlap prevention; the salon schema is inaccessible to app_user |  |
| [SA-OPS-018](#sa-ops-018) | Informational | Verified | P1 | DevOps/production | Spec claims infrastructure that does not exist (broker, Redis, Hangfire, Serilog, S3) | SA-ARCH-012, SA-VERT-011 |
| [SA-SUB-019](#sa-sub-019) | Informational | Verified | P2 | Subscription/billing | Entitlement change propagation and fail-open behaviour (informational) |  |

## 2. Canonical findings (detail)

### SA-API-001
**Production auth rate limiter collapses into one global bucket for all tenants (gateway IP)**

- **Area / category:** Backend/API — Availability / multi-tenancy / security
- **Severity:** Critical
- **Status:** Partially Verified (code + deploy config read; not run)
- **Independent verification:** QA-A: Confirmed; QA-B: Confirmed (canonical, G10)
- **Duplicates (same defect, other reports):** SA-OPS-001
- **Evidence:** `backend/laundryghar/core.WebApi/Program.cs:208-217` ("auth" policy partitions on `httpContext.Connection.RemoteIpAddress`, 10/60 s); `:507` `UseForwardedHeadersIfEnabled()` is a no-op unless `ForwardedHeaders:Enabled`; `deploy/docker-compose.yml:25` ("ForwardedHeaders stays OFF on the services — the gateway is the trusted edge") and `:83-84` (gateway only, commented out); `laundryghar.ServiceDefaults/Extensions.cs:267-287`. The policy covers `Auth.cs:56`, `CustomerAuth.cs:45`, `PartnerAuth.cs:33`, `Signup.cs:31-32` and `OAuth.cs:73,172,198,317,351`.
- **Observed behaviour:** in the shipped topology every request reaches `core` from the gateway container. `RemoteIpAddress` is therefore the gateway's IP for all users, and the "auth" limiter becomes one 10-requests-per-minute bucket shared by every login, OTP send/verify, token refresh, Google sign-in, partner auth and signup across all tenants. The same applies to `oauth_register` (3/hour platform-wide) and the anonymous `api_key` bucket. `LoginHistory.IpAddress` and …
- **Impact:** platform-wide login and refresh outage at trivial load. A refresh 429 logs users out. One abusive client can lock out every tenant. Auth audit trails lose client IPs.
- **Remediation:** enable `ForwardedHeaders` on the three services with `KnownProxies`/`KnownIPNetworks` set to the gateway or compose network rather than clearing them. Remove `/refresh` endpoints from the 10/min policy and give them their own per-user or per-family limit.
- **Tests required:** integration test with two distinct `X-Forwarded-For` values from a trusted proxy that asserts independent partitions; test that refresh is not throttled by login traffic.
- **Specialist's dependencies / priority note:** P0. Related area: SEC / OPS. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-AUTHZ-001
**Any `users.create` holder can create a `platform_admin` account (unauthenticated → platform takeover via self-signup)**

- **Area / category:** Authorization (RBAC/ABAC) — Privilege escalation / RBAC
- **Severity:** Critical
- **Status:** Verified (static end-to-end trace by specialist, QA-A and architect; DB insert step reproduced as app_user; HTTP not executed)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `core.Application/Identity/Users/Commands/CreateUser/CreateUser.cs:22-55`: `UserType` is taken from the request. The only check is `UserType.IsValid` (L31), and `platform_admin` is in `UserType.All` (`SharedDataModel/Enums/UserType.cs:12,31-34`). With a password supplied, `Status=Active` (L46). There is no actor-rank or user-type ceiling. Contrast `SetUserType.cs:43-59`, which does block this. - Endpoint: `core.WebApi/Endpoints/Identity/AdminUsers.cs:33` (`permission:users.create`). The same path is reachable through `AdminAccessControl.cs:39` → `InviteUser.cs:29-35`, which calls `CreateUserCommand` with the client's `UserType` before the membership grant. - `users.create` is held by …
- **Observed behaviour:** `POST /api/v1/admin/users {email:"x@attacker", password:"…", userType:"platform_admin"}` as a brand/franchise/store admin creates an active platform admin. `users.create` is `high` risk, but step-up OTP goes to the attacker's own verified identifier. Logging in as the new account yields a token with `user_type=platform_admin`, which passes every permission policy, bypasses RLS on all tables, and is exempt from suspension.
- **Impact:** Complete loss of multi-tenant isolation. Any internet user who can pass a phone OTP can read and modify every tenant's data, billing and configuration.
- **Remediation:** in `CreateUserCommandHandler`, reuse the `SetUserType` guard. Reject `platform_admin` unless `actor.IsPlatformAdmin`, and reject any type whose priority outranks the actor. Better still, derive `user_type` from the granted role (`UserType.ForPrimaryRole`) and stop accepting it from clients. Add a DB trigger or CHECK that only a bypass session can write `user_type='platform_admin'`.
- **Tests required:** brand/franchise/store admin create and invite with `platform_admin` and with `brand_admin` (from store_admin) are refused. A platform admin can still create one.
- **Specialist's dependencies / priority note:** P0 · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-TEN-001
**Migration 0031's restrictive scope policy denies ALL rows (and all audited writes) for customer, API-key and commerce-host sessions**

- **Area / category:** Multi-tenancy — Tenant isolation / availability (fail-closed outage)
- **Severity:** Critical
- **Status:** Verified (SQL-level reproduction by DB, QA-A and QA-C; HTTP not executed)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-AUTHZ-006, SA-DB-001
- **QA correction to the specialist text:** QA-A: wallet, loyalty and refund tables are not in the 0031 list (their reads stay brand-wide, not denied). QA-C: also blocks non-platform staff on the commerce host.
- **Evidence:** - `db/migrations/0031_subbrand_scope_rls.up.sql:L82` makes `IF v_nodes IS NULL THEN RETURN NULL` the result for an unresolved `scope_nodes`. L198 creates the policy `AS RESTRICTIVE … FOR ALL`. L140-178 list 39 tables, including `commerce.payments`, `identity_access.audit_logs`, `kernel.system_settings`, `order_lifecycle.orders`, `order_items`, `pickup_requests`, `delivery_slots` and `tenancy_org.stores`. - `db/migrations/0025…up.sql:L49-68`: `kernel.split_setting` maps `'?'` to NULL. - `RlsConnectionInterceptor.cs:L85-90, L103` writes `"?"` when `ICurrentTenant.ScopeNodes` is null. - `JwtTokenService.cs:L98-119` (customer tokens) and `L122-145` (OAuth customer tokens) emit no `scope_nodes`; …
- **Observed behaviour:** the SQL reproduction (`repro_scen.sql`, run after applying 0031 verbatim) gave: - S1, operations-host customer: orders 2→0, payments 1→0; - S2, commerce-host customer: payments 2→0; - S3, commerce-host brand admin: payments 2→0, orders 2→0; - S4, operations-host brand admin with `scope_nodes=brand:A`: unchanged at 2/2. The same NULL makes the `WITH CHECK` fail, so every audited insert or update by these principals raises a policy violation.
- **Impact:** if 0031 is applied and services run as `app_user`, all of the following break: - customer order history, pickups and slots; - every commerce-host read and write for staff and customers (payments, wallets, finance, analytics, partner billing); - API-key integrations. Conversely, if these flows "work" somewhere, RLS is not actually enforced in that environment. Either way the documented isolation model and the runtime disagree. The existing test …
- **Remediation:** 1. Make `CommerceHostCurrentTenant` delegate the subject slice (`ScopeNodes`, `Roles`, `Permissions`, `UserType`, `TokenUse`, `CustomerId`) to the same claim reads as `HttpContextCurrentTenant`. Better: compose it around `HttpContextCurrentTenant`. 2. Give non-staff principals a defined scope semantics in the `within_scope_cols` predicate. For example, return `true` when `app.current_token_use` is in `customer`, `customer_mcp` or `api_key` (the brand and customer policies still confine them), or have the interceptor publish `""` plus a …
- **Tests required:** add customer, API-key and commerce-adapter sessions to `SubBrandScopeRlsTests`, and an audit insert under each session type. Add a test asserting that `CommerceHostCurrentTenant` and `HttpContextCurrentTenant` publish identical GUCs for the same principal.
- **Specialist's dependencies / priority note:** P0. Blocks any production use of 0031. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-API-007
**Customer online payment lifecycle is broken end to end (captured money can be ignored; orders never marked paid)**

- **Area / category:** Backend/API — Payments / correctness
- **Severity:** High
- **Status:** Verified (code read); Razorpay multi-attempt semantics not tested against sandbox
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-SOLID-002
- **Evidence:** `RazorpayWebhookHandler.cs:200-206` (`captured` acted on only when `pending`; otherwise "acknowledged"); `:268-283` (`payment.failed` sets terminal `failed`); `CustomerPaymentHandlers.cs:158-166` (an invalid client signature sets `failed`); `:48-81` (client-supplied `Amount`, `OrderId` not validated against the customer's order or amount due). Grep for `AmountPaid`/`PaymentStatus` writes finds only offline and COD paths. `VerifyPaymentHandler` writes no outbox event despite the webhook comment (`RazorpayWebhookHandler.cs:215`). Client: no call sites of initiate/verify in `customer-mobile/app`, and `walletTopUp: false` (`customer-mobile/src/constants/config.ts:112-116`).
- **Observed behaviour:** (1) a first failed attempt (`payment.failed`), or a tampered or buggy `/verify`, makes the payment terminal. A later successful capture on the same Razorpay order is then ignored with 200, so the customer is charged and the system shows failed. (2) Even a captured payment never updates `orders.amount_paid` or `payment_status`. (3) Wallet top-ups are credited only by the client `/verify` call; the webhook does not credit the wallet, so an app …
- **Impact:** lost revenue reconciliation, wrongly unpaid orders, and support load. The online payment workflow is not production-ready.
- **Remediation:** treat `failed` as non-terminal while the Razorpay order is open (accept `captured` from `pending|failed`). Bind amount and order server-side from the order's `amount_due`. Apply payment effects (order `amount_paid`, wallet credit) in one idempotent "on captured" routine called by both verify and webhook.
- **Tests required:** webhook sequence `failed` → `captured` ends captured and paid; verify plus webhook race credits exactly once.
- **Specialist's dependencies / priority note:** P0 before taking online payments. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-008
**Cancellation refunds are queued but never executed**

- **Area / category:** Backend/API — Payments / workflow completeness
- **Severity:** High
- **Status:** Verified (QA-A)
- **Independent verification:** QA-A: Confirmed – status corrected
- **Evidence:** `operations.Application/Orders/Common/OrderCancellationRefund.cs:44-80` inserts `payment_refunds` with `Status="pending"` and an outbox `refund.initiated`. The only other `PaymentRefunds` consumers are `AdminPaymentHandlers.cs:103-117` (reads). `NotificationMappingService.cs:433` sends "A refund … has been initiated".
- **Observed behaviour:** cancelling a paid order creates a pending refund row and notifies the customer, but no job or handler calls the gateway or credits the wallet for pending refunds.
- **Impact:** customers are told they are refunded when they are not.
- **Remediation:** add an idempotent refund executor (worker plus inbox marker as in `PartnerBookingDebitService`) that moves `pending` to `succeeded|failed` through the gateway or wallet.
- **Tests required:** cancel a paid order and assert the refund is executed once.
- **Specialist's dependencies / priority note:** P0 (with SA-API-007). · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-009
**Admin refund calls Razorpay inside a retried DB transaction; cumulative cap is racy**

- **Area / category:** Backend/API — Payments / idempotency
- **Severity:** High
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-DB-006
- **QA correction to the specialist text:** QA-A: a DB trigger caps refunds, but it is racy, fires after the Razorpay call, and no setup script applies it.
- **Evidence:** `AdminPaymentHandlers.cs:111-118` (cap computed outside any lock); `:148-220` (`ExecuteInTransactionAsync` → `_gateway.InitiateRefundAsync(...)` inside the lambda); `CommerceDbContext.cs:103-115` (the execution strategy re-runs the whole lambda on transient failure); `RazorpayPaymentGateway.cs:117-150` (no idempotency header or receipt on the refund call).
- **Observed behaviour:** a transient DB error at `SaveChanges` or commit re-executes the lambda and calls Razorpay a second time, giving a double refund. A gateway success followed by a DB failure leaves a refund at Razorpay with no record. Two concurrent refunds with different or no idempotency keys can together exceed the captured amount; wallet refunds then over-credit.
- **Impact:** direct money loss.
- **Remediation:** persist the refund row as `processing` first and commit, call the gateway outside the transaction with a deterministic idempotency reference, then update. Lock the payment row (`FOR UPDATE`) when computing the cap.
- **Tests required:** simulated transient failure that asserts a single gateway call; parallel refunds that assert the cap holds.
- **Specialist's dependencies / priority note:** P1. Related area: DB. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-012
**Notification worker sends every tenant's WhatsApp/SMS with one arbitrary tenant's credentials**

- **Area / category:** Backend/API — Multi-tenancy / data protection
- **Severity:** High
- **Status:** Verified (code read); which row wins at runtime Not Tested
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-SOLID-007
- **Evidence:** `commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:31-62` (singleton; RLS-bypassed query over all `system_settings` rows of category `whatsapp`/`sms`; `FirstOrDefault` with no brand filter and no ordering; the comment claims platform rows are preferred, but the code does not do that). `RoutingChannelSender.cs:98-130,132-158` ignores `request.BrandId`. Registered as a singleton at `commerce.WebApi/Program.cs:259`. Contrast with OTP, which is brand-aware (`RoutingOtpSender.cs:53-57`).
- **Observed behaviour:** once any brand saves its own WhatsApp or MSG91 settings, the worker may send all brands' customer notifications (order status, payment, refund) from that brand's sender account and phone number, using that brand's templates and DLT ids.
- **Impact:** cross-tenant data exposure (customers' phone numbers and order details go through another tenant's business account), wrong branding, and billing to the wrong tenant. This blocks white-label SaaS.
- **Remediation:** resolve credentials per `request.BrandId` (brand row, then platform row, deterministically), and cache per brand.
- **Tests required:** two brands with distinct credentials; assert each notification uses its own brand's credentials.
- **Specialist's dependencies / priority note:** P0 for multi-tenant launch. Related area: SEC. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-ARCH-013
**Platform control plane is not separated from the tenant plane; global authority is a single mutable column**

- **Area / category:** Architecture — Architecture / authorization model
- **Severity:** High
- **Status:** Verified (code read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** root cause of SA-AUTHZ-001/002/003/004
- **Evidence:** - `laundryghar.Utilities/Auth/PermissionHandler.cs:31-33`: every permission is granted when the `user_type` claim is `platform_admin`. - `laundryghar.Utilities/Middlewares/TenantResolutionMiddleware.cs:34-40`: `bypass_rls` for the same claim. - `HttpContextCurrentUser.cs:62-63`: `IsPlatformAdmin` derived the same way. - The claim is minted from `identity_access.users.user_type`, which tenant-plane handlers write from client input (`core.Application/Identity/Users/Commands/CreateUser/CreateUser.cs:31,45`). - Users INSERT RLS is `WITH CHECK (true)` (`db/migrations/0029_users_brand_rls.up.sql:122`). - Platform endpoints (entitlements, plans, brands) are served by the same host and token …
- **Observed behaviour:** any write path to `users.user_type` is a full platform takeover. SA-AUTHZ-001 is one instance; SA-AUTHZ-003 → 002 chains reach the same outcome against existing platform users.
- **Impact:** tenant isolation depends on every identity-admin handler being perfect, with no structural or database backstop.
- **Remediation:** - Phase 0: server-derive `user_type`; add a DB trigger allowing `platform_admin` writes only from a platform/bypass role. - Phase 1–3: give platform operators a separate token audience and endpoint group that tenant tokens cannot satisfy.
- **Tests required:** tenant-plane create/invite/set-type of `platform_admin` → refused; a platform-audience token is required for `/admin/entitlements`, `/admin/brands` and platform invoices.
- **Specialist's dependencies / priority note:** P0 (trigger and derivation), P1 (audience split). · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/01b-architect-challenge-review.md`](docs/audit/specialists/01b-architect-challenge-review.md)

### SA-ARCH-014
**Tenant context is implemented three times and per lane, and the implementations diverge**

- **Area / category:** Architecture — Architecture / tenancy plumbing
- **Severity:** High
- **Status:** Verified (code read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** root cause of SA-TEN-001/002, SA-SUB-004
- **Evidence:** - Three `ICurrentTenant` implementations: `laundryghar.Utilities/Services/HttpContextCurrentTenant.cs`, `commerce.Infrastructure/Worker/CommerceHostCurrentTenant.cs`, `commerce.Infrastructure/Worker/WorkerCurrentTenant.cs`. - The commerce one omits `CustomerId` and the subject GUCs (SA-AUTHZ-005). - Customer tokens carry no `scope_nodes` (`core.Infrastructure/Auth/JwtTokenService.cs:59-64` vs `:98-110`), which 0031's predicate treats as unresolved and denies (`db/migrations/0031_subbrand_scope_rls.up.sql:82`). - Worker trust is per call site (`BrandPlatformBillingService.cs:63` vs `:156`).
- **Observed behaviour:** each new lane or host must re-implement context correctly. The 0031 migration was written against the staff lane only and broke the customer and commerce lanes.
- **Impact:** outages (SA-DB-001), fail-open customer RLS on commerce (SA-TEN-002), silent worker no-ops (SA-SUB-004).
- **Remediation:** one `TenantContextResolver` mapping every token type (user, customer, partner, api_key, worker) to the same GUC set, with an explicit customer scope node. Add a test matrix (lane × host × restrictive-policy table) run against a migrated schema in CI (depends on SA-ARCH-008).
- **Tests required:** the lane matrix above.
- **Specialist's dependencies / priority note:** P0 for the customer/commerce lanes; P1 for consolidation. --- · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/01b-architect-challenge-review.md`](docs/audit/specialists/01b-architect-challenge-review.md)

### SA-AUTHZ-002
**Identity write handlers lack target-rank and target-scope guards (in-brand account takeover)**

- **Area / category:** Authorization (RBAC/ABAC) — Privilege escalation / horizontal + vertical IDOR
- **Severity:** High
- **Status:** Verified (code read; not executed)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `SetPersonStatus.cs:24-68`. `POST /admin/access-control/people/{id}/status` (`AdminAccessControl.cs:51`, `permission:users.update`, risk `normal` per `IdentitySeeder.cs:129`). Action `"activate"` sets `PasswordHash` to a caller-supplied password for any non-deleted user regardless of current status (L33-46). - `UpdateUser.cs:18-75` (`users.update`) rewrites `Email`/`PhoneE164` (L27-28) and bank/UPI/KYC fields (L54-63) of any target id. - `DeactivateUser.cs:14-23` (`users.deactivate`). - None of these handlers loads the actor's rank or calls `IsWithinScope`/`ScopedToCallerBrand`. The only boundary is users RLS (`0029`: same brand via any membership). - `store_admin` and `franchise_owner` …
- **Observed behaviour:** a store admin of store S1 can set the brand admin's password (or change their email and then use forgot-password), then log in as the brand admin with a full-permission token. They can also edit any other store's staff and their payout bank details. The dedicated `users.set_password` permission (critical, step-up) is bypassed by a `normal`-risk door.
- **Impact:** Vertical escalation inside a tenant, account takeover, payout fraud (bank/UPI change on rider/staff profiles).
- **Remediation:** - Add one `TargetUserGuard` (shared helper) to every identity write handler. It should require `ScopedToCallerBrand`, require the target's highest role priority to be ≥ the actor's, and require `IsWithinScope` on the target's memberships. - Restrict `"activate"` to `Invited`/`Locked` users and require `users.set_password`. - Move email/phone/bank changes behind a high-risk permission with step-up and bump `perm_version`.
- **Tests required:** store_admin → brand_admin activate/update/deactivate refused; store_admin → other store's staff refused; own-store junior allowed.
- **Specialist's dependencies / priority note:** P0 · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-AUTHZ-003
**`GrantMembership` accepts any user id platform-wide (cross-tenant attachment → cross-tenant takeover)**

- **Area / category:** Authorization (RBAC/ABAC) — Cross-tenant privilege escalation
- **Severity:** High (High; Critical if victim user UUIDs are obtainable (unconfirmed))
- **Status:** Partially Verified (code read; UUID-discovery path not established)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-TEN-004
- **Evidence:** `GrantMembership.cs:42-225`. Scope, brand and rank checks apply to the *target scope and role* (L144-180), but nothing checks that `cmd.Request.UserId` belongs to the actor's brand. `user_scope_memberships` has RLS off (`0029_users_brand_rls.up.sql:149` comment; no later migration enables it). The insert therefore succeeds for any existing user id. `IsPrimary=true` also flips the victim's existing primary memberships (L188-193). Once the membership exists, `identity_access.user_in_brand(victim, attackerBrand)` is true (`0029`), so the victim becomes visible and writable to the attacker's brand under users RLS. That enables SA-AUTHZ-002 against them, including a platform-admin account if it …
- **Observed behaviour:** brand admin of self-signed-up brand A → `POST /admin/roles/memberships/grant {userId: <brand B admin or platform admin>, scopeType:"brand", roleId:<store_staff>}` succeeds. The attacker then uses `SetPersonStatus activate` or `UpdateUser` email on that user and logs in as them.
- **Impact:** Cross-tenant account takeover. Can also unexpectedly re-home a victim's primary scope.
- **Remediation:** in `GrantMembershipCommandHandler`, require the target user to be within the actor's brand (`ScopedToCallerBrand`) unless the actor is a platform admin. New users should only be attachable through the invite flow, which creates them. Enable RLS on `user_scope_memberships` with a brand-resolving policy (a separate audit, per the 0029 note).
- **Tests required:** granting a membership to a user with no membership in the actor's brand → 403/404.
- **Specialist's dependencies / priority note:** P0 · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-AUTHZ-004
**No permission ceiling on role edits and user overrides; platform-plane handlers rely on permission codes only (self-grant `saas.manage` → free entitlements / mark own platform invoice paid)**

- **Area / category:** Authorization (RBAC/ABAC) — Privilege escalation / entitlement bypass
- **Severity:** High
- **Status:** Verified (QA-A)
- **Independent verification:** QA-A: Confirmed – status corrected
- **Evidence:** - `SetUserPermissionOverride.cs:37-99` (`permissions.assign`) accepts any permission code (L64-66) and any scope type including `platform`, with no check that the actor holds the code or that the scope is within the actor's. - `SetRoleCells.cs:43-106` enables any cell on an own-brand role without checking the actor's own permissions. - Platform-plane handlers `SetBrandFeature.cs:15-54` and `ApplyBundleToBrand.cs` (route `AdminEntitlements.cs:34-38`, `permission:saas.manage`) contain no `IsPlatformAdmin` check. Contrast `PlatformPlanCommands.cs:28,129,203,244`, which do. - `brand_feature` RLS allows own-brand writes (`0005_split_features_from_modules.up.sql:158-161`).
- **Observed behaviour:** a brand admin grants themselves `saas.manage` (step-up OTP to their own phone), refreshes, then `POST /admin/entitlements/brands/{ownBrand}/features {featureKey, enabled:true}` licenses premium features without paying. `SetInvoiceStatus` on their own platform invoice is likely possible the same way (not traced).
- **Impact:** Revenue loss and an entitlement-model bypass. It also undermines the RBAC model generally, since any brand admin can mint any code their brand can see.
- **Remediation:** enforce a grant ceiling. An actor may only grant codes they themselves hold, and never codes flagged platform-scope (`permissions.requires_scope`/platform module). Add `IsPlatformAdmin` (or a platform-audience check) to every handler under `/admin/entitlements` and `/admin/brands`. Restrict override scope types to nodes within the actor's scope.
- **Tests required:** a brand admin granting `saas.manage`/`brands.create` via override or role cells → refused; `SetBrandFeature` as a non-platform caller → 403.
- **Specialist's dependencies / priority note:** P0/P1 · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-DB-002
**The documented fresh-build path cannot reproduce the production schema; re-running it silently reverts the RLS bypass fix**

- **Area / category:** Database (index/idempotency/RLS) — Migrations / operability
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-C: Confirmed
- **Duplicates (same defect, other reports):** SA-ARCH-008, SA-SUB-020, SA-VERT-010
- **Evidence:** - `deploy/README.md:35` and `ops/backup/README.md:5` prescribe `db/build_from_scratch.sh` + `db/tools/migrate.sh up`. - `build_from_scratch.sh:77-120` applies only the FK patches, triggers, discriminators, token lineage and `rls_proposal.sql`. - ~130 other patches (`rls_enable_*`, `harden_app_user_and_rls_bypass.sql`, `payment_idempotency.sql`, `subscriptions_module.sql`, `seed_navigator_modules.sql`, …) are applied by no ordered script.
- **Observed behaviour:** - `migrate.sh up` fails at `0005_split_features_from_modules.up.sql:102` (`relation "identity_access.modules" does not exist`). - Several patches assert on rows created only by the .NET `IdentitySeeder` (`phase1_slice_e…:92`, `phase4_role_vertical_key.sql:113`). - Seeds need a hand-created brand `5b375161-…`. - `build_from_scratch.sh:89-90` re-runs `rls_proposal.sql`, whose `kernel.rls_bypass()` (`rls_proposal.sql:85-87`) accepts only `'on'`. …
- **Impact:** - Disaster recovery, staging and new-region builds are not reproducible from the repo. - CI never validates the real DDL. - A re-run on production turns every platform-admin and worker bypass into zero rows. That fails closed, but it is an outage.
- **Remediation:** freeze a baseline (`pg_dump --schema-only` of production) as `db/migrations/0000_baseline.up.sql`. Retire `db/patches/` from the bootstrap. Make `rls_proposal.sql` stop redefining `rls_bypass()`. Add a CI job that builds a PostgreSQL service from baseline + migrations and runs `migrate.sh verify` plus RLS smoke tests.
- **Tests required:** a CI job that builds the schema; an assertion that `kernel.rls_bypass()` returns true for `'true'`.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-012
**Customer-identity and salon RLS policies use a raw uuid cast that throws on an empty brand GUC, even under bypass: Google customer sign-in fails**

- **Area / category:** Database (index/idempotency/RLS) — RLS correctness
- **Severity:** High
- **Status:** Verified (DB reproduced; code path traced; HTTP not executed)
- **Independent verification:** QA-C: Confirmed
- **Related:** SA-TEN-001 (same "empty GUC" class)
- **Evidence:** - `db/migrations/0001_customer_social_auth_and_pin.up.sql:49-54` (`custident_tenant`: `current_setting('app.bypass_rls')='true' OR brand_id = current_setting('app.current_brand_id', true)::uuid`); `phase4_salon_fulfillment_schema.sql:90-95`. - The interceptor writes `''` for a null brand (`RlsConnectionInterceptor.cs:59`). - `/api/v1/customer/auth/google` is anonymous with bypass (`core.WebApi/Program.cs:620-646`). - `CustomerGoogleSignInHandler.cs:88-93` queries `CustomerIdentities`.
- **Observed behaviour:** with brand `''` and bypass `'true'`, `SELECT … FROM customer_identities WHERE …` → `ERROR: invalid input syntax for type uuid: ""`. The planner evaluates the cast while planning, so the bypass short-circuit does not help.
- **Impact:** customer Google sign-in returns a 500 in the production role configuration. This is the same DEF-002 defect class that `fix_legacy_*_rls_policies.sql` removed elsewhere.
- **Remediation:** replace with `kernel.rls_bypass() OR brand_id = kernel.current_brand_id()`, scoped `TO app_user`. Same for the four salon policies.
- **Tests required:** anonymous plus bypass query on customer_identities.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-FE-001
**admin-web production image bakes only 3 of its 9 API base URLs**

- **Area / category:** Frontend/clients — Build/Deploy correctness (Related area: OPS)
- **Severity:** High
- **Status:** Verified (reproduced by QA-B: vite build with 3 URLs omits 6 base URLs)
- **Independent verification:** QA-B: Confirmed – reproduced
- **Evidence:** - `admin-web/src/api/client.ts:26-34,275-283`: nine `VITE_*_URL` values (identity, catalog, orders, engagement, analytics, commerce, warehouse, logistics, finance). - `admin-web/Dockerfile:24-31`: only `ARG VITE_IDENTITY_URL`, `VITE_CATALOG_URL` and `VITE_ORDERS_URL`. - `.github/workflows/release.yml:44-52`: the same three build args. - `deploy/docker-compose.yml:102-109`: the same three. - `admin-web/.dockerignore` excludes `.env` and `.env.*`. - `admin-web/deploy/nginx.conf:16-18`: `try_files $uri $uri/ /index.html`.
- **Observed behaviour:** - In the released or compose-built image, `engagementClient`, `analyticsClient`, `commerceClient`, `warehouseClient`, `logisticsClient` and `financeClient` get `baseURL: undefined`. - Their requests go to the admin nginx origin. A GET there returns `index.html` (the `unwrap` call then fails); a POST returns 405/404. - Screens affected: dashboard (analytics), CMS, coupons/packages/promotions/subscriptions, warehouse board, …
- **Impact:** most of the back office is non-functional in the only shipped deployment path. Local dev works because `.env` has all nine values, which hides the problem.
- **Remediation:** add the six missing `ARG`/`ENV` lines plus `VITE_GOOGLE_CLIENT_ID` to the Dockerfile, release.yml and compose. Better still, inject one `window.__CONFIG__` at container start, which also allows runtime per-environment config.
- **Tests required:** a CI step that builds the image and greps `dist/assets/*.js` for each gateway prefix (`/engagement`, `/analytics`, …); a smoke e2e of the dashboard and CMS against compose.
- **Specialist's dependencies / priority note:** P0 before any production deploy. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-MOB-001
**Rider task status endpoint has no state machine: any leg can be moved to any status, reviving cancelled legs and forcing cancelled orders to "delivered"**

- **Area / category:** Mobile/delivery/maps — Workflow integrity / authorization (business-logic).
- **Severity:** High
- **Status:** Verified (code read end-to-end; not executed).
- **Independent verification:** QA-C: Confirmed; Architect: Confirmed
- **Related:** SA-SOLID-001 — kept distinct: delivery-leg state machine on the rider endpoint (order-side bypass is SA-SOLID-001)
- **Evidence:** `backend/laundryghar/operations.Application/Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs`: - `L34-42` checks only membership in `["started","arrived","collected","completed","failed"]`; - `L96` `da.Status = cmd.Status;` runs without reading the current `da.Status`; - `L148-257` delivery completion sets `o.Status = "delivered"`, hardcodes `FromStatus = "out_for_delivery"`, adds a COD `Payment` and an outbox event when `o.DeliveredAt == null`, without checking the order is `out_for_delivery` or not cancelled; - `L263-264` calls `RiderLoad.DecrementAsync` on every completed/failed call.
- **Observed behaviour:** - A rider can PATCH `completed` on a leg that was `cancelled` (customer cancel at `CustomerPickupCommands.cs:395-407`) or `failed`. - A rider can PATCH `started` on a completed leg. - A rider can complete a delivery leg of a cancelled order, which turns the order delivered and records COD cash. - Each repeated completed/failed decrements `riders.current_load`, which drives auto-dispatch capacity (`AutoDispatchService.cs:149`). - The rider app's …
- **Impact:** Corrupt order lifecycle and finance (phantom COD payments, wrong `amount_paid`/`payment_status`), and load counter drift leading to over-assignment. It also undermines admin/customer consistency.
- **Remediation:** Add a per-leg transition table in the handler, for example: `assigned|accepted → started → arrived → (collected) → completed|failed`; terminal = `completed|failed|cancelled|expired|rejected`. Then: - return Conflict for illegal transitions and a 200 no-op for same-status repeats, decrementing load only on the first terminal entry; - before delivery completion, require the order's strategy to allow `→ delivered` from its current status; - use a conditional `UPDATE … WHERE status = @expected` (or an `xmin` concurrency token).
- **Tests required:** cancelled→completed rejected; completed→started rejected; double completed does not change load twice; completing a delivery leg of a cancelled order rejected.
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-OPS-003
**Uploaded files live in the container's `/tmp` and are lost on every redeploy**

- **Area / category:** DevOps/production — Data durability / scaling
- **Severity:** High
- **Status:** Verified (config read)
- **Independent verification:** QA-B: Confirmed (canonical, G13)
- **Duplicates (same defect, other reports):** SA-API-017
- **Evidence:** `operations.Infrastructure/Storage/FileStorageProviderFactory.cs:16-37`: only `local` works, and `s3`/`azure-blob` throw `NotSupportedException`. `operations.Infrastructure/DependencyInjection.cs:32-47`. `LocalStorageOptions.cs:9-15`: default root is `/tmp/laundryghar-uploads`. `deploy/docker-compose.yml:48-58`: the `operations` service has no `Storage__*` env and no volume. Callers include inspection photos, rider proof photos, rider KYC documents and catalog item images (`operations.Application/.../UploadRiderDocument.cs`, `UploadProofPhoto.cs`, `UploadInspectionPhoto.cs`, `ItemImageCommands.cs`). `ops/backup/backup.sh` backs up only the DB.
- **Observed behaviour:** files are written to the ephemeral container filesystem. `docker compose pull && up -d` recreates the container and deletes every file, while DB rows still reference the storage keys.
- **Impact:** loss of compliance-relevant evidence (KYC, proof of delivery, damage inspection). The operations host also cannot run more than one replica.
- **Remediation:** implement the S3/Blob provider at the existing seam before go-live. Interim step: mount a named volume at an explicit `Storage__Local__RootPath` and include it in backups.
- **Tests required:** provider contract tests (save/read/delete, brand-prefixed key).
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-004
**pg_partman maintenance for orders/audit/process/notification/decision logs is scheduled only on a developer Mac**

- **Area / category:** DevOps/production — Database operations
- **Severity:** High
- **Status:** Verified (config read). Runway date is taken from the doc, not measured.
- **Independent verification:** QA-B: Confirmed (time-bound)
- **Duplicates (same defect, other reports):** SA-MOB-012
- **Evidence:** `database_scripts/99_cross_cutting_schema_qualified.sql` (create_parent for `identity_access.audit_logs`, `order_lifecycle.orders`, `order_lifecycle.process_logs` monthly with premake 6; `engagement_cms.notifications_log` monthly with premake 3; `logistics.rider_location_pings` daily). `db/migrations/0024_authz_abac_foundation.up.sql:144-149` (`authz.decision_log` monthly). The only scheduler is `db/tools/com.laundryghar.partman.plist:30`, which hard-codes `/Users/gtmkumar/...`. `PartitionMaintenanceService.cs` only calls `logistics.ensure_rider_ping_partitions`. A repo-wide grep for `run_maintenance|pg_cron` finds only `build_from_scratch.sh:104` (one-off) and the script itself. …
- **Observed behaviour:** no production component premakes monthly partitions.
- **Impact:** once premade partitions run out, new `orders` and `audit_logs` rows go to the default partition (later partition creation conflicts) or inserts fail. That is a revenue-path outage on a timer.
- **Remediation:** extend `PartitionMaintenanceService` to call a SECURITY DEFINER wrapper around `partman.run_maintenance_proc()` under a single-runner advisory lock, or enable pg_cron or the partman background worker on the managed DB. Add an alert on default-partition row count > 0.
- **Tests required:** integration test asserting future partitions exist N months ahead after a maintenance run.
- **Specialist's dependencies / priority note:** P0. Related area: DB. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-QC-001
**Admin refund API contract ("gateway"/"wallet") violates the `refund_type` CHECK; the Razorpay refund is issued before the failing INSERT**

- **Area / category:** QA (DB/mobile) — Financial integrity / API–DB contract
- **Severity:** High
- **Status:** Partially Verified (DB constraint reproduced T7d; handler traced; HTTP not executed)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-API-009
- **Evidence:** - `commerce.Application/Commerce/Common/Dtos/CommerceDtos.cs:438-449`: `IssueRefundRequest.RefundType // "gateway" or "wallet"`. - `AdminPaymentHandlers.cs:132` copies `req.RefundType` into the row. `:150` branches on `"wallet"`. `:217-221` calls `_gateway.InitiateRefundAsync` (the real `RazorpayPaymentGateway.cs:117` via `SettingsFirstPaymentGateway`) before `_db.PaymentRefunds.Add` + `SaveChangesAsync` (`:227-230`). - `database_scripts/06_bc6_commerce.sql:418-419`: `refund_type CHECK IN ('full','partial','goodwill','dispute_loss')`, unchanged by any patch or migration (grep). It matches `SharedDataModel/Enums/RefundType.cs`. - No validator constrains `RefundType` (grep), and validators do …
- **Observed behaviour:** - Any caller that follows the documented contract gets a 23514 CHECK violation at SaveChanges: - `"gateway"` → Razorpay has already refunded the money; the DB transaction rolls back, leaving no refund row and no audit row, and a retry refunds again; - `"wallet"` → the wallet credit rolls back, so the wallet-refund feature can never persist. - A caller that sends a DB-valid value such as `"full"` always takes the gateway branch, so wallet refunds …
- **Impact:** money leaves with no ledger record, and the cap check (app SUM and trigger) cannot see it, so repeated attempts over-refund without limit. The wallet-refund feature is dead.
- **Remediation:** - Split the API field into `RefundMethod` (`original|wallet`), mapped to `refund_method`, and `RefundType` (`full|partial|…`), validated against `RefundType` constants. - Insert the refund row as `processing` and commit before calling the gateway; update it afterwards. This also fixes SA-API-009.
- **Tests required:** handler unit test mapping each documented value; integration test that a refund with each method persists; a fake gateway asserting zero calls when the row insert fails.
- **Specialist's dependencies / priority note:** P1 (blocked behind SA-TEN-001 for brand staff today; reachable by platform admins). · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/10c-qa-verification-db-mobile.md`](docs/audit/specialists/10c-qa-verification-db-mobile.md)

### SA-TEN-002
**Commerce host never sets `app.current_customer_id`, so customer-level RLS on payments, wallets, refunds and loyalty degrades to brand equality**

- **Area / category:** Multi-tenancy — Intra-tenant isolation (defense in depth)
- **Severity:** High (originally Medium; QA-C reproduced cross-customer wallet visibility on the commerce host (10c D2, T1b); QA-A had Medium)
- **Status:** Verified (reproduced: customer saw another customer's wallet, QA-C T1b)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-AUTHZ-005
- **Evidence:** - The A0.6 fix exists only in `HttpContextCurrentTenant.cs:L30-46`. - `CommerceHostCurrentTenant.cs:L69` publishes `UserId = sub` for every token, including customer tokens (whose `sub` is the customer id), and it has no `CustomerId` member, so the default `null` applies (`ICurrentTenant.cs:L31`). - Policy shape: `rls_proposal.sql:L272-279` (`current_customer_id() IS NULL OR customer_id = current_customer_id()`).
- **Observed behaviour:** in the S2 repro (before 0031) customer `c1` saw the payments of `c1` and `c2` at the RLS level.
- **Impact:** in the very host that serves payments and wallets, customer isolation rests only on per-handler `CustomerId` predicates. This is the exact fail-open condition A0.6 documented, and it was re-introduced through the second adapter.
- **Remediation:** same change as SA-TEN-001 step 1 (one shared claim-to-GUC mapping).
- **Tests required:** RLS test with the commerce adapter's GUCs; customer A must not read customer B's payment.
- **Specialist's dependencies / priority note:** P1. Fix together with SA-TEN-001. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-TEN-003
**A suspended brand (including a ToS or manual suspension) can lift its own suspension through cancel → withdraw**

- **Area / category:** Multi-tenancy — Tenant lifecycle / server-side restriction bypass
- **Severity:** High
- **Status:** Verified. The DB function behaviour was reproduced (`repro_susp.sql`) and the HTTP path was traced statically.
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `BrandSuspensionMiddleware.cs:L54` allow-lists `/api/v1/admin/cancellation` while a brand is suspended. - `AdminCancellation.cs:L35-36`: `POST ""` and `POST "withdraw"` require only `settings.manage`, which `brand_admin` holds per `settings_permissions.sql:L8`. - `RequestBrandCancellation.cs:L60-67` rejects only `archived`, then at `L94` calls `SetCancellationStateAsync(...,"cancelled")`. - `WithdrawBrandCancellation.cs:L56` calls `SetCancellationStateAsync(...,"active")`. - `0015_brand_cancellation.up.sql:L277-289` rejects only `archived` and then sets the new status unconditionally. Its own comment (L282-283) says a suspended brand is "not this function's business", but no code enforces …
- **Observed behaviour:** repro output was `request-cancel was=suspended`, then `withdraw was=cancelled`, leaving the final row `status=active, suspension_reason=tos`.
- **Impact:** suspension, the platform's main server-side tenant restriction, can be undone by the tenant itself in two requests. That defeats both ToS enforcement and non-payment enforcement (the latter until the next dunning tick).
- **Remediation:** 1. In `set_brand_cancellation_state`, refuse `p_status='cancelled'` when the current status is `suspended`. Alternatively, record the prior status in `brand_cancellations` and have withdrawal restore it rather than forcing `active`. 2. Have `RequestBrandCancellation` reject suspended brands, or preserve the suspension through the wind-down.
- **Tests required:** a migration test (suspended → cancelled must raise, or withdraw must restore `suspended`) and a handler test.
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-VERT-002
**Salon and tiffin templates are publicly sellable at signup, but neither vertical is operable**

- **Area / category:** Verticals/domain — Product readiness / onboarding
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed (canonical, G2)
- **Duplicates (same defect, other reports):** SA-ARCH-004
- **Evidence:** - Templates. `db/migrations/0011_vertical_templates.up.sql:41,71-73` — salon template `is_public` defaults to true. `db/migrations/0012_recurring_fulfillment_mode.up.sql:155-156` sets tiffin `is_public = true`. `CompleteSignup.cs:73-75` and `GetSignupTemplates.cs:29` offer any public template. - Salon. `phase4_salon_fulfillment_schema.sql` creates the tables, but there are no EF entities, handlers or endpoints (grep `salon_fulfillment|StaffMember|ResourceBooking` in `backend/`). The appointments module routes to `/appointments` with `appointment.manage` (`phase4_salon_pack.sql:19-25`). `admin-web/src/App.tsx:87-116` has no such route (`*` redirects to `/`). `appointment.manage` is not …
- **Observed behaviour:** A provider can sign up as a salon or tiffin business and is provisioned (features, seeded catalog, terminology). It then has no way to take an appointment or set up a recurring delivery. The only order path is the laundry one (SA-VERT-001), and the salon "Appointments" menu item leads back to the dashboard (and is visible only to platform admins, since nobody holds `appointment.manage`).
- **Impact:** Revenue and reputation risk: a paying tenant is sold a product that does not work. 0012 also contradicts 0011's own stated gate ("Listing an unrunnable template … would be worse than not offering it", `0011…:76-79`).
- **Remediation:** Immediately set `is_public=false` for `salon` and `tiffin` in a new migration, and tie public visibility to an operability checklist (creation path, booking API, client screen). Then build each vertical as its own feature module (see the module-boundary recommendation below).
- **Tests required:** A test that `GetSignupTemplates` returns only templates whose fulfillment mode has a creation path. An E2E signup → first booking test per public template.
- **Specialist's dependencies / priority note:** P0 for the visibility flip; P1/P2 for building the verticals. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-API-002
**Gateway rate-limit partition is attacker-controlled (bypass and targeted tenant throttling)**

- **Area / category:** Backend/API — Security / availability
- **Severity:** High
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed; QA-B: Confirmed (canonical, G11)
- **Duplicates (same defect, other reports):** SA-AUTHZ-010, SA-OPS-002, SA-TEN-005
- **Evidence:** `laundryghar.Gateway/RateLimitPartitioning.cs:24-31` (brand partition preferred), `:34-38` (unauthenticated `X-Brand-Id` header accepted as the key), `:40-63` (unverified JWT `brand_id`), `:80-86` (leftmost raw `X-Forwarded-For`, read directly from the header); `Program.cs:218` (brand bucket = 10× the IP limit).
- **Observed behaviour:** (a) A client can send a random GUID in `X-Brand-Id` on every request and get a fresh 3,000/min bucket each time, so per-IP limiting is fully bypassable. The class comment says forging "cannot … raise a limit", which is incorrect. (b) An attacker who knows a victim brand's id can send traffic with that `X-Brand-Id` and exhaust the victim tenant's whole budget, so all of that tenant's users get 429. (c) Spoofed `X-Forwarded-For` rotates the IP …
- **Impact:** the outer DoS backstop is ineffective, and cross-tenant denial of service is possible.
- **Remediation:** key the brand partition only on a validated token (validate the JWT at the gateway, or move brand partitioning into the services after authentication). Never key on an unauthenticated header. Use the forwarded-headers middleware with known proxies instead of parsing XFF by hand.
- **Tests required:** `Resolve` ignores `X-Brand-Id` for unauthenticated requests; spoofed XFF from an untrusted hop is ignored.
- **Specialist's dependencies / priority note:** P1. Related area: SEC. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-004
**POS CreateOrder idempotency is check-then-act on jsonb with no unique constraint (duplicate orders and double balance debits)**

- **Area / category:** Backend/API — Idempotency / data integrity
- **Severity:** High
- **Status:** Verified (code read); race Not Tested
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-DB-011
- **Evidence:** `CreateOrderCommand.cs:71-94` (lookup `metadata @> {"idempotency_key":…}`), `:685-689` (key embedded through string interpolation), `:757-803` (insert plus coupon, loyalty and package debits). No unique index on the order idempotency key exists in `database_scripts/` or `db/migrations|patches` (grep for `idempotency` indexes lists only payments, wallet_transactions, refunds, pickups and partner wallet). Client: `pos-web/src/api/orders.ts:38-50`.
- **Observed behaviour:** two concurrent submissions with the same key (POS double-tap, or an axios retry after a 401-refresh in `pos-web/src/api/client.ts:90-93`) both miss the lookup and both commit. The result is two orders, two coupon redemptions, double loyalty burn and double package debit. A key containing `"` produces invalid JSON (22P02 → 422), and a crafted key can inject extra metadata fields.
- **Impact:** financial double-charges and duplicate orders under real network conditions.
- **Remediation:** add an `idempotency_key` column with a partial `UNIQUE (brand_id, idempotency_key)` (mirroring `pickup_idempotency_and_source.sql`) and catch 23505 to return the winner. Serialise metadata with `JsonSerializer`.
- **Tests required:** a parallel-request integration test (Testcontainers) asserting one order and one ledger debit.
- **Specialist's dependencies / priority note:** P1. Related area: DB. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-005
**No optimistic concurrency anywhere; balances and counters are lost-update prone**

- **Area / category:** Backend/API — Concurrency / data integrity
- **Severity:** High
- **Status:** Verified (absence confirmed by grep; code paths read); races Not Tested
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-DB-008
- **Evidence:** zero matches for `IsConcurrencyToken|IsRowVersion|ConcurrencyCheck|xmin|DbUpdateConcurrencyException` in non-test code. Read-modify-write examples: coupon `CurrentUsageCount++` after a separate max-uses check (`CreateOrderCommand.cs:292-293,771`); loyalty balance and `Version++` (`:388-415`); package `CreditValueUsed +=` (`:485`); wallet `Balance +=` (`CustomerWalletHandlers.cs:181`, `AdminPaymentHandlers.cs:184`); order `AmountPaid +=` (`RecordOfflinePaymentCommand.cs:174-175`, `UpdateMyTaskStatus.cs:225`); order status (`UpdateOrderStatusCommand.cs:59-64`). The only row lock is the partner wallet (`CommerceDbContext.cs:118-124`). `ExceptionHandler.cs:306-345` would map a concurrency …
- **Observed behaviour:** concurrent operations on the same customer, coupon or order overwrite each other (last write wins). Coupon global caps and per-customer caps can be exceeded, and wallet and loyalty balances drift from their append-only ledgers.
- **Impact:** money drift between the balance column and the ledger, promotional budget overrun, and silent status regressions.
- **Remediation:** map Postgres `xmin` as a concurrency token on money-bearing aggregates (wallet, customer loyalty, coupon, customer package, order) and translate `DbUpdateConcurrencyException` to 409. Alternatively use atomic `UPDATE … SET x = x + @d WHERE … AND x + @d <= cap` as already done for slots.
- **Tests required:** parallel debit/credit tests asserting `balance == SUM(ledger)`; coupon cap race test.
- **Specialist's dependencies / priority note:** P1. Related area: DB. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-DB-005
**Identity tables without RLS: cross-tenant role grants and PII/token reads are possible at the DB layer**

- **Area / category:** Database (index/idempotency/RLS) — RLS coverage
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-C: Confirmed
- **Related:** SA-AUTHZ-003 (app-layer grant) — kept distinct: this is the DB-layer control (RLS off on identity tables)
- **Evidence:** live, `user_scope_memberships`, `user_profiles`, `login_history`, `otp_codes`, `refresh_tokens`, `password_resets`, `permissions` and `role_permissions` have `relrowsecurity=f`, and their policies from `rls_proposal.sql` are inert. `identity_access.users` has `rls_users_insert WITH CHECK true`.
- **Observed behaviour:** from a brand-A session, an INSERT into `user_scope_memberships` granting a user brand B's `brand_owner_b` role succeeded. The role itself was invisible to A under RLS, but the FK ignores RLS. The session could also count memberships of all brands.
- **Impact:** escalation across tenants depends entirely on app checks in the access-control handlers. Token/OTP hashes and profiles of every tenant are readable by any app_user query.
- **Remediation:** enable RLS on `user_scope_memberships` with a policy deriving the brand from (scope_type, scope_id) via a SECURITY DEFINER helper, or add a `brand_id` column. Enable `rls_user_self` on the token/OTP/profile tables, keeping the bypass for auth paths. Restrict the `users` INSERT check.
- **Tests required:** an A session cannot insert a membership whose role, scope or brand is B.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-MOB-002
**One job can be held by two riders: no uniqueness, locking or status check on assignment creation; no reassignment semantics**

- **Area / category:** Mobile/delivery/maps — Concurrency / dispatch.
- **Severity:** High
- **Status:** Verified for the manual paths (deterministic). Suspected for the races (not reproduced).
- **Independent verification:** QA-C: Confirmed; Architect: Confirmed
- **Duplicates (same defect, other reports):** SA-DB-022
- **Evidence:** - `operations.Application/Orders/Pickup/Commands/PickupCommands.cs:204-279` (`AssignPickupCommand`) never checks `pr.Status == "pending"` or any existing live assignment. It always inserts a new `DeliveryAssignment` and increments load. - `operations.Application/Orders/Delivery/Commands/DeliveryAssignmentCommands.cs:29-107` behaves the same for any order/pickup/leg. - `database_scripts/04_bc4_order_lifecycle.sql:332-335` has only non-unique indexes. - `commerce.Infrastructure/Worker/Services/AutoDispatchService.cs:313-315,396-400` re-checks then inserts with no lock. - `operations.Application/Logistics/RiderSelf/Commands/OfferActions/OfferActions.cs:59-71` runs its "taken" check before and …
- **Observed behaviour:** - Calling admin assign twice (or assign after auto-dispatch) yields two active legs for one pickup. The first rider's leg is neither cancelled nor its load released. - Auto-dispatch plus manual assign, or two worker replicas, can double-insert.
- **Impact:** Two riders dispatched to one customer, duplicate payouts/COD, and load drift.
- **Remediation:** 1. Add a partial unique index `ON delivery_assignments(pickup_request_id) WHERE status IN ('offered','assigned','accepted','started','arrived') AND pickup_request_id IS NOT NULL`, plus `(order_id, leg_type)` with the same predicate. 2. In handlers, check `pr.Status == 'pending'` and map 23505 to 409. 3. Add an explicit reassign command that cancels the old leg and adjusts load in one transaction. 4. Make accept/expire conditional updates (`UPDATE … SET status='accepted' WHERE id=@id AND status='offered' AND offer_expires_at > now()`).
- **Tests required:** concurrent assign (Testcontainers), assign-after-assign 409, accept-vs-expire race, reassign moves load.
- **Specialist's dependencies / priority note:** P0. Depends on SA-MOB-001 for status vocabulary. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-003
**Proof-of-delivery OTP is never generated, so the OTP gate is inert; verification has no attempt limit**

- **Area / category:** Mobile/delivery/maps — Security / workflow.
- **Severity:** High
- **Status:** Verified (repo-wide search for writers of `DeliveryOtp`/`PickupOtp` / `delivery_otp`; DB triggers searched).
- **Independent verification:** QA-C: Confirmed; Architect: Confirmed
- **Evidence:** - The only writes in the codebase are the partner-dispatch `PickupOtp = req.PickupOtp` (`PartnerDispatch/Commands/AssignPartnerDispatch/AssignPartnerDispatch.cs:67`). - Order OTP is read at `UpdateMyTaskStatus.cs:90-94`, `VerifyTaskOtp.cs:39-48` and `RiderTaskMapper.cs:93-94`. - The DDL `04_bc4_order_lifecycle.sql:44-45` has no default. - `VerifyTaskOtp.cs:43-59` only stamps `OtpAttemptedAt`, with no counter or lockout. - The customer sees OTP only when it exists (`CreateOrderCommand.cs:841-844`).
- **Observed behaviour:** `requiresOtp` is false for all app-created orders. Riders complete deliveries without any customer handshake, and the photo is optional. If OTPs were seeded, there is no brute-force protection.
- **Impact:** No proof of handover, which is a dispute and fraud vector for COD/high-value items. The "OTP-verified delivery" product claim is not met.
- **Remediation:** - Generate CSPRNG 4–6 digit pickup/delivery OTPs when the order enters `pickup_scheduled`/`out_for_delivery` (strategy transition effect). - Add `otp_attempts` with a lockout after N attempts per leg. - Expose the OTP to the customer by push/SMS.
- **Tests required:** order out_for_delivery has OTP; complete without verify → 400; 6th wrong attempt → locked.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-OPS-006
**Release images ship from a red `main`; no deploy, migration or approval stage**

- **Area / category:** DevOps/production — CI/CD
- **Severity:** High
- **Status:** Verified (workflow files plus GitHub run history)
- **Independent verification:** QA-B: Confirmed
- **Evidence:** `.github/workflows/release.yml:7-10, 20-81`: triggers on push to `main` and has no dependency on CI. CI runs on `main` (newest first): 36294076412 failure, 36294068022 cancelled, 32801423356, 30655059625, 29593179258, 29587337692, 29584182499 and 29524708837 all failure. Release run 36294076432 (same SHA 274b7af) succeeded, as did 36294068000, 32801423294, 30655059355 and 29593179194. Latest CI failures: rider-mobile job 108549585491 (`npm ci` ERESOLVE) and customer-mobile job 108549585512 (TS2882 on `../global.css`). The backend (108549585458) and admin-web (108549585404) jobs passed.
- **Observed behaviour:** `latest` is overwritten on every push regardless of test results. There is no promotion between environments, no deploy job and no rollback procedure beyond re-tagging by hand.
- **Impact:** untested artifacts reach production hosts through the documented `pull && up` flow. CI has been red since it was created, which means the signal is ignored.
- **Remediation:** run release on `workflow_run: CI, conclusion success` (or merge the jobs with `needs:`). Stop using `latest` for deploys and pin `${TAG}` to the SHA. Fix the two mobile failures. Enable branch protection that requires CI.
- **Tests required:** none (pipeline config).
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-SOLID-003
**Payment status is an unconstrained string. Royalty revenue filters on `"completed"`, which the DB never permits, so franchise royalties come out as 0**

- **Area / category:** OOP/SOLID — Encapsulation (string-typed state) / OCP
- **Severity:** High
- **Status:** Verified (static plus DDL. Not run.)
- **Independent verification:** QA-B: Confirmed (and see the new SA-QB-001)
- **Evidence:** - The `CommercePaymentStatus` constants (`SharedDataModel/Enums/CommercePaymentStatus.cs`) have no `"completed"`. - The DB CHECK is `('pending','initiated','authorized','captured','succeeded','failed','cancelled','refunded','partially_refunded','disputed')` (`database_scripts/06_bc6_commerce.sql:L379-L381`). No patch adds `'completed'` (grep of `db/`). - The readers that need `"completed"` are `RoyaltyCommands.cs:L98-L107` and `RoyaltyGenerationService.cs:L195-L203`. - `AdminPaymentHandlers.cs:L95` and `OrderCancellationRefund.cs:L49` accept `"captured"|"completed"`, which excludes COD `"succeeded"` rows (`UpdateMyTaskStatus.cs:L215`).
- **Observed behaviour:** the automatic royalty worker and the manual royalty calculation (without `GrossRevenueOverride`) both sum zero payments. COD-settled payments are not refundable through the admin refund path.
- **Impact:** franchisor royalty revenue is not billed. Finance reports are wrong.
- **Remediation:** use the `CommercePaymentStatus` constants everywhere (forbid raw literals with an analyzer or banned-API rule) and add a `CommercePaymentStatus.Settled = { Captured, Succeeded }` set used by royalty, refund and reconciliation queries.
- **Tests required:** a royalty calculation over seeded captured and succeeded payments returns non-zero; a refund of a COD payment follows a defined rule.
- **Specialist's dependencies / priority note:** P0 for franchise billing. Related area: FINANCE. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-SUB-001
**Self-serve trials never end: `trialing` brand subscriptions are never converted, invoiced or expired, and trial features are perpetual**

- **Area / category:** Subscription/billing — (a-brand) Billing lifecycle / revenue leakage
- **Severity:** High
- **Status:** Verified (code read end-to-end; not executed)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `core.Application/Identity/Signup/Commands/CompleteSignup.cs:L199-L225`: creates `Status="trialing"`, `NextBillingAt = now+14d`, no invoice. - `core.Application/Identity/Signup/TemplateProvisioner.cs:L66-L77`: trial features inserted `Enabled=true`, `ValidUntil` unset (perpetual), `Source="bundle"`. - `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs:L67-L69`: the renewal pass selects only `s.Status == "active"`. - `BrandPlatformBillingService.cs:L201-L207`: dunning only touches subscriptions that already have a `past_due` invoice, and a trial has no invoice. - Repo-wide grep for `trialing` in .cs/.sql found no code that moves a brand subscription out of `trialing`.
- **Observed behaviour:** every self-signed-up brand gets its template's default tier (`pro` for laundry). That tier never expires, no invoice is ever raised, and the brand can never be suspended for non-payment.
- **Impact:** 100 % revenue leakage on the only self-serve acquisition funnel. Signup is anonymous and rate-limited only by OTP, so anyone can create unlimited free Pro tenants.
- **Remediation:** - In `RunCycleAsync`, add a pass for `status='trialing' AND current_period_end <= now`. It should either: - convert to `active` and issue the first invoice, if a payment method is on file or `AutoRenew`; or - move to `past_due` so dunning applies. - Optionally set `brand_feature.valid_until = trial end` for bundle rows during the trial, so features lapse without the worker.
- **Tests required:** an integration test for the worker conversion pass (trial → invoice → past_due → suspend); a test that a trial brand's token loses features after expiry when it does not convert.
- **Specialist's dependencies / priority note:** P0. Needs SA-SUB-004 and SA-SUB-005 fixed first, or the pass will not run. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-002
**Paying an overdue (`past_due`) brand invoice via Razorpay is ignored, and a payment link cannot be created for it, so the "pay to reinstate" path is broken**

- **Area / category:** Subscription/billing — (a-brand) Payments / dunning / reactivation
- **Severity:** High
- **Status:** Verified (code read; not executed)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-API-011
- **QA correction to the specialist text:** QA-A: the status check is at ProcessPaylinkWebhook.cs:71-72 (not L109-115).
- **Evidence:** - `commerce.Infrastructure/Worker/Services/BrandPlatformBillingService.cs:L186-L195`: dunning sets every overdue invoice to `status='past_due'`. - `core.Application/Identity/Entitlements/Commands/ProcessPaylinkWebhook.cs:L109-L115`: the webhook marks the invoice paid only if `Status == "issued"`; otherwise it returns `"invoice already past_due"` with 200 OK, so Razorpay does not retry. - `core.Application/Identity/Entitlements/Commands/CollectBrandPlatformInvoice.cs:L25-L27`: a payment link can only be created for an `issued` invoice. A link that already exists is returned, but a `past_due` invoice without one cannot get one. - Reinstatement requires no `issued`/`past_due` invoices due …
- **Observed behaviour:** - Suspension only ever follows `past_due` invoices, so every suspended brand's invoices are `past_due`. - A brand that pays one of those links through Razorpay is acknowledged but not credited, and stays suspended. - Recovery needs a platform admin to run `sync-payment` (`CollectBrandPlatformInvoice.cs:L58-L73`, which does accept any non-paid status) or to mark the invoice paid manually.
- **Impact:** paying customers stay locked out. Support load rises, and payment is lost silently, because Razorpay receives 200.
- **Remediation:** accept `issued` and `past_due` in the webhook and in link creation. Also clear `next_attempt_at` and record `paid_at` and the payment id.
- **Tests required:** a webhook test for a `past_due` invoice, followed by a reinstatement test with the worker.
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-003
**A brand subscription never returns from `past_due` to `active`, so after one late payment renewals stop for good**

- **Area / category:** Subscription/billing — (a-brand) Billing lifecycle
- **Severity:** High
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `BrandPlatformBillingService.cs:L201-L207` sets the subscription to `past_due`. - The reinstatement pass only changes `brands.status` (`L176-L182`). - The renewal pass only processes `Status == "active"` (`L67-L69`). - The only writer of `Status="active"` on an existing subscription is the operator action `ApplyBundleToBrand.cs:L107`. - Entity doc still says `active | cancelled` (`BrandPlatformSubscription.cs:L26-L27`).
- **Observed behaviour:** once dunning has marked a subscription `past_due`, paying clears the brand suspension but no further renewal invoices are ever issued. The brand keeps its features at no cost.
- **Impact:** revenue leakage for every brand that is ever late, plus an MRR report that is wrong.
- **Remediation:** in the reinstatement pass, also set `brand_platform_subscription.status='active'` where it is `past_due` and there are no open invoices.
- **Tests required:** worker test for past_due → paid → active → next renewal invoiced.
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-004
**The brand renewal pass runs outside a trusted worker scope, so RLS hides all subscriptions and no renewal invoice is ever issued under `app_user`**

- **Area / category:** Subscription/billing — (a-brand) Background workers / tenant context. Related area: DB/RLS.
- **Severity:** High
- **Status:** Partially Verified (code read; AsyncLocal and RLS runtime semantics reasoned about, not executed)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-API-010
- **Evidence:** - `BrandPlatformBillingService.cs:L62-L64`: `await RunDunningAsync(ct);` then `_scopeFactory.CreateAsyncScope()`. This is the plain scope, not `CreateWorkerAsyncScope()`. - `WorkerScope.cs:L26-L49`: the marker is an `AsyncLocal<bool>`. Setting it inside `RunDunningAsync` (`L156`) does not flow back to the caller after `await`. - `CommerceHostCurrentTenant.cs:L80-L90`: with no HttpContext and no marker, `BypassRls=false` and `BrandId=null`. - `RlsConnectionInterceptor.cs:L59-L106` writes those GUCs. - `phase4_brand_platform_subscription.sql:L58-L72`: policy `rls_bypass() OR brand_id = current_brand_id()`, so zero rows are visible. - Production must connect as `app_user` …
- **Observed behaviour:** `due` is always empty in production, so renewals silently issue nothing. The cycle still logs normally.
- **Impact:** there is no recurring revenue even when the worker is enabled. It would pass a local test run as the `postgres` superuser, which ignores RLS.
- **Remediation:** a one-line change to `_scopeFactory.CreateWorkerAsyncScope()` at `L63`. Consider also asserting `WorkerScope.IsWorkerScope` in worker-only code paths.
- **Tests required:** an integration test that runs `BrandPlatformBillingService` against an `app_user` connection with RLS on, and asserts an invoice is created.
- **Specialist's dependencies / priority note:** P0. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-006
**Subscription state and entitlements are disconnected: features are granted without payment and kept after cancellation or non-payment**

- **Area / category:** Subscription/billing — (a-brand) Entitlement integrity
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `CancelBrandPlatformSubscription.cs:L47-L49,L64-L68`: "The brand keeps any already-licensed features until an operator changes entitlement separately." - `ApplyBundleToBrand.cs:L60-L78`: features are licensed immediately. `L107` forces `sub.Status="active"`, which reactivates a `cancelled` or `past_due` subscription without payment. The invoice is only issued, never required. - `ScopeResolver.cs:L197-L202`: entitlement reads only `brand_feature`; there is no join to subscription status. - The only billing-driven gate is `brands.status='suspended'`, applied by the worker after 3 attempts plus 14 days of grace (`BrandPlatformBillingService.cs:L216-L227`), and that worker is off by default …
- **Observed behaviour:** a cancelled, `past_due` or never-paid subscription keeps full feature access indefinitely, unless the brand is suspended, which depends on SA-SUB-002 to SA-SUB-005 working.
- **Impact:** entitlements are not tied to the commercial contract. This weakens Q4.
- **Remediation:** make subscription status an input to entitlement. Smallest change: on cancel (at period end) and on `past_due` beyond grace, set `brand_feature.valid_until` on `source='bundle'` rows, or have ScopeResolver treat `bundle` rows as entitled only while the subscription is `active` or `trialing`. Keep `manual` add-ons explicit.
- **Tests required:** cancel, then token mint, asserting the bundle features are gone at period end; ApplyBundle on a cancelled subscription without payment, asserting the expected (decided) behaviour.
- **Specialist's dependencies / priority note:** P0/P1. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-007
**No tenant-facing billing: owners cannot see their plan or invoices, cannot pay or upgrade, and are given no usable remediation for 402s**

- **Area / category:** Subscription/billing — (a-brand) Commercial UX / self-service. Related area: FE.
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - All `/api/v1/admin/entitlements/*` endpoints require `saas.read`/`saas.manage` (`AdminEntitlements.cs:L28-L38`), granted only to `platform_admin` (`seeder_parity_r3sec1.sql:L146-L162`). `brand_admin`'s grant list has no `saas.*` (`IdentitySeeder.cs:L515-L571`). - `BrandSuspensionMiddleware.cs:L51` allow-lists `/api/v1/admin/entitlements` "to see the tier, the invoices, and pay them", but the suspended owner is refused by the authorization layer. - `ApiAuthorizationResultHandler.cs:L42` sends owners to `/settings?tab=plan`. admin-web Settings has no `plan` tab (`SettingsPage.tsx:L21-L57`, where `platform-payments` is `platformOnly`). The toast says "Settings → Licensing" …
- **Observed behaviour:** owners are never told they owe money. Once suspended they see generic errors and cannot reach their invoice or pay it. Every recovery is a manual operator action.
- **Impact:** blocks self-serve SaaS. Suspensions without notice also carry legal and reputational risk.
- **Remediation:** - Add brand-scoped read endpoints (for example `GET /api/v1/admin/billing/me`) gated by a new tenant permission (`billing.read`) and keyed to the caller's `brand_id`, never a route parameter. - Add an owner-initiated "pay invoice" endpoint that creates or returns the payment link. - Add a Billing page in admin-web, and handle `brand_suspended`. - Emit notification outbox events on invoice issue and on `past_due`.
- **Tests required:** owner can read only their own invoices; owner can create a link while suspended; another brand's invoice id returns 404.
- **Specialist's dependencies / priority note:** P0 for commercial launch. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-FE-004
**Admin order management hardcodes the laundry state machine and ignores the server's `allowedTransitions`**

- **Area / category:** Frontend/clients — Multi-vertical correctness
- **Severity:** High
- **Status:** Verified (client code plus backend strategy read)
- **Independent verification:** QA-B: Confirmed, strengthened (live for laundry brands)
- **Evidence:** - `admin-web/src/pages/orders/orderStatus.ts:13-63`: a laundry ladder, with a comment pointing at a non-existent `laundryghar.Orders/.../OrderStateMachine.cs`. - `pages/orders/OrderDetailDrawer.tsx:504`: `advanceableTargets(order.status)`. - `pages/orders/OrdersPage.tsx:33,122`: the status filter is `ORDER_STATUS_LIST`. - Backend: `operations.Application/Fulfillment/Salon/SalonAppointmentStrategy.cs:47-66` uses `booked→confirmed→checked_in→in_service→completed`. - The server already returns `AllowedTransitions` (`operations.Application/Orders/Orders/Dtos/OrderDtos.cs:137`). POS uses it (`pos-web/src/pages/orders/OrderDetailPage.tsx:50`); admin-web's type does not even declare it.
- **Observed behaviour:** for a salon (`booked`), logistics or recurring order, `ALLOWED_TRANSITIONS[status]` is `undefined`, so no action buttons appear and the order cannot be progressed from the admin console. The status filter offers only laundry statuses. Status *labels* are vertical-aware (`useStatusLabeler`), so the screen looks correct but cannot be operated.
- **Impact:** Q9. Order processing for any non-laundry vertical is not usable in the main business console.
- **Remediation:** add `allowedTransitions` to `OrderDto` in `types/api.ts` and use it, as POS does. Build the filter from `useFulfillmentConfig()` stages plus terminal statuses. Keep the laundry map only as a fallback.
- **Tests required:** component tests for the drawer with a salon fixture (`booked` shows Confirm / Cancel); an e2e against a seeded salon brand.
- **Specialist's dependencies / priority note:** P1, a prerequisite for any non-laundry tenant. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-MOB-004
**No geocoding or coordinate capture anywhere: geofence, distance-aware dispatch, coordinate navigation and parcel fare quote are inert or broken**

- **Area / category:** Mobile/delivery/maps — Maps / location infrastructure.
- **Severity:** High
- **Status:** Verified.
- **Independent verification:** QA-C: Confirmed; Architect: Confirmed
- **Evidence:** - The only `CreatePoint` calls are `BatchLocationPing.cs:53,85`. - No assignment to `GeoLocation` exists for `CustomerAddress`, `Store`, `Warehouse` or `DeliveryAssignment` (grep of `GeoLocation *=`; entities in `laundryghar.SharedDataModel/Entities/*`). - The customer address request has no lat/lng (`customer-mobile/src/types/api.ts:235-254`), and the customer app has no location dependency. - `GetFareQuoteQuery.cs:57-59` throws `BusinessRuleException` when either point is null. - `GeofenceEvaluator.cs:83,100` uses `leg.GeoLocation` and the store points. - `AutoDispatchService.cs:127,215-216` handles null coordinates, which ranks without distance. - In …
- **Observed behaviour:** - The parcel flow fails at quote for every customer. - Geofence auto-arrive and store-drop never fire. - Dispatch is load-only. - Rider directions fall back to text search. - Only seed data (`db/patches/seed_rider_ops_demo.sql:65`) has coordinates, so demos look functional.
- **Impact:** Point-to-point/courier is not operable, distance-based payouts and fares are meaningless, and the "auto status on arrival" claim is false.
- **Remediation:** 1. Add optional `latitude/longitude` to the create/update address DTO and persist them as a Point. 2. In customer-mobile, add an `expo-location` "use my current location" button and/or a pin-drop on `react-native-maps` (dev-build/config plugin required). 3. Add a server-side `IGeocoder` port with one provider adapter, called on address save when lat/lng are absent. Store the provider, accuracy and timestamp. 4. Copy address geo into `delivery_assignments.geo_location` at assignment time. 5. Allow stores to be pinned in admin.
- **Tests required:** address saved with coordinates → quote succeeds; leg created copies geo; geofence flips within 150 m.
- **Specialist's dependencies / priority note:** P0 for the logistics vertical; P1 for laundry. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-SOLID-001
**Order status transitions are implemented separately in 5 write paths and have diverged (missed notifications, loyalty only via the rider app, state-machine bypass)**

- **Area / category:** OOP/SOLID — SRP / encapsulation / duplicated business rules
- **Severity:** High
- **Status:** Verified (static trace. Not run.)
- **Independent verification:** QA-B: Confirmed (canonical, G6)
- **Duplicates (same defect, other reports):** SA-API-006
- **Evidence:** - The writers are `UpdateOrderStatusCommand.cs:L52-L117`, `CancelOrderCommand.cs:L48-L101`, `CancelOrderByCustomerCommand.cs:L45-L94` and `UpdateMyTaskStatus.cs`. The rider handler writes the order in two places: delivery completion at `L152-L256`, which hard-codes `o.Status = "delivered"` at L159 and `FromStatus = "out_for_delivery"` at L173-L174, and pickup-leg hops in `AdvancePickupLegAsync` at L318-L397. - Event producers: cancels and admin updates emit `"order.status_changed"` (L93, L87, L117). Rider delivery emits `"delivery.completed"` (`UpdateMyTaskStatus.cs:L238`). Pickup hops emit no outbox event (L361-L392). - Consumer: `NotificationMappingService.cs:L109-L117` subscribes to …
- **Observed behaviour:** - (a) Customers get no ORDER_DELIVERED notification when the rider completes a delivery, which is the main path. - (b) No ORDER_PICKED_UP notification when the rider collects. - (c) No cancellation notification on any path. - (d) Loyalty points are earned only when a rider completes a delivery. Staff or POS orders moved to `delivered` through `UpdateOrderStatus` never earn. - (e) The rider delivery path never calls `EnsureTransition`. It is …
- **Impact:** customer communication failures and inconsistent loyalty accrual across channels. Audit history can be wrong. Each new vertical multiplies these copies.
- **Remediation:** add one application service, `IOrderTransitionService.TransitionAsync(order, toStatus, actor, reason, ct)`, that calls `strategy.EnsureTransition`, sets `Status`, `LifecycleState` and `Version`, applies `ApplyTransitionEffects`, adds the history row and always emits `order.status_changed` with `fromStatus` and `toStatus`. Route all 5 paths through it. Keep `delivery.completed` as an additional event. Add `("order.status_changed","cancelled")` to the template map, or emit `order.cancelled`. Decide whether loyalty should key off `LifecycleState …
- **Tests required:** for each path (admin PATCH, admin cancel, customer cancel, rider pickup collect, rider delivery complete), assert exactly one history row with the correct `FromStatus`, one `order.status_changed` outbox row, and that the template resolves. Assert that a rider completing a `disputed` order is rejected. Assert that loyalty earns for both …
- **Specialist's dependencies / priority note:** P1. Prerequisite for SA-SOLID-004. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-VERT-001
**Order creation ignores the brand's vertical; every non-parcel order is a laundry `process_deliver` order stamped `vertical_key='laundry'`**

- **Area / category:** Verticals/domain — Domain model / multi-vertical seam
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed (canonical, group G1)
- **Duplicates (same defect, other reports):** SA-ARCH-003, SA-ONB-004, SA-SOLID-004
- **Evidence:** - `backend/laundryghar/operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:51-60` — `isParcel` comes from `req.JobType`. `:620-627` — mode is `PointToPoint` or `ProcessDeliver`, nothing else. `:629-647` — `Order` is built without setting `VerticalKey`. The comment at `:643-646` admits "Phase 2 sets it explicitly from Brand.VerticalKey once multiple verticals coexist". - `Order.cs:44,48` — the entity defaults are `Laundry` and `ProcessDeliver`. - `JobType.cs` — `All = {laundry, parcel}`. - grep shows `FulfillmentMode.DefaultFor` and `CatalogKind.DefaultFor` have no callers.
- **Observed behaviour:** A salon or tiffin brand (both creatable through public signup) that places an order through `POST /orders` gets a laundry state machine (`placed → pickup_scheduled → … → sorting → qc …`), a TAT-based promised delivery date, and `orders.vertical_key='laundry'`. `SalonAppointmentStrategy` and `RecurringDeliveryStrategy` are registered but unreachable from any creation path.
- **Impact:** Breaks the "one brand = one vertical" promise at the data layer. Vertical-keyed reporting (`orders.vertical_key` index in `phase0_multi_vertical.sql:72`) mislabels orders. Salon and tiffin tenants run laundry workflows.
- **Remediation:** Load `brand.VerticalKey` in `CreateOrderHandler` and set `order.VerticalKey` from it. Derive the mode from a single policy: `JobType.Parcel → point_to_point`, otherwise `FulfillmentMode.DefaultFor(brand.VerticalKey)`. Reject a mode the brand's vertical does not allow (for example `process_deliver` on a salon brand) with a 422. Add a DB trigger or CHECK that `orders.vertical_key` equals the brand's vertical.
- **Tests required:** Handler tests asserting a salon brand gets `appointment`/`booked`, a laundry brand gets `process_deliver`, and parcel on a laundry brand gets `point_to_point` with `vertical_key='laundry'`. A parity test that laundry behaviour is unchanged.
- **Specialist's dependencies / priority note:** P1. Blocks any salon or tiffin onboarding. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-VERT-004
**Invoices hardcode laundry tax identity (SAC 999712, "Laundry & Dry-Cleaning Services") and laundry billable statuses for every vertical**

- **Area / category:** Verticals/domain — Compliance / shared-service coupling
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed, strengthened
- **Evidence:** - `operations.Application/Orders/Invoices/InvoiceTaxCalculator.cs:18` sets `DefaultSacCode = "999712"`. - `GenerateInvoiceCommand.cs:156` sets `SacCode = InvoiceTaxCalculator.DefaultSacCode` for every order. `:37-38` limits billable statuses to `ready/delivered/closed`. - `InvoicePdfRenderer.cs:44` prints `SAC {code} — Laundry & Dry-Cleaning Services` on every PDF. - `Invoice.cs:14,47`.
- **Observed behaviour:** A courier (logistics) brand, which IS operable through the public `courier` template, gets GST invoices for delivered parcel orders that declare laundry SAC 999712 and the laundry service description. A salon order in `completed` could never be invoiced, because `completed` is not billable.
- **Impact:** Incorrect GST tax documents for non-laundry tenants (a compliance and legal exposure in India). The blueprint already planned a per-strategy `TaxProfile` (`MULTI_VERTICAL_BLUEPRINT.md` §2.2), but it was never built.
- **Remediation:** Add `TaxProfile` (SAC + description) to `IFulfillmentStrategy`, or a per-vertical row in config with brand override. Make billable statuses `LifecycleState ∈ {completed, closed}` plus the explicit laundry `ready` prepaid case, instead of laundry literals.
- **Tests required:** Invoice tests per mode: laundry gets 999712, point_to_point gets the configured courier SAC, salon `completed` is billable.
- **Specialist's dependencies / priority note:** P1 for logistics tenants already onboardable. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-ONB-002
**Tenant branding (logo, colours, theme) has no usable write path and no client consumes it**

- **Area / category:** Onboarding/white-label — White-label
- **Severity:** High
- **Status:** Partially Verified (code verified; the RLS read-as-empty behaviour is taken from the policy source plus measurements recorded in migration comments, not reproduced)
- **Independent verification:** QA-B: Confirmed
- **Evidence:** - `database_scripts/01_bc1_tenancy_org.sql:L42-70`: the branding columns exist. - `core.Application/Identity/TenancyOrg/Dtos/BrandDtos.cs:L20-27`: `UpdateBrandRequest` has `LogoUrl` only, no colours or favicon. - `Brands/Commands/UpdateBrand/UpdateBrand.cs:L15-35`: `FindAsync` on `Brands`, which is `rls_admin_only` (`db/patches/rls_proposal.sql:L315-341`; enabled in `_applied_rls_bc1_bc2.sql:L42`). A brand owner holds `brands.update` (`core.Infrastructure/Seeders/IdentitySeeder.cs:L515-516`) but gets 404. - A repo-wide grep shows `PrimaryColor` is assigned nowhere. - `SignupDtos.cs:L26-32`: no branding inputs at signup. - admin-web has no branding panel …
- **Observed behaviour:** - A provider cannot set a logo or colours. - A platform admin can set only a raw logo URL. There is no upload endpoint and no per-tenant asset path for logos. - No surface renders tenant branding.
- **Impact:** Step 3 of the target flow ("logo, branding") and tier T2 are not deliverable. A self-signed-up owner cannot edit even their own brand name.
- **Remediation:** 1. Add a brand-self `PUT /admin/brand/branding` that reads and writes via SECURITY DEFINER or a narrow RLS policy (`id = current_brand_id()`). It should cover name, logo, favicon and colours, with hex validation. 2. Add a logo upload that reuses `IFileStorageProvider` with the brand prefix. 3. Add a public `GET /public/branding` (Host or brandCode). 4. Have admin-web set CSS variables at runtime and have the mobile apps theme at runtime for the shared app.
- **Tests required:** owner can update their own brand and cannot update another; colour validation; the public branding endpoint resolves per brand.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-VERT-003
**No appointment-grade scheduling: no staff availability, service duration, operating-hours enforcement or double-booking protection**

- **Area / category:** Verticals/domain — Scheduling / capacity
- **Severity:** High
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed as a target-capability gap
- **Evidence:** - `database_scripts/04_bc4_order_lifecycle.sql:347-365` — `delivery_slots.slot_type IN ('pickup','delivery')`, an integer `capacity`, `UNIQUE(store_id, slot_date, slot_start, slot_type)`. There is no staff or resource dimension and no duration. - `PickupCommands.cs:399-414` — an atomic `booked_count < capacity` increment (counter only). - `phase4_salon_fulfillment_schema.sql:52-84` — `appointments(staff_member_id, scheduled_start, scheduled_end)` with only a non-unique index `idx_appointments_staff_slot` (`:72`). `resource_bookings(booked_from, booked_to)` has no `EXCLUDE` constraint. `appointments.order_id` has no FK, despite the header comment at `:10-11` claiming a composite FK. - …
- **Observed behaviour:** The platform can cap how many pickups go into a store's time window. It cannot answer "is stylist X free from 15:00 to 15:45", cannot block overlapping bookings of the same staff member or chair, and does not enforce opening hours or holidays on any booking.
- **Impact:** The salon (and any booking-based service) value proposition cannot be met. If appointment rows were written as-is, double-booking would be unprevented at both the DB and the code level.
- **Remediation:** When salon is built, use `tstzrange` with `EXCLUDE USING gist (staff_member_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&) WHERE status NOT IN ('cancelled','no_show')` (requires `btree_gist`), and the same on `resource_bookings`. Add a small availability service that composes operating hours, holidays, staff shifts and existing bookings. Add `duration_minutes` to the service catalog. Add the missing FK to `orders(id, created_at)`.
- **Tests required:** Concurrent double-booking test (two transactions, same staff, overlapping range: one must fail). Operating-hours rejection test. Holiday rejection test.
- **Specialist's dependencies / priority note:** P2 (only needed once salon is pursued; gate salon behind SA-VERT-002 until then). · **Consolidated roadmap phase:** P5
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-DB-003
**SECURITY DEFINER brand-lifecycle functions are callable by `app_user` with any brand id; `purge_brand` was explicitly meant not to be**

- **Area / category:** Database (index/idempotency/RLS) — RLS bypass / privilege
- **Severity:** Medium (originally High; QA-C: no HTTP path passes a foreign brand id and app_user can already self-set bypass (SA-TEN-007))
- **Status:** Verified
- **Independent verification:** QA-C: Confirmed – severity corrected (to Medium)
- **Related:** SA-TEN-007 — kept distinct: grant on SECURITY DEFINER purge/export functions; revoke must be sequenced with a maintenance role (RetentionSweepService needs it)
- **QA correction to the specialist text:** QA-C: revoking from app_user must be sequenced with a dedicated maintenance role (RetentionSweepService.cs:241 calls purge).
- **Evidence:** - `db/migrations/0015_brand_cancellation.up.sql:143-165` (`export_brand`, granted to app_user, "trusts its argument"), `:175-250` (`purge_brand`; `:248-250` "REVOKE ALL … FROM PUBLIC — NOT granted to app_user"). - `harden_app_user_and_rls_bypass.sql:59-67` sets `ALTER DEFAULT PRIVILEGES IN SCHEMA kernel GRANT EXECUTE ON FUNCTIONS TO app_user`. Every later kernel function therefore gets an explicit app_user grant, which `REVOKE … FROM PUBLIC` does not remove. - Live: 22 SECURITY DEFINER functions, all owned by superuser, all executable by app_user.
- **Observed behaviour:** from a brand-A session, `kernel.export_brand('B')` returned all of B's rows (7 tables). `kernel.purge_brand('B')` deleted every B row (rolled back). Neither function compares its argument with `kernel.current_brand_id()`.
- **Impact:** any SQL execution as app_user (an injection, or a single unchecked handler argument) can exfiltrate or destroy another tenant. `RetentionSweepService.cs:241` actually *depends* on this accidental grant, because it runs on the app_user connection.
- **Remediation:** 1. `REVOKE EXECUTE ON FUNCTION kernel.purge_brand(uuid) FROM app_user`, and run the purge worker on a dedicated `app_maintenance` role. 2. Inside every brand-taking SECURITY DEFINER function, add `IF NOT kernel.rls_bypass() AND p_brand_id IS DISTINCT FROM kernel.current_brand_id() THEN RAISE …`. 3. Replace `GRANT EXECUTE ON ALL FUNCTIONS` default privileges with explicit grants.
- **Tests required:** an app_user cross-brand call to each function must raise.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-MOB-005
**Customer can attach another customer's address to a pickup request (IDOR); the rider is dispatched there with that address's phone**

- **Area / category:** Mobile/delivery/maps — Authorization / IDOR / privacy.
- **Severity:** Medium
- **Status:** Verified (DB layer, QA-C T7b; HTTP not executed)
- **Independent verification:** QA-C: Confirmed; Architect: Confirmed
- **Evidence:** - `operations.Application/Orders/Pickup/Commands/PickupCommands.cs:52-103` sets `AddressId = req.AddressId` with no lookup. Neither `CustomerSchedulePickupHandler` (`:336-470`) nor the endpoint (`CustomerOrderEndpoints.cs` SchedulePickup) validates ownership. - `database_scripts/04_bc4_order_lifecycle.sql:262` declares `address_id UUID NOT NULL REFERENCES customer_addresses(id)`. - The rider task resolves the address from `pr.AddressId` (`GetMyTasksToday/*.cs:73-101`) and shows `CustomerPhone: addr?.RecipientPhone ?? c?.PhoneE164` (`RiderTaskMapper.cs:128`).
- **Observed behaviour:** With a guessed or leaked address UUID, a customer can make a pickup appear at a victim's address. The rider calls the victim (address recipient phone).
- **Impact:** Harassment/abuse vector, wrong-address dispatch, and possible cross-brand reference.
- **Remediation:** In `CustomerSchedulePickupHandler`, verify `CustomerAddresses.Any(a => a.Id == req.AddressId && a.CustomerId == cmd.CustomerId && a.BrandId == cmd.BrandId && a.DeletedAt == null)` and return 404 otherwise. Apply the same in admin create (address must belong to `req.CustomerId`).
- **Tests required:** foreign addressId → 404; own address → 201.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-OPS-010
**Backup/DR is daily logical dumps only, with no PITR, no encryption and no proven schedule**

- **Area / category:** DevOps/production — Backup / DR
- **Severity:** Medium
- **Status:** Partially Verified (scripts read; never executed here, no evidence they run anywhere)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `ops/backup/backup.sh:54-82` (`pg_dump -Fc` + `pg_dumpall --globals-only`, `pg_restore --list` integrity check, plaintext upload to S3/rclone, 14-day local prune). `ops/backup/com.laundryghar.backup.plist:32` (macOS path), `README.md:24-29` (cron as text only). `verify-backup.sh:65-73` (asserts only `tables > 0`). `restore.sh:28-78` (safe by default: new DB, typed confirmation). Per-tenant: only the export function (`BrandExportService.cs:8-27`) and no restore path.
- **Impact:** RPO is 24 h and RTO is a full logical restore. Dumps (including role password hashes in globals) are unencrypted outside the DB. Single-tenant recovery requires a full side restore plus manual copying.
- **Remediation:** use managed PG PITR as the primary mechanism (README L31-33 already suggests it). Encrypt archives. Schedule `verify-backup.sh` with row-count and RLS-policy checks. Write a brand-scoped restore runbook.
- **Tests required:** scheduled verify job with alerting on failure.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-QA-001
**InviteUser is not atomic: the user is committed before the membership guards run**

- **Area / category:** QA (security) — Privilege escalation / workflow integrity. Related area: AUTHZ.
- **Severity:** Medium
- **Status:** Verified (code read; not executed)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-AUTHZ-001
- **Evidence:** - `core.Application/Identity/AccessControl/Commands/InviteUser/InviteUser.cs:29-35` dispatches `CreateUserCommand`, then `GrantMembershipCommand`. - `CreateUser.cs:72` calls `SaveChangesAsync` itself. - `laundryghar.Utilities/CQRS/Dispatcher/Dispatcher.cs:15-29` has no transaction behaviour, and `TransactionBehavior` is never registered (`ServiceCollectionExtensions.cs:14`). - `GrantMembership.cs:144-185` throws on scope or rank violations only *after* the user row exists.
- **Observed behaviour:** `POST /api/v1/admin/access-control/invite` with a role or scope the actor may not grant returns 403. The account with the requested `user_type` and password (`Status=Active`) is nonetheless already committed. Because `user_type=platform_admin` needs no membership to receive full authority (`PermissionHandler.cs:32-33`, `ScopeResolver.cs:27-49`), GrantMembership's H2a, H2b and H2c guards give the invite path no protection at all. In the benign …
- **Impact:** it widens SA-AUTHZ-001. It also means "the membership guard protects user creation" is not true anywhere.
- **Remediation:** wrap InviteUser in `ExecuteInTransactionAsync` (or validate the grant before creating), and apply the SA-AUTHZ-001 type ceiling inside `CreateUserCommandHandler`.
- **Tests required:** an invite rejected by the grant guard leaves no user row; an invite with a forbidden `user_type` is refused before any write.
- **Specialist's dependencies / priority note:** P0 together with SA-AUTHZ-001. · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/10a-qa-verification-security.md`](docs/audit/specialists/10a-qa-verification-security.md)

### SA-QC-003
**Stale pg_partman config for `order_lifecycle.process_logs` makes `partman.run_maintenance_proc()` abort, so no partman table gets premake or retention**

- **Area / category:** QA (DB/mobile) — Database operations / data retention
- **Severity:** Medium
- **Status:** Verified on the rebuilt schema (T12). The production `part_config` state is unknown.
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-OPS-004
- **Evidence:** - `99_cross_cutting_schema_qualified.sql` registers `order_lifecycle.process_logs` with partman. - `db/patches/phase1_slice_c_laundry_fulfillment.sql:24-28,56-90` moves `process_logs` and its partitions to `laundry_fulfillment` but never updates `partman.part_config` (grep: the only part_config writer outside the base scripts is `0024…down.sql:8`). - The only scheduled entry point, `db/tools/run_partman_maintenance.sh` (and the plist), runs `CALL partman.run_maintenance_proc()`.
- **Observed behaviour:** - `CALL partman.run_maintenance_proc()` → `ERROR: Given parent table not found in system catalogs: order_lifecycle.process_logs`; a 38-day-old ping partition survived. - `SELECT partman.run_maintenance('logistics.rider_location_pings')` dropped it. - `part_config` lists `order_lifecycle.process_logs` with 0 children.
- **Impact:** - Even after a scheduler is added (SA-OPS-004), every run fails. So: - no new monthly partitions for orders, audit_logs, notifications_log and decision_log (rows fall into the DEFAULT partitions); - no 14-day deletion of rider GPS history (DPDP, SA-MOB-012). - The failure is silent unless the cron output is monitored.
- **Remediation:** a migration that does `UPDATE partman.part_config SET parent_table='laundry_fulfillment.process_logs' WHERE parent_table='order_lifecycle.process_logs'` (or `undo`/re-`create_parent`). Add a CI/startup assertion that every `part_config.parent_table` resolves, and call maintenance per table so one bad row cannot block the rest.
- **Tests required:** an integration test that runs `run_maintenance_proc()` on the migrated schema, and a retention test that drops a partition older than 14 days.
- **Specialist's dependencies / priority note:** P1 (with SA-OPS-004). · **Consolidated roadmap phase:** P0
- **Source:** [`docs/audit/specialists/10c-qa-verification-db-mobile.md`](docs/audit/specialists/10c-qa-verification-db-mobile.md)

### SA-API-003
**Validation pipeline not wired: 40 FluentValidation validators never execute; CQRS behaviors are dead code**

- **Area / category:** Backend/API — Input validation / architecture
- **Severity:** Medium (originally High; QA-B: DB CHECKs, request-size limits and attachment-only serving bound impact; 39 not 40 validators dead. QA-A kept High — dissent recorded)
- **Status:** Verified (code read + type-set diff)
- **Independent verification:** QA-A: Confirmed; QA-B: Confirmed – severity corrected to Medium (canonical, G4)
- **Duplicates (same defect, other reports):** SA-ARCH-005, SA-SOLID-005
- **QA correction to the specialist text:** QA-B: 39 validators are dead, not 40 (PartnerBookingLocation runs via SetValidator).
- **Evidence:** `laundryghar.Utilities/CQRS/Extensions/ServiceCollectionExtensions.cs:14` registers only `Dispatcher`; `CQRS/Dispatcher/Dispatcher.cs:15-45` calls handlers directly; `CQRS/Registration/BehaviorRegistrar.cs:13-25` has no callers. The only validation path is `Validation/ValidationFilter.cs:13-52`. Orphaned validators include `CreateOrderValidator` (`operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:886-925`), `CustomerSchedulePickupValidator` (`Pickup/Commands/PickupCommands.cs:773-814`), `UpdateOrderStatusValidator` (`UpdateOrderStatusCommand.cs:160-175`), `UploadInspectionPhotoValidator` (`UploadInspectionPhoto.cs:84-115`, MIME allowlist + 10 MB), rider document validator …
- **Observed behaviour:** rules such as "Quantity > 0", "channel in list", "≤ 50 cart items", "Amount > 0", "image/jpeg|png|webp only" and "≤ 5 MB KYC" are never enforced on these paths. DB CHECKs backstop some of them (for example `order_items.quantity > 0`, `database_scripts/04_bc4_order_lifecycle.sql:154`), returning 422 through `ExceptionHandler`, but many have no backstop: cart size, upload MIME and size limits for KYC, string lengths stored in jsonb.
- **Impact:** unbounded payloads, wrong data stored, and security-relevant upload restrictions missing. Existing unit tests that exercise validators directly give false assurance.
- **Remediation:** register `ValidationBehavior` (call `RegisterBehaviors`, or wire just validation into `Dispatcher`), and add an architecture test that fails when a validator's target type is never validated. Remove or rename dead behaviors.
- **Tests required:** a reflection test that every `AbstractValidator<T>` is reachable; endpoint tests for the cases above.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-015
**Watermark cursors can skip events permanently (notifications, loyalty earn)**

- **Area / category:** Backend/API — Event processing correctness
- **Severity:** Medium
- **Status:** Verified (code read; defect acknowledged in-code)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `NotificationMappingService.cs:119-178` (cursor `(occurred_at,id)`, where `occurred_at` is the application's `UtcNow` taken at handler start, not commit time; a failed event is skipped at `:164-171`); `LoyaltyEarnService.cs:95-115` (strict `OccurredAt >` without a tiebreak); `PartnerBookingDebitService.cs:20-33` documents both defects and fixes them only for the partner path.
- **Observed behaviour:** an event whose transaction commits after a later-stamped event has already been consumed is never seen. Loyalty additionally drops events that tie on `OccurredAt` beyond the batch.
- **Impact:** silently missing order and refund notifications and missing loyalty points under concurrent load.
- **Remediation:** move both consumers to the `outbox_consumed_events` inbox pattern already used by `PartnerBookingDebitService`.
- **Tests required:** an interleaved-commit test where an event committed late is still processed.
- **Specialist's dependencies / priority note:** P2. Related area: DB. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-016
**Unbounded `pageSize` on list endpoints**

- **Area / category:** Backend/API — Performance / DoS
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-A: Confirmed
- **Evidence:** `laundryghar.Utilities/Common/PaginatedList.cs:11-42` (no maximum); endpoints only clamp the lower bound, for example `AdminOrderEndpoints.cs:52`, `CustomerOrderEndpoints.cs:74,221` and `WarehouseInspections.cs` `GetAll`. The only caps are in `commerce.WebApi/Endpoints/Analytics/AnalyticsAdmin.cs:75,89`.
- **Observed behaviour:** `?pageSize=1000000` is passed through to `Take()`.
- **Impact:** memory and DB pressure; one tenant's request can degrade the shared database for every tenant.
- **Remediation:** clamp in `PaginatedList.CreateAsync` (for example to a maximum of 200) and document it in OpenAPI.
- **Tests required:** `pageSize=10000` returns at most 200 items.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-018
**Customer app does not send an idempotency key for booking, so the server guard is unused**

- **Area / category:** Backend/API — Idempotency (client/server contract)
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-MOB-018
- **Evidence:** `customer-mobile/src/api/orders.ts:128-136` (no header); `customer-mobile/app/(app)/booking/pay.tsx:390-404` (no `idempotencyKey` in the body); server support at `CustomerOrderEndpoints.cs:192-194` and `PickupCommands.cs:350-358,464-492`.
- **Observed behaviour:** retries (timeout or 401-refresh-retry) create duplicate pickup requests and consume an extra slot unit each.
- **Impact:** duplicate bookings and slot exhaustion.
- **Remediation:** generate a UUID per checkout attempt in the client and send it as `Idempotency-Key`.
- **Tests required:** client unit test that the header is present; server test already exists for the key path.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-019
**Admin/POS-created pickups ignore slot capacity, but rejection releases capacity**

- **Area / category:** Backend/API — Double booking / data integrity
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `PickupCommands.cs:38-50` (admin path calls `CreatePickup` directly, with no `booked_count` increment and no slot-brand check), `:104` (`PickupSlotId = req.SlotId`), `:657-672` (`RejectPickup` decrements `booked_count` for any pickup with a slot).
- **Observed behaviour:** slots can be overbooked through the admin path, and rejecting those pickups decrements capacity that was never taken, which then lets customers overbook as well.
- **Impact:** double booking and capacity drift.
- **Remediation:** route admin creation through the same atomic slot-increment transaction as the customer path.
- **Tests required:** admin booking on a full slot is rejected; booked count stays consistent after reject.
- **Specialist's dependencies / priority note:** P2. Related area: DB. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-020
**Refresh-token rotation is not atomic**

- **Area / category:** Backend/API — Auth / concurrency
- **Severity:** Medium
- **Status:** Verified (code read); race Not Tested
- **Independent verification:** QA-A: Confirmed
- **Evidence:** `core.Application/Identity/Auth/Commands/RefreshToken/RefreshTokenHandler.cs:43-112` (read, then set `RevokedAt` in memory, insert child, `SaveChanges`; no `WHERE revoked_at IS NULL` condition or concurrency token).
- **Observed behaviour:** two concurrent refreshes with the same token both succeed, leaving two live child tokens and defeating single-use rotation. Conversely, sequential near-simultaneous refreshes from two tabs trigger reuse detection and revoke the family, causing a forced logout. This combines with SA-API-001's shared bucket.
- **Impact:** weakened theft detection and spurious logouts.
- **Remediation:** `UPDATE refresh_tokens SET revoked_at=now() WHERE id=@id AND revoked_at IS NULL` and treat 0 rows as reuse. Optionally add a short grace window that returns the already-minted child.
- **Tests required:** parallel refresh test produces exactly one success.
- **Specialist's dependencies / priority note:** P2. Related area: SEC. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-ARCH-006
**Outbox is a DB-polling integration with no broker, no typed contracts, and inconsistent consumer semantics**

- **Area / category:** Architecture — Integration / reliability
- **Severity:** Medium
- **Status:** Partially Verified (code read; event skipping is Suspected — not reproduced)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - Relay publishes to a logging stub unconditionally in all environments: `commerce.WebApi/Program.cs:289-290`; `Worker/Stubs/LoggingEventPublisher.cs:7-10`. - Consumers poll `outbox_events` directly with different cursor semantics: - `LoyaltyEarnService.cs:90-112` — strict `OccurredAt > lastOccurredAt` watermark, no id tie-break. - `NotificationMappingService.cs:119-137` — `(OccurredAt, Id)` watermark. - `PartnerBookingDebitService.cs:100-112` — inbox anti-join on `outbox_consumed_events`; its own comment explains that a time watermark can step over events that commit out of `OccurredAt` order. - Producers stamp `OccurredAt = now` before commit (e.g. `CreateQualityCheck.cs:122`), so commit …
- **Observed behaviour:** Integration between contexts is "shared table + polling" with three different correctness models.
- **Impact:** Loyalty credits (and to a lesser degree notifications) can be silently skipped when two transactions interleave; adding vertical-specific consumers would copy whichever pattern is nearest. No path to an external broker without per-consumer rework despite the ADR claim.
- **Remediation:** Standardise every consumer on the inbox pattern already proven in `PartnerBookingDebitService`; introduce a small `OutboxEventTypes` + payload record catalogue in a shared contracts namespace; either remove the no-op relay or make it explicitly "mark-as-relayed" with a TODO for a broker. No broker needed now.
- **Tests required:** Concurrency test inserting two events with inverted commit order, asserting each consumer processes both.
- **Specialist's dependencies / priority note:** P1 for loyalty (money-adjacent), P2 otherwise. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/01-architecture.md`](docs/audit/specialists/01-architecture.md)

### SA-ARCH-010
**Commerce (payments, wallets, subscriptions, billing workers) has no test project**

- **Area / category:** Architecture — Testability
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed
- **Evidence:** Test csproj references (`tests/core.Tests/core.Tests.csproj:19-22`, `tests/operations.Tests/operations.Tests.csproj:20-26`, `tests/operations.IntegrationTests/operations.IntegrationTests.csproj:31-36`) — none references `commerce.Application` or `commerce.Infrastructure` (96 source files incl. `RazorpayWebhookHandler.cs`, `SubscriptionBillingService.cs` 551 lines, wallet handlers).
- **Impact:** The money-moving bounded context — and the one hosting all workers — has no automated regression net; refactoring towards modules (SA-ARCH-001/006/007) is high-risk there.
- **Remediation:** Add `commerce.Tests` mirroring `operations.Tests` (EF InMemory or Testcontainers) starting with webhook idempotency, wallet balance mutations and the outbox consumers.
- **Tests required:** as above.
- **Specialist's dependencies / priority note:** P1 (before touching commerce structure). · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/01-architecture.md`](docs/audit/specialists/01-architecture.md)

### SA-AUTHZ-007
**Scope check is decoupled from permission source (permission union × node union = scope amplification)**

- **Area / category:** Authorization (RBAC/ABAC) — RBAC/ABAC design flaw
- **Severity:** Medium
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** `ScopeResolver.cs:117-161` unions permissions from all ancestor-or-self memberships of the active node. `ScopeResolver.cs:168-170` emits all membership nodes as `scope_nodes`. `IsWithinScope` (`HttpContextCurrentUser.cs:107-120`) and `kernel.within_scope_cols` (`0031…up.sql:85-108`) pass if any node covers the resource.
- **Observed behaviour:** a user who holds `store_admin` at S1 and any low-privilege role at brand level (for example a read-only role granted from the person drawer) gets store_admin's write permissions with brand-wide reach.
- **Impact:** least privilege fails for multi-membership users. Each membership's permissions should be bounded by that membership's node.
- **Remediation:** evaluate `(permission, node)` pairs. Emit per-node permission sets, or in `IsWithinScope` require that the node granting the specific permission covers the resource. The ABAC engine (`authz.within_scope`) is the natural home for this once enabled.
- **Tests required:** store role at S1 + brand read-only role → write to S2 refused.
- **Specialist's dependencies / priority note:** P1 · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-AUTHZ-009
**User suspension/deactivation does not revoke live sessions; revocation check fails open**

- **Area / category:** Authorization (RBAC/ABAC) — Session/permission revocation
- **Severity:** Medium
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** - `SetPersonStatus.cs:48-68` and `DeactivateUser.cs:14-23` set `Status=Suspended` but neither bumps `PermVersion` nor revokes refresh tokens. `SetUserType.cs:75` shows the intended pattern. - Refresh does check status (`RefreshTokenHandler.cs:67-69`), so exposure equals the access-token lifetime: 15 min (`appsettings.json:21`). - `TokenVersionStore.cs:41-45` returns null on any error, and `TenantResolutionMiddleware.cs:61-71` treats null as pass (fail-open). Cache TTL is 15 s per process. - `EnforceTokenVersion` applies only to `token_use=user`. Partner and customer tokens have no live revocation.
- **Impact:** a fired or compromised employee keeps full access for up to 15 minutes after suspension. A DB hiccup silently disables revocation.
- **Remediation:** bump `PermVersion` and revoke the refresh-token family in both suspend paths. Make the version check fail closed for high/critical permissions, or at least log and alert.
- **Tests required:** suspend user → next request with the old token → 401 within the TTL.
- **Specialist's dependencies / priority note:** P1 · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-DB-009
**Background workers claim rows without a guarded update or SKIP LOCKED: duplicate publish/charge across replicas, stuck claims**

- **Area / category:** Database (index/idempotency/RLS) — Idempotency / jobs
- **Severity:** Medium
- **Status:** Verified (outbox claim race reproduced); Partially Verified (dispatcher, billing)
- **Independent verification:** QA-C: Confirmed (additional defect)
- **Related:** SA-OPS-005 — kept distinct: outbox relay republishes even without a race (QA-C additional defect)
- **Evidence:** - `OutboxEventRelayService.cs:102-118`: SELECT, then `UPDATE … WHERE id` (EF, no status predicate). - `NotificationDispatcherService.cs:113-130`: same. - `SubscriptionBillingService.cs:353-383`: charge before the attempt row. - No `FOR UPDATE SKIP LOCKED` or advisory lock anywhere (grep). - No reaper for `publishing`/`sending`.
- **Observed behaviour:** session A and session B both read `pending` and both got `UPDATE 1`, so both would publish.
- **Impact:** today this is safe only while exactly one commerce replica runs. Horizontal scaling causes duplicate events, notifications and possibly mandate debits. A crash between claim and outcome strands rows permanently.
- **Remediation:** claim with `UPDATE … SET status='publishing', claimed_at=now() WHERE id IN (SELECT id … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`; add a lease timeout; insert the billing attempt in `initiated` state before charging; or take `pg_try_advisory_lock` per job.
- **Tests required:** two-worker relay test.
- **Specialist's dependencies / priority note:** P2 (P1 before scaling out). · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-010
**Coupon usage limits enforced only in the application**

- **Area / category:** Database (index/idempotency/RLS) — Idempotency / invariants
- **Severity:** Medium
- **Status:** Partially Verified (code + schema; no DB guard exists to test)
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** `CustomerCouponHandlers.cs:85-142`; `CreateOrderCommand.cs:292-311, 770-771`; live `coupon_redemptions` has no unique beyond the PK.
- **Impact:** concurrent redemptions exceed `max_total_uses` and `max_uses_per_customer`, and "one coupon per order" can be broken.
- **Remediation:** guarded `UPDATE commerce.coupons SET current_usage_count = current_usage_count + 1 WHERE id=@id AND (max_total_uses IS NULL OR current_usage_count < max_total_uses)` (rows==1), plus partial UNIQUE `(order_id) WHERE reverted_at IS NULL`. For single-use, add UNIQUE `(coupon_id, customer_id) WHERE reverted_at IS NULL`, applicable when `is_single_use_per_cust`; this needs a trigger or a separate table.
- **Tests required:** parallel redemption.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-014
**The ABAC store's raw connections run without tenant GUCs: brand policies, roles and entitlements are invisible, and decision-log writes are rejected**

- **Area / category:** Database (index/idempotency/RLS) — RLS × ABAC integration
- **Severity:** Medium
- **Status:** Verified (DB)
- **Independent verification:** QA-C: Confirmed
- **Evidence:** - `AbacServiceCollectionExtensions.cs:33-37` uses a separate `NpgsqlDataSource` on the same app_user connection string. - `NpgsqlAbacStore.cs:42-48` claims it "bypasses RLS deliberately … the role has SELECT only on these two tables"; both statements are false. - `:238-253` (roles / brand_feature), `:325-332`. - `DecisionLogWriter.cs:125-140` uses binary `COPY authz.decision_log`. - `0024_authz_abac_foundation.up.sql:172-182` enables RLS on decision_log.
- **Observed behaviour:** with no GUCs, 58 platform policies were visible and 0 brand policies; the subject query returned `role_codes={}` and `entitlements={}` while the true values were `{brand_owner_b}` and `{bookings}`. COPY gave `ERROR: COPY FROM not supported with row-level security`.
- **Impact:** - ABAC defaults to shadow/disabled (`AbacOptions.cs:15-18`), so today the parity data is wrong and every decision-log flush fails. - After an enforce cutover, brand policies would never apply. - `subject.roles contains X` deny rules would evaluate against an empty set and fail open.
- **Remediation:** run these reads under `SET LOCAL app.bypass_rls='true'` in a transaction, or via SECURITY DEFINER read functions. Replace COPY with batched INSERT or a SECURITY DEFINER writer.
- **Tests required:** an integration test against app_user (not superuser).
- **Specialist's dependencies / priority note:** P2 (P1 before any ABAC enforce). · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-018
**69 FKs without supporting indexes; 55 redundant indexes**

- **Area / category:** Database (index/idempotency/RLS) — Indexing
- **Severity:** Medium
- **Status:** Verified (catalog)
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** scratch `fk_noindex.txt`, `redundant_idx.txt` (see I30/I31).
- **Impact:** `purge_brand`, customer erasure and parent deletes seq-scan child tables. Duplicate indexes inflate write cost on orders, payments and fulfillment_unit.
- **Remediation:** add FK indexes on high-churn children with `CREATE INDEX CONCURRENTLY` (migration flagged `-- migrate: no-transaction`). Drop redundant indexes after confirming production `pg_stat_user_indexes`.
- **Tests required:** none (catalog check in CI).
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-FE-002
**admin-web logout does not end the session after any page reload**

- **Area / category:** Frontend/clients — Session management / Security (Related area: AUTH)
- **Severity:** Medium (originally High; QA-B: requires local device access)
- **Status:** Partially Verified (full code path traced client and server; not executed)
- **Independent verification:** QA-B: Confirmed – severity corrected to Medium
- **Evidence:** - `admin-web/src/stores/authStore.ts:75-85`: the refresh token is deliberately not persisted, so it is `null` after a reload. - `admin-web/src/components/layout/Topbar.tsx:43-53`: `if (refreshToken) await logout(refreshToken)`, so the `/logout` call is skipped when the in-memory token is gone. - `core.WebApi/Endpoints/Identity/Auth.cs:185-210`: the server logout would clear the cookie even without a token, but the client never calls it. Even when it is called, the cookie is scoped to the refresh path only (`Auth.cs:51`), so `/logout` cannot read it to revoke the token family. - `admin-web/src/components/layout/ProtectedRoute.tsx:42-67`: when the access token is missing, the app silently …
- **Observed behaviour:** an admin logs in, reloads the tab (or opens a new one) and clicks Logout. The local state is cleared and they land on `/login`. Visiting `/` then triggers the cookie refresh, which succeeds, so they are signed in again without credentials. The refresh family stays valid until it expires.
- **Impact:** on shared or counter computers, logout is ineffective. Combined with SameSite=Strict this is not cross-site exploitable, but the next person at the machine gets the previous admin's session (which could be a `platform_admin`).
- **Remediation:** always `POST /logout` with `withCredentials: true`, without the `if`. Widen the cookie path to `/identity/api/v1/auth` (refresh and logout), or add a cookie-reading `/auth/refresh/logout` under the same path, so the server can revoke the family and not just delete the cookie.
- **Tests required:** Playwright: login, reload, logout, goto `/`, expect `/login`. Backend: an integration test that logout via cookie revokes the family, so a later refresh returns 401.
- **Specialist's dependencies / priority note:** P0/P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-FE-003
**pos-web stores the refresh token in localStorage, and its refresh path ignores the HttpOnly cookie**

- **Area / category:** Frontend/clients — Token storage security (Related area: SEC)
- **Severity:** Medium (originally High; QA-B: requires XSS; pos-web not deployed)
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed – severity corrected to Medium
- **Evidence:** - `pos-web/src/stores/authStore.ts:64-82`: `persist(...)` with `name: 'lg-pos-auth'` and `localStorage`, and no `partialize`, so `accessToken`, `refreshToken` and `user` are all written to disk. - `pos-web/src/api/client.ts:136-144`: the 401 interceptor throws `'No refresh token available'` unless the body token exists, and does not send `withCredentials`. - Compare `admin-web/src/stores/authStore.ts:77-85`, which explicitly avoids this pattern for XSS reasons.
- **Observed behaviour:** any XSS on the POS origin (or a browser extension, or someone at a shared counter tablet) can read a long-lived refresh token and mint access tokens until it expires or is rotated.
- **Impact:** counter tablets are exactly the shared, poorly managed devices where this risk is highest. The two staff web apps also behave inconsistently.
- **Remediation:** mirror admin-web: add `partialize` that excludes `refreshToken`, and switch the interceptor refresh to `refreshAccessToken()` (which uses the cookie, `client.ts:179-191`).
- **Tests required:** a unit test that the persisted `lg-pos-auth` JSON has no `refreshToken`; an e2e that a hard reload still refreshes through the cookie.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-FE-005
**Brand switching and logout do not scope or clear the client cache, so data from one brand shows under another**

- **Area / category:** Frontend/clients — Tenant isolation (client), data correctness
- **Severity:** Medium
- **Status:** Verified (code); not executed
- **Independent verification:** QA-B: Confirmed
- **Evidence:** - `admin-web/src/components/layout/BrandSwitcher.tsx:61-67`: `setActiveBrand` only, with no `queryClient` invalidation. - `hooks/useOrders.ts:17-24` and `hooks/useAnalytics.ts:15-23`: query keys without a brand. Catalog keys likewise (`hooks/useCatalog.ts:104-166`). - `App.tsx:46-52`: `refetchOnWindowFocus: false`, `staleTime: 30_000`. - `hooks/useNavigator.ts:9`: key `['navigator', accessToken]`, but the server navigator varies by `X-Brand-Id` (`GetNavigator.cs:37-45`, `HttpContextCurrentUser.cs:131-137`). - `Topbar.tsx:49-51`: logout clears the stores but not the `QueryClient`, and `api/client.ts:261-262` (forced logout) clears auth but not `lg-admin-brand`. - …
- **Observed behaviour:** - A platform admin on `/orders` who switches from brand A to brand B keeps seeing A's orders, because the key is unchanged and nothing triggers a refetch. Mutations then go out with `X-Brand-Id: B` against A's ids. - The sidebar keeps A's vertical modules (e.g. laundry "Fabrics" on a salon brand) for up to 5 minutes. - After a forced logout, the next user's terminology can come from the previous user's stored brand. - (Only some hooks include …
- **Impact:** operators act on the wrong tenant's data. The server is still authoritative (RLS / brand filters), so this is a confusion and integrity risk, not a direct leak.
- **Remediation:** include `useEffectiveBrandId()` in every brand-scoped query key, or call `queryClient.clear()` in `setActiveBrand` and on every logout path; key the navigator on `[accessToken, brandId]`; call `clearBrand()` in the 401 forced-logout path.
- **Tests required:** a hook test that switching brand changes the keys or clears the cache; an e2e that switches brand and asserts the order list re-fetches with the new header.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-FE-010
**Customer booking falls back to hardcoded demo garments and prices in production**

- **Area / category:** Frontend/clients — Mock data in production path
- **Severity:** Medium
- **Status:** Verified (client); whether the server accepts such a request is Not Tested
- **Independent verification:** QA-B: Confirmed
- **Evidence:** `customer-mobile/app/(app)/booking/items.tsx:27,158-184`: `if (live.length > 0) return live; return DEMO_ITEMS.map(...)`, not gated by `__DEV__`. `src/data/demoItems.ts:13-21` (Shirt ₹170 … Coat ₹480). `app/(app)/booking/pay.tsx:376-386` then submits `itemId: null` and `estimatedUnitPrice` set to the demo price.
- **Observed behaviour:** when a brand's price list is empty, or the request fails (`priceList` is undefined), the customer is shown fabricated laundry items and prices and can schedule a pickup with them. For any non-laundry brand this always shows laundry garments.
- **Impact:** a misleading price promise and wrong data in pickup requests.
- **Remediation:** gate the fallback with `__DEV__`; in production render `ErrorState` / `EmptyState` with retry.
- **Tests required:** a component test for an empty price list in production mode, which should show the empty state with no demo items.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-FE-011
**Client quality gates: mobile CI is red, pos-web is outside CI/CD, web apps have no unit tests**

- **Area / category:** Frontend/clients — Testing / CI (Related area: OPS, QA)
- **Severity:** Medium
- **Status:** Verified (reproduced by QA-B: npm ci ERESOLVE and TS2882)
- **Independent verification:** QA-B: Confirmed – reproduced
- **Duplicates (same defect, other reports):** SA-MOB-017
- **Evidence:** - `rider-mobile` `npm ci` gives ERESOLVE (`react-dom@19.2.8` vs `react@19.2.3`, lockfile `rider-mobile/package-lock.json:14602-14607`). - customer and rider `tsc --noEmit` give TS2882 at `app/_layout.tsx:11`. `tsconfig.json` includes `expo-env.d.ts`, but that file is gitignored (`customer-mobile/.gitignore:5`) and only generated by `expo start`. - CI run 36294076412 on `main`: both mobile jobs failed. - `.github/workflows/ci.yml:47-86` covers admin-web lint and build plus the mobile jobs; pos-web is absent from ci.yml, release.yml and docker-compose. - No `*.test.*` files exist in admin-web or pos-web. `admin-web/e2e/saas-billing.mjs` is a single runner-less Playwright script that needs a …
- **Impact:** regressions in POS and admin UI logic (the SA-FE-004 class of bug) cannot be caught. Mobile PRs merge on a red pipeline.
- **Remediation:** regenerate the rider lockfile (or align `react`/`react-dom`); commit a stub `expo-env.d.ts` (`/// <reference types="expo/types" />`) or add a `*.css` module declaration; add a pos-web job (lint, build) and a Dockerfile; add Vitest plus React Testing Library to the web apps, starting with `orderStatus`/`allowedTransitions`, `routePermissions` and the client interceptors.
- **Tests required:** as above.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-MOB-008
**Rider offline queue treats server rejections as "offline", poisons itself, and drops failure reasons; location pings are not queued**

- **Area / category:** Mobile/delivery/maps — Mobile resilience / data integrity.
- **Severity:** Medium
- **Status:** Verified (code).
- **Independent verification:** QA-C: Confirmed
- **Evidence:** - `rider-mobile/app/(app)/tasks/[id].tsx:360-406`: any error, including `ApiError("Incorrect OTP.")` (400 from `VerifyTaskOtp.cs:59`), goes to `enqueue({status:'completed'})` with the message "No connection right now". The same pattern appears in `markArrived` (`:346-355`). - `rider-mobile/src/hooks/useOfflineQueueFlush.ts:42-58` replays a `failed` item via `updateTaskStatus` (reason/note discarded) and `break`s on the first error, so a permanently rejected item blocks every later item forever. - `rider-mobile/src/lib/backgroundLocation.ts:49-51` and `sendCurrentLocation.ts:43-45` drop pings when offline.
- **Observed behaviour:** A wrong OTP shows "offline" and queues a completion that the server will always reject (once OTPs exist). After that, every later queued update stalls. Failure reasons are lost.
- **Impact:** Silent data loss, riders believing tasks are complete when they are not, and gaps in tracking history.
- **Remediation:** - Enqueue only on network or 5xx errors (`!error.response || status >= 500`). - On 4xx, show the server message and drop or mark the item failed. - Replay `failed` via `failTaskStatus`. - Continue past permanently failed items. - Optionally buffer the last N pings.
- **Tests required:** jest: 400 not enqueued; poison item skipped; failed replay carries reason.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-009
**Background location task may run without hydrated auth and trigger `logout()`, wiping stored tokens**

- **Area / category:** Mobile/delivery/maps — Mobile auth / background execution.
- **Severity:** Medium
- **Status:** Suspected (device behaviour not observed).
- **Independent verification:** QA-C: Not verifiable here
- **Evidence:** - `rider-mobile/src/lib/backgroundLocation.ts:30-52`: the task posts through `logisticsClient`. - `rider-mobile/src/api/client.ts:136-186`: no token means no Authorization header, then 401, then `_getRefreshToken()` returns null, then `throw`, then `_onAuthFailure()`. - `rider-mobile/src/store/authStore.ts:54-70,84-89`: `logout()` clears state and deletes the SecureStore tokens. - Tokens are hydrated only inside the root component effect (`rider-mobile/app/_layout.tsx:268-279`, `void hydrate()` at `:275`), which does not run when the OS launches JS headlessly for a background location event.
- **Observed behaviour:** After the OS kills the app during a shift, the next background location delivery can sign the rider out silently.
- **Impact:** Riders silently logged out mid-shift, and tracking stops.
- **Remediation:** In the task, read tokens directly from SecureStore if the store is not hydrated, and never call `onAuthFailure` from a headless context (skip the ping on 401).
- **Tests required:** jest unit with an unhydrated store → no logout.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-010
**Location ping ingestion is unvalidated and not gated by duty/assignment; client timestamps drive staleness and partition routing**

- **Area / category:** Mobile/delivery/maps — Input validation / privacy / integrity.
- **Severity:** Medium
- **Status:** Verified (code).
- **Independent verification:** QA-C: Confirmed
- **Evidence:** - `operations.Application/Logistics/RiderSelf/Dtos/RiderSelfDtos.cs:6-16` has no validators (no validator exists for `LocationPingInput`). - `BatchLocationPing.cs:42-47` resolves the rider without checking `IsOnDuty`/`Status`, takes `PingedAt = p.PingedAt` (client time) at `:56`, and sets `LastPingAt = latest.PingedAt` at `:87`. - `RiderSelfEndpoints.cs:126-145` accepts an unbounded `List<LocationPingInput>`. - `GeofenceEvaluator.cs:24-25` uses a 150 m radius with no accuracy filter.
- **Observed behaviour:** - Lat/lng out of range or NaN reach NTS/PostGIS, which likely surfaces as a 500. - Future or past timestamps fall into the DEFAULT partition or fail when no partition exists. - A device with a skewed clock appears fresh or stale on the admin board. - Off-duty and terminated riders' locations are stored. - Large batches cause heavy inserts. - Spoofed coordinates auto-flip legs to `arrived`.
- **Impact:** Privacy (tracking outside shift), DoS surface, misleading live board, and geofence manipulation.
- **Remediation:** - Add a FluentValidation validator: lat ∈ [-90,90], lng ∈ [-180,180], batch ≤ 50, `PingedAt` within [now-15m, now+2m] (else clamp to server time), accuracy ≤ 200 m for geofence evaluation. - Reject or ignore pings when `!IsOnDuty || Status != active`. - Store server receive time in `created_at` and use it for staleness.
- **Tests required:** validator unit tests; off-duty ping → 204/ignored.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-011
**Deactivating a rider does not stop tracking, task access or open legs**

- **Area / category:** Mobile/delivery/maps — Authorization lifecycle.
- **Severity:** Medium
- **Status:** Verified — scope corrected by QA-C: deactivate path is a false positive (soft-delete filter); gap exists only for UpdateRider suspend/terminate
- **Independent verification:** QA-C: Confirmed – evidence corrected
- **QA correction to the specialist text:** QA-C: DeactivateRider soft-deletes and the global filter blocks rider self-service; the gap is only via UpdateRider suspend/terminate.
- **Evidence:** - `operations.Application/Logistics/Riders/Commands/DeactivateRider/*.cs:40` sets only `rider.Status = Terminated`, leaving `IsOnDuty` and open legs untouched. - `laundryghar.Utilities/Auth/RiderOnlyRequirement.cs` checks claims only (`token_use`, `user_type`). - Rider-self handlers resolve by `UserId+BrandId` with no status filter (`BatchLocationPing.cs:43-46`, `UpdateMyTaskStatus.cs:44-47`).
- **Observed behaviour:** Until the access token expires (and longer, if refresh is not revoked), a terminated rider can ping, view tasks and complete legs with COD side-effects.
- **Impact:** Ex-partner retains operational access and customer PII.
- **Remediation:** - On deactivate: set `IsOnDuty=false`, cancel or flag open legs for reassignment, and revoke refresh tokens. - Add `r.Status == "active"` (or not terminated) to the rider self-resolve in all rider-self handlers via one shared helper.
- **Tests required:** terminated rider → 403/404 on ping and status.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-OPS-005
**Background jobs assume a single commerce instance; two notification lanes can double-send or wedge**

- **Area / category:** DevOps/production — Scaling / reliability
- **Severity:** Medium (originally High; QA-B: documented topology is single-instance; becomes P0/High before scale-out)
- **Status:** Partially Verified (code read; concurrency not reproduced)
- **Independent verification:** QA-B: Confirmed – severity corrected to Medium (canonical, G12)
- **Duplicates (same defect, other reports):** SA-API-014, SA-ARCH-007
- **Evidence:** `commerce.WebApi/Program.cs:298-322`: all workers are registered in the API host. `OutboxEventRelayService.cs:101-120` and `NotificationDispatcherService.cs:115-131` claim rows by `SELECT ... WHERE status IN (pending, failed)` then `SaveChanges(status = publishing/sending)` in a READ COMMITTED transaction, with no row lock and no concurrency token. A grep for `IsConcurrencyToken|IsRowVersion|xmin` over the backend found 0 hits, so EF's UPDATE is keyed by id only. The comments ("prevent concurrent workers", "guard against concurrent workers") are not borne out by the SQL. No code path resets `sending` or `publishing` (grep).
- **Observed behaviour:** with two commerce replicas, both can claim the same row and both send. A crash between claim and outcome leaves the row stuck in `sending`/`publishing` forever, so the message is never delivered.
- **Impact:** duplicate customer WhatsApp/SMS/push messages (cost and spam) and silent loss after a crash. Scaling the commerce API for load also multiplies job runners.
- **Remediation:** claim with `UPDATE ... SET status='sending', locked_until=now()+interval 'N min' WHERE id IN (SELECT id ... FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`, and reclaim rows whose lease has expired. Move workers into a separate `worker` compose service pinned to one replica, using the same image with a flag.
- **Tests required:** Testcontainers test running two dispatchers concurrently and asserting each row is sent once, plus a test that a stale lease is reclaimed.
- **Specialist's dependencies / priority note:** P1 (P0 before any multi-replica deployment). Related area: DB. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-007
**Migrations are a manual step; CI never applies or rolls them back**

- **Area / category:** DevOps/production — Database change management
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed
- **Related:** SA-DB-002
- **Evidence:** `ci.yml:88-111` (pairing and duplicate-number lint only). `deploy/README.md:57-59` (operator runs `migrate.sh up`). `db/tools/migrate.sh:138-170`: up/down in one transaction per file with a checksum. `no-transaction` files are not atomic (L83-93). There is no `pg_advisory_lock` around runs. Integration tests use `postgres:16-alpine` (`operations.IntegrationTests/*.cs`, e.g. `Phase1SqlMigrationTests.cs:24`), while production compose and the backup verifier use `postgres:18` (`docker-compose.yml:119`, `verify-backup.sh:23`).
- **Observed behaviour:** `.down.sql` files are never executed by automation. The deploy order (migrate, then roll images) is left to a human. Nothing checks that EF and the schema agree at deploy time.
- **Impact:** untested rollbacks, version skew between the test and production engines, and concurrent operator runs racing each other.
- **Remediation:** add a CI job on PG18 with partman and postgis that runs `build_from_scratch.sh` → `migrate.sh up` → `migrate.sh down all` → `migrate.sh up` → `verify`. Wrap `cmd_up`/`cmd_down` in `pg_advisory_lock`. Add an expand/contract convention for backward-compatible deploys.
- **Tests required:** the CI job itself.
- **Specialist's dependencies / priority note:** P1. Related area: DB. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-008
**Logs, traces and metrics are not tenant-aware, and production exports nothing**

- **Area / category:** DevOps/production — Observability
- **Severity:** Medium
- **Status:** Verified (code and config read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-TEN-014
- **Evidence:** `laundryghar.ServiceDefaults/Extensions.cs:119-170`: OTel registers instrumentation but exports only if `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and `deploy/docker-compose.yml` sets no `OTEL_*`. No EF Core or Npgsql OTel instrumentation (csproj package list). A grep for `BeginScope|SetTag|AddTag|Baggage|LogContext|Serilog` finds no tenant enrichment. `laundryghar.Utilities/CQRS/Context/CorrelationContext.cs:9-19` generates a random GUID unrelated to the trace id. No alerting configuration anywhere in `deploy/`, `ops/` or `.github/`.
- **Observed behaviour:** in production the only signals are stdout logs and, if a DSN is set, Sentry errors. Neither can be filtered by brand.
- **Impact:** no per-tenant SLOs, no way to isolate a noisy or failing tenant, and no paging on outages.
- **Remediation:** add one middleware after `TenantResolutionMiddleware` that opens a log scope `{brand_id, user_id}` and tags `Activity.Current` with `tenant.brand_id` (also on worker scopes). Set the OTLP endpoint in compose and add Npgsql OTel. Define alerts for 5xx rate, 429 rate, outbox/notification dead-letter counts, partition runway and backup age.
- **Tests required:** unit test that the middleware sets the scope and tag.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-009
**Secrets-provider abstraction is documented as done but absent from code**

- **Area / category:** DevOps/production — Secrets management / doc contradiction
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed
- **Evidence:** `backend/laundryghar/PRODUCTION_ENV.md:85-134` and `HANDOFF.md:797-803` describe `ISecretsProvider`, `FileSecretsProvider`, `Secrets:Provider=file` and `laundryghar.ServiceDefaults/Secrets/SecretsProviderFactory.cs`. `ls laundryghar.ServiceDefaults` shows only `Extensions.cs` and `ExternalDependencyResilience.cs`. A grep for `ISecretsProvider|Secrets:Provider|KeyPerFile` returns 0 hits in .cs/.csproj. `AddServiceDefaults` (Extensions.cs:26-52) wires none of this. Secrets reach containers as plain env vars from `deploy/.env` (`docker-compose.yml:21-27,44`). Secret-looking key names: `DB_CONNECTION_STRING`, `PII_ENCRYPTION_KEY`, `JWT_PRIVATE_KEY`, `POSTGRES_PASSWORD`, `SENTRY_DSN`, plus app …
- **Impact:** operators following the docs will set `Secrets__Provider=file` and get nothing. There is no rotation path or central store, and secrets are visible in `docker inspect`.
- **Remediation:** correct the docs. Use the already-supported `Jwt__PrivateKeyPath` with Docker secrets, and add `builder.Configuration.AddKeyPerFile("/run/secrets", optional:true)` in ServiceDefaults (one line, framework-provided).
- **Tests required:** config-binding test for key-per-file.
- **Specialist's dependencies / priority note:** P1. Related area: SEC. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-012
**Compose cannot pull the CI-built images, and pos-web has no deploy path**

- **Area / category:** DevOps/production — Deployment
- **Severity:** Medium
- **Status:** Verified (config read)
- **Independent verification:** QA-B: Confirmed
- **Evidence:** `deploy/docker-compose.yml:39,52,64,76,110` give `image: laundryghar-core` etc. (unqualified, so Docker Hub). `deploy/README.md:62-70` says to `docker compose pull` the GHCR images. `release.yml:67` pushes to `ghcr.io/<owner>/laundryghar-*`. `pos-web` has no Dockerfile, CI job or release matrix entry (`find -name Dockerfile*` shows only backend and admin-web). `admin-web` VITE URLs are baked at build time (`admin-web/Dockerfile:24-31`, `release.yml:46-52`), so it is one image per environment.
- **Impact:** the documented deploy flow either fails or pulls the wrong images. The POS client cannot be deployed from the repo.
- **Remediation:** qualify the images as `ghcr.io/${OWNER}/laundryghar-core:${TAG:-latest}`. Add pos-web to CI and release. Consider runtime config injection (`/config.js`) for SPAs.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-014
**Noisy-neighbour controls are global, not per tenant or plan**

- **Area / category:** DevOps/production — Resource isolation
- **Severity:** Medium
- **Status:** Verified (code read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `ResilientForwarderHttpClientFactory.cs`: `AddConcurrencyLimiter(permitLimit: 100, queueLimit: 0)` and `MaxConnectionsPerServer = 100` per cluster, shared by all tenants. `Gateway/Program.cs:218`: one `BrandPermitLimit` for every brand, regardless of subscription tier. The compose file has no CPU or memory limits. There is one shared Npgsql pool per host (`SharedDataModel/DependencyInjection.cs:86-91`). Core's `api_key` policy does read per-key limits (`core.WebApi/Program.cs:227-283`), which is a positive.
- **Impact:** one heavy tenant can exhaust a cluster's in-flight slots or the DB pool for everyone.
- **Remediation:** add a per-brand concurrency partition at the gateway (verified-token brand only, see SA-OPS-002) and map limits from plan entitlements. Add container resource limits.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-QB-002
**Integration tests report "Passed" when Docker is missing, and migration and rollback coverage is thin**

- **Area / category:** QA (platform) — Test integrity / QA
- **Severity:** Medium
- **Status:** Verified (code read; CI log read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `tests/operations.IntegrationTests/Phase4SalonSchemaTests.cs:15-20,49-51`: `catch (Exception) { _dockerAvailable = false; }` and then `if (!_dockerAvailable) return;`. The same pattern appears 60 times across 17 files (grep). - Only 13 of the 33 `.up.sql` migrations are applied by any test, each onto a minimal fixture (`RepoPaths.Migration(...)` grep). No test executes a `.down.sql`, and no test runs `build_from_scratch.sh` + `migrate.sh up`. - There are no tests for `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `RazorpayWebhookHandler`, royalty or `NotificationSettingsCache` (grep of `tests/`). - There are no booking or slot concurrency tests (grep for `WhenAll`/`concurren` finds …
- **Observed behaviour:** on any runner without Docker, the whole integration suite goes green without asserting anything. On GitHub it did execute (284 passed in 2 min 39 s, job 108549585458). The suites that exist would not catch G1, G3, SA-SOLID-001/002/003 or SA-QB-001.
- **Impact:** false assurance, and migration regressions (like the 0005 bootstrap failure) reach operators.
- **Remediation:** use `Assert.Skip`/`SkippableFact` (or fail when `CI=true`) instead of `return`. Add a CI job that builds Postgres 16 with partman and postgis from the documented bootstrap, runs `migrate.sh up`, then `down`/`up` for the last N migrations, and checks EF mappings against `information_schema`. Add handler tests for the paths listed above.
- **Tests required:** as above. Add a meta-test that fails if any integration test returns early on CI.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/10b-qa-verification-platform.md`](docs/audit/specialists/10b-qa-verification-platform.md)

### SA-QC-002
**Sub-brand RLS (0031) makes the per-brand `COUNT(*)+1` number generators scope-blind: routine intra-tenant unique violations**

- **Area / category:** QA (DB/mobile) — Integrity / RLS side-effect
- **Severity:** Medium
- **Status:** Verified (DB, T8) for warehouse batches; Partially Verified (code) for expenses
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-DB-007 / SA-TEN-001
- **Evidence:** - `CreateWarehouseBatch.cs:48-49` (`COUNT(b.BrandId==brandId)+1`, `WB-{date}-{n}`) and `ExpenseCommands.cs:204-205` (`EXP-{date}-{n}`) assume the count sees the whole brand. - Under the 0031 RESTRICTIVE policy (`0031…up.sql:144-200`; both tables are in the list and carry `warehouse_id` / `franchise_id`), a warehouse- or franchise-scoped user sees only its own rows. - The uniques are global (`UNIQUE (batch_number)`, `UNIQUE (expense_number)`).
- **Observed behaviour:** the brand had 1 batch (W1). W2-scoped staff counted 0 and generated `WB-20261009-0001`, which raised `duplicate key … warehouse_batches_batch_number_key`. Because the count does not change when the insert fails, W2 stays blocked for the rest of the day.
- **Impact:** in any multi-warehouse or multi-franchise brand, scoped staff hit 500s on batch and expense creation whenever their visible count equals a sibling's on the same day. That is far more frequent than the cross-tenant case in SA-DB-007. Expenses become affected once SA-TEN-001 is fixed for the commerce host.
- **Remediation:** replace COUNT+1 with a per-brand counter row using the `next_order_number` upsert pattern, executed in a SECURITY DEFINER function or under a brand-only predicate. Make the uniques `(brand_id, number)` (SA-DB-007).
- **Tests required:** two warehouse-scoped users in one brand create batches on the same day; two franchise-scoped users create expenses.
- **Specialist's dependencies / priority note:** P2, with SA-DB-007. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/10c-qa-verification-db-mobile.md`](docs/audit/specialists/10c-qa-verification-db-mobile.md)

### SA-SOLID-008
**The `LoggingChannelSender` null object breaks the `IChannelSender` contract in production, so undelivered notifications are recorded as "sent"**

- **Area / category:** OOP/SOLID — LSP
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed
- **Evidence:** - `Stubs/LoggingChannelSender.cs:L17-L31` returns success and logs `phone=` and `email=` at Information level. - It is registered unconditionally (`commerce.WebApi/Program.cs:L262`, `L287`). `RoutingChannelSender.cs:L80-L95` routes email, in_app, voice, any unknown channel, and WhatsApp or SMS without credentials to it. - The dispatcher treats any non-throwing return as success: it sets `Status = "sent"` (`NotificationDispatcherService.cs:L170-L225`).
- **Observed behaviour:** email notifications, and all WhatsApp and SMS sends for tenants without credentials, are marked `sent` with provider `logging-stub` and are never retried. The only trace is a Debug-level log line.
- **Impact:** silent customer-communication failure that operators can't see. PII also ends up in logs.
- **Remediation:** outside Development, return a distinct outcome (for example a `ChannelSendResult` carrying `Delivered=false`) or throw a `ChannelNotConfiguredException`, and store the status as `suppressed` or `failed:not_configured`. Mask PII in the log line.
- **Tests required:** with no credentials in Production mode, the outbox row is not `sent`.
- **Specialist's dependencies / priority note:** P1. Related area: NOTIFICATIONS / SEC. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-TEN-007
**The database layer trusts the application completely: self-settable bypass GUC, blanket platform-admin bypass, and SECURITY DEFINER functions taking caller-supplied brand ids**

- **Area / category:** Multi-tenancy — Isolation architecture
- **Severity:** Medium
- **Status:** Verified (SQL repro S6 + code and migration read)
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-DB-015
- **Evidence:** - `kernel.rls_bypass()` reads `app.bypass_rls`, a plain GUC that `app_user` can set (`harden_app_user_and_rls_bypass.sql:L31-38`). S6: after `SET ROLE app_user; set_config('app.bypass_rls','true',false)` the session saw all 3 rows across both brands. - Every platform-admin request runs with full bypass (`TenantResolutionMiddleware.cs:L38-47`), so RLS `WITH CHECK` never applies to those writes. - Since 0015/0019, more than ten SECURITY DEFINER functions are granted to `app_user` and trust their brand argument. `kernel.export_brand` (`0015:L143-165`) dumps an entire tenant for any `p_brand_id`; `set_brand_cancellation_state` (`0015:L265-294`) changes any brand's status. - The comments record …
- **Impact:** RLS is a guard against *forgotten predicates*, not against SQL injection or a single mis-wired handler. One raw-SQL injection or one wrong argument yields cross-tenant reads or writes. The steady growth of SECURITY DEFINER escape hatches widens that surface. - Recommended remediation (incremental, no rewrite): 1. Add a read policy on `brands` (`id = current_brand_id()`) so tenants can read their own row, then retire the read-only DEFINER …
- **Remediation:** See source report.
- **Tests required:** calling each DEFINER function with a foreign brand id while `current_brand_id` is set must fail.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-TEN-008
**Suspension, cancellation and deletion are HTTP-only and partial gates**

- **Area / category:** Multi-tenancy — Tenant lifecycle
- **Severity:** Medium
- **Status:** Partially Verified (code read; workers sampled)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-AUTHZ-014, SA-SUB-017, SA-SUB-018
- **Evidence:** - `BrandSuspensionMiddleware.cs:L128-134` skips anonymous traffic and platform admins, and `L78-83` / `BrandStatusStore.cs:L47-53` fail open. - The worker services (`SubscriptionBillingService`, `AutoDispatchService`, `RoyaltyGenerationService`, `LoyaltyEarnService`, `NotificationMappingService`) contain no brand-status check: grep for `brand_status`/`BrandStatus` in `commerce.Infrastructure/Worker/Services` finds none. - `DeleteBrand.cs:L14-24` only stamps `deleted_at`. `kernel.brand_status` ignores `deleted_at` (`0009:L23-34`), and `ScopeResolver` does not check brand state (`core.Application/Identity/Auth/Common/ScopeResolver.cs:L51-100`).
- **Observed behaviour:** - A suspended or cancelled brand's customers keep being billed and its orders keep being auto-dispatched. - A "deleted" brand's users keep logging in and operating. - `AdminCancellation.cs:L21` and `RetentionSweepService.cs:L198-200` both state "there is no delete endpoint", but `DELETE /api/v1/admin/brands/{id}` exists (`AdminBrands.cs:L30, L62-66`).
- **Impact:** lifecycle states do not mean what the product says they mean. There is billing and legal exposure (charging end customers of a frozen tenant).
- **Remediation:** - Add a shared `IBrandStatusStore` check (or SQL `JOIN brands … status='active'`) to every worker query that acts on behalf of a brand. - Make `DeleteBrand` set `status='archived'` (or remove the endpoint in favour of cancellation), and make `brand_status` return `archived` when `deleted_at` is set.
- **Tests required:** worker tests with a suspended brand fixture; a login test for a deleted brand.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-TEN-010
**Isolation test suite does not exercise the real runtime path**

- **Area / category:** Multi-tenancy — Test coverage
- **Severity:** Medium
- **Status:** Verified (read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-QB-002, SA-ARCH-010, SA-QA-002
- **Evidence:** - `RbacRlsFixture.cs:L43-47` (pooling off), `L86-120` (6 of 12 GUCs set, under a "byte-for-byte mirror" claim) and `L140-280` (trimmed hand-written DDL). - `SubBrandScopeRlsTests.cs` has no customer, API-key or commerce-adapter case. - CI (`ci.yml:L31-36`) runs the tests, but no job builds the DB from `database_scripts` + `db/patches` + `db/migrations` and asserts RLS coverage.
- **Impact:** SA-TEN-001 and SA-TEN-002 shipped despite substantial RLS test volume.
- **Remediation:** - Generate the fixture's GUC setter from `RlsConnectionInterceptor` itself, for example by instantiating the interceptor with a fake `ICurrentTenant`. - Add one smoke test per `ICurrentTenant` adapter × principal type. - Add a CI job that applies the real migration chain and re-runs the 0027 guard.
- **Tests required:** the tests just described.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-TEN-015
**The runtime DB role password is hard-coded and re-applied by patches**

- **Area / category:** Multi-tenancy — Security / operations
- **Severity:** Medium
- **Status:** Verified (read)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** `db/patches/app_user_role.sql:L34` and `db/patches/harden_app_user_and_rls_bypass.sql:L47` run `ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user'` unconditionally.
- **Impact:** re-running either "idempotent" patch against production silently resets the RLS-subject role to a publicly known password.
- **Remediation:** remove the password from the patches and set it out of band (secrets manager), or use `\password` / `psql -v`.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-AUTHZ-011
**Plan entitlements are enforced only on the staff lane (token stripping); customer/partner lanes and non-brand-scoped staff are not covered live**

- **Area / category:** Authorization (RBAC/ABAC) — Subscription entitlement enforcement
- **Severity:** Medium
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed (canonical)
- **Duplicates (same defect, other reports):** SA-SUB-010
- **Evidence:** entitlement is applied only in `ScopeResolver.cs:185-241` (staff tokens). `CustomerOnly`/`PartnerOnly` policies carry no feature check (`PermissionPolicyProvider.cs:151-189`). No runtime feature checks exist in operations/commerce application code (grep for `BrandFeatureGate|BrandFeatures` hits only core identity files). `PermVersionBumper.BumpBrandMembersAsync` bumps only brand-scoped memberships (`PermVersionBumper.cs:38-44`), so franchise/store staff keep stale entitlements until token expiry.
- **Impact:** customers of a brand without, say, the wallet/loyalty/subscription feature can still call those customer APIs if data exists. A downgrade takes up to 15 min to bite for store staff.
- **Remediation:** add a `RequireFeature("<key>")` endpoint filter backed by a cached brand-feature lookup, applied to customer/partner groups. Bump all members whose memberships resolve to the brand (reuse `user_in_brand`).
- **Tests required:** brand without feature X → customer endpoint for X → 402.
- **Specialist's dependencies / priority note:** P1 · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-ONB-007
**GSTIN captured at signup never reaches invoices**

- **Area / category:** Onboarding/white-label — Provisioning / Compliance
- **Severity:** Medium
- **Status:** Verified (code)
- **Independent verification:** QA-B: Confirmed
- **Related:** SA-VERT-004
- **Evidence:** - `CompleteSignup.cs:L122` and `L304-307`: the GSTIN goes to `brands.config.gstin`. - The "OWN" franchise is created without a GSTIN (`L184-194`; `Franchise.Gstin` exists at `SharedDataModel/Entities/TenancyOrg/Franchise.cs:L19`). - Invoices read `franchise.Gstin` (`operations.Application/Orders/Invoices/Commands/GenerateInvoiceCommand.cs:L80-87`), and the renderer prints "GSTIN: Unregistered / Composition" when it is null (`InvoicePdfRenderer.cs:L61-64`).
- **Observed behaviour:** A GST-registered provider's invoices state that they are unregistered.
- **Impact:** GST compliance defect on every self-signed-up registered business.
- **Remediation:** In `CompleteSignup`, set `Franchise.Gstin` from the request after validating the 15-character GSTIN pattern.
- **Tests required:** a signup with a GSTIN produces an invoice showing that GSTIN.
- **Specialist's dependencies / priority note:** P1 (small fix). · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-ONB-010
**Signup has no plan choice, silently tolerates a missing plan, and has no handler or integration tests**

- **Area / category:** Onboarding/white-label — Provisioning robustness / Test coverage
- **Severity:** Medium
- **Status:** Verified (code); tests searched
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `CompleteSignup.cs:L200-225`: if `template.DefaultBundleCode` is null, or its `module_bundle` row is absent, no subscription is created and no error is raised. `TemplateProvisioner.cs:L56` also returns 0 features in that case. - The plan is never caller-selectable (`SignupDtos.cs:L26-32`). - The duplicate-phone and brand-code checks are check-then-insert (`L84-85`, `L279-301`). The unique constraints (`users.phone_e164 UNIQUE`, `database_scripts/02_bc2_identity_access.sql:L23`; `brands.code UNIQUE`) would surface concurrent duplicates as unmapped DB errors. - Tests: only pure helpers are covered (`tests/core.Tests/Signup/TemplateProvisionerTests.cs:L17-76`; …
- **Observed behaviour:** Under a template or bundle mismatch, a brand can be created with no plan and no licensed features.
- **Impact:** Silent, unbillable or empty tenants. Regressions in the tenant-creation endpoint, which is the platform's most abuse-prone surface, would not be caught.
- **Remediation:** 1. Fail the signup (500 plus an alert) when a public template has no resolvable bundle. 2. Optionally accept a `planCode` validated against the vertical's bundles. 3. Map 23505 to 409. 4. Add handler and integration tests.
- **Tests required:** As listed in T-16, plus missing bundle → error and concurrent duplicate phone → 409.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-QB-001
**COD and online payment rows carry no `franchise_id`, so royalty under-counts even after SA-SOLID-003 is fixed**

- **Area / category:** QA (platform) — Finance correctness / data model (Related area: PAY, FINANCE)
- **Severity:** Medium
- **Status:** Verified (static)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-SOLID-003
- **Evidence:** - `operations.Application/Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs:196-222`: the COD `Payment` sets `BrandId`, `CustomerId` and `OrderId`, but no `FranchiseId`. - `commerce.Application/Commerce/Customer/Payments/CustomerPaymentHandlers.cs:54-81`: online payments also have no `FranchiseId`. - Only `RecordOfflinePaymentCommand.cs:147` sets it. - `SharedDataModel/Entities/Commerce/Payment.cs:17` (`Guid? FranchiseId`). No DB trigger populates it (grep of `db/` and `database_scripts/`). - Royalty filters on `p.FranchiseId == …` (`RoyaltyCommands.cs:98-107`; `RoyaltyGenerationService.cs:195-203`).
- **Observed behaviour:** once the `"completed"` literal is fixed, royalty revenue will include only staff-recorded offline payments. Rider-collected COD and online payments will still be excluded.
- **Impact:** franchisor royalties stay understated, and the error is silent because the fix for SA-SOLID-003 would look like it works.
- **Remediation:** set `FranchiseId = order.FranchiseId` in both initialisers, or compute royalty by joining `payments → orders.franchise_id`. Backfill existing rows from `orders`.
- **Tests required:** royalty over seeded COD, online and offline payments equals their sum. An insert test asserting that `payments.franchise_id` is not null when `order_id` is set.
- **Specialist's dependencies / priority note:** P0 together with SA-SOLID-003. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/10b-qa-verification-platform.md`](docs/audit/specialists/10b-qa-verification-platform.md)

### SA-SUB-005
**Brand platform billing worker is disabled by default and undocumented**

- **Area / category:** Subscription/billing — (a-brand) Operations / configuration
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-A: Confirmed – minor correction
- **QA correction to the specialist text:** QA-A: the opt-in flag is named in docs/SAAS_PLATFORM_ARCHITECTURE.md:324 (so not wholly undocumented).
- **Evidence:** - `commerce.Infrastructure/Worker/Options/WorkerOptions.cs:L140` sets `BrandPlatformBillingEnabled = false`. - `commerce.WebApi/appsettings.json` and `.Development.json` have no `Worker` section. - `PRODUCTION_ENV.md` documents `Worker__SubscriptionBillingEnabled` (`L325`) but not `Worker__BrandPlatformBillingEnabled`. - `BrandPlatformBillingService.cs:L40-L45` returns immediately when the flag is off.
- **Observed behaviour:** by default no renewals, no dunning and no automatic suspension happen in any environment.
- **Impact:** suspension-on-nonpayment, which `0021` describes as shipped, is inert unless an operator knows an undocumented flag.
- **Remediation:** document the flag, and decide whether it should default to on in Production (it must be fixed together with SA-SUB-004). Add a startup warning in Production when it is off.
- **Tests required:** config test pinning the intended value (the same pattern as `EntitlementConfigTests`).
- **Specialist's dependencies / priority note:** P1, after SA-SUB-004. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-008
**Plan limits / quotas do not exist (a-brand) or are not enforced (a-franchise); Starter "1 location" is modelled so that Starter cannot create any store**

- **Area / category:** Subscription/billing — Entitlements / quotas / metering
- **Severity:** Medium
- **Status:** Partially Verified (the code chain was read; permission→module ownership depends on seeded data that I could not query)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - (a-brand): `module_bundle` and `brand_platform_subscription` have no limit columns (`phase4_bundle_pricing.sql:L25-L29`, `phase4_brand_platform_subscription.sql:L21-L39`). - (a-franchise): `MaxStores/MaxWarehouses/MaxUsers/MaxOrdersPerMonth/MaxRiders` are copied at `FranchiseSubscriptionCommands.cs:L64-L68`, and a repo-wide grep shows no reader outside DTOs and commands. `CreateStore`/`CreateWarehouse`/`AddStore` have no count check. - Starter = `bookings, scheduling` plus non-sellable features (`0007_align_plan_tiers.up.sql:L58-L77`). The `stores` module is gated by `multi_location` (`0005:L121`). `stores.create` resolves to the `stores` module by exact key match …
- **Observed behaviour:** no tier limits users, orders or locations by count. A Starter brand (applied by an operator) has `stores.create` stripped from its token and cannot add a single location. Self-signup avoids this only because no template defaults to `starter`.
- **Impact:** plans cannot be priced on usage. The Starter tier is unusable as designed.
- **Remediation:** - Add `limits jsonb` (or typed columns) to `module_bundle`, snapshot it on the subscription, and enforce it in the few create handlers that matter (stores, users, riders) with a shared `IPlanLimitGuard` that returns 402 `plan_limit_reached`. - Model "1 location" as a limit, not as a missing feature.
- **Tests required:** create-store at the limit returns 402; Starter can create exactly one store.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-009
**Five sellable features gate nothing (`wallet`, `loyalty`, `online_payments`, `item_tracking`, `whatsapp_bot`)**

- **Area / category:** Subscription/billing — (a-brand) Entitlement coverage
- **Severity:** Medium
- **Status:** Partially Verified (no live DB to list `features` without modules; the migrations and grep agree)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - Created module-less at `0005_split_features_from_modules.up.sql:L66-L83`. - `0019_premium_feature_modules.up.sql:L4-L11` acknowledges that "a feature with no module … gates absolutely nothing". It fixed only `custom_domain`/`white_label_app` (`L23-L30`) and only emits a `RAISE WARNING` for the rest (`L108-L123`). - No later migration or patch inserts a module for these keys (grep of `INSERT INTO identity_access.modules` across `db/`). - `ScopeResolver.cs:L224-L240` can only strip permissions reachable through a module. - The tier contents still sell them (`0007:L83-L116`).
- **Observed behaviour:** a Starter or Growth brand can use wallet, loyalty and online-payment endpoints with ordinary permissions (`wallet.*`, `loyalty.manage`, `payment.*`, owned by other modules). `api_access` is the exception and is enforced at key authentication (`0016:L113-L137`).
- **Impact:** paid tier differentiators are free. Q4 is overstated.
- **Remediation:** as migration 0019 did, add dedicated modules or permissions for these features and point their permissions' `module_key` at them. Turn the 0019 WARNING into an EXCEPTION in a new migration, so it fails CI.
- **Tests required:** a catalogue test asserting that every `is_sellable` feature is referenced by at least one active module that owns at least one permission.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-011
**Franchise SaaS subscriptions (ADR-010 "module B") are data model + CRUD only, never billed or enforced, which contradicts ADR-010**

- **Area / category:** Subscription/billing — (a-franchise) Commercial model consistency
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `FranchiseSubscriptionCommands.cs:L27-L104` (assign: status `trialing`/`pending`, never advanced). - Grep shows `FranchiseSubscriptionInvoices` referenced only by the DbContext and configuration; no worker or handler. - ADR-010 (`docs/ADRs/ADR-010-recurring-billing-and-dunning.md`) states "Suspend-on-nonpayment for franchises is a defined lifecycle (module B), enforced by a dunning job". - admin-web `PlatformPlansPage` manages these plans (`App.tsx:L111`).
- **Observed behaviour:** two unrelated SaaS plan catalogues exist (`module_bundle` for brands, `platform_plans` for franchises). Only the first drives entitlements. Operators can "assign a SaaS plan" that bills nothing and limits nothing.
- **Impact:** confusion, false confidence, and a likely double-modelling of tenant pricing.
- **Remediation:** decide whether franchises are a separate payer. If not, deprecate `platform_plans`/`franchise_subscriptions` and hide the page. If so, reuse the brand billing worker pattern. Update ADR-010.
- **Tests required:** n/a until decided.
- **Specialist's dependencies / priority note:** P2 (product decision). · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-012
**Plan-change billing defects: changing tier during a trial bills the trial window at full price (plus proration); downgrades strip paid-for features immediately**

- **Area / category:** Subscription/billing — (a-brand) Upgrade/downgrade correctness
- **Severity:** Medium
- **Status:** Verified (code read)
- **Independent verification:** QA-A: Confirmed
- **Evidence:** `ApplyBundleToBrand.cs:L100-L143`. For an existing `trialing` subscription: - `Status="active"` is set (`L107`) while `CurrentPeriodStart` stays the signup time. - If the new price is higher, a proration invoice is added for the remaining trial (`L112-L129`). - Then `hasInvoice` is false for the trial start, so a full-price invoice for the trial period is added (`L132-L143`). For a downgrade, `sub.Price` is lowered at once and bundle rows are deleted at once (`L54-L56`). The comment "takes effect at the next renewal" (`L110-L111`) describes neither.
- **Observed behaviour:** an upgrade mid-trial produces two invoices, about 14 days of proration plus a full month. A downgrade removes features the brand has already paid for this period.
- **Impact:** overbilling disputes on one side, underdelivery on the other.
- **Remediation:** - For `trialing`: change the plan only (keep `trialing`, issue no invoice). - For downgrade: schedule it (`pending_bundle_code`, applied at renewal), or keep features until `current_period_end`.
- **Tests required:** extend `PlanChangeTests` with trial-upgrade and downgrade billing assertions. They currently assert features only.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-013
**Brand platform invoices are not GST invoices and carry no payment record**

- **Area / category:** Subscription/billing — (a-brand) Invoicing / compliance (also notes (b))
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `BrandPlatformInvoice.cs:L9-L31`: amount, status and link only. There is no invoice number, tax breakdown, supplier/recipient GSTIN, SAC code, place of supply, PDF, `paid_at` or payment id. - `phase4_brand_platform_subscription.sql:L41-L53` shows the same. - Brand GSTIN is captured only into `brands.config` at signup (`CompleteSignup.cs:L303-L307`). - (b): `SubscriptionBillingService.cs:L178-L186` uses IGST 18% always (wrong for intra-state) and `invCount+1` numbering, which is race-prone and not gap-free per brand.
- **Observed behaviour:** a platform invoice cannot serve as a tax invoice. Payments cannot be reconciled to a gateway payment id.
- **Impact:** compliance blocker for charging Indian businesses. Weak reconciliation.
- **Remediation:** add `invoice_number` (DB sequence per financial year), a tax breakdown (reuse `TaxBreakdown`), `paid_at`, `gateway_payment_id` and `payment_method` columns. Populate them from the webhook and sync. Generate PDFs through the existing invoice tooling (`db/patches/invoice_generation.sql` is the customer-side precedent).
- **Tests required:** invoice numbering is gap-free and unique under concurrency; CGST+SGST vs IGST selection.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-014
**Brand dunning makes no charge attempts, sends no notices, and its state changes are not audited**

- **Area / category:** Subscription/billing — (a-brand) Dunning / auditability
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `BrandPlatformBillingService.cs:L184-L207`: an "attempt" is just `attempt_count+1` once per backoff window (default 1440 min). There is no gateway call and no mandate for brands. - Brand-side entity comment: "Not auto-charged yet … deferred P0" (`BrandPlatformInvoice.cs:L6-L7`). - All transitions run through `ExecuteSqlAsync`/SECURITY DEFINER functions (`L178-L179`, `L186-L207`, `L231-L233`). `AuditSaveChangesInterceptor.cs:L82-L101` only sees EF ChangeTracker entries, so `past_due`, suspension and reinstatement leave only log lines.
- **Observed behaviour:** brands are suspended about 3 attempts plus 14 days after the due date without a single charge or message.
- **Impact:** avoidable suspensions, and no evidentiary trail for disputes.
- **Remediation:** emit an outbox notification event and an explicit `audit_logs` row (`IAuditWriter`) for each transition. Add a mandate or auto-collect for brands later.
- **Tests required:** worker test asserting audit and outbox rows per transition.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-SUB-015
**(b) Recurring mandate charge integration is suspect: likely non-existent Razorpay endpoint/header, asynchronous statuses treated as failures, no webhook reconciliation of subscription invoices**

- **Area / category:** Subscription/billing — (b) Payment gateway. Related area: PAY/commerce.
- **Severity:** Medium
- **Status:** Suspected (Razorpay API semantics from general knowledge; not checked against Razorpay; not run)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `RazorpayPaymentGateway.cs:L226-L282` POSTs `v1/subscriptions/{id}/charge` with a `Razorpay-Idempotency` header. Razorpay's documented recurring flow is order + `payments/create/recurring` with a token. I am not aware of either the endpoint or the header. - Any status other than `captured` maps to non-success. - `SubscriptionBillingService.cs:L392-L436` treats every non-`success` result, including `created`/`authorized`/`initiated`, as a failure and advances dunning. - `RazorpayWebhookHandler.cs:L95-L99` reconciles only `payments.gateway_order_id`, and subscription charges create no `payments` row with an order id. - `GatewaySubscriptionCharger.cs:L32` resolves `IPaymentGateway` in a …
- **Observed behaviour:** successful asynchronous debits can be recorded as failures, leading to customers being wrongly suspended. Per-brand gateway credentials may not resolve in the worker.
- **Impact:** customer-subscription revenue and churn risk. Shares the gateway and webhook infrastructure that SaaS billing would reuse.
- **Remediation:** validate against the Razorpay sandbox; treat pending statuses as pending and finalise them by webhook; use `CreateWorkerAsyncScope` in the charger.
- **Tests required:** gateway contract test against recorded sandbox responses.
- **Specialist's dependencies / priority note:** P1 for (b). Not a blocker for (a). · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-API-021
**Single-region and laundry-only assumptions baked into the API**

- **Area / category:** Backend/API — Multi-business / multi-vertical readiness
- **Severity:** Medium
- **Status:** Partially Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `CompleteSignup.cs:115-120` (INR / IN / Asia/Kolkata / en-IN forced for every new tenant); `UpdateMyTaskStatus.cs:159,173` (laundry statuses hard-coded); `LoyaltyEarnService.cs:94,109` (earn only on `delivery.completed`, so appointment-mode orders never earn); no customer appointment, booking or staff-slot endpoint exists for the salon strategy (grep `appointment` in `*/Endpoints` returns nothing; the strategy exists in `operations.Application/Fulfillment/Salon/SalonAppointmentStrategy.cs`); the customer booking API is pickup-centric (`CustomerOrderEndpoints.cs:50-54`).
- **Observed behaviour:** the platform can only onboard Indian tenants and fully serve laundry or parcel flows. Salon and other verticals have a state machine but no booking API or loyalty hook.
- **Impact:** blocks the "one vertical per tenant" target for anything other than laundry or parcel.
- **Remediation:** take currency, country, timezone and locale from signup input or the vertical template; emit vertical-neutral completion events (`order.completed`) from strategies; add an appointment booking slice before selling salon.
- **Tests required:** signup with a non-IN template; loyalty earn on a salon completion.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-ARCH-001
**Three "services" share one EF model, one database role and overlapping table ownership (distributed monolith)**

- **Area / category:** Architecture — Architecture / modularity / coupling
- **Severity:** Medium (originally High; QA-B: structural observation, no runtime defect of its own)
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed – severity corrected to Medium
- **Evidence:** - `backend/laundryghar/*/*.csproj` — every WebApi, Infrastructure and (via Domain/Utilities) Application project references `laundryghar.SharedDataModel`. - `laundryghar.SharedDataModel/Persistence/LaundryGharDbContext.cs:34-217` — single context mapping tenancy, identity, catalog, orders, laundry fulfilment, logistics, commerce, finance, kernel, analytics, engagement. - `laundryghar.AppHost/AppHost.cs:23-28,60,72,87` — identical `ConnectionStrings__Default` for all hosts. - Overlapping write ownership: `IOperationsDbContext.cs:98-112` (Payments, Coupons, CouponRedemptions, LoyaltyPointsLedger, CustomerPackages, PackageUsageLedger, Promotions, PaymentRefunds, CashBooks, CashBookEntries) vs. …
- **Observed behaviour:** The process split (core/operations/commerce) is a deployment/scaling partition, not a bounded-context partition. Cross-context invariants (e.g. coupon usage limits, rider load) are enforced by whichever host happens to write, inside its own DB transaction.
- **Impact:** Any schema or entity change forces coordinated redeploy of all hosts; no team/module can evolve independently; adding a vertical means editing the shared assembly every host loads (directly hurts Q7). Microservice-style operational cost (3 hosts, gateway, JWKS hops) without microservice-style independence.
- **Remediation:** Stop treating the hosts as services. Declare module ownership per table (one writing module per table, documented and test-enforced), move cross-module writes behind in-process module APIs (e.g. `ICouponRedemptionService` owned by Commerce, called by Orders in the same transaction), and add architecture tests that fail when a module's Application assembly references another module's entities for writes. Do not split the database.
- **Tests required:** Architecture tests (assembly dependency rules); existing order-placement tests must keep coupon/loyalty/package atomicity.
- **Specialist's dependencies / priority note:** P1 (precondition for vertical modules). · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/01-architecture.md`](docs/audit/specialists/01-architecture.md)

### SA-AUTHZ-012
**Vertical (business-type) boundary is not enforced server-side**

- **Area / category:** Authorization (RBAC/ABAC) — Multi-vertical authorization
- **Severity:** Medium
- **Status:** Verified (absence searched across endpoints and operations/commerce application code)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-FE-006, SA-MOB-015, SA-ONB-003, SA-VERT-005
- **Evidence:** the only vertical gate is in the menu builder (`GetNavigator.cs:39-45`). No endpoint or handler reads `Brand.VerticalKey` to authorize. `CreateOrderCommand.cs:644-646` notes that vertical resolution "lands" later.
- **Impact:** a laundry tenant can call salon/logistics APIs (and vice versa) wherever the permission code is held and the feature is licensed or core. The target of exactly one primary vertical per tenant is a UI convention, not a server control.
- **Remediation:** tag endpoint groups with a vertical and add a filter comparing it to the caller brand's `vertical_key` (cached), or fold the vertical into the entitlement feature map so licensing implies the vertical.
- **Tests required:** laundry brand → salon endpoint → 403/402.
- **Specialist's dependencies / priority note:** P2 · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-DB-004
**No composite tenant foreign keys: rows can reference another tenant's parents**

- **Area / category:** Database (index/idempotency/RLS) — Integrity / tenant isolation
- **Severity:** Medium (originally High; QA-C: defence-in-depth; cross-tenant reads still blocked by parent RLS)
- **Status:** Verified
- **Independent verification:** QA-C: Confirmed – severity corrected (to Medium)
- **Duplicates (same defect, other reports):** SA-TEN-011
- **Evidence:** live, 0 of 617 FKs include brand_id. Example: `fk_patch_06_commerce.sql`-style `FOREIGN KEY (order_id, order_created_at) REFERENCES orders(id, created_at)` (`db/HANDOFF.md` §5).
- **Observed behaviour:** as app_user in brand A: - `INSERT orders(brand_id=A, franchise_id=B-franchise, store_id=B-store, customer_id=B-customer)` → INSERT 1. - `INSERT payments(brand_id=A, order_id=B-order)` → INSERT 1.
- **Impact:** RLS WITH CHECK only validates the row's own brand_id. A handler that trusts a client-supplied id creates cross-tenant links. Those links then leak through joins: the B-store name appears on an A invoice, and the B order is updated through the A payment.
- **Remediation:** add `UNIQUE (brand_id, id)` on parents and composite FKs `(brand_id, x_id) REFERENCES parent(brand_id, id)` for the hot paths: orders↔customers/stores/franchises, payments↔orders, order_items↔orders, pickup_requests↔stores/customers. Use `NOT VALID` then `VALIDATE` for a zero-downtime rollout.
- **Tests required:** a cross-brand FK insert must fail.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-007
**Globally unique business numbers generated per tenant: cross-tenant unique violations and an existence oracle**

- **Area / category:** Database (index/idempotency/RLS) — Integrity / multi-tenancy
- **Severity:** Medium (originally High; QA-C: functional DoS + weak existence oracle; no data exposure)
- **Status:** Verified (expense reproduced; others by code + schema)
- **Independent verification:** QA-C: Confirmed – severity corrected (to Medium)
- **Duplicates (same defect, other reports):** SA-TEN-012
- **Evidence:** - `ExpenseCommands.cs:204-205` `EXP-{yyyyMMdd}-{brandCount+1}` against `database_scripts/07_bc7_finance_royalty.sql:150` `expense_number … UNIQUE`. - `CreateWarehouseBatch.cs:48-49` `WB-{yyyyMMdd}-{count+1}`. - `PickupCommands.cs:61-63` and `CreateParcelOrderCommand.cs:163-166` `PKP-{yyyy}-{brandId[..4]}-{count+1}`. - `GenerateTags.cs:34-42` `LG-{brandId[..4]}-{count+i}`. - `SaveCommercials.cs:53` `AGR-{franchiseCode}-{year}`.
- **Observed behaviour:** brand A inserted `EXP-20261009-00001`. Brand B, seeing 0 expenses, generated the same number and got `duplicate key value violates unique constraint "expenses_expense_number_key"`.
- **Impact:** - Tenant B cannot create expenses or batches whenever its count equals another tenant's on the same day. - The error reveals another tenant's activity. - COUNT+1 also races inside a tenant and costs O(n) per insert (E3: 384 ms for a 30k-row count under RLS, synthetic).
- **Remediation:** brand-scoped uniques `(brand_id, number)`, plus per-brand counters using the existing `next_order_number` pattern (`INSERT … ON CONFLICT DO UPDATE … RETURNING`).
- **Tests required:** two brands creating their first expense, batch and pickup on the same day; concurrent creates in one brand.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-013
**Tables with tenant data that RLS cannot protect: analytics materialized views**

- **Area / category:** Database (index/idempotency/RLS) — RLS coverage
- **Severity:** Medium (QA-C Medium (QA-A Low))
- **Status:** Verified
- **Independent verification:** QA-C: Confirmed
- **Duplicates (same defect, other reports):** SA-TEN-013
- **Evidence:** 7 MVs in `analytics` with `brand_id`, granted SELECT to app_user (`harden_app_user_and_rls_bypass.sql` §4); handlers filter by brand (`GetDailyStoreRevenue.cs:29-33`, `GetDashboard.cs:33-60`).
- **Observed behaviour:** a brand-A session read 76 brand-A and 76 brand-B rows from `mv_daily_store_revenue`.
- **Impact:** isolation is app-only. Any new report endpoint that forgets `Where(BrandId)` leaks revenue and customer LTV across tenants.
- **Remediation:** expose MVs only through `security_barrier` views filtering `brand_id = kernel.current_brand_id() OR kernel.rls_bypass()`, and revoke direct MV SELECT.
- **Tests required:** an A session sees only A rows through the views.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-MOB-006
**Offer→accept dispatch mode is not wired end-to-end (no rider UI, offers hidden from the task list, no notification)**

- **Area / category:** Mobile/delivery/maps — Feature completeness / dispatch.
- **Severity:** Medium
- **Status:** Verified.
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** - The endpoints exist (`RiderSelfEndpoints.cs:64-65`). - The rider app has no call to `/accept` or `/decline` (grep of `rider-mobile/src`, `rider-mobile/app`). - `RiderTaskMapper.cs:11-12` `OpenStatuses = ["assigned","accepted","started","arrived"]` excludes `offered`. - The `assignment.offered` outbox event (`AutoDispatchService.cs:445`) has no consumer (grep). - The mode can be enabled by a platform admin (`db/patches/dispatch_offer_states.sql`, `dispatch_permissions.sql`, `DispatchConfig.cs:160-170`).
- **Observed behaviour:** Enabling `offer_accept` means every offer expires unseen and pickups fall back to push after `MaxOfferRounds`, with added latency.
- **Impact:** Configurable but non-functional dispatch mode, and misleading platform settings.
- **Remediation:** - Either block enabling `offer_accept` until it ships, or add: an offer card in rider home/tasks (include `offered` in a separate query), accept/decline calls, and push on `assignment.offered`. - In both cases fix the race in SA-MOB-002.
- **Tests required:** rider sees the offer; accept → task appears; decline → re-offered.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-007
**Riders get no push notification for new or changed assignments; the app learns of work only by 30 s polling while foregrounded**

- **Area / category:** Mobile/delivery/maps — Real-time / notifications.
- **Severity:** Medium
- **Status:** Verified (code search).
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** - Riders register push tokens (`RiderSelfEndpoints.cs:58,147-156`; `RiderPushToken.cs:32-62`). - No notification is enqueued for a rider recipient on assign/auto-assign/cancel (grep of `RecipientType` "rider"; `AutoDispatchService.cs:354-371` writes an outbox event with no consumer; `PickupCommands.cs:259-264` writes no notification). - Polling: `rider-mobile/src/hooks/useRiderTasks.ts:57`.
- **Observed behaviour:** A newly assigned or cancelled job is invisible until the rider opens the app.
- **Impact:** Slow pickups and missed cancellations (compounds SA-MOB-001).
- **Remediation:** Map `assignment.auto_assigned`, manual assign and leg cancellation to a notification for the rider's push tokens through the existing `ExpoPushChannelSender`.
- **Tests required:** assign → notification row for the rider recipient.
- **Specialist's dependencies / priority note:** P1. Depends on SA-MOB-017 (FCM config) for Android delivery. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-013
**Serviceability, zones and service areas are not enforced anywhere in the booking or dispatch path**

- **Area / category:** Mobile/delivery/maps — Delivery-zone / operating-region control.
- **Severity:** Medium
- **Status:** Verified (code).
- **Independent verification:** QA-C: Confirmed
- **Evidence:** - `operations.Application/Catalog/Customer/Self/Queries/SelfQueries.cs:89-116` checks pincode equality only. - `customer-mobile/src/hooks/useCatalog.ts:172-179` defines `useServiceability`, but it has no caller. - `CustomerSchedulePickupHandler` / `CreatePickup` (`PickupCommands.cs:52-120,336-470`) perform no serviceability check. - Slots are brand-wide, and the slot's store becomes the pickup store (`PickupCommands.cs:364-373`; `pickup.tsx:202-206`). - `RiderRanker` has no maximum distance (`RiderRanker.cs:56-82`). - There are no polygon columns or spatial queries despite PostGIS.
- **Observed behaviour:** Customers anywhere can book. The assigned store is whichever store owns the chosen slot, possibly far from the customer, and riders can be auto-assigned regardless of distance.
- **Impact:** Unfulfillable orders and wrong-store routing. A multi-tenant region definition is missing.
- **Remediation:** 1. Enforce `CheckServiceability(address.pincode)` server-side in pickup and parcel creation. 2. Resolve the store from the serviceable territory/store before slot selection, and filter slots by that store. 3. Later, add an optional `service_area GEOGRAPHY(POLYGON)` on stores/territories with `ST_Covers`.
- **Tests required:** unserviceable pincode → 422; slots filtered by resolved store.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-014
**Customer tracking is a status timeline only, and pickup progress never reflects rider start or arrival**

- **Area / category:** Mobile/delivery/maps — Tracking consistency.
- **Severity:** Medium
- **Status:** Verified.
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** - `GetMyOrderTrackingHandler` returns history only (`OrderQueries.cs:207-232`). - The customer tracking screen expects `rider_dispatched`/`arrived` (`customer-mobile/app/(app)/orders/tracking/[id].tsx:57-66`). - The constant exists (`SharedDataModel/Enums/PickupRequestStatus.cs:7`), but nothing writes it. - `UpdateMyTaskStatus.cs` only advances the pickup request on `completed` (`:135-141`); on `collected` the pickup target is `null` (`:84-85`).
- **Observed behaviour:** The pickup shows `assigned` until the drop at the store. There is no rider location or ETA.
- **Impact:** Customer/rider/admin views diverge, generating support load.
- **Remediation:** - Map leg `started→pickup rider_dispatched` and `arrived→arrived` in `UpdateMyTaskStatusHandler` and the geofence. - Later, expose a customer-scoped "rider approaching" endpoint, available only while the leg is `started`/`arrived` for the caller's own request, returning a coarse location and rider first name. Stop it on terminal states.
- **Tests required:** started leg → pickup status `rider_dispatched`.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-016
**Customer order cancellation does not cancel or release the order's delivery legs**

- **Area / category:** Mobile/delivery/maps — Workflow propagation.
- **Severity:** Medium
- **Status:** Verified (QA-C)
- **Independent verification:** QA-C: Confirmed
- **Evidence:** - `operations.Application/Orders/Orders/Commands/CancelOrderByCustomerCommand.cs:35-111` updates the order, history, outbox and refund only. - Compare `CustomerPickupCommands.cs:395-407`, which cancels legs and decrements load. - Cancellable statuses: `StateMachineStrategyBase.cs:62-63` (`placed`, `pickup_scheduled`).
- **Observed behaviour:** For orders, including parcel orders, that already have a leg, the rider keeps the job. Combined with SA-MOB-001, the rider can still "complete" it.
- **Impact:** Wasted trips and phantom deliveries.
- **Remediation:** Reuse the pickup-cancel block: cancel active legs by `order_id`, decrement load, and notify the rider (SA-MOB-007).
- **Tests required:** cancel order → legs cancelled.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-SOLID-006
**Coupon rules are implemented three times and have drifted. First-order and eligibility rules are never enforced, and validate-apply trusts client totals.**

- **Area / category:** OOP/SOLID — Duplicated business rules / encapsulation
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - The three copies are `CreateOrderCommand.cs:L268-L338` (with `Math.Round`), `CustomerCouponHandlers.cs:L59-L150` (no rounding, L110-L112) and `CustomerPickupCommands.cs:L46-L100` (with rounding). - `IsFirstOrderOnly` and `CustomerEligibility` are only read and written by admin CRUD (`CouponHandlers.cs:L90-L93`, `L153-L156`). The commerce handler's XML doc claims step 4 enforces first-order-only (`CustomerCouponHandlers.cs:L50`), but no code does. - `ValidateApplyCouponHandler` takes `OrderId` and `OrderSubtotal` from the client (`CommerceDtos.cs:L592-L597`), never loads the order, inserts a redemption and increments `CurrentUsageCount` (L121-L143).
- **Observed behaviour:** "first order only" coupons are usable on any order. Any authenticated customer can burn a coupon's global usage cap through `/customer/coupons/validate-apply` (live endpoint `CouponsCustomer.cs:L21`; the mobile app defines but doesn't call it, `customer-mobile/src/api/commerce.ts:L101-L109`).
- **Impact:** promotion leakage, and the shared coupon budget can be exhausted by one customer.
- **Remediation:** extract `CouponEligibilityPolicy.Evaluate(coupon, customerStats, subtotal)` (pure, in commerce.Application or shared) and call it from all three. Implement first-order and eligibility checks. Make validate-apply load the order (brand, customer and subtotal from the DB), or remove the endpoint.
- **Tests required:** unit tests of the policy (rounding, cap, minimum, first order, eligibility); an integration test that validate-apply rejects a foreign or mismatched order.
- **Specialist's dependencies / priority note:** P2. Related area: BUSINESS / API. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-SOLID-009
**Anemic, fully mutable shared data model. Bounded contexts write each other's tables, and Domain projects are empty.**

- **Area / category:** OOP/SOLID — Encapsulation / domain invariants / bounded-context separation
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-ARCH-002
- **Evidence:** - 143 entities, 3,556 public setters, 0 behaviour methods (`SharedDataModel/Entities/`). One 144-set context (`LaundryGharDbContext.cs`). `core.Domain`, `operations.Domain` and `commerce.Domain` hold only csproj files. - `IOperationsDbContext.cs:L78-L108` exposes commerce and finance sets (`Payments`, `Coupons`, `CouponRedemptions`, `LoyaltyPointsLedger`, `PackageUsageLedger`, `CashBookEntries`). The "READ-ONLY here" note (L80-L83) is a comment only. CreateOrder writes coupon, loyalty and package ledgers, and the rider flow writes `Payments`. - In the other direction, `ICommerceDbContext.cs:L84` exposes `Orders` (written by `RecordOfflinePaymentCommand.cs:L174-L180`). Commerce …
- **Observed behaviour:** no type owns any invariant. SA-SOLID-001, 002, 003, 006 and 011 are concrete results of this.
- **Impact:** every new vertical or rule copies field-level mutation code. Ownership of money ledgers is unclear. - Recommended remediation (proportionate, no rewrite): DB-first scaffolding is a reasonable trade-off, so keep it. Add small domain services, or entity partial-class methods (EF tolerates them), for the few high-value invariants: order transition (001), payment projection (002), coupon policy (006) and dispatch assignment (011). Narrow the …
- **Remediation:** See source report.
- **Tests required:** the ones listed under each dependent finding, plus an architecture test that handlers outside the owning context don't call `.Add` or `.Update` on a foreign context's sets.
- **Specialist's dependencies / priority note:** P2 (incremental). Trade-off: database-first is deliberate (`LaundryGharDbContext.cs:L17-L33`). The defect is that nothing compensates for the missing invariant owner. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-SOLID-010
**God-handlers with no direct tests: `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `OAuth`**

- **Area / category:** OOP/SOLID — SRP / testability
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `CreateOrderCommand.cs:L51-L806` is one method with five injected dependencies (L35-L39) and about 15 responsibilities (section markers L62-L756). - `UpdateMyTaskStatus.cs:L37-L280`. - `OAuth.cs:L47-L886` is static, uses the concrete context and renders HTML. `ResolveDefaultBrandIdAsync` (`L607-L626`) hard-codes a single default brand (`CustomerAuth:DefaultBrandCode`, defaulting to `LG-MAIN`). Related area: TENANCY. - Tests: `grep` for `CreateOrderHandler`, `UpdateMyTaskStatus`, `CancelOrderHandler`, `ValidateApplyCoupon`, `RazorpayWebhook`, `SubscriptionBilling`, `AutoDispatch`, `RiderRanker`, `NotificationDispatcher` and `RoutingChannelSender` in `tests/` returns 0 hits each. OAuth is …
- **Observed behaviour:** the most business-critical code paths have no regression net. Defects 001 to 003 and 006 sit in exactly these untested areas.
- **Impact:** See source report.
- **Remediation:** extract pure collaborators from CreateOrder: `IOrderPricingService` (lines and add-ons), `IDiscountPipeline` (coupon, loyalty, package, promotions), `IOrderFactory` (Order, history, outbox; also used by `CreateParcelOrderCommand.cs:L100-L200`). Unit-test them with the InMemory pattern already used (`tests/operations.Tests/Catalog/Import/ImportTestSupport.cs:L23-L27`). Add `tests/commerce.Tests`.
- **Tests required:** golden-total tests for CreateOrder (express, add-ons, coupon, loyalty, GST, unregistered franchise); rider-flow state tests; webhook idempotency tests.
- **Specialist's dependencies / priority note:** P1 for tests, P2 for refactor. Related area: QA. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-VERT-006
**Notification templates are a hardcoded laundry/logistics status switch; the vertical-tagged event catalog is never read**

- **Area / category:** Verticals/domain — Shared service coupling
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed
- **Evidence:** - `commerce.Infrastructure/Worker/Channels/NotificationChannelPreferencePolicy.cs:43-60` hardcodes `(event, status)` → template. It covers `pickup_scheduled`, `picked_up`, `ready`, `out_for_delivery`, `delivered`, `fulfillment.lost`, and so on. - It is called from `NotificationMappingService.cs:214`. - `db/patches/phase2_slice_j_notification_event_catalog.sql:28-49` creates `engagement_cms.notification_event_catalog` with a `vertical_key`. grep shows no C# reader.
- **Observed behaviour:** Salon statuses (`booked`, `confirmed`, `checked_in`, `completed`, `no_show`) map to `null`, so no customer notification is sent. Adding a vertical requires a code change in the commerce worker. The DB catalog drifts silently from code.
- **Impact:** Every new vertical needs a code deploy for notifications, which contradicts "config, not code" (`PLATFORM_STRATEGY.md` §3).
- **Remediation:** Have `ResolveTemplate` read the catalog (cached), keeping the switch as a fallback. Add salon/recurring rows.
- **Tests required:** Template-resolution tests per mode, driven by catalog rows.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-VERT-007
**Clients are laundry-shaped and only superficially vertical-aware; the order DTO does not expose the mode**

- **Area / category:** Verticals/domain — Client architecture
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-FE-004
- **Evidence:** - Admin web — transitions. `admin-web/src/pages/orders/orderStatus.ts:1-60` hardcodes the laundry transition map ("Client-side mirror of … OrderStateMachine"). `OrderDetailDrawer.tsx:504` renders status buttons from it via `advanceableTargets(order.status)`. By contrast, POS uses the backend's `order.allowedTransitions` (`pos-web/src/pages/orders/OrderDetailPage.tsx:50`), which the backend populates from the strategy (`OrderDtos.cs:131-137`). - Admin web — mode list. `admin-web/src/lib/fulfillment.ts:9-13` has no `recurring`. - Order DTO. `OrderDtos.cs:35,149` exposes `JobType` but not `FulfillmentMode` or `VerticalKey`, so all four clients infer the mode from `jobType === 'parcel'`. - …
- **Observed behaviour:** The admin drawer shows laundry actions (for example `picked_up → received`) on parcel orders, which the backend then rejects with 422. A courier brand's customer app shows a "Laundry" booking option.
- **Impact:** Terminology leakage (the risk `PLATFORM_STRATEGY.md` §12 itself names), operator confusion, and failed actions. Every new vertical requires edits in four apps.
- **Remediation:** Expose `fulfillmentMode` and `verticalKey` on `OrderDto`. Switch the admin drawer to `allowedTransitions`, as POS already does. Drive the customer FAB options from the brand's vertical and modes. Wire the server terminology pack into customer, rider and POS.
- **Tests required:** Admin drawer test that a parcel order shows the backend-provided targets. Customer-mobile test that a logistics brand shows no laundry option.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-API-013
**Hard-coded "Laundry Ghar" brand identity in customer-facing messages and emails**

- **Area / category:** Backend/API — White-label / multi-tenancy
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** QA-A: Confirmed
- **Duplicates (same defect, other reports):** SA-ONB-006
- **Evidence:** `NotificationMappingService.cs:423-437` (all fallback SMS/WhatsApp bodies say "Laundry Ghar" and use ₹); `core.Application/Identity/Settings/EmailTemplates.cs:14-49`; `core.Infrastructure/Email/SettingsMailer.cs:82-97`; `RazorpayPaymentGateway.cs:183`.
- **Observed behaviour:** any tenant without a configured template sends customers messages branded as another company.
- **Impact:** brand leakage and loss of trust for every non-LaundryGhar tenant.
- **Remediation:** render fallbacks with `{brand.name}` and the brand currency symbol, and source the email header from brand white-label settings.
- **Tests required:** fallback body for a brand named "X" contains "X" and not "Laundry Ghar".
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-FE-009
**Server terminology is wired only in admin-web; customer, rider and POS ship hardcoded laundry copy**

- **Area / category:** Frontend/clients — Laundry coupling / multi-vertical UX
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `customer-mobile/src/lib/terminology.ts`, `rider-mobile/src/lib/terminology.ts` and `pos-web/src/lib/terminology.ts` all say words "come from `GET /api/v1/terminology`", but no API call and no non-test import exist in any of the three apps (a grep for `terminology` outside the lib and tests returns nothing). - Copy: `customer-mobile/src/i18n/locales/en.json:3,88,92,114,165-166,188` ("What needs washing?", "garment", "20% off your first wash", "Laundry pickup"); 21 laundry-term lines in customer `en.json`. - POS: `components/print/GarmentTags.tsx`, `pages/orders/OrderDetailPage.tsx:154` ("Garment Tags"). - Rider: inspection is laundry "garment condition" (`src/api/tasks.ts:9`). - …
- **Impact:** a salon or courier tenant sees laundry vocabulary, which the strategy doc names as a top product risk. The code looks vertical-ready only because it carries dead helper modules.
- **Remediation:** add a `useTerminology()` hook (copy the admin version) to the three apps and route item nouns through `itemNoun()`. Move vertical-specific strings into terminology keys.
- **Tests required:** render tests with a salon pack asserting no "garment" or "wash" text appears.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-ONB-001
**"Go live" and custom domains are database rows that no request path can reach**

- **Area / category:** Onboarding/white-label — Provisioning / Routing / White-label
- **Severity:** Medium (originally High; QA-A Medium vs QA-B High; orchestrator: Partially Verified and no tenant depends on custom domains today; raise to High when Phase 4 starts)
- **Status:** Partially Verified (code and config read end-to-end; YARP's default Host rewrite is library behaviour, not executed)
- **Independent verification:** QA-B: Confirmed (canonical, G8)
- **Duplicates (same defect, other reports):** SA-OPS-013, SA-TEN-009
- **Evidence:** - `backend/laundryghar/laundryghar.Gateway/Program.cs:L45-55` (`MakeRoute`): routes match on path only. The only transform is `PathPattern`, with no `RequestHeaderOriginalHost`. - `laundryghar.Gateway/Program.cs:L283-290,L310-316`: comments claim X-Forwarded-Host is forwarded. - `laundryghar.ServiceDefaults/Extensions.cs:L267-283`: `ForwardedHeaders = XForwardedFor | XForwardedProto` only, so `Request.Host` is never rewritten from X-Forwarded-Host. - `deploy/docker-compose.yml:L87-89`: identity and engagement clusters are `http://core:8080`, so core sees Host `core`. - `core.Infrastructure/Services/BrandResolver.cs:L67-71,L106-142`: the Host lookup uses `context.Request.Host.Host`. - …
- **Observed behaviour:** - `POST /provider-onboarding/go-live` returns `<code>.laundryghar.app` and the wizard marks the brand "live". - In a real request through the Gateway, the Host is the upstream name, so `resolve_brand_domain('core')` returns null and the result is negatively cached for 30 s. - No certificate is issued, there is nothing to serve on that host, and a browser app on the domain would be CORS-blocked. - `BrandResolverHostTests` pass only because they …
- **Impact:** - Business: "live on a sub-domain in minutes" is reported as achieved when it is not. - Docs: `docs/TASKS.md` T-09 and T-17 are marked `Done`, and the T-17 acceptance item "brand reachable on its sub-domain" is contradicted by the code.
- **Remediation:** 1. Add a YARP `RequestHeaderOriginalHost=true` transform, or add `ForwardedHeaders.XForwardedHost` with `AllowedHosts` tied to verified domains. 2. Use Host in `CustomerBrandResolver` as well. 3. Make CORS dynamic for verified domains. 4. Pick an option for OQ-6, preferably Cloudflare-for-SaaS or Caddy on-demand TLS with an `ask` endpoint backed by `resolve_brand_domain`. 5. Do not mark a brand "live" until a storefront exists and `ssl_status='active'`.
- **Tests required:** Gateway-level integration test (Host header in → brand resolved upstream); a CORS test for a verified domain.
- **Specialist's dependencies / priority note:** P1. Depends on the OQ-6 decision and on a storefront. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-ONB-005
**Back-office `CreateBrand` creates an unprovisioned tenant with an implicit vertical**

- **Area / category:** Onboarding/white-label — Provisioning consistency
- **Severity:** Medium
- **Status:** Verified (code)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-VERT-009
- **Evidence:** - `core.Application/Identity/TenancyOrg/Brands/Commands/CreateBrand/CreateBrand.cs:L17-49` and `Dtos/BrandDtos.cs:L9-18`: no `VerticalKey` in the request. - `Brand.VerticalKey` defaults to `Laundry` (`SharedDataModel/Entities/TenancyOrg/Brand.cs:L16`). - No owner, membership, own franchise, `brand_feature` rows, subscription or catalogue are created. Compare `CompleteSignup.cs:L97-235`. - Endpoint: `AdminBrands.cs:L28`.
- **Observed behaviour:** A platform-created brand is always "laundry". With `Entitlement:Enforced=true` (`core.WebApi/appsettings.json:L12-14`) it has only core features, and no one can log in to it until memberships are granted by hand.
- **Impact:** There are two provisioning paths that produce different shapes. Sales-led onboarding is error-prone, and the vertical cannot be chosen.
- **Remediation:** Make `CreateBrand` require a `TemplateKey` and reuse `TemplateProvisioner`, plus owner invite, franchise and trial, through a shared `BrandProvisioner` service. Keep a single code path.
- **Tests required:** Admin-created brand has the template vertical, features and own franchise. A request without a template returns 422.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-ONB-008
**No client implements signup, the provider wizard, branding or the white-label app config; mobile is one hardcoded build**

- **Area / category:** Onboarding/white-label — Application provisioning / UX completeness
- **Severity:** Medium
- **Status:** Verified (searched all four clients)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-FE-007, SA-FE-008
- **Evidence:** - No `signup`, `provider-onboarding` or `white-label` calls exist in `admin-web/src`, `pos-web/src`, `customer-mobile/{src,app}` or `rider-mobile/{src,app}`. - `admin-web/src/App.tsx:L57-116` has only `/login`, `/accept-invite` and the authenticated pages. - `customer-mobile/app.config.ts:L8-30` hardcodes the name, slug, bundle id and package, and the brand comes from the build-time `DEFAULT_BRAND_CODE` (`L56`; `src/constants/config.ts:L49`). - `customer-mobile/eas.json` has empty submit credentials and a placeholder EAS project id (`app.config.ts:L4-6`). - `GetAppConfig.cs:L46-97` output has no consumer.
- **Observed behaviour:** The onboarding funnel can only be exercised with raw API calls. White-label mobile apps would require hand-edited configs.
- **Impact:** Target steps 1, 3 and 6 are not deliverable to an end user.
- **Remediation:** 1. Add an anonymous signup page in admin-web (or a small separate onboarding SPA) and a wizard panel that reads `/provider-onboarding`. 2. For T3, make `app.config.ts` read a JSON produced from `GetAppConfig` (`BRAND_CONFIG_PATH`) with per-brand EAS profiles. 3. Do not fork source per brand.
- **Tests required:** e2e signup → OTP → login → wizard shows steps.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-OPS-016
**Mobile release pipeline is not operational (OTA, CI, white-label)**

- **Area / category:** DevOps/production — Release strategy
- **Severity:** Medium
- **Status:** Verified (config read plus CI logs)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `customer-mobile/app.config.ts:4-6` (`EAS_PROJECT_ID = 'laundryghar-customer'`, with a TODO saying OTA returns 404) and `rider-mobile/app.config.ts:6` (same). `eas.json:6-47` defines dev/preview/prod channels but `submit.production` credentials are empty (L48-59). No EAS workflow exists in `.github/workflows`. The bundle id is fixed (`app.config.ts:19,27`) and the brand is chosen by the build-time `DEFAULT_BRAND_CODE`. Mobile CI jobs 108549585491/108549585512 fail. A positive: `MobileAppConfig.IsForceUpdate` exists server-side (`SharedDataModel/Entities/EngagementCms/MobileAppConfig.cs:20`). I did not trace client consumption.
- **Impact:** no OTA hotfix channel, no reproducible store builds, and per-tenant branded store apps would need a build matrix that does not exist.
- **Remediation:** run `eas project:init` and commit the real UUIDs. Add `eas build`/`eas update` workflows gated on mobile CI. Decide between a single multi-brand app and a per-tenant build matrix.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P2. Related area: FE/MOB. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-TEN-006
**Anonymous public tenant content (banners, app-config, onboarding slides) returns nothing under enforced RLS**

- **Area / category:** Multi-tenancy — Tenant branding delivery / RLS design
- **Severity:** Medium
- **Status:** Partially Verified. The SQL analogue was reproduced (S5) and the code read; I did not hit the HTTP endpoint.
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `PublicEngagement.cs:L29-45` explicitly assumes "RLS cannot be relied upon here" and adds `.Where(BrandId)` (e.g. `GetPublicBanners…:L20-27`). - Core sets `bypass_rls` only for the auth, signup and OAuth paths (`core.WebApi/Program.cs:L543-561, L620-646`). `/api/v1/public` is not among them. - `engagement_cms.app_banners`, `mobile_app_config` and `onboarding_slides` have RLS enabled with `rls_brand` (`rls_enable_engagement_cms.sql:L27-36`; `rls_proposal.sql:L218-219`).
- **Observed behaviour:** S5 (an anonymous session with an explicit `brand_id=A` predicate) returned 0 rows. An explicit predicate cannot widen RLS.
- **Impact:** a pre-login branded mobile experience (Q8) is empty whenever RLS is enforced. If it renders in some environment, that environment is not running as an RLS subject.
- **Remediation:** add a narrowly scoped SECURITY DEFINER read function per public resource (the house pattern used by `brand_app_identity`), or add a `FOR SELECT` policy that permits `status='active' AND is_active` rows to anonymous sessions only when they carry a resolved brand GUC. In that case, publish the anonymous resolved brand into a dedicated GUC.
- **Tests required:** an integration test calling the public endpoints as `app_user` with RLS on.
- **Specialist's dependencies / priority note:** P1. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/02-multitenancy.md`](docs/audit/specialists/02-multitenancy.md)

### SA-AUTHZ-008
**ABAC engine is inert; attribute-based rules are hand-coded per handler**

- **Area / category:** Authorization (RBAC/ABAC) — ABAC readiness
- **Severity:** Medium
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `AbacOptions.cs:15` (`Enabled` default false). - No `Abac` key in any `appsettings*.json` or `deploy/` file (grep). - `AbacAuthorizationService.EvaluateAsync` returns `Skipped` when disabled. - `AbacAuthorizationHandler.cs:73-78` succeeds when the endpoint declares no target. No endpoint declares one (no `AbacResource`/`RequireAbac`/`IAbacAuthorizationService` usage in core/operations/commerce). - `AbacAuthorizationHandler.cs:97-103` succeeds on evaluator exceptions (fail-open by design for shadow).
- **Observed behaviour:** all ABAC-like decisions (ownership, store/franchise, rider assignment, resource state) are hand-written at roughly 75 `IsWithinScope` sites plus self-filters. The `requires_scope` flag (0027) projects only into inert policy rows.
- **Impact:** there is no central policy engine to enforce new ABAC rules. Each new resource depends on developers remembering checks, which is how SA-AUTHZ-002/003 arose.
- **Remediation:** keep RBAC authoritative and turn on shadow mode for one module (commerce first, per the plan) to collect parity data. Change the handler's exception path to deny once in enforce mode.
- **Tests required:** an endpoint with `AbacResource` in enforce mode denies on a policy deny and on an evaluator exception.
- **Specialist's dependencies / priority note:** P2 · **Consolidated roadmap phase:** P5
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-DB-017
**The per-row plpgsql scope predicate makes RLS scans about 8× slower**

- **Area / category:** Database (index/idempotency/RLS) — Performance
- **Severity:** Medium
- **Status:** Verified (synthetic)
- **Independent verification:** QA-C: Not re-run
- **Evidence:** `0031_subbrand_scope_rls.up.sql:60-110` (`LANGUAGE plpgsql`, called per row).
- **Observed behaviour:** brand-A `count(*)` over 30k orders took 384 ms with RLS and 48 ms with bypass. The E2 idempotency probe took 421 ms.
- **Impact:** reports, counts and number generation slow down linearly with tenant size.
- **Remediation:** rewrite as an inlinable SQL expression over `(SELECT kernel.current_scope_nodes())`, e.g. `'platform' = ANY(nodes) OR ('store:'||store_id) = ANY(nodes) OR …`.
- **Tests required:** plan test showing an InitPlan rather than a per-row function call.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P5
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-OPS-015
**Horizontal-scaling blockers (DB-Q8 infrastructure view)**

- **Area / category:** DevOps/production — Scalability
- **Severity:** Medium
- **Status:** Verified (code read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** in-process OutputCache with eviction that does not fan out (`OutputCaching.cs:27-29`, acknowledged in the code). IMemoryCache with short TTLs for token version (15 s), brand status (30 s), ABAC policy (15 s) and feature catalog (5 min) (`TokenVersionStore.cs:16`, `BrandStatusStore.cs:15`, `PolicyCache.cs:36`, `FeatureCatalog.cs:21`). In-memory rate limiters per instance (SA-OPS-001/002). Local file storage (SA-OPS-003). In-process workers (SA-OPS-005). Pooling: Npgsql default (max 100 per connection string per process), `EnableRetryOnFailure(3)`, no PgBouncer. RLS GUCs are set session-level on every `ConnectionOpened` (`RlsConnectionInterceptor.cs:92-100`) and cleared by Npgsql's `DISCARD …
- **Impact:** replicas × 100 can exceed the managed PG `max_connections`. The obvious mitigation, transaction-mode PgBouncer, would break tenant isolation unless the GUC strategy changes. Revocation and suspension propagate per instance within TTL bounds, which is acceptable.
- **Remediation:** set an explicit `Maximum Pool Size` per host and document the connection budget. If PgBouncer is adopted, use session pooling, or move to `set_config(...,true)` inside explicit transactions. Use Redis for OutputCache and rate limits before running more than one replica.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P2. Related area: DB. · **Consolidated roadmap phase:** P5
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-API-023
**Development credentials and OTP master codes committed to appsettings**

- **Area / category:** Backend/API — Secrets management
- **Severity:** Low
- **Status:** Verified (key names only; values not copied)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `core.WebApi/appsettings.Development.json`, `operations.WebApi/appsettings.Development.json` and `commerce.WebApi/appsettings.Development.json` contain non-empty `ConnectionStrings:Default` (user `app_user`, host localhost) and `ConnectionStrings:Admin` (user `postgres`, host localhost). `core.WebApi/appsettings.Development.json:21-23` contains `Otp:TestCode` / `Otp:CustomerTestCode`. Non-Development `appsettings.json` files contain no secrets. No `.env`, key or pem files are tracked (`git ls-files`). The startup guard rejects test codes only when `IsProduction()` (`core.WebApi/Program.cs:136-145`), so a Staging environment with these keys set would accept master OTPs.
- **Impact:** low (localhost dev values), but reused passwords or a mis-set environment name would expose them.
- **Remediation:** move dev values to user-secrets or `.env`, and extend the guard to every non-Development environment.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. Related area: SEC. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-API-025
**Plaintext email addresses in logs**

- **Area / category:** Backend/API — Privacy / logging
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `GoogleLoginHandler.cs:78`, `InviteEmailSender.cs:46`, `SetPersonStatus.cs:85`, `SettingsMailer.cs:58,65,70`. By contrast OTP SMS logs mask the phone (`Msg91OtpDispatcher.cs:59`), and DevLog OTP output is Development-only (`OtpChannelPlanner.cs:47-48`).
- **Impact:** PII in centralised logs (DPDP minimisation).
- **Remediation:** mask with the same helper used for phones.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. Related area: SEC. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-AUTHZ-013
**Identity-axis (`user_type`) gates where permission gates belong; platform-scoped dispatch settings reachable by brand admins**

- **Area / category:** Authorization (RBAC/ABAC) — Hardcoded authorization
- **Severity:** Low
- **Status:** Verified (code read; DB block inferred from policy text)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `AdminSettings.cs:181-185` (`UserType == "brand_admin"`). `UpdateDispatchSettings.cs:38-39` upserts a platform row (`brandId: null`) and is reachable by brand admins with `settings.manage` (only `offer_accept` needs `dispatch.mode.manage`, L27). The write is stopped only by `system_settings` WITH CHECK (`0027…up.sql:28-30`), which yields an unhandled DB error rather than a 403.
- **Impact:** correctness depends on the DB backstop. The user_type gate already mis-fired once (A-2).
- **Remediation:** replace `Forbidden()` with permission/`IsPlatformAdmin` checks. Make dispatch settings platform-only in the handler.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3 · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-FE-014
**WebMCP exposes customer search and order-status mutation to in-browser AI agents in production builds**

- **Area / category:** Frontend/clients — Security hardening
- **Severity:** Low
- **Status:** Verified (code); a browser with the API was not available
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `admin-web/src/App.tsx:123-125` calls `initWebMCP(router)` unconditionally; `src/lib/webmcp.ts:83-215` registers `search_customers` (returns names and phones), `list_orders`, `get_order`, `update_order_status` and `open_admin_page`. There is no `import.meta.env.DEV` guard (unlike `Agentation`, `App.tsx:136`).
- **Impact:** in browsers that enable WebMCP, any agent (including one steered by prompt injection from page content) can read customer PII and change order states with the admin's privileges.
- **Remediation:** gate behind `import.meta.env.DEV`, or behind an explicit opt-in setting plus a confirmation step for mutations.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-FE-015
**Web routers have no error boundary**

- **Area / category:** Frontend/clients — Failure recovery
- **Severity:** Low
- **Status:** Verified (grep: no `errorElement` or `ErrorBoundary` in `admin-web/src` or `pos-web/src`; both mobile apps wrap the app in `ErrorBoundary`, `customer-mobile/app/_layout.tsx:23,308`)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** See source report.
- **Impact:** a render error, or a lazy-chunk 404 after a redeploy (all routes use `lazy()`), shows React Router's default "Unexpected Application Error" with no recovery.
- **Remediation:** add a root `errorElement` with a reload button and handling for chunk-load errors.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-FE-016
**Shared-device residue after logout (POS cart PII, rider offline queue, mobile query caches)**

- **Area / category:** Frontend/clients — Session hygiene / privacy
- **Severity:** Low
- **Status:** Verified (code)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `pos-web/src/stores/cartStore.ts:32,70` persists the selected `AdminCustomerDto` to `localStorage`, and `Topbar.tsx:16-27` logout does not clear the cart. `rider-mobile/src/store/offlineQueueStore.ts:16` uses a global AsyncStorage key that logout (`src/store/authStore.ts:54-70`) does not clear. No client calls `queryClient.clear()` on logout (grep).
- **Impact:** the next staff member sees the previous customer's details in the POS basket. Rider B's session replays rider A's queued status PATCHes; the server should reject them, but they are noise. Earlier users' cached lists flash briefly.
- **Remediation:** clear the cart, offline queue and query cache in every logout path.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-MOB-020
**Customer slot listing has no in-handler brand predicate (RLS-only), contrary to its comment**

- **Area / category:** Mobile/delivery/maps — Tenant isolation (defence in depth).
- **Severity:** Low
- **Status:** Partially Verified (RLS application not verified).
- **Independent verification:** QA-C: Not re-verified (moot today)
- **Evidence:** - `operations.Application/Orders/Delivery/Queries/DeliverySlotQueries.cs:49-53`: the comment claims "explicit brand predicate is the in-handler defense-in-depth", but the query has none. - The endpoint passes no brand (`CustomerOrderEndpoints.cs:292-296`).
- **Impact:** If RLS is not active for this connection, customers see other brands' store slots and IDs.
- **Remediation:** Pass `u.BrandId` and filter `s.BrandId == brandId`.
- **Tests required:** cross-brand slot not returned.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-MOB-021
**Map provider keys stored unencrypted and echoed to every settings reader**

- **Area / category:** Mobile/delivery/maps — Secrets handling.
- **Severity:** Low
- **Status:** Verified (code).
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** `core.Application/Identity/Settings/Commands/UpdateMaps/UpdateMaps.cs:52` (`isEncrypted: false`); `GetAdminSettings.cs:44` returns `GoogleApiKey`/`MapboxToken` (Read permission at `AdminSettings.cs:50`).
- **Impact:** These are browser keys, intrinsically public once used, but without HTTP-referrer or URL restrictions (not verifiable from the repo) they can be abused for quota and billing.
- **Remediation:** Document that keys must be referrer-restricted, and mask them in the GET except for `settings.manage`.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-OPS-011
**Health checks never check the database**

- **Area / category:** DevOps/production — Operability
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** QA-B: Confirmed
- **Duplicates (same defect, other reports):** SA-API-024
- **Evidence:** `ServiceDefaults/Extensions.cs:172-203`: only the `self` check exists, so `/health` and `/alive` are equivalent. `Gateway/HealthServicesEndpoint.cs:52-101`: `/health/services` probes `/health` (comments say `/health/ready`, which is not mapped), is unauthenticated and is published on the public gateway.
- **Impact:** a DB outage still reports healthy, and service topology is disclosed publicly.
- **Remediation:** add an Npgsql readiness check tagged `ready` on `/health`. Keep `/alive` for liveness only. Restrict `/health/services` to internal access.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-OPS-017
**Container and supply-chain hygiene**

- **Area / category:** DevOps/production — Hardening
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** base images use floating tags (`Dockerfile:15,25` `sdk:10.0`/`aspnet:10.0`; `admin-web/Dockerfile:16,33` `node:22-alpine`/`nginx:alpine`; compose `postgres:18`). Actions are pinned to major versions, not SHAs (`ci.yml`, `release.yml`). There is no image scan or SBOM. `backend/laundryghar/.dockerignore:1-6` does not exclude `appsettings.Development.json`, which holds local `app_user`/`postgres` credentials and OTP test codes, so those files are copied into images (not loaded in Production). `admin-web/deploy/nginx.conf:21-24` has no CSP or HSTS.
- **Impact:** See source report.
- **Remediation:** pin digests and SHAs, add Trivy/Grype to release, exclude Dev JSONs, add a CSP.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-QA-002
**Gateway rate-limit unit tests assert the vulnerable behaviour**

- **Area / category:** QA (security) — Test quality / regression lock-in. Related area: OPS, TEN.
- **Severity:** Low
- **Status:** Verified (read)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Related:** SA-API-002
- **Evidence:** `backend/laundryghar/tests/operations.Tests/Auth/RateLimitPartitioningTests.cs`: - `:63-69` `the_x_brand_id_header_takes_precedence` - `:71-77` `the_forwarded_client_ip_is_used_when_present` (leftmost XFF trusted) - `:27-37` `two_brands_on_the_same_ip_do_not_share_a_budget`
- **Observed behaviour:** the suite encodes exactly the bypass and tenant-DoS properties in SA-API-002. A correct fix will turn these tests red, which invites someone to "fix the fix" back.
- **Impact:** false assurance; the regression is locked in.
- **Remediation:** replace them with negative tests (see the next section, T9). Mark the old ones as documenting the defect until the fix lands.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P1, with SA-API-002. (I considered two more candidates and did not raise them: the refund-cap trigger's apply path is covered by SA-API-009, SA-DB-006 and SA-DB-002; the residual fail-open on wallet and loyalty tables after 0031 is a correction to … · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/10a-qa-verification-security.md`](docs/audit/specialists/10a-qa-verification-security.md)

### SA-QB-003
**Production guidance for ForwardedHeaders contradicts itself, and both options are unsafe as coded**

- **Area / category:** QA (platform) — Configuration / documentation (Related area: OPS, SEC)
- **Severity:** Low
- **Status:** Partially Verified (config and code read; not executed)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `backend/laundryghar/PRODUCTION_ENV.md:135-150` says to enable `ForwardedHeaders__Enabled=true` on all services. - `deploy/docker-compose.yml:25` says it "stays OFF on the services". - `deploy/README.md:55` says to set it on the gateway only. - `ServiceDefaults/Extensions.cs:273-283`: when it is enabled, `KnownIPNetworks` and `KnownProxies` are cleared, so `X-Forwarded-For` is trusted from any peer.
- **Observed behaviour:** with it OFF, SA-API-001 applies (one auth bucket for the whole platform). With it ON, the services trust any forwarded IP header without a proxy allow-list, and per-IP limits then rely on header handling at the gateway that is not proven.
- **Impact:** operators cannot follow the docs and end up safe.
- **Remediation:** keep one documented setting. Enable it on the services with `KnownIPNetworks` set to the compose or cluster network, have the gateway overwrite `X-Forwarded-For`, and remove the conflicting text.
- **Tests required:** an integration test with two clients through a YARP hop gives two independent buckets; a spoofed `X-Forwarded-For` from an untrusted peer is ignored.
- **Specialist's dependencies / priority note:** P1, before the SA-API-001 fix. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/10b-qa-verification-platform.md`](docs/audit/specialists/10b-qa-verification-platform.md)

### SA-SOLID-014
**The pickup flow enforces minimum order value and sets the expected COD from client-supplied prices**

- **Area / category:** OOP/SOLID — Business rule placed on the client side
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `PickupCommands.cs:L81-L93` computes `estimatedAmount = req.EstimatedAmount ?? Σ(EstimatedUnitPrice × qty)` from the request and runs `MinOrderValueRule` against it. The rider's expected cash comes from `EstimatedAmount` (`PickupCommands.cs:L196-L202`, `L242`). The server prices the actual order later in CreateOrder (L138-L209), which re-checks the minimum (L215-L217).
- **Impact:** the pickup-stage minimum can be bypassed (send a large estimate), and the rider COD expectation is client-controlled. Final billing is still computed server-side, which limits the damage.
- **Remediation:** re-price cart lines server-side with `PriceResolver` when item IDs are present, and treat the client estimate as display-only.
- **Tests required:** a pickup with an inflated `EstimatedAmount` against a server-priced cart below the minimum is rejected.
- **Specialist's dependencies / priority note:** P3. Related area: BUSINESS. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-SUB-016
**Webhook idempotency is check-then-act on status only: no event dedupe, no lock or concurrency token, no amount check on brand paylinks**

- **Area / category:** Subscription/billing — Payments / idempotency (a-brand and b). Related area: DB, PAY.
- **Severity:** Low
- **Status:** Verified (code read); race not reproduced
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `ProcessPaylinkWebhook.cs:L109-L115` and `RazorpayWebhookHandler.cs:L186-L233` read the row, check status, and write it. No `SELECT … FOR UPDATE`, no `IsConcurrencyToken`/xmin anywhere in `laundryghar.SharedDataModel` (grep), and no processed-event table keyed on the Razorpay event id or `x-razorpay-event-id`. - The brand paylink handler does not compare `amount_paid`/currency with `inv.Amount`.
- **Observed behaviour:** duplicate deliveries are harmless in sequence (status check). Concurrent duplicates of `payment.captured` can both pass and emit two `payment.captured` outbox events. Positive controls: partner wallet top-up uses an idempotency key (`ProcessPartnerPaylinkWebhook.cs:L99-L115`); refunds have a unique `idempotency_key` and a DB cap trigger (`payment_idempotency.sql:L12-L20`).
- **Impact:** low for brand invoices (idempotent end state); possible double side effects downstream of the customer outbox.
- **Remediation:** a `webhook_events(event_id PK, received_at)` insert-first dedupe, or a conditional `UPDATE … WHERE status='pending'` with a row-count check. Verify `amount_paid` on the paylink.
- **Tests required:** two concurrent identical webhooks produce one outbox event.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

### SA-FE-012
**Customer payments, wallet top-up and packages are not implemented in the UI**

- **Area / category:** Frontend/clients — Feature completeness
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `customer-mobile/app/(app)/booking/pay.tsx:586-594` ("UPI & Card — coming soon"; `initiatePayment` in `src/api/commerce.ts:149-156` has no caller); `src/constants/config.ts:116` (`walletTopUp: false`); `useAvailablePackages` / `useMyPackages` (`src/hooks/useCommerce.ts:51-65`) are not referenced under `app/`; `rider-mobile/app/(app)/notifications.tsx:1-26` is a static placeholder.
- **Impact:** customers can pay only by wallet or COD; prepaid packages exist in admin but cannot be bought.
- **Remediation:** wire the existing initiate/verify APIs behind the Razorpay SDK, and add a packages screen.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-API-022
**Inconsistent error contract; no API versioning**

- **Area / category:** Backend/API — API contract
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `ExceptionHandler.cs:198-232,368-369` (custom envelope; exception type names used as error keys); endpoints return bare `Results.NotFound()`, `Results.Unauthorized()` and `BadRequest(string)` (for example `CustomerOrderEndpoints.cs:73,84`, `RazorpayWebhook.cs:45`); the gateway assumes `application/problem+json` (`laundryghar.Gateway/Program.cs:141-156`); a `DbUpdateConcurrencyException` would become a generic 400 (`ExceptionHandler.cs:327-345`); no `Asp.Versioning` package (grep).
- **Impact:** clients must handle three error shapes, and breaking changes cannot be versioned per tenant.
- **Remediation:** adopt `AddProblemDetails` with the envelope as an extension, return typed errors consistently, and map concurrency to 409.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08-backend-api.md`](docs/audit/specialists/08-backend-api.md)

### SA-ARCH-009
**Layering is nominal: `Utilities` is a cross-cutting god-library; composition roots are copy-pasted per host**

- **Area / category:** Architecture — Maintainability / dependency hygiene
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `laundryghar.Utilities.csproj:9-32` — `FrameworkReference Microsoft.AspNetCore.App`, SharedDataModel, MailKit, Npgsql, OpenAPI; contains CQRS, ABAC PDP/store, middlewares, email sender, audit interceptor, output caching (file inventory). - Every `*.Application` references Utilities (`core.Application.csproj:24-29`, `operations.Application.csproj:33-37`), so Application code can (and does) use ASP.NET types: `IFormFile` in commands, e.g. `operations.Application/Logistics/RiderSelf/Commands/UploadProofPhoto/UploadProofPhoto.cs:5,22` (6 files). - Per-context DbContext interfaces expose raw `DbSet<T>` with very wide surfaces (e.g. `IOperationsDbContext` ~80 sets spanning 9 schemas), so they …
- **Impact:** Each new vertical module would copy another ~150 lines of host wiring; cross-cutting changes (e.g. a new middleware) must be made three times.
- **Remediation:** Split Utilities into `Platform.Abstractions` (CQRS interfaces, Result, ICurrentUser — no ASP.NET) and `Platform.Web` (middlewares, auth handlers, OpenAPI); add one `AddPlatformWebDefaults()/UsePlatformPipeline()` extension used by every host; add NetArchTest rules.
- **Tests required:** Architecture tests; a middleware-order test per host.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/01-architecture.md`](docs/audit/specialists/01-architecture.md)

### SA-ARCH-011
**MCP downstream URLs are not wired for AppHost or compose; synchronous core→operations coupling**

- **Area / category:** Architecture — Configuration / inter-service coupling
- **Severity:** Low
- **Status:** Partially Verified (config read; not run)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `core.WebApi/Program.cs:100-118,408-436` builds keyed HttpClients from `DownstreamServices:{Catalog,Orders}BaseUrl`; defaults are `https://localhost:7254` (`core.WebApi/appsettings.json:28-31`), `http://localhost:5056` (Development), `http://localhost:5002` (`Mcp/Infrastructure/Http/DownstreamClients.cs:24-25`). Neither `AppHost.cs:56-62` nor `deploy/docker-compose.yml` sets them (grep `DownstreamServices` in `deploy/` → none), while operations actually listens on 5302 / `operations:8080`.
- **Observed behaviour:** MCP tools would call a non-existent host in both the Aspire dev loop and the compose deployment unless an operator sets env vars that no runbook mentions.
- **Impact:** The only synchronous inter-service dependency is broken-by-default; it also shows the MCP feature is laundry-specific (`LaundryTools`, `core.WebApi/Mcp/Tools/LaundryTools.cs`) code living in the platform-core host.
- **Remediation:** Inject `DownstreamServices__*` in AppHost and compose (point at the gateway or operations), or call the operations Application handlers in-process if hosts are consolidated.
- **Tests required:** Startup config test asserting non-default downstream URLs outside Development.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/01-architecture.md`](docs/audit/specialists/01-architecture.md)

### SA-AUTHZ-015
**Partner isolation is a single (RLS-only) layer**

- **Area / category:** Authorization (RBAC/ABAC) — Defence-in-depth
- **Severity:** Low
- **Status:** Partially Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `GetPartnerBookingTrack.cs:31-33` and `GetMyPartnerBookingsQuery` filter by id only. Isolation is `rls_partner` (`db/patches/rls_partner.sql:54-67`, `rls_partner_dispatch.sql:38`).
- **Impact:** See source report.
- **Remediation:** add explicit `PartnerId ==` predicates from `ICurrentUser`/claims.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3 · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/06-abac-rbac.md`](docs/audit/specialists/06-abac-rbac.md)

### SA-DB-019
**Soft-delete tables: no partial unique constraints**

- **Area / category:** Database (index/idempotency/RLS) — Integrity
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** live, 30 of 30 uniques on tables with `deleted_at` are non-partial; `docs/SCHEMA_FULL.sql:17` states the opposite convention.
- **Impact:** deleted coupon, service or store codes and deleted customers' phones block reuse.
- **Remediation:** partial uniques where reuse is a product requirement.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-020
**Staff identity is globally unique by email and phone**

- **Area / category:** Database (index/idempotency/RLS) — Multi-tenant data model
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** `users_email_key`, `users_phone_e164_key` (`UserConfiguration.cs:47-48`).
- **Impact:** a consultant or franchisee cannot hold accounts in two independent tenants.
- **Remediation:** product decision. Either a global person with per-brand memberships (the current model, which then needs cross-brand membership support) or a brand-scoped unique.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-FE-013
**admin-web `/settings` gate drifts from server authorization; the route map is hand-synced**

- **Area / category:** Frontend/clients — Client/server authz drift
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `admin-web/src/hooks/usePermissions.ts:46-51` and `components/layout/RequirePermission.tsx:24-31` gate on `user_type in (platform_admin, brand_admin)`, but the server now gates on `permission:settings.read` / `settings.manage` (`core.WebApi/Endpoints/Identity/AdminSettings.cs:31-64`). `lib/routePermissions.ts:1-23` says the server modules table must be mirrored manually because the navigator DTO omits `requiredPermission` (`GetNavigator.cs:85`).
- **Impact:** a custom role granted `settings.manage` is blocked by the UI; the stale comments contradict R3-SEC-3, which is fixed server-side. The security impact is nil because the server is authoritative.
- **Remediation:** gate on `hasPermission('settings.read')`. Add `requiredPermission` to `NavItemDto` and derive route gates from it.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/09-frontend-mobile.md`](docs/audit/specialists/09-frontend-mobile.md)

### SA-MOB-019
**Riders retain indefinite access to customer PII for historical tasks**

- **Area / category:** Mobile/delivery/maps — Privacy / data minimisation.
- **Severity:** Low
- **Status:** Verified (code).
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** `GET /rider/tasks?date=` (`RiderSelfEndpoints.cs:84,307-319`) maps through `RiderTaskMapper` with `CustomerName`, `CustomerPhone` (`RiderTaskMapper.cs:120-130`) and the address line for any past date.
- **Impact:** Ex-customer contact details are available to partners long after service, contrary to DPDP minimisation.
- **Remediation:** Mask phone and address for completed or failed legs older than N hours in `RiderTaskMapper` (earnings needs only order number, amount and time).
- **Tests required:** past-date task DTO has masked PII.
- **Specialist's dependencies / priority note:** P2. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/12-mobile-delivery-maps.md`](docs/audit/specialists/12-mobile-delivery-maps.md)

### SA-SOLID-011
**Dispatch and assignment logic is duplicated across bounded contexts and has drifted**

- **Area / category:** OOP/SOLID — Duplicated business rules / misplaced responsibility
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - Manual assignment `AssignPickupHandler` (`PickupCommands.cs:L204-L278`) seeds `CodAmount` (L242, L255) and emits no outbox event. - Auto assignment `AutoDispatchService.AssignPickupAsync` (`commerce.Infrastructure/Worker/Services/AutoDispatchService.cs:L301-L376`) seeds no `CodAmount` but emits `assignment.auto_assigned`. - Twin helpers: `RiderLoad` / `RiderLoadHelper` (paths in the table above), and `ResolvePickupCodAmount` in `PickupCommands.cs:L196` and `PickupCod.cs:L17`. - Partly mitigated: the rider "collected" step re-derives COD (`UpdateMyTaskStatus.cs:L69-L74`).
- **Impact:** drift in expected cash between assignment and collection for auto-dispatched pickups. Assignment rules live in a commerce worker, away from the logistics code.
- **Remediation:** a single `PickupAssignmentService` in operations.Application, invoked by both the handler and the worker (the worker can resolve it from a scope). Delete `RiderLoadHelper` and the duplicate COD helper.
- **Tests required:** auto and manual assignment produce identical `DeliveryAssignment` fields and events.
- **Specialist's dependencies / priority note:** P3. Related area: LOGISTICS. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-SOLID-012
**Dependency inversion holds by convention only. Application reaches ASP.NET Core and Npgsql through a catch-all Utilities project, and nothing enforces the layering.**

- **Area / category:** OOP/SOLID — DIP / ISP
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `laundryghar.Utilities.csproj` has `FrameworkReference Microsoft.AspNetCore.App` (L10), Npgsql, MailKit and a SharedDataModel reference. It spans 7,514 lines across CQRS, ABAC, auth, middleware, OpenAPI and email. - `IFormFile` appears in Application commands (`UploadProofPhoto.cs:L22`, `UploadInspectionPhoto.cs`, `UploadRiderDocument.cs`, `SubmitPickupInspection.cs`, `ItemImageCommands.cs`, `ItemImportParseCommands.cs`). - `using Npgsql` and `PostgresException` appear in `PickupCommands.cs:L10` and `L500-L508`. `operations.Application.csproj` references Npgsql directly. - DbContext interfaces have 46 to 79 members (ISP). - The unused legacy `ICurrentUserService` …
- **Impact:** low today. Handlers don't misuse the concrete context. But nothing prevents erosion, and Application can't be reused outside ASP.NET.
- **Remediation:** add NetArchTest rules: Application must not depend on `*.Infrastructure`, `LaundryGharDbContext` or `Microsoft.AspNetCore.Http`. Replace `IFormFile` with a `(Stream, contentType, fileName)` value object at the endpoint. Move the unique-violation detection behind `IOperationsDbContext.IsUniqueViolation(ex)`. Delete `ICurrentUserService`.
- **Tests required:** the architecture tests themselves.
- **Specialist's dependencies / priority note:** P3. Trade-off: largely acceptable. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-SOLID-013
**Settings-resolved providers lose their logger (`_logger as ILogger<OtherType>` always evaluates to null)**

- **Area / category:** OOP/SOLID — DI misuse
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** `SettingsFirstPaymentGateway.cs:L113-L127` (`Build`, cast at L124) passes `_logger as ILogger<RazorpayPaymentGateway>` where `_logger` is `ILogger<SettingsFirstPaymentGateway>`. The two types are unrelated sealed classes, so the covariant cast fails and the result is `NullLogger`. The same pattern appears in `RoutingChannelSender.cs:L118` (`WhatsAppCloudChannelSender`) and `L154` (`Msg91SmsChannelSender`).
- **Observed behaviour:** in production (non-Development) the Razorpay `LogError` calls (`RazorpayPaymentGateway.cs:L63`, `L133`, `L195`) and the WhatsApp sender logs are discarded when credentials come from DB settings. The exceptions still propagate.
- **Impact:** See source report.
- **Remediation:** inject `ILoggerFactory` and call `CreateLogger<RazorpayPaymentGateway>()`.
- **Tests required:** a unit test that the built inner gateway receives a non-null logger.
- **Specialist's dependencies / priority note:** P3. Related area: OBSERVABILITY. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/07-oop-solid.md`](docs/audit/specialists/07-oop-solid.md)

### SA-VERT-008
**Catalog discriminator is inert, and the service model is laundry-shaped**

- **Area / category:** Verticals/domain — Domain model
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `Item.cs:14-16` and `ItemConfiguration.cs:18` store `catalog_kind`, but nothing in backend or clients branches on it (grep `\.CatalogKind\b` and `catalogKind`). - `ItemCommands.cs:38` defaults new items to `laundry_garment` whatever the brand's vertical. - `CatalogKind.DefaultFor` (`CatalogKind.cs:32-37`) omits tiffin, whereas `TemplateProvisioner.CatalogKindFor` (`:146-152`) maps tiffin → `product`. - `Service.cs` carries TAT/express/QC flags and no duration. - `CreateOrderCommand.cs:194` hardcodes `UnitOfMeasure = "piece"`.
- **Observed behaviour:** A salon item created in admin is labelled `laundry_garment`. The vertical-to-kind mapping exists twice and disagrees.
- **Impact:** Data quality; future per-kind logic would misbehave on existing rows.
- **Remediation:** Use one `CatalogKind.DefaultFor` (add tiffin), call it from `ItemCommands` using the brand's vertical, and delete the duplicate in `TemplateProvisioner`.
- **Tests required:** Unit test of `DefaultFor` for all four verticals. Item creation on a salon brand yields `service`.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P3
- **Source:** [`docs/audit/specialists/04-verticals.md`](docs/audit/specialists/04-verticals.md)

### SA-ONB-009
**The vertical cannot be changed through any governed path, and a raw change would not re-provision**

- **Area / category:** Onboarding/white-label — Business-type lifecycle
- **Severity:** Low
- **Status:** Verified (code)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - No command assigns `Brand.VerticalKey` except signup (repo-wide grep; `CompleteSignup.cs:L114`). - The DB trigger blocks changes only once orders exist (`db/patches/phase0_multi_vertical.sql:L75-99`). - `TemplateProvisioner` runs only at signup.
- **Observed behaviour:** Before the first order, an operator with SQL access can change the vertical. Features, catalogue `catalog_kind`, roles and templates then stay those of the old vertical. After the first order, the change is impossible.
- **Impact:** A low risk today, because there is no API. It becomes a hazard once support tooling is built.
- **Remediation:** A platform-only `ChangeBrandVertical` command that is allowed only before the first order and re-runs feature expansion. Document it as a platform-admin action, with an audit row.
- **Tests required:** a change after orders exist returns 409; a change before orders re-expands features.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-ONB-011
**`go-live` ignores wizard prerequisites, and adding a primary custom domain can strand the brand off its verified host**

- **Area / category:** Onboarding/white-label — Provisioning correctness
- **Severity:** Low
- **Status:** Partially Verified (prerequisite bypass Verified by code read; primary-domain demotion collision Suspected, SQL reasoning only)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `OnboardingCommands.cs:L148-159`: `GoLive` does not consult `brand_onboarding_facts`, so it succeeds with zero locations or items despite `TASKS.md` T-17 stating "two steps cannot be skipped". - The catalogue step is "done" with seeded unpriced items (`GetOnboardingState.cs:L53-58`). - `AddBrandDomain.cs:L60-73` demotes the existing (verified) primary when an unverified domain is added as primary. That drops `PrimaryDomain` (the facts query requires a verified primary). - A later `ensure_brand_subdomain` takes the `ON CONFLICT … SET is_primary=true` path (`0017:L154-159`), which would collide with `idx_brand_domains_one_primary` (`0002:L58-60`).
- **Impact:** A brand can be shown as "live" while unable to take a priced order. Re-going-live can error.
- **Remediation:** 1. In `GoLive`, require the location and catalogue facts, plus at least one priced item. 2. In `AddBrandDomain`, never demote a verified primary in favour of an unverified domain. Promote only after verification.
- **Tests required:** go-live with no location → 422; add an unverified primary → the existing verified primary is kept.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-ONB-012
**White-label app identifiers can collide between brands**

- **Area / category:** Onboarding/white-label — White-label mobile
- **Severity:** Low
- **Status:** Verified (code + existing test data)
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `core.Application/Identity/WhiteLabel/Queries/GetAppConfig.cs:L89-90,L106-114`: `IdentifierSegment` strips all non-alphanumerics. - `tests/core.Tests/WhiteLabel/AppIdentifierTests.cs:L23-41` asserts `LG-MAIN → lgmain` and `7 → b7`. - Brand codes are unique only as given (`brands.code UNIQUE`), so `LG-MAIN` and `LGMAIN`, or `7` and `B7`, map to the same `com.laundryghar.<seg>.customer`.
- **Impact:** Two tenants could get the same bundle id. Store bundle ids are immutable after first submission.
- **Remediation:** Persist a per-brand `app_identifier` with a UNIQUE constraint, allocated once (with a suffix on collision) instead of being derived on every call.
- **Tests required:** two colliding codes → distinct identifiers.
- **Specialist's dependencies / priority note:** P3 (before T-22 ships). · **Consolidated roadmap phase:** P4
- **Source:** [`docs/audit/specialists/05-onboarding-whitelabel.md`](docs/audit/specialists/05-onboarding-whitelabel.md)

### SA-DB-016
**Pooled-connection tenant context is safe for EF, but rests on session-level GUCs**

- **Area / category:** Database (index/idempotency/RLS) — RLS runtime
- **Severity:** Low
- **Status:** Partially Verified
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** `DependencyInjection.cs:60-64,86-96`; `RlsConnectionInterceptor.cs:30-55,93-107`.
- **Observed behaviour:** in SQL, a session-level `set_config` persisted into the "next request" on the same connection (orders visible), and `DISCARD ALL` cleared it. EF re-runs the interceptor on every open and writes all 12 GUCs, so the EF path is safe.
- **Impact:** - Raw `NpgsqlDataSource` paths (ABAC) do not set GUCs. That is safe today because they fail closed (SA-DB-014). - A transaction-pooling PgBouncer would break the model: no session affinity, and `DISCARD ALL` is unavailable. - Setting `No Reset On Close=true` in a connection string would make leakage depend solely on the interceptor.
- **Remediation:** document "session pooling only" for any pooler. Consider `set_config(…, true)` inside an EF transaction for write paths. Add a startup assertion that `No Reset On Close` is not set.
- **Tests required:** pool-reuse test across two tenants.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P5
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-DB-021
**No DB-level booking overlap prevention; the salon schema is inaccessible to app_user**

- **Area / category:** Database (index/idempotency/RLS) — Integrity / vertical readiness
- **Severity:** Low
- **Status:** Verified
- **Independent verification:** QA-C: Not re-verified
- **Evidence:** 0 exclusion constraints live; `salon_fulfillment` has no USAGE for app_user (live `permission denied for schema`); 0 C# references.
- **Impact:** the salon vertical is DB-only and not usable. When it is wired, appointment overlap must be prevented at the DB.
- **Remediation:** `CREATE EXTENSION btree_gist; ALTER TABLE salon_fulfillment.resource_bookings ADD EXCLUDE USING gist (resource_id WITH =, tstzrange(start_at,end_at) WITH &&) WHERE (status <> 'cancelled')`, plus grants and kernel-helper policies.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3 (before salon GA). · **Consolidated roadmap phase:** P5
- **Source:** [`docs/audit/specialists/08b-database.md`](docs/audit/specialists/08b-database.md)

### SA-OPS-018
**Spec claims infrastructure that does not exist (broker, Redis, Hangfire, Serilog, S3)**

- **Area / category:** DevOps/production — Documentation accuracy
- **Severity:** Informational
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Duplicates (same defect, other reports):** SA-ARCH-012, SA-VERT-011
- **Evidence:** `PRODUCTION_SPEC.md:104, 114, 118` lists Serilog, Hangfire, MassTransit, Redis, RabbitMQ and S3/Azure Blob. The csproj package list has none of them. `IEventPublisher` is `LoggingEventPublisher` in all environments (`commerce.WebApi/Program.cs:290`; `Stubs/LoggingEventPublisher.cs:17-30`), so outbox rows are marked `published` after a log line. Consumers poll the DB directly (e.g. PartnerBookingDebit, LoyaltyEarn, NotificationMapping).
- **Impact:** readiness assessments based on the spec overstate capability.
- **Remediation:** update the spec to "DB-polled outbox, no broker" until a broker is introduced. Nothing currently requires one.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P1
- **Source:** [`docs/audit/specialists/11-devops.md`](docs/audit/specialists/11-devops.md)

### SA-SUB-019
**Entitlement change propagation and fail-open behaviour (informational)**

- **Area / category:** Subscription/billing — (a-brand) Consistency
- **Severity:** Informational
- **Status:** Verified
- **Independent verification:** Not independently re-verified by QA (specialist evidence accepted; see report)
- **Evidence:** - `PermVersionBumper.cs:L36-L44` bumps only brand-scope members after an entitlement change, so franchise- and store-scoped tokens keep old permissions for up to `AccessMinutes` (15, `JwtSettings.cs:L11`). - `BrandStatusStore.cs:L47-L53` and `FeatureCatalog.cs:L64-L70` fail open on DB errors.
- **Impact:** bounded lag of 15 minutes or less. A DB outage disables suspension (documented trade-off).
- **Remediation:** bump every member whose scope resolves to the brand (join franchises and stores). This is acceptable to defer.
- **Tests required:** See source report and docs/audit/08-test-strategy.md.
- **Specialist's dependencies / priority note:** P3. · **Consolidated roadmap phase:** P2
- **Source:** [`docs/audit/specialists/03-subscription.md`](docs/audit/specialists/03-subscription.md)

## 3. Duplicate IDs (preserved, pointing to canonical)

| Duplicate ID | Canonical | Original severity | Title (as reported) | QA correction | Source |
|---|---|---|---|---|---|
| SA-API-006 | [SA-SOLID-001](#sa-solid-001) | High | Order state machine is not enforced atomically, and the rider delivery path bypasses it |  | `08-backend-api.md` |
| SA-API-010 | [SA-SUB-004](#sa-sub-004) | High | Platform (tenant) subscription renewals run without the worker RLS bypass, so renewal invoices are never … |  | `08-backend-api.md` |
| SA-API-011 | [SA-SUB-002](#sa-sub-002) | High | Paylink webhook ignores `past_due` invoices, so a late-paying tenant is never reinstated |  | `08-backend-api.md` |
| SA-API-014 | [SA-OPS-005](#sa-ops-005) | Medium | Workers have no claim locking or leader election; stuck rows are never recovered; there is no real event … |  | `08-backend-api.md` |
| SA-API-017 | [SA-OPS-003](#sa-ops-003) | Medium | File storage is local-disk only and upload content checks are unenforced |  | `08-backend-api.md` |
| SA-API-024 | [SA-OPS-011](#sa-ops-011) | Low | Health checks are shallow and the gateway aggregate probe cannot reach services in the compose topology |  | `08-backend-api.md` |
| SA-ARCH-002 | [SA-SOLID-009](#sa-solid-009) | Medium | Domain layer is empty; business rules live in large transaction scripts and are duplicated across hosts |  | `01-architecture.md` |
| SA-ARCH-003 | [SA-VERT-001](#sa-vert-001) | High | Brand vertical is never applied when an order is created; salon/tiffin brands get laundry orders labelled … |  | `01-architecture.md` |
| SA-ARCH-004 | [SA-VERT-002](#sa-vert-002) | High | Salon and tiffin verticals are scaffolding (strategy + SQL), not operable modules |  | `01-architecture.md` |
| SA-ARCH-005 | [SA-API-003](#sa-api-003) | Medium | CQRS pipeline behaviours (validation, transaction, audit, caching, exception) are dead code | QA-B: 39 dead validators, not 40. | `01-architecture.md` |
| SA-ARCH-007 | [SA-OPS-005](#sa-ops-005) | Medium | Background workers run in-process in the commerce API host with no leader election; host cannot be scaled out … |  | `01-architecture.md` |
| SA-ARCH-008 | [SA-DB-002](#sa-db-002) | High | No reproducible schema source of truth: the documented bootstrap does not create columns/schemas the EF model … | QA-B/DB: understated — migrate.sh up fails at 0005; the documented bootstrap cannot complete. | `01-architecture.md` |
| SA-ARCH-012 | [SA-OPS-018](#sa-ops-018) | Low | Architecture documents materially out of date with the code |  | `01-architecture.md` |
| SA-AUTHZ-005 | [SA-TEN-002](#sa-ten-002) | Medium | Commerce host never publishes `customer_id` (A0.6 fail-open persists) or the subject GUCs |  | `06-abac-rbac.md` |
| SA-AUTHZ-006 | [SA-TEN-001](#sa-ten-001) | High | 0031 RESTRICTIVE sub-brand policy denies every customer request and every non-platform commerce-host request … |  | `06-abac-rbac.md` |
| SA-AUTHZ-010 | [SA-API-002](#sa-api-002) | Medium | Gateway rate-limit partition keyed on client-controlled `X-Brand-Id` / unverified JWT |  | `06-abac-rbac.md` |
| SA-AUTHZ-014 | [SA-TEN-008](#sa-ten-008) | Low | Suspension gate fails open and skips the partner lane |  | `06-abac-rbac.md` |
| SA-DB-001 | [SA-TEN-001](#sa-ten-001) | Critical | Customer sessions blocked by RESTRICTIVE `rls_subbrand_scope`: customers cannot read or create orders, … | QA-C: scope broader — commerce-host non-platform staff are also denied. | `08b-database.md` |
| SA-DB-006 | [SA-API-009](#sa-api-009) | High | The refund cap (trigger + app check) is not concurrency-safe: over-refund reproduced |  | `08b-database.md` |
| SA-DB-008 | [SA-API-005](#sa-api-005) | High | Wallet and other balance/counter mutations are read-modify-write with no lock or concurrency token |  | `08b-database.md` |
| SA-DB-011 | [SA-API-004](#sa-api-004) | Medium | Order-creation idempotency and payment capture are check-then-act; the webhook lookup is an unindexed … |  | `08b-database.md` |
| SA-DB-015 | [SA-TEN-007](#sa-ten-007) | Medium | RLS is defence-in-depth only: the bypass GUC is self-settable and platform admins bypass on every request |  | `08b-database.md` |
| SA-DB-022 | [SA-MOB-002](#sa-mob-002) | Low | Rider offer acceptance has no DB uniqueness for the active assignment |  | `08b-database.md` |
| SA-FE-006 | [SA-AUTHZ-012](#sa-authz-012) | Medium | Vertical restrictions are client/navigation-only; the API does not enforce them (Q13) |  | `09-frontend-mobile.md` |
| SA-FE-007 | [SA-ONB-008](#sa-onb-008) | Medium | No business self-signup UI and no brand management UI, although the backend endpoints exist |  | `09-frontend-mobile.md` |
| SA-FE-008 | [SA-ONB-008](#sa-onb-008) | Medium | Branding and white-label: brand is fixed per build, theme is hardcoded, custom domains have no consumer (Q8) |  | `09-frontend-mobile.md` |
| SA-MOB-012 | [SA-OPS-004](#sa-ops-004) | Medium | 14-day location retention is not reliably enforced | QA-C: partman 14-day retention is configured and works per table; it fails because maintenance is unscheduled (SA-OPS-004) and aborts on a stale part_config row (SA-QC-003). | `12-mobile-delivery-maps.md` |
| SA-MOB-015 | [SA-AUTHZ-012](#sa-authz-012) | Medium | Tenant and business type are build-time constants in the mobile apps; vertical does not drive mobile flows or … |  | `12-mobile-delivery-maps.md` |
| SA-MOB-017 | [SA-FE-011](#sa-fe-011) | Medium | Mobile release/CI readiness: rider `npm ci` fails, both typechecks fail, EAS/OTA/submit and FCM are … |  | `12-mobile-delivery-maps.md` |
| SA-MOB-018 | [SA-API-018](#sa-api-018) | Low | Customer app creates pickups without an Idempotency-Key despite server support |  | `12-mobile-delivery-maps.md` |
| SA-ONB-003 | [SA-AUTHZ-012](#sa-authz-012) | High | Business-type (vertical) restrictions fail open for tenant users and are not enforced in API authorization |  | `05-onboarding-whitelabel.md` |
| SA-ONB-004 | [SA-VERT-001](#sa-vert-001) | High | The salon template is public, but orders always run the laundry pipeline (template fulfilment mode is never … |  | `05-onboarding-whitelabel.md` |
| SA-ONB-006 | [SA-API-013](#sa-api-013) | Medium | New tenants' customer notifications and invoices carry LaundryGhar/laundry branding (white-label leak) |  | `05-onboarding-whitelabel.md` |
| SA-OPS-001 | [SA-API-001](#sa-api-001) | High | Core's `auth` rate limiter collapses every user onto the gateway's IP |  | `11-devops.md` |
| SA-OPS-002 | [SA-API-002](#sa-api-002) | High | Gateway rate limit is bypassable and lets one tenant's budget be burned by anyone |  | `11-devops.md` |
| SA-OPS-013 | [SA-ONB-001](#sa-onb-001) | Medium | Custom-domain brand resolution does not survive the gateway hop, and has no TLS automation | QA-B: status should be Partially Verified (same evidence as SA-ONB-001). | `11-devops.md` |
| SA-SOLID-002 | [SA-API-007](#sa-api-007) | High | Online payment capture never updates the order's `AmountPaid` or `PaymentStatus`, so riders are told to … | QA-B: latent today because no client calls payment initiate/verify; orchestrator keeps High because it blocks enabling online payment. | `07-oop-solid.md` |
| SA-SOLID-004 | [SA-VERT-001](#sa-vert-001) | High | The vertical extension seam exists but is bypassed. New verticals still need core edits on the server and in … |  | `07-oop-solid.md` |
| SA-SOLID-005 | [SA-API-003](#sa-api-003) | Medium | The CQRS pipeline behaviours are dead code, and 40 validators never run (including `CreateOrderValidator` and … | QA-B: 39 dead validators, not 40; the channel whitelist DOES have a DB CHECK (04_bc4_order_lifecycle.sql:38). Dispatcher evidence is Dispatcher.cs:15-45 and ServiceCollectionExtensions.cs:14. | `07-oop-solid.md` |
| SA-SOLID-007 | [SA-API-012](#sa-api-012) | High | The notification worker uses another brand's WhatsApp or SMS credentials for every tenant | QA-B: the NotificationSettingsCache TTL is 60 s, not 5 minutes. | `07-oop-solid.md` |
| SA-SUB-010 | [SA-AUTHZ-011](#sa-authz-011) | Medium | Entitlements are enforced only on staff-JWT permission checks; customer-facing APIs, workers and API-key … |  | `03-subscription.md` |
| SA-SUB-017 | [SA-TEN-008](#sa-ten-008) | Low | Suspension gate does not apply to API-key principals |  | `03-subscription.md` |
| SA-SUB-018 | [SA-TEN-008](#sa-ten-008) | Low | Background workers ignore brand suspension and cancellation |  | `03-subscription.md` |
| SA-SUB-020 | [SA-DB-002](#sa-db-002) | Medium | SaaS billing schema lives only in `db/patches/` behind a separate script; migrations 0008/0021 depend on it |  | `03-subscription.md` |
| SA-TEN-004 | [SA-AUTHZ-003](#sa-authz-003) | High | Global staff identities plus unscoped membership writes allow cross-tenant tampering and account takeover |  | `02-multitenancy.md` |
| SA-TEN-005 | [SA-API-002](#sa-api-002) | Medium | Gateway per-tenant rate limiting is keyed on a client-controlled brand id |  | `02-multitenancy.md` |
| SA-TEN-009 | [SA-ONB-001](#sa-onb-001) | Medium | Custom-domain (Host) tenant resolution is unreachable in the shipped topology |  | `02-multitenancy.md` |
| SA-TEN-011 | [SA-DB-004](#sa-db-004) | Low | No composite tenant foreign keys; cross-tenant references are prevented only in handlers |  | `02-multitenancy.md` |
| SA-TEN-012 | [SA-DB-007](#sa-db-007) | Low | Globally unique columns on tenant data leak existence and allow cross-tenant blocking |  | `02-multitenancy.md` |
| SA-TEN-013 | [SA-DB-013](#sa-db-013) | Low | Analytics: app-only isolation over materialized views, global refresh by any tenant |  | `02-multitenancy.md` |
| SA-TEN-014 | [SA-OPS-008](#sa-ops-008) | Informational | Observability and caches are not tenant-aware |  | `02-multitenancy.md` |
| SA-VERT-005 | [SA-AUTHZ-012](#sa-authz-012) | Medium | The vertical boundary is enforced only in navigation and bundle application, not at the API or aggregate level |  | `04-verticals.md` |
| SA-VERT-009 | [SA-ONB-005](#sa-onb-005) | Low | Only self-signup can set a brand's vertical; platform-admin brand creation always yields a laundry brand with … |  | `04-verticals.md` |
| SA-VERT-010 | [SA-DB-002](#sa-db-002) | Medium | Multi-vertical schema lives only in `db/patches/phase*.sql`, which the documented fresh-build path does not … |  | `04-verticals.md` |
| SA-VERT-011 | [SA-OPS-018](#sa-ops-018) | Informational | Documentation contradicts code on multi-vertical status |  | `04-verticals.md` |

