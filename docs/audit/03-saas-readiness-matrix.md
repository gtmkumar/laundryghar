# Report 3 — SaaS Readiness Matrix

Date: 2026-10-09. Status vocabulary: **Fully Supported / Partially Supported / Not Supported / Not Verified**. Finding IDs are canonical entries in [`FINDINGS.md`](../../FINDINGS.md). The priority column uses the roadmap phases in [07-remediation-roadmap.md](07-remediation-roadmap.md), P0 (verified critical risk) to P5. Evidence is cited by finding ID; file paths and line ranges are in the registry entries and the specialist reports.

**Reading guide**
- **Mechanism exists, guarantee fails:** this is the pattern behind most rows. LaundryGhar already has much of the machinery a multi-tenant SaaS needs: a brand-as-tenant model, signed JWT claims, RLS on every brand table, a feature-entitlement catalogue, transactional self-signup, a fulfilment-strategy seam, and pool-safe tenant context. Several of these are wired incorrectly, or are bypassable, on specific lanes.
- **Billing and multi-vertical rows are earlier-stage:** the data model and seams exist, but the lifecycle and the vertical-specific workflows do not run.

## 1. Tenancy and isolation

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Tenant model and identifiers (Platform → Brand → Territory → Franchise → Store; partner tenant) | **Fully Supported** (model) | 02 §current state; `brand_id` on 126 tables; partner `rls_partner` | Staff identities are global; memberships span brands (SA-DB-020) | Low on its own; enables the takeover chain below | Keep the model. Add membership target guards (SA-AUTHZ-003). | P0 |
| Tenant context resolution (JWT claim authoritative; header override only for platform admins) | **Partially Supported** | Positive control in 02/06 (`TenantResolutionMiddleware.cs:38-47`); [SA-ARCH-014](../../FINDINGS.md#sa-arch-014) three diverging implementations; [SA-TEN-001](../../FINDINGS.md#sa-ten-001) customer tokens lack `scope_nodes`; [SA-TEN-002](../../FINDINGS.md#sa-ten-002) commerce omits `customer_id` | Per-lane, per-host context composition | **Critical**: customer and commerce lanes denied under 0031; customers see each other's wallets on commerce | One `TenantContextResolver` for every lane. Customer scope node. Commerce adapter parity tests. | P0 → P1 |
| Application-layer tenant filters | **Partially Supported** | Every handler applies an explicit `RequireBrandId()`; cross-brand IDOR guard on order creation (02 positive controls). 159 handler predicates not individually audited. [SA-MOB-005](../../FINDINGS.md#sa-mob-005): pickup address not owner-checked | No EF global query filters; correctness depends on every handler | High if a predicate is missed | Keep the explicit predicates. Add resource guards (address ownership). Add architecture tests. | P0 (MOB-005), P1 |
| Database RLS (defence in depth) | **Partially Supported** | 126/126 brand tables enforced; A/B isolation proven live (08b). [SA-TEN-007](../../FINDINGS.md#sa-ten-007): self-settable bypass. [SA-DB-003](../../FINDINGS.md#sa-db-003): DEFINER purge/export callable. [SA-DB-005](../../FINDINGS.md#sa-db-005): identity tables without RLS. [SA-DB-012](../../FINDINGS.md#sa-db-012): uuid cast throws. [SA-DB-013](../../FINDINGS.md#sa-db-013): MVs unprotected | The DB trusts the app completely | High | Remove the self-settable GUC. Separate maintenance/worker role. RLS on identity tables. Safe casts. | P0–P3 |
| Cross-tenant referential integrity | **Not Supported** | [SA-DB-004](../../FINDINGS.md#sa-db-004): none of 617 FKs include `brand_id`. [SA-DB-007](../../FINDINGS.md#sa-db-007): global business-number uniques. [SA-QC-002](../../FINDINGS.md#sa-qc-002): scope-blind generators | Composite tenant FKs; tenant-scoped uniques | Medium (defence in depth, functional DoS) | Composite FKs on high-value relations. `UNIQUE(brand_id, number)`. Atomic counters. | P1–P3 |
| Pooled-connection safety | **Partially Supported** | EF interceptor rewrites 12 GUCs per open (positive control). [SA-DB-016](../../FINDINGS.md#sa-db-016): session-level GUCs; raw `NpgsqlDataSource` paths ([SA-DB-014](../../FINDINGS.md#sa-db-014)) | PgBouncer transaction mode incompatible; pooling-on never tested | Medium (scale) | `SET LOCAL` per transaction, or a documented pool topology. Pooling-on test. | P5 |
| Cross-tenant execution paths (workers, notifications, caches) | **Not Supported** | [SA-API-012](../../FINDINGS.md#sa-api-012): one tenant's WhatsApp/SMS credentials used for all. [SA-TEN-008](../../FINDINGS.md#sa-ten-008): workers ignore suspension. [SA-OPS-008](../../FINDINGS.md#sa-ops-008): caches and telemetry not tenant-aware | Brand-keyed worker context | **High**: customer PII flows through another tenant's business account | Brand-keyed credential cache. Lifecycle check in workers. | P0, P1 |

## 2. Tenant lifecycle, onboarding and provisioning

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Self-service signup (backend) | **Partially Supported** | `POST /api/v1/signup/complete` is OTP-first and transactional: brand, owner, franchise, features, catalogue and 14-day trial (05 positive controls). [SA-ONB-010](../../FINDINGS.md#sa-onb-010): no plan choice, no tests | No signup UI ([SA-ONB-008](../../FINDINGS.md#sa-onb-008)) | The anonymous owner receives `users.create`, which feeds [SA-AUTHZ-001](../../FINDINGS.md#sa-authz-001) | Fix AUTHZ-001 first. Add plan selection and tests. Build a signup UI. | P0, P2, P4 |
| Exactly one primary business type | **Partially Supported** | Single NOT NULL column with CHECK; one template per signup (05). [SA-VERT-002](../../FINDINGS.md#sa-vert-002): salon/tiffin public but inoperable. [SA-ONB-005](../../FINDINGS.md#sa-onb-005): back-office always creates laundry. SA-ONB-009: no governed change | Operability check; governed vertical change | High: selling a product that cannot run | Hide non-operable templates now. One provisioning service. | P0, P4 |
| Vertical-driven provisioning (workflows, catalog, roles, terminology) | **Not Supported** | [SA-VERT-001](../../FINDINGS.md#sa-vert-001): orders ignore the vertical. [SA-VERT-004](../../FINDINGS.md#sa-vert-004): laundry SAC on every invoice. [SA-API-013](../../FINDINGS.md#sa-api-013): "Laundry Ghar" in messages | `IVerticalModule.Provision` | High: wrong tax identity, wrong workflow | Order spine selects the strategy from `Brand.VerticalKey`. Tax profile per vertical. | P3 |
| Suspension, cancellation and reactivation | **Partially Supported** | Suspension gate is server-side with safe reasons (03 positive controls). [SA-TEN-003](../../FINDINGS.md#sa-ten-003): suspended brand self-unsuspends via cancel→withdraw (reproduced). SA-TEN-008: HTTP-only | Lifecycle state machine; worker enforcement | High | Guard cancel/withdraw by suspension reason. Enforce in workers. | P0, P1 |
| Tenant deletion, export and erasure | **Partially Supported** | Export bounded to caller's brand (positive control). SA-DB-003: purge DEFINER callable by `app_user`. SA-TEN-008: delete is a soft flag | Maintenance role; verified purge | Medium | Revoke from `app_user` once a maintenance role exists (`RetentionSweepService` needs it). | P0/P1 |
| Custom domains and go-live | **Not Supported** (end to end) | [SA-ONB-001](../../FINDINGS.md#sa-onb-001): Host lost at gateway; no TLS automation (Partially Verified) | Forwarded Host; certificate automation | Medium now, High in Phase 4 | Forward Host and trust it. ACME/wildcard TLS. | P4 |

## 3. Subscription, billing and entitlements

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Plan catalogue (tiers, features, bundles) | **Fully Supported** (catalogue) | Migrations 0005–0008; tier-integrity assertions (03) | Quotas absent ([SA-SUB-008](../../FINDINGS.md#sa-sub-008)); 5 sellable features gate nothing ([SA-SUB-009](../../FINDINGS.md#sa-sub-009)) | Medium: revenue leakage | Add limits; wire gates. | P2 |
| Entitlement enforcement, staff API lane | **Fully Supported** | Token-mint stripping, 402 vs 403, `perm_version` revocation (03, 06) | — | — | Keep | — |
| Entitlement enforcement, customer/partner/API-key/worker lanes | **Not Supported** | [SA-AUTHZ-011](../../FINDINGS.md#sa-authz-011) | `RequireFeature` metadata on every lane | Medium | Endpoint metadata check from an entitlement snapshot | P2 |
| Entitlement integrity (cannot self-grant) | **Not Supported** | [SA-AUTHZ-004](../../FINDINGS.md#sa-authz-004): brand admin self-grants `saas.manage`, marks own invoice paid (verified) | Grant ceiling; platform-only handlers | High | Grant ceiling; control-plane separation ([SA-ARCH-013](../../FINDINGS.md#sa-arch-013)) | P0 |
| Trials, renewals and dunning | **Not Supported** | [SA-SUB-001](../../FINDINGS.md#sa-sub-001), [SA-SUB-003](../../FINDINGS.md#sa-sub-003), [SA-SUB-004](../../FINDINGS.md#sa-sub-004) (PV), SA-SUB-005, [SA-SUB-014](../../FINDINGS.md#sa-sub-014) | Working lifecycle | High: no recurring revenue | Worker scope fix; trial end; state machine | P2 |
| Payment collection for platform invoices | **Not Supported** | [SA-SUB-002](../../FINDINGS.md#sa-sub-002): `past_due` payments ignored. [SA-SUB-007](../../FINDINGS.md#sa-sub-007): owners can't see or pay invoices | Tenant billing UI; paylink for `past_due` | High | Accept `past_due`; tenant billing screens | P2 |
| Subscription → entitlement linkage | **Not Supported** | [SA-SUB-006](../../FINDINGS.md#sa-sub-006) | Projection from subscription state | High | Versioned projection (architect §4.5) | P2 |
| GST-compliant invoicing (platform and tenant) | **Not Supported** | [SA-SUB-013](../../FINDINGS.md#sa-sub-013); SA-VERT-004; [SA-ONB-007](../../FINDINGS.md#sa-onb-007): GSTIN never reaches invoices | Tax profile, GSTIN, SAC per vertical | High (compliance) | Tax profile per vertical and tenant | P2–P3 |
| Franchise-level SaaS subscriptions (ADR-010 module B) | **Not Supported** (data model only) | [SA-SUB-011](../../FINDINGS.md#sa-sub-011) | Billing or removal | Low | Decide scope | P2 |

## 4. Access control

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Authentication (JWT RS256, lanes, refresh) | **Partially Supported** | RS256 pinned, issuer/audience/lifetime on all hosts (06). [SA-API-020](../../FINDINGS.md#sa-api-020): refresh rotation not atomic. [SA-AUTHZ-009](../../FINDINGS.md#sa-authz-009): suspension doesn't revoke sessions. [SA-FE-002](../../FINDINGS.md#sa-fe-002), [SA-FE-003](../../FINDINGS.md#sa-fe-003): client token handling | Atomic rotation; immediate revocation | Medium | Fix rotation; version-bump on suspend | P1 |
| RBAC on backend endpoints | **Partially Supported** | All 514 endpoints gated (06). Identity-admin escalation: SA-AUTHZ-001/002/003/004, [SA-QA-001](../../FINDINGS.md#sa-qa-001) | Rank, scope and type ceilings | **Critical** | Server-derived `user_type`; TargetUserGuard; DB trigger | P0 |
| ABAC | **Not Supported** (inert) | [SA-AUTHZ-008](../../FINDINGS.md#sa-authz-008); SA-DB-014 | — | Medium | Resource guards and DB backstops first; ABAC later | P5 |
| Vertical boundary enforcement | **Not Supported** at the API | [SA-AUTHZ-012](../../FINDINGS.md#sa-authz-012) (dups VERT-005, ONB-003, FE-006, MOB-015) | Feature/vertical metadata on vertical endpoints | Medium | `RequireFeature`/capability gates | P3 |
| Rate limiting and abuse controls | **Not Supported** (as designed) | [SA-API-001](../../FINDINGS.md#sa-api-001): auth limiter is one global bucket (Critical). [SA-API-002](../../FINDINGS.md#sa-api-002): gateway partition client-controlled. [SA-OPS-014](../../FINDINGS.md#sa-ops-014): not per plan | Trusted client IP; verified-claim keys | **Critical** (availability) | Trusted forwarded headers; partition on verified claims | P0, P1 |

## 5. Business verticals and product experience

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Laundry vertical (end to end) | **Partially Supported** | Real across 4 clients for laundry (09, 12). Online capture broken ([SA-API-007](../../FINDINGS.md#sa-api-007)). Refunds never executed ([SA-API-008](../../FINDINGS.md#sa-api-008)). Royalty always 0 ([SA-SOLID-003](../../FINDINGS.md#sa-solid-003)). State-machine bypass ([SA-SOLID-001](../../FINDINGS.md#sa-solid-001)) | Money paths; transition service | High | P0 money fixes; P3 transition service | P0, P3 |
| Courier/parcel (logistics) vertical | **Partially Supported** | Public template; parcel orders cannot advance past `picked_up` in admin ([SA-FE-004](../../FINDINGS.md#sa-fe-004)); no coordinates, so no fares ([SA-MOB-004](../../FINDINGS.md#sa-mob-004)); laundry SAC on parcel invoices | Location module; server transitions | High | P3 Location module; P3 admin uses `allowedTransitions` | P3 |
| Salon / appointments | **Not Supported** | SA-VERT-002, [SA-VERT-003](../../FINDINGS.md#sa-vert-003), SA-DB-021 | Appointment domain, staff capacity, overlap constraints | High if sold | Hide template (P0); build module (P5) | P0, P5 |
| Recurring (tiffin) / marketplace | **Not Supported** | SA-VERT-002; no generator; no inventory or seller model (04) | Modules | — | P5 | P5 |
| Branding and white-label | **Not Supported** | [SA-ONB-002](../../FINDINGS.md#sa-onb-002), SA-ONB-008, SA-API-013 | Write path, runtime theming, mobile build pipeline | Medium | P4 | P4 |
| Admin, POS and user management | **Partially Supported** | Real for laundry. [SA-FE-001](../../FINDINGS.md#sa-fe-001): production admin image omits 6/9 API URLs (reproduced). pos-web not in CI/CD ([SA-OPS-012](../../FINDINGS.md#sa-ops-012)) | Build config; pipeline | High | Pass all URLs; add pos-web to CI | P0, P1 |

## 6. Mobile, delivery and maps

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Customer mobile app | **Partially Supported, blocked** | Real app (12). Under 0031 customers can't list orders/slots or book (SA-TEN-001, reproduced). Online pay placeholder ([SA-FE-012](../../FINDINGS.md#sa-fe-012)). Demo data fallback ([SA-FE-010](../../FINDINGS.md#sa-fe-010)) | Customer lane; payments | Critical (availability) | Fix 0031 lane together with MOB-005 | P0 |
| Delivery-partner (rider) app | **Partially Supported** | [SA-MOB-001](../../FINDINGS.md#sa-mob-001), [SA-MOB-002](../../FINDINGS.md#sa-mob-002), [SA-MOB-003](../../FINDINGS.md#sa-mob-003), SA-MOB-006/007/008 | Leg state machine; uniqueness; OTP; push | High | Leg transition service; partial unique index | P0–P3 |
| Admin delivery management | **Partially Supported** | Live board brand+franchise scoped with stale indicator (12 positive controls). Store-level scope missing; laundry transitions hardcoded | Store scope; server transitions | Medium | P3 | P3 |
| Geocoding, serviceability, zones | **Not Supported** | SA-MOB-004, [SA-MOB-013](../../FINDINGS.md#sa-mob-013) | Location module (geocoder port, ServiceAreaResolver) | High for courier | P3 | P3 |
| Live tracking and location privacy | **Partially Supported** | Rider→admin polling only; retention never runs (SA-OPS-004, [SA-QC-003](../../FINDINGS.md#sa-qc-003)); [SA-MOB-010](../../FINDINGS.md#sa-mob-010) unvalidated pings | Retention; validation; customer view | Medium (DPDP) | Schedule partman; fix `part_config`; validate pings | P0, P1 |
| Mobile release pipeline | **Not Supported** | [SA-FE-011](../../FINDINGS.md#sa-fe-011) (reproduced): CI red; [SA-OPS-016](../../FINDINGS.md#sa-ops-016): no EAS/OTA/FCM | Green CI; EAS profiles | Medium | P1 | P1 |

## 7. Production operations

| Capability | Current Status | Evidence | Gap | Risk | Recommended Action | Priority |
|---|---|---|---|---|---|---|
| Reproducible schema and migrations | **Not Supported** | [SA-DB-002](../../FINDINGS.md#sa-db-002): `build_from_scratch.sh` + `migrate.sh up` fails at 0005 (reproduced twice); `phase*` patches off-path; rebuild reverts the RLS hardening. [SA-OPS-007](../../FINDINGS.md#sa-ops-007): migrations manual | Baseline plus migrations; schema build in CI | High | Freeze a baseline; CI schema build; EF model check | P0/P1 |
| CI/CD and release gating | **Not Supported** | [SA-OPS-006](../../FINDINGS.md#sa-ops-006): release ships from red `main`; CI never green on `main` (runs 3–8); backend tests pass in CI | Release depends on CI | High | `needs: ci`; fix mobile jobs | P1 |
| File storage | **Not Supported** for production | [SA-OPS-003](../../FINDINGS.md#sa-ops-003): uploads in container `/tmp`; S3/Azure throw | Object storage adapter | High (data loss) | Implement S3-compatible provider | P0 |
| Partition maintenance | **Not Supported** | [SA-OPS-004](../../FINDINGS.md#sa-ops-004): launchd on a developer Mac; runway 2026-12-01 per `db/HANDOFF.md`. SA-QC-003: stale config aborts | Scheduled job in deploy | High, possibly Critical if a live DB shares the runway | In-cluster scheduler; fix `part_config` | P0 |
| Background jobs at scale | **Not Supported** beyond one replica | [SA-OPS-005](../../FINDINGS.md#sa-ops-005); [SA-DB-009](../../FINDINGS.md#sa-db-009) | Worker host, advisory locks, SKIP LOCKED | Medium now, High before scale-out | Worker host with locks | P1 |
| Observability (tenant-aware) | **Not Supported** | SA-OPS-008: logs, traces and metrics not tenant-tagged; nothing exported | Tenant tags; exporter | Medium | OTel brand tag; exporter | P1 |
| Backup, restore and DR | **Partially Supported** | Solid scripts with verify (11 positive controls). [SA-OPS-010](../../FINDINGS.md#sa-ops-010): no PITR, no encryption, no proven schedule; no tenant-level restore | PITR; schedule; drills | Medium | WAL archiving; restore drills | P0 (prove schedule), P1 |
| Secrets management | **Partially Supported** | Fails closed at startup (positive control). [SA-OPS-009](../../FINDINGS.md#sa-ops-009): documented provider absent. [SA-TEN-015](../../FINDINGS.md#sa-ten-015): `app_user` password hardcoded in patches. [SA-API-023](../../FINDINGS.md#sa-api-023): dev OTP master codes in appsettings | Secret store | Medium | Secret store; rotate DB password | P1 |
| Horizontal scaling | **Not Supported** | SA-OPS-015 (local storage, in-process caches/limiters/workers) | Stateless hosts | Medium | After P1 items | P5 |

## 8. Summary by area

| Area | Overall | Blocking items |
|---|---|---|
| Tenant isolation | Partially Supported (mechanism) / **Not Supported** (guarantee) | SA-AUTHZ-001, SA-AUTHZ-003, SA-TEN-001, SA-TEN-002, SA-API-012 |
| Tenant lifecycle | Partially Supported | SA-TEN-003, SA-TEN-008 |
| Subscription lifecycle and billing | **Not Supported** (lifecycle) | SA-SUB-001/002/003/004/006/007 |
| Entitlements | Partially Supported | SA-AUTHZ-004, SA-AUTHZ-011 |
| Onboarding | Partially Supported (backend only) | SA-AUTHZ-001, SA-ONB-008, SA-VERT-002 |
| Branding / white-label | **Not Supported** | SA-ONB-001, SA-ONB-002, SA-ONB-008 |
| Vertical provisioning | **Not Supported** | SA-VERT-001, SA-VERT-004, SA-AUTHZ-012 |
| Access control | Partially Supported | SA-AUTHZ-001/002/003/004, SA-API-001 |
| Observability | **Not Supported** | SA-OPS-008 |
| Data isolation (DB) | Partially Supported | SA-TEN-007, SA-DB-005, SA-DB-004 |
| Production operations | **Not Supported** | SA-DB-002, SA-OPS-003, SA-OPS-004, SA-OPS-006 |
| Mobile and delivery | Partially Supported | SA-TEN-001, SA-MOB-001/002/004 |
