# 10b — QA, Testing and Independent Verification (architecture, product, client, operations slice)

Agent key: `qa-b` · AREA code for new findings: `QB` · Date: 2026-10-09 · Branch `claude/brave-dijkstra-6hlddw` (HEAD `a9fedd0`; the only commits after the CI-tested SHA `274b7af` touch `docs/`, so CI results on `274b7af` apply to the code as it is now; checked with `git log 274b7af..HEAD -- . ':!docs/audit'`).

## Scope and method

Reports re-verified: `01-architecture.md` (SA-ARCH-*), `04-verticals.md` (SA-VERT-*), `05-onboarding-whitelabel.md` (SA-ONB-*), `07-oop-solid.md` (SA-SOLID-*), `09-frontend-mobile.md` (SA-FE-*), `11-devops.md` (SA-OPS-*), and the non-security, architecture, integration and ops findings in `08-backend-api.md` (SA-API-001/002/003/014/017). For dedupe I also cross-referenced `02-multitenancy.md`, `03-subscription.md`, `06-abac-rbac.md` and `08b-database.md`. Tenancy, authz, subscription and payment correctness belong to the other QA agent. Where one of my findings falls into that area I only note the dedupe.

Coverage: I re-traced every Critical and High finding in my set (35 findings, one of them Critical) and 17 of about 35 Mediums, choosing the ones whose verdict could change. I also checked 1 Low. For each one I opened the cited files, followed callers and DI, and looked for mitigations the specialist may have missed: DB CHECKs, endpoint filters, request-size limits, `Content-Disposition`, other workers, other scripts, client call sites and CI logs.

I did not change any code. My only repo write is this file. All scratch work lives in `…/scratchpad/qa-b/`.

## Commands run and results

| # | Command (cwd) | Result |
|---|---|---|
| 1 | `grep -rhoE "AbstractValidator<…>"` vs `grep -rhoE "ValidationFilter<…>"` over `backend/laundryghar`, then `comm -23` (scratch `validated.txt`, `filtered.txt`, `unfiltered.txt`) | 130 validator target types and 92 filtered types, leaving **40 unfiltered**. One of them, `PartnerBookingLocation`, still runs as a child validator through `SetValidator` (`PartnerBookingValidators.cs:9-14`, and its parent `CreatePartnerBookingRequest` is filtered). That leaves **39** validators that are truly dead. |
| 2 | `grep -rn "RegisterBehaviors\|AddCustomCQRS\|IPipelineBehavior"` (all backend, including Utilities) | `RegisterBehaviors` has no caller. `AddCustomCQRS` registers only `Dispatcher` (no behaviours). `CommandDispatcher`/`QueryDispatcher` are never registered. |
| 3 | `grep` for `amount_paid` / `AmountPaid =` / `CREATE TRIGGER` in `backend`, `db`, `database_scripts` | Order `AmountPaid` is written only by `RecordOfflinePaymentCommand.cs:174-176` and `UpdateMyTaskStatus.cs:225-227`. No trigger on payments touches orders (the only one is `trg_check_refund_cap`). |
| 4 | `grep` for `initiatePayment\|verifyPayment` in `customer-mobile/app` and `customer-mobile/src` | Only the wrapper `src/api/commerce.ts:149-167` matches. **No call sites.** |
| 5 | `grep -rn "phase0\|phase2\|seed_navigator_modules\|build_from_scratch\|migrate.sh"` (sh/md/yml/sql) | No ordered script applies `phase0_*`, `phase2_slice_b_*` or `seed_navigator_modules.sql`. `docs/SCHEMA_FULL.sql` has 0 matches for `vertical_key`. |
| 6 | `grep -rn "pg_try_advisory\|SKIP LOCKED\|FOR UPDATE\|LeaderElection"` (backend) | Only the partner-wallet `FOR UPDATE` (`CommerceDbContext.cs:123`). Workers take no locks. |
| 7 | `grep -rn "run_maintenance\|ensure_.*partition\|pg_cron"` | `PartitionMaintenanceService` calls only `logistics.ensure_rider_ping_partitions`. `identity_access.ensure_audit_partitions` has no caller apart from the one-off `SELECT` at the end of its own patch. |
| 8 | GitHub MCP `actions_list list_workflow_runs` (gtmkumar/laundryghar) | Every CI run on `main` failed or was cancelled (runs 3–8). Every Release run on the same SHAs succeeded (for example CI 36294076412 failed and Release 36294076432 succeeded on `274b7af`). |
| 9 | GitHub MCP `list_workflow_jobs 36294076412` + `get_job_logs` | Backend **passed**: core.Tests 137/137, operations.Tests 426/426, operations.IntegrationTests 284/284 in 2 min 39 s, so Docker really ran. admin-web lint+build and migration lint passed. rider-mobile failed at `npm ci` (ERESOLVE). customer-mobile failed at `tsc` with `app/_layout.tsx(11,8) TS2882 '../global.css'`. |
| 10 | Copied the 4 client apps (without `node_modules`/`dist`) to `scratchpad/qa-b/apps/` with `tar`, then ran `scratchpad/qa-b/run_clients.sh` (Node v22.22.0, npm 10.9.4; registry reachable via `npm ping`) | See the test inventory below. |
| 11 | `VITE_IDENTITY_URL=… VITE_CATALOG_URL=… VITE_ORDERS_URL=… npx vite build --outDir scratchpad/qa-b/fe001/dist` (admin-web copy, only the 3 URLs that the Dockerfile, compose and release bake in), then `grep -rl api.example.com/<prefix>` | identity 2, catalog 1, orders 1, and **commerce, finance, analytics, warehouse, logistics, engagement all 0**. This reproduces SA-FE-001: 6 of the 9 axios base URLs are undefined in the production bundle. |

Not runnable here: .NET build/tests (no SDK), Docker/Testcontainers, a live Postgres with pg_partman/postgis (so `build_from_scratch.sh` could not be executed), Razorpay and WhatsApp sandboxes, and the admin-web `e2e` script (it needs a live stack).

## Verification table

Verdict key: `Confirmed`, `Confirmed – severity/status corrected`, `Not reproduced`, `Rejected – false positive`, `Duplicate of SA-…`.

| ID | Specialist sev/status | QA verdict | Corrected sev/status | Evidence QA read | Notes |
|---|---|---|---|---|---|
| SA-VERT-001 | High / Verified | **Confirmed** (canonical, group G1) | High / Verified (static) | `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:51-60,618-647`; `CreateParcelOrderCommand.cs:93-110`; `SharedDataModel/Entities/OrderLifecycle/Order.cs:44`; `db/patches/phase0_multi_vertical.sql:40-62` (column DEFAULT 'laundry', no insert trigger) | I found only two `new Order` sites. Neither reads `Brand.VerticalKey`. No DB trigger derives the vertical; the only brand trigger blocks *changing* `brands.vertical_key`. Mitigation the specialist did not mention: there is no client signup UI (SA-FE-007), so today a salon or tiffin brand can only come from the public API. |
| SA-ARCH-003 | High / Verified | **Duplicate of SA-VERT-001** | — | same | |
| SA-ONB-004 | High / Verified | **Duplicate of SA-VERT-001** (mode) + SA-VERT-002 (public template) | — | `db/migrations/0011_vertical_templates.up.sql:41` (salon `is_public` default true) | |
| SA-SOLID-004 | High / Verified | **Duplicate of SA-VERT-001** (the OCP framing adds the admin-web transition map, which is SA-FE-004) | — | `operations.Application/DependencyInjection.cs:33-40` (4 strategies registered); `FulfillmentMode.DefaultFor` has no callers | |
| SA-VERT-002 | High / Verified | **Confirmed** (canonical, G2) | High | `0011…up.sql:41,63-89`; `0012_recurring_fulfillment_mode.up.sql:154-156` (tiffin set public); `GetSignupTemplates.cs:29`; `CompleteSignup.cs:72-75`; `db/patches/phase4_salon_fulfillment_schema.sql:25-75` (4 tables); grep shows no salon or delivery-schedule entities or handlers in `backend/` | 0012 flips tiffin to public "because its fulfilment mode exists". That contradicts 0011's own operability gate, because nothing generates occurrences. The exposure is API-only today (no signup UI). |
| SA-ARCH-004 | High / Verified | **Duplicate of SA-VERT-002** | — | same | |
| SA-VERT-003 | High (target) / Verified | **Confirmed** as a target-capability gap | High for the salon target; not a defect for laundry | `phase4_salon_fulfillment_schema.sql` (no exclusion constraint); `SalonAppointmentStrategy` is state-only | Related: SA-DB-021 (Low, DB view). |
| SA-VERT-004 | High / Verified | **Confirmed, strengthened** | High | `operations.Application/Orders/Invoices/InvoiceTaxCalculator.cs:18`; `GenerateInvoiceCommand.cs:156`; `InvoicePdfRenderer.cs:44,106`; `db/patches/invoice_generation.sql:100` | Live today, not only for salon: the **courier/logistics template is public** (`0011…:30`). Parcel orders invoice as "SAC 999712 — Laundry & Dry-Cleaning Services". |
| SA-ONB-007 | Medium / Verified | **Confirmed** | Medium | `CompleteSignup.cs:122,184-193,303-307` (GSTIN stored in `brands.config`; the own franchise is created with no `Gstin`); `GenerateInvoiceCommand.cs:87,151` (reads `franchise.Gstin`) | Self-signed-up tenants always get invoices with no supplier GSTIN. |
| SA-ARCH-008 | High / Verified | **Confirmed. Understated** (dedupe canonical **SA-DB-002**) | High / Verified (static) | `deploy/README.md:34-35`; `db/build_from_scratch.sh:10-24,77-104`; `db/patches/apply_patches.sh` (FK files only); `apply_saas_billing_patches.sh:24-31`; `db/migrations/0004…up.sql:34-90`; `0005…up.sql:89-100`; `db/patches/seed_navigator_modules.sql:12`; `docs/SCHEMA_FULL.sql` (0 × `vertical_key`) | It is worse than "would not match". `identity_access.modules` is created only by `seed_navigator_modules.sql`, and `modules.vertical_key` only by `phase2_slice_b_fabric_module.sql`, so on the documented bootstrap `migrate.sh up` **fails at 0005**. 0004 also silently widens zero constraints, because the `vertical_key` CHECKs come from phase0. SA-DB-002 independently reports the same 0005 failure. |
| SA-VERT-010 | Medium / Partially Verified | **Duplicate of SA-DB-002** (severity under-rated) | High | same | Also duplicates SA-SUB-020 (Medium). |
| SA-API-003 | High / Verified | **Confirmed – severity corrected to Medium** (canonical, G4) | Medium / Verified | `Utilities/CQRS/Extensions/ServiceCollectionExtensions.cs:10-34`; `Dispatcher/Dispatcher.cs:15-45`; `Registration/BehaviorRegistrar.cs:13-25`; `Validation/ValidationFilter.cs:20-35`; `database_scripts/04_bc4_order_lifecycle.sql:38` (channel CHECK), `:154` (quantity > 0); `operations.WebApi/Endpoints/Logistics/RiderSelfEndpoints.cs:72,89,94`, `Warehouse/WarehouseInspections.cs:40` (RequestSizeLimit); `WarehouseInspections.cs:90-94`, `RiderTasksAdmin.cs:30-35`, `RiderDocumentsAdmin.cs:35` (`fileDownloadName` gives an attachment disposition) | The defect is real: 39 dead validators. But mitigations cut the impact. The DB backstops quantity and channel. Every upload endpoint has a `RequestSizeLimit`. Every uploaded file is served as an attachment, so a stored MIME type does not render inline. The remaining gaps are type and size limits inside those caps, cart size, and lengths stored in jsonb. All callers are authenticated. |
| SA-SOLID-005 | Medium / Verified | **Duplicate of SA-API-003**, with one factual error | — | `04_bc4_order_lifecycle.sql:38` | The report says the channel whitelist is "not backstopped". **It is**: `CHECK (channel IN ('walkin','app','whatsapp','call','web','pos'))`. |
| SA-ARCH-005 | Medium / Verified | **Duplicate of SA-API-003** | — | same | |
| SA-SOLID-001 | High / Verified | **Confirmed** (canonical, G6) | High | `UpdateMyTaskStatus.cs:148-182` (hard-coded `delivered` / `FromStatus = "out_for_delivery"`, no `EnsureTransition`), `:355-392` (pickup hops add no outbox row); `CancelOrder*.cs:53-93` and `UpdateOrderStatusCommand.cs:60-117` (only `order.status_changed`); `NotificationMappingService.cs:109-117` (does not subscribe to `delivery.completed`); `NotificationChannelPreferencePolicy.cs:43-62` (no `cancelled` mapping); `LaundryProcessStrategy` transition table | Exactly 5 status writers (grep for `order.Status =` / `LifecycleState =`). |
| SA-SOLID-002 | High / Verified | **Confirmed – severity corrected to Medium (latent)**. Duplicate of SA-API-007 | Medium | `RazorpayWebhookHandler.cs:168-240` (payment row and outbox only); `CustomerPaymentHandlers.cs:54-81`; `UpdateMyTaskStatus.cs:116-125,185-228`; no trigger (cmd 3); **no client call site** (cmd 4) | The code defect is real. But no shipped client starts an online order payment, so the "rider collects COD on a prepaid order" scenario cannot happen today. It becomes High or P0 the day the payment UI ships. The payments QA owns the final rating for SA-API-007. |
| SA-SOLID-003 | High / Verified | **Confirmed** (and see the new SA-QB-001) | High | `RoyaltyCommands.cs:98-107`; `RoyaltyGenerationService.cs:195-203`; `database_scripts/06_bc6_commerce.sql:379-381`; `RecordOfflinePaymentCommand.cs:162` (Captured); `UpdateMyTaskStatus.cs:215` (Succeeded) | Mitigations: the worker is off by default (`WorkerOptions.cs:68`), and a manual `GrossRevenueOverride` exists (`RoyaltyCommands.cs:88`). Fixing the literal alone is not enough (see SA-QB-001). |
| SA-SOLID-007 | High / Verified | **Confirmed**. Duplicate pair with SA-API-012 (canonical: SA-API-012) | High | `commerce.Infrastructure/Worker/Channels/NotificationSettingsCache.cs:10-65`; `RoutingChannelSender.cs:103,138` | Minor correction: the cache TTL is **60 s** (`:19`), not 5 minutes. There is no `OrderBy` and no brand parameter. The class comment claims platform rows are preferred; the code does not do that. |
| SA-SOLID-008 | Medium / Verified | **Confirmed** | Medium | `Worker/Stubs/LoggingChannelSender.cs:30` (returns a success result); `commerce.WebApi/Program.cs:262`; `RoutingChannelSender.cs:24,37` | |
| SA-ONB-001 | High / Partially Verified | **Confirmed** (canonical, G8) | High / Partially Verified | `laundryghar.Gateway/Program.cs:45-55` (only a `PathPattern` transform); `ServiceDefaults/Extensions.cs:267-287` (`XForwardedFor\|XForwardedProto` only, known proxies cleared); `deploy/docker-compose.yml:25,87-96`; `core.Infrastructure/Services/BrandResolver.cs:67-100` | Kept at High because of what happens on a miss. An unresolved Host falls through to `X-Brand-Id`, then to `?brandCode`, then to the default `LG-MAIN` (`BrandResolver.cs:40,73-86`). Anonymous traffic on a custom domain is therefore silently attributed to the default brand rather than failing. |
| SA-OPS-013 | Medium / Suspected | **Duplicate of SA-ONB-001**. Status should be Partially Verified | — | same | Also duplicates SA-TEN-009 (Medium). |
| SA-ONB-002 | High / Partially Verified | **Confirmed** | High / Partially Verified | `core.Application/Identity/TenancyOrg/Brands/Commands/UpdateBrand/UpdateBrand.cs:15-35` (`FindAsync` under RLS); `db/patches/rls_proposal.sql:315-341` (`rls_admin_only` on brands); `_applied_rls_bc1_bc2.sql:42`; `deploy/.env.example:5` (runtime user is `app_user`, so RLS applies); `0009_brand_status_lookup.up.sql:6-20` (documents the trap) | |
| SA-ONB-003 | High / Partially Verified | **Confirmed – severity corrected to Medium** (canonical, G9) | Medium | `GetNavigator.cs:38-45`; `SharedDataModel/Enums/VerticalKey.cs:34-37`; `TenantResolutionMiddleware.cs:30-45` | Fail-open is confirmed. But the brand's **feature entitlement** still gates server-side, and a cross-vertical feature can only be licensed by a platform admin. The vertical check is defence in depth and terminology correctness, not the primary access control. That is consistent with SA-VERT-005, SA-FE-006 and SA-AUTHZ-012, which are all Medium. |
| SA-FE-006 | Medium / Partially Verified | **Duplicate of SA-ONB-003** | Medium | same | |
| SA-ONB-006 | Medium / Verified | **Confirmed** | Medium | `NotificationMappingService.cs:432` ("…for your Laundry Ghar order"); `InvoicePdfRenderer.cs:44,106` | Overlaps SA-API-013 (brand name strings) and SA-VERT-004 (invoice identity). |
| SA-FE-001 | High / Verified | **Confirmed – reproduced** | High / Verified | `admin-web/Dockerfile:23-29`; `deploy/docker-compose.yml` admin-web `args` (3 URLs); `.github/workflows/release.yml:48-51`; `admin-web/src/api/client.ts:26-34` (9 URLs); `admin-web/.dockerignore` (excludes `.env.*`, so no fallback); `admin-web/deploy/nginx.conf` (SPA fallback returns `index.html` for unknown paths) | Command 11: the bundle contains only 3 gateway prefixes. |
| SA-FE-002 | High / Partially Verified | **Confirmed – severity corrected to Medium** | Medium | `admin-web/src/stores/authStore.ts:72-86`; `components/layout/Topbar.tsx:43-53`; `core.WebApi/Endpoints/Identity/Auth.cs:39-51,185-212` | The trace is correct. Exploiting it needs the same browser profile after logout (no remote vector, and the cookie is SameSite). |
| SA-FE-003 | High / Verified | **Confirmed – severity corrected to Medium** | Medium | `pos-web/src/stores/authStore.ts:64-82` (no `partialize`); `pos-web/src/api/client.ts:136-145` | Real, but it needs XSS or device access, and pos-web has **no deploy path and no CI job** (SA-OPS-012, `ci.yml`), so it is not shipped today. Raise it back to High once pos-web is deployed. |
| SA-FE-004 | High / Verified | **Confirmed, strengthened (live for laundry brands)** | High | `admin-web/src/pages/orders/orderStatus.ts:43-75`; `operations.Application/Fulfillment/Logistics/*Strategy.cs:37-47` | Parcel (`point_to_point`) orders exist today. Admin offers `picked_up → received`, which the server rejects, and never offers `picked_up → out_for_delivery`. Parcel orders cannot be advanced from the admin console past `picked_up`. |
| SA-FE-005 | Medium / Verified | **Confirmed** | Medium | `admin-web/src/components/layout/BrandSwitcher.tsx:61-67`; `hooks/useOrders.ts:17-24` (no brand in query keys); no `queryClient.clear/removeQueries` in `admin-web/src` | |
| SA-FE-010 | Medium / Verified | **Confirmed** | Medium | `customer-mobile/app/(app)/booking/items.tsx:158-184` (not gated by `__DEV__`) | |
| SA-FE-011 | Medium / Partially Verified | **Confirmed – reproduced** | Medium / Verified | CI job logs (cmd 9) plus my local runs | |
| SA-API-001 | Critical / Partially Verified | **Confirmed** (canonical, G10) | Critical / Partially Verified | `core.WebApi/Program.cs:197-216,505-515`; `deploy/docker-compose.yml:25,35-46`; `ServiceDefaults/Extensions.cs:267-287` | Anyone can lock out every tenant's logins with about 10 unauthenticated requests per minute through the documented topology. See SA-QB-003 for the contradictory fix guidance. |
| SA-OPS-001 | High / Partially Verified | **Duplicate of SA-API-001** (severity raised to Critical) | Critical | same | |
| SA-API-002 | High / Verified | **Confirmed** (canonical, G11) | High | `laundryghar.Gateway/RateLimitPartitioning.cs:24-92` | |
| SA-OPS-002 | High / Verified | **Duplicate of SA-API-002** | High | same | SA-TEN-005 and SA-AUTHZ-010 rate this Medium (see Contradictions). |
| SA-OPS-003 | High / Verified | **Confirmed** (canonical, G13) | High | `operations.Infrastructure/Storage/LocalStorageOptions.cs:16`; `DependencyInjection.cs:32-50` (local is the only provider); `deploy/docker-compose.yml` (no volumes on services); `PRODUCTION_ENV.md:160-178` | Uploads include inspection photos, item images, KYC documents and proof photos. |
| SA-API-017 | Medium / Verified | **Duplicate of SA-OPS-003** (storage part) | — | plus the RequestSizeLimits above | The upload-check part is partly mitigated (see SA-API-003). |
| SA-OPS-004 | High / Verified | **Confirmed** (time-bound) | High; **raise to Critical if any live DB shares the documented runway** | `database_scripts/99_cross_cutting_schema_qualified.sql:17-77` (premake 6/6/6/3); `commerce.Infrastructure/Worker/Services/PartitionMaintenanceService.cs:11-30,107`; `db/patches/audit_logs_partition_maintenance.sql:21,75`; `db/HANDOFF.md:175-181` | `ensure_audit_partitions` exists but no worker calls it. The documented runway ends **2026-12-01**, under 2 months from today. |
| SA-OPS-005 | High / Partially Verified | **Confirmed – severity corrected to Medium** (canonical, G12) | Medium (P0 before scale-out) | `commerce.WebApi/Program.cs:304-321`; cmd 6 | The documented topology runs one replica, so double-send needs scale-out. A stuck claim needs a crash inside a narrow window. Consistent with SA-ARCH-007, SA-API-014 and SA-DB-009 (all Medium). |
| SA-ARCH-007 / SA-API-014 | Medium | **Duplicate of SA-OPS-005** | Medium | same | |
| SA-OPS-006 | High / Verified | **Confirmed** | High | `.github/workflows/release.yml:1-81` (no `needs`/`workflow_run`); `ci.yml`; cmd 8 | CI on `main` has never been green. Release has never failed. |
| SA-OPS-007 | Medium / Verified | **Confirmed** | Medium | `ci.yml:83-110` (checks file pairing only); `deploy/README.md:56-58` | |
| SA-OPS-009 | Medium / Verified | **Confirmed** | Medium | `PRODUCTION_ENV.md:85-100` vs grep (no `SecretsProvider`, `KeyPerFile` or KeyVault in code) | |
| SA-OPS-011 | Low / Verified | **Confirmed** | Low | `ServiceDefaults/Extensions.cs:174-178` (`self` check only); no `AddNpgSql` or `AddDbContextCheck` | |
| SA-OPS-012 | Medium / Verified | **Confirmed** | Medium | `deploy/docker-compose.yml` (`image: laundryghar-core` etc., with `build:`) vs `release.yml:63-66` (`ghcr.io/<owner>/…`); no pos-web service | |
| SA-ARCH-001 | High / Verified | **Confirmed – severity corrected to Medium** | Medium | `operations.Application/Common/Interfaces/IOperationsDbContext.cs:96-111` (commerce and finance DbSets) | The facts hold, but the 3-host consolidation is deliberate (`admin-web/.env.example` comments) and causes no runtime defect by itself. The concrete harms are already separate findings (SA-SOLID-006, SA-SOLID-011). Per brief rule 7, a structural observation with a modest recommended remedy is Medium. |
| SA-ARCH-010 | Medium / Verified | **Confirmed** | Medium | `tests/*/**.csproj` (no reference to `commerce.*`); `operations.Tests/Commerce/QuotaUnitTests.cs` (SharedDataModel enums only) | |
| SA-VERT-006 | Medium / Verified | **Confirmed** | Medium | `NotificationChannelPreferencePolicy.cs:43-62` | |

Tally: 50 rows. **29 Confirmed** (2 of them reproduced, 3 strengthened, 1 understated), **7 Confirmed – severity corrected**, all lowered to Medium (SA-API-003, SA-SOLID-002, SA-ONB-003, SA-FE-002, SA-FE-003, SA-OPS-005, SA-ARCH-001). Separately, SA-OPS-001 is raised from High to Critical through its duplicate SA-API-001, **14 Duplicates**, **0 Not reproduced**, **0 Rejected**.

## Dedupe groups (one canonical ID per group)

| Group | Canonical | Duplicates / overlapping | Canonical severity |
|---|---|---|---|
| G1 Order creation ignores the brand's vertical | **SA-VERT-001** | SA-ARCH-003, SA-ONB-004 (mode part), SA-SOLID-004, part of SA-API-021 | High |
| G2 Salon and tiffin publicly sellable but not operable | **SA-VERT-002** | SA-ARCH-004, SA-ONB-004 (template part). Related: SA-VERT-003, SA-DB-021 | High (P0 flip of `is_public`) |
| G3 Documented bootstrap cannot build the schema | **SA-DB-002** | SA-ARCH-008, SA-VERT-010, SA-SUB-020. Related: SA-OPS-007 | High |
| G4 CQRS behaviours dead, 39 validators unexecuted | **SA-API-003** | SA-SOLID-005, SA-ARCH-005 | Medium |
| G5 Online capture never updates the order | **SA-API-007** (payments QA) | SA-SOLID-002 | Medium while no client path exists (my view) |
| G6 Divergent order-status writers | **SA-SOLID-001** | SA-API-006 | High |
| G7 Notification worker uses one tenant's provider credentials | **SA-API-012** | SA-SOLID-007 | High |
| G8 Custom-domain Host lost at the gateway | **SA-ONB-001** | SA-OPS-013, SA-TEN-009. Client side: SA-FE-008 | High |
| G9 Vertical boundary fails open, not enforced in the API | **SA-ONB-003** | SA-VERT-005, SA-FE-006, SA-AUTHZ-012 | Medium |
| G10 Core `auth` limiter collapses onto the gateway IP | **SA-API-001** | SA-OPS-001 | Critical |
| G11 Gateway rate-limit partition is client-controlled | **SA-API-002** | SA-OPS-002, SA-TEN-005, SA-AUTHZ-010 | High |
| G12 Workers have no claim lock or leader, stuck claims | **SA-OPS-005** | SA-ARCH-007, SA-API-014, SA-DB-009, part of SA-OPS-015 | Medium (P0 before scale-out) |
| G13 Local `/tmp` file storage | **SA-OPS-003** | SA-API-017 (storage part) | High |
| G14 Laundry tax identity on invoices | **SA-VERT-004** | SA-ONB-006 (invoice part) | High |
| G15 "Laundry Ghar" strings in customer messages | **SA-API-013** | SA-ONB-006 (notification part), SA-VERT-006 (template switch) | Medium |
| G16 No branding write path or consumer; no signup UI | **SA-ONB-002** | SA-FE-008 (theme), SA-ONB-008 = SA-FE-007 (no signup or branding UI) | High |
| G17 admin-web hardcoded laundry transitions | **SA-FE-004** | SA-VERT-007 (client part) | High |
| G18 Back-office CreateBrand has an implicit vertical | **SA-ONB-005** | SA-VERT-009 | Medium |
| G19 Anemic domain, shared model | **SA-SOLID-009** | SA-ARCH-002, SA-ARCH-001 (Medium) | Medium |
| G20 Shallow health checks | **SA-OPS-011** | SA-API-024 | Low |
| G21 Telemetry and caches not tenant-aware | **SA-OPS-008** | SA-TEN-014 | Medium |
| G22 Docs claim infrastructure or features that are absent | **SA-OPS-018** | SA-ARCH-012, SA-VERT-011, SA-OPS-009 (secrets part) | Informational (SA-OPS-009 stays Medium) |

## Contradictions between reports

1. **Severity conflicts for the same defect.** G10: Critical (API) vs High (OPS). G3: High (ARCH, DB) vs Medium (VERT, SUB). G8: High (ONB) vs Medium (OPS, TEN). G9: High (ONB) vs Medium (VERT, FE, AUTHZ). G11: High (API, OPS) vs Medium (TEN, AUTHZ). G12: High (OPS) vs Medium (ARCH, API, DB). G13: High (OPS) vs Medium (API). G4: High (API) vs Medium (SOLID, ARCH). The canonical severities above resolve each one with the reason recorded in the verification table.
2. **Status conflict on the same evidence.** SA-OPS-013 is "Suspected" while SA-ONB-001 is "Partially Verified" for an identical trace. It should be Partially Verified.
3. **Factual errors.** SA-SOLID-005 says the channel whitelist has no DB backstop, but it does (`04_bc4_order_lifecycle.sql:38`). SA-API-003, SA-SOLID-005 and SA-ARCH-005 count "40" dead validators; it is 39, because `PartnerBookingLocation` runs through `SetValidator`. SA-SOLID-007 says the cache TTL is 5 minutes; it is 60 s. SA-ARCH-008 says a fresh build "would not match"; in fact `migrate.sh up` fails at 0005, which agrees with SA-DB-002.
4. **The verdict question numbers differ across reports**, so Qn verdicts cannot be compared by number:
   - Q5 is "one primary business type" in 04 but "onboarding/provisioning" in 05.
   - Q6 is "vertical isolation" in 04 but "white-label" in 05.
   - Q7 is "new verticals without modification" in 01 and 07 but "cross-vertical reach" in 04.
   - Q8 is "one business type" in 05 but "branding" in 02 and 09.
   - Q9 is "clients genuinely implemented" in 05 and 09 but "backend API foundation" in 08.
   - Q13 is "vertical restriction" in 09 but "entitlements" in 03 and 06.
   - Q15 is "production-ready" in 01 and 11 but "workflows reliable" in 08.
   
   The orchestrator should re-key every verdict to one question list before aggregating.
5. **Branding verdict.** 02 gives "Partially Supported" (isolation angle). 05 and 09 give "Not Supported". Substantively there is no write path and no consumer (SA-ONB-002), so it is Not Supported as a capability.
6. **Severity of the online-payment defect (G5).** 07 and 08 rate it High, but 08 and 09 themselves record that no client calls initiate or verify. The High rating describes the future state.
7. **Production guidance on ForwardedHeaders contradicts itself.** See SA-QB-003.
8. **Tracking docs vs code.** `docs/GAP_ANALYSIS.md` M4 says "Done" for vertical-aware orders, while the code is the G1 defect. `phase4_salon_pack.sql:6-8` says salon becomes "fully entitleable", which G2 contradicts. 0012 contradicts 0011's own rule for when a template may be public.

## New findings

### SA-QB-001 — COD and online payment rows carry no `franchise_id`, so royalty under-counts even after SA-SOLID-003 is fixed
- Category: Finance correctness / data model (Related area: PAY, FINANCE)
- Severity: Medium (it ships with the SA-SOLID-003 fix, so treat it as P0 together with that one)
- Status: Verified (static)
- Evidence:
  - `operations.Application/Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs:196-222`: the COD `Payment` sets `BrandId`, `CustomerId` and `OrderId`, but **no `FranchiseId`**.
  - `commerce.Application/Commerce/Customer/Payments/CustomerPaymentHandlers.cs:54-81`: online payments also have no `FranchiseId`.
  - Only `RecordOfflinePaymentCommand.cs:147` sets it.
  - `SharedDataModel/Entities/Commerce/Payment.cs:17` (`Guid? FranchiseId`). No DB trigger populates it (grep of `db/` and `database_scripts/`).
  - Royalty filters on `p.FranchiseId == …` (`RoyaltyCommands.cs:98-107`; `RoyaltyGenerationService.cs:195-203`).
- Observed behaviour: once the `"completed"` literal is fixed, royalty revenue will include only staff-recorded offline payments. Rider-collected COD and online payments will still be excluded.
- Reproduction / verification method: read the three `new Payment` initialisers and the two royalty queries. A runtime test would seed one COD, one online and one offline payment for a franchise and run `CalculateRoyalty`. I did not run it (no .NET).
- Impact: franchisor royalties stay understated, and the error is silent because the fix for SA-SOLID-003 would look like it works.
- Recommended remediation: set `FranchiseId = order.FranchiseId` in both initialisers, or compute royalty by joining `payments → orders.franchise_id`. Backfill existing rows from `orders`.
- Regression tests required: royalty over seeded COD, online and offline payments equals their sum. An insert test asserting that `payments.franchise_id` is not null when `order_id` is set.
- Dependencies / priority: P0 together with SA-SOLID-003.

### SA-QB-002 — Integration tests report "Passed" when Docker is missing, and migration and rollback coverage is thin
- Category: Test integrity / QA
- Severity: Medium
- Status: Verified (code read; CI log read)
- Evidence:
  - `tests/operations.IntegrationTests/Phase4SalonSchemaTests.cs:15-20,49-51`: `catch (Exception) { _dockerAvailable = false; }` and then `if (!_dockerAvailable) return;`. The same pattern appears **60 times across 17 files** (grep).
  - Only 13 of the 33 `.up.sql` migrations are applied by any test, each onto a minimal fixture (`RepoPaths.Migration(...)` grep). No test executes a `.down.sql`, and no test runs `build_from_scratch.sh` + `migrate.sh up`.
  - There are no tests for `CreateOrderHandler`, `UpdateMyTaskStatusHandler`, `RazorpayWebhookHandler`, royalty or `NotificationSettingsCache` (grep of `tests/`).
  - There are no booking or slot concurrency tests (grep for `WhenAll`/`concurren` finds only matview and RLS tests).
- Observed behaviour: on any runner without Docker, the whole integration suite goes green without asserting anything. On GitHub it did execute (284 passed in 2 min 39 s, job 108549585458). The suites that exist would not catch G1, G3, SA-SOLID-001/002/003 or SA-QB-001.
- Impact: false assurance, and migration regressions (like the 0005 bootstrap failure) reach operators.
- Recommended remediation: use `Assert.Skip`/`SkippableFact` (or fail when `CI=true`) instead of `return`. Add a CI job that builds Postgres 16 with partman and postgis from the documented bootstrap, runs `migrate.sh up`, then `down`/`up` for the last N migrations, and checks EF mappings against `information_schema`. Add handler tests for the paths listed above.
- Regression tests required: as above. Add a meta-test that fails if any integration test returns early on CI.
- Dependencies / priority: P1.

### SA-QB-003 — Production guidance for ForwardedHeaders contradicts itself, and both options are unsafe as coded
- Category: Configuration / documentation (Related area: OPS, SEC)
- Severity: Low (it decides how SA-API-001 gets fixed)
- Status: Partially Verified (config and code read; not executed)
- Evidence:
  - `backend/laundryghar/PRODUCTION_ENV.md:135-150` says to enable `ForwardedHeaders__Enabled=true` on all services.
  - `deploy/docker-compose.yml:25` says it "stays OFF on the services".
  - `deploy/README.md:55` says to set it on the gateway only.
  - `ServiceDefaults/Extensions.cs:273-283`: when it is enabled, `KnownIPNetworks` and `KnownProxies` are **cleared**, so `X-Forwarded-For` is trusted from any peer.
- Observed behaviour: with it OFF, SA-API-001 applies (one auth bucket for the whole platform). With it ON, the services trust any forwarded IP header without a proxy allow-list, and per-IP limits then rely on header handling at the gateway that is not proven.
- Impact: operators cannot follow the docs and end up safe.
- Recommended remediation: keep one documented setting. Enable it on the services with `KnownIPNetworks` set to the compose or cluster network, have the gateway overwrite `X-Forwarded-For`, and remove the conflicting text.
- Regression tests required: an integration test with two clients through a YARP hop gives two independent buckets; a spoofed `X-Forwarded-For` from an untrusted peer is ignored.
- Dependencies / priority: P1, before the SA-API-001 fix.

## Test inventory and results

| Suite | Where run | Result |
|---|---|---|
| core.Tests (11 test files) | GitHub CI job 108549585458 @274b7af | 137/137 passed |
| operations.Tests (35 files) | same | 426/426 passed |
| operations.IntegrationTests (44 files, Testcontainers) | same | 284/284 passed in 2 min 39 s (Docker present). Silent-pass risk: see SA-QB-002 |
| commerce.* | — | **no test project** (SA-ARCH-010 confirmed) |
| admin-web `npm ci` / `npm run lint` / `npm run build` | local scratch, Node 22 | ci ok; lint 0 errors, 12 warnings; build ok (`✓ built in 2.45s`); **no unit tests**; `e2e` script needs a live stack (Not Tested) |
| pos-web `npm ci` / `npx tsc -b` / `npm run lint` | local scratch | ci ok; tsc ok; lint 0 errors, 2 warnings; **no tests; not in CI** |
| customer-mobile `npm ci` / `npm run typecheck` / `npx jest --ci` | local scratch | ci ok; **typecheck FAIL**: `app/_layout.tsx(11,8) TS2882 '../global.css'` (same as CI); jest 11 suites, **170/170 passed** (exit 0, re-run to confirm) |
| rider-mobile `npm ci` | local scratch | **FAIL ERESOLVE** (same as CI); passes only with `--legacy-peer-deps` |
| rider-mobile `npm run typecheck` / `npx jest --ci` (after `--legacy-peer-deps`) | local scratch | **typecheck FAIL** (exit 2, same TS2882 on `../global.css`; CI never reaches this step); jest 8 suites, **91/91 passed** |

Missing tests, by the risk they would catch:
- **Vertical regressions:** order creation for laundry, salon, tiffin and parcel brands asserting `vertical_key` and `fulfillment_mode` (G1). A test that `GetSignupTemplates` returns only operable templates (G2). admin-web uses `allowedTransitions` for parcel and salon orders (SA-FE-004). Invoice SAC by vertical (SA-VERT-004). Navigator and terminology for a non-laundry tenant under real RLS (G9).
- **Booking and dispatch concurrency:** pickup-slot capacity under parallel bookings (SA-API-019), POS idempotency race (SA-API-004), rider offer acceptance (SA-DB-022), two concurrent worker instances (G12).
- **Migrations and rollback:** a full documented bootstrap from empty, `up → down → up` for every migration, and an EF model-vs-schema check (G3, SA-QB-002).
- **Money paths:** COD, online and offline payments into royalty (SA-SOLID-003, SA-QB-001); online capture updates the order (G5); notification credentials chosen per brand (G7).

## Verdict challenges (Q5–Q10, Q14, Q15)

The question numbering differs between reports (Contradiction 4), so I state each verdict by its substance.

- **One primary business type at onboarding (04-Q5, 05-Q8): Partially Supported. I agree.** The single immutable column and template-based signup are real, but orders ignore the vertical (G1) and two of the four public templates are inoperable (G2).
- **Vertical isolation and new verticals without modification (04-Q6/Q7, 01-Q7, 07-Q7):** extensibility is **Not Supported**, which agrees with 01 and 07. 04's "Partially Supported" answers a different question (cross-vertical reach), and on that one I agree.
- **White-label and branding (05-Q6, 09-Q8, 02-Q8): Not Supported.** 02's "Partially Supported" covers isolation of branding data only (Contradiction 5).
- **Q9, clients genuinely implemented: Partially Supported, with a caveat no report weighs.** The admin-web image that the shipped Dockerfile, compose and release pipeline produce is missing 6 of its 9 API base URLs (reproduced), so commerce, finance, analytics, warehouse, logistics and engagement screens cannot work as deployed. Mobile typecheck is red, and pos-web is not deployable.
- **Q10, scheduling and fulfilment: I agree with 04.** Laundry and logistics are Partially Supported (parcel orders cannot be advanced from admin-web past `picked_up`, SA-FE-004). Salon, tiffin and marketplace are Not Supported.
- **Q14, OOP/SOLID: Partially Supported. I agree** (01 and 07 are consistent).
- **Q15, production-ready: Not Supported. I agree** (01, 08 and 11). The strongest independently verified blockers are:
  - the documented bootstrap cannot complete (G3);
  - release is not gated on CI, and CI has never been green on `main`;
  - partition runway is time-bound (2026-12-01 documented);
  - the platform-wide auth throttle (G10);
  - ephemeral uploads (G13);
  - the broken admin-web production bundle (SA-FE-001).

## Not verified

- Runtime behaviour of everything above: no .NET SDK, Docker, live Postgres with partman and postgis, or gateway was available. YARP's default Host rewrite is taken from library behaviour.
- Whether any production database exists and how much partition runway it has (SA-OPS-004 relies on the `db/HANDOFF.md` figure).
- SA-DB-002's claim that re-running `build_from_scratch.sh` reverts the hardened `kernel.rls_bypass()`. That belongs to the DB/tenancy QA and I did not re-trace it.
- The other Mediums and Lows in my set that I did not sample: SA-ARCH-002/006/009/011/012, SA-VERT-005/007/008/009/011, SA-ONB-005/008–012, SA-SOLID-006/009–014, SA-FE-007–009/012–016, SA-OPS-008/010/014–018. They are listed in the dedupe groups where relevant but were not independently re-traced.
- Security, tenancy, authz, subscription and payment findings outside the items above belong to the other QA agent.
