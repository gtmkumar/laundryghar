# LaundryGhar — Gap Analysis Round 3 (2026-06-12)

Produced by a 6-agent verification audit of `GAP_ANALYSIS_R2.md` against the current code, plus a fresh new-gap hunt (API gateway, role/permission/navigation, items & franchise UX, premium mobile design). Owners: **be** = dotnet-backend-developer · **aw** = admin-web · **pw** = pos-web · **cm** = customer-mobile · **rm** = rider-mobile · **ux** = uiux-design-architect · **sec** = security-code-reviewer · **qa** = qa-test-engineer.

## R2 scorecard (verified 2026-06-12)

**CLOSED since R2** (don't re-litigate): DOC-1 (rider delivery completes orders + COD payment + history + event), DOC-3 (delete account), DOC-5 (loyalty earn/burn), MOB-1 (live slot picker w/ capacity), MOB-3, MOB-4, MOB-7 (OTP on tracking, backend + app), MOB-8, MOB-9 (haptics both apps), MOB-10 (skeletons both apps), MOB-15 (earnings drill-down), MOB-16 rider side (offline banner + queue), MOB-17, MOB-19 rider side, DOC-9 rider side (camera inspection + magic-byte sniffing), duty-toggle backend sync, POS-1..POS-6 (cart persist, idempotency, change calc, partial/credit payments, brandId cache key, invalidation), POS-7 (mostly), WEB-1 (dashboard error states), WEB-2 (ConfirmDialog deployed ×41), WEB-4 (cash-book close + variance + handover), WEB-7, WEB-9 (ConfirmDialog a11y; FormDrawer still open), WEB-16, SEC-1 (retry → cms.notification.manage), SEC-2 (mcp:booking scope enforced, token types partitioned), SEC-3 (brandId threaded through OTP senders), SEC-8 (DCR rate-limited), SEC-10 (security headers middleware), master OTP correctly env-gated, catalog image endpoints traversal-safe + brand-scoped, `FEATURES.bookingApi=true` (booking persists).

## Open register

### P0 — High

| id | issue | impact | solution | owner |
|---|---|---|---|---|
| R3-GW-1 | **No API gateway** — 4 clients hardcode 9 per-service base URLs; CORS/rate-limit/headers duplicated per service | Config sprawl, larger attack surface, per-service CORS drift, painful mobile dev-host config | **YARP reverse proxy** as a 10th Aspire project (`laundryghar.Gateway`, :8080): path-based routes `/identity/*→5050`, `/catalog/*→5001`… (or `/api/v1/<area>` mapping), forward Authorization + X-Brand-Id untouched (no token re-issue — JWKS validation stays in services), central CORS + security headers + global rate limit, ForwardedHeaders enabled in services, aggregate `/health`. Additive: per-service ports keep working; clients switch to ONE base URL | be |
| R3-SEC-1 | **Seeder↔live drift**: 8 endpoint-enforced permission codes exist only in `db/patches/*.sql` (`payment.record`, `customer.create`, `rider.verify`, `rider.settle`, `subscription.read/manage`, `saas.read/manage`) | Fresh bootstrap (CI/staging/DR) missing codes → those endpoints work only for platform_admin; env parity broken | Fold all patch-defined codes + role grants into `IdentitySeeder` `PermissionDefs` + `SeedRolePermissionsAsync`; add CI drift check (grep `permission:` strings vs seeded set) | be |
| R3-SEC-2 | **POS not lockable separately from Orders** — POS order ops use shared `orders.create/update` (only `payment.record` was split) | Can't give counter staff POS without admin Orders module (explicit user requirement) | New `pos.order.create`/`pos.order.read` family on POS-used endpoints (Orders service accepts either family on shared routes or POS lane added); seed grants (store_admin etc.) + SQL patch for live DB; admin matrix gets a POS row independent of Orders | be |
| R3-SEC-3 | **Settings endpoints gated by UserType string** (`SettingsEndpoints.cs:90`), not a permission code | Secret-bearing writes (payments/WhatsApp/SMS keys) outside the permission model; not matrix-controllable | Add `settings.read`/`settings.manage` codes, per-endpoint `permission:` gates, seed to brand_admin + patch live DB | be |
| R3-BE-1 | **Promotions never apply** (DOC-6) — CRUD-only; first-order discount dead | Marketing money features are fiction | Evaluate active promotions at order create (first-order, %/flat) alongside coupons; expose `discountTotal` breakdown | be |
| R3-BE-2 | **Customer coupon at checkout missing** (MOB-13) — pickup request carries no couponCode; wallet 10% discount client-side only | Customers can see coupons but never use them; trust gap | Accept + validate `couponCode` on pickup request (persist for order conversion), server-side discount calc; cm adds input on pay screen | be + cm |
| R3-BE-3 | **Pickup reschedule missing** (MOB-14) — entity field exists, no endpoint, no app path | Missed pickup = dead end, support call | `POST /customer/pickup-requests/{id}/reschedule` (new date/slot, status guard) + cm reschedule screen from order/pickup detail | be + cm |
| R3-WEB-1 | **Admin/POS routes auth-only** (WEB-3/F4) — any authed user mounts any route by URL; opaque 403s; settings submit handlers ungated | UX/info-disclosure defect (backend does enforce) | `RequirePermission` route wrapper from navigator `requiredPermission`, 403 page, gate submit handlers on `canManage` | aw + pw |
| R3-WEB-2 | **Webhook URL built by `:5173→:5002` string-replace** (WEB-8) | Razorpay webhook URL wrong everywhere but dev | Serve the canonical webhook URL from backend settings GET (env-aware), display read-only | be + aw |
| R3-WEB-3 | **Admin pagination hard-caps** (WEB-5): Orders/Riders/Customers/Subscriptions fixed pageSize 100/200; dashboard counts from loaded slice | Rows silently vanish at scale; KPIs undercount | Infinite scroll (house pattern) on remaining lists + dedicated count endpoints for dashboard | aw + be |
| R3-POS-1 | **POS order search missing** — today-only list, no number/phone/date search; receipt reprint impossible | Counter can't find yesterday's order | Search field + date range + customer phone filter on OrdersPage (server-side query exists) | pw |
| R3-POS-2 | **POS dev creds shown unconditionally + no JWT exp check** (WEB-15) | Cred hint in prod; stale-token UI | Wrap in `import.meta.env.DEV`; add exp check to ProtectedRoute (mirror admin-web) | pw |
| R3-POS-3 | **POS cash-book close hides variance** — closingBalance only, `varianceReason: null` hardcoded | ADR-009 shrinkage attribution inoperable at the counter | Show expected vs counted variance + mandatory reason when ≠0 (mirror admin-web CashBookDetailDrawer) | pw |
| R3-RM-1 | **Inspection screen entry-point unverified/broken** — `inspection/[id].tsx` exists; navigation from pickup task flow may not be wired | Shipped feature unreachable in the field | Verify + wire "Inspect garments" step into pickup leg of `tasks/[id].tsx` before collect-confirm | rm |
| R3-MOB-1 | **Keyboard covers Android form fields** (both apps) — no KeyboardAvoidingView on address/profile/inspection forms | Users type blind on Android | KeyboardAvoidingView + insets on all form screens | cm + rm |

### P1 — Medium

| id | issue | solution | owner |
|---|---|---|---|
| R3-BE-4 | Subscription/plan status changes re-POST full DTO (WEB-6) | PATCH status-only endpoints + row-version guard; aw consumes | be + aw |
| R3-BE-5 | No email channel sender (DOC-10) | SMTP `EmailChannelSender` behind settings (or hide email toggles) | be |
| R3-BE-6 | DPDP consent never recorded (DOC-4) | Record consent + text snapshot at customer signup & pref changes; cm checkbox UI | be + cm |
| R3-BE-7 | Catalog image upload trusts client content-type (SEC-9 residue) | Magic-byte sniffing (reuse rider-inspection validator) | be |
| R3-BE-8 | `rider.read.` trailing-dot permission string suspected on proof-photo route | Verify/fix policy string | be |
| R3-BE-9 | Rider inspection photos orphaned in `delivery_assignments.metadata` — not linked to `garment_inspections` at intake | Link/migrate at warehouse scan-in | be |
| R3-AW-1 | FormDrawer lacks Escape/focus-trap/scroll-lock (the prepared a11y diffs — apply them) | Port ConfirmDialog a11y into FormDrawer | aw |
| R3-AW-2 | Catalog items/categories/services have no delete row-action; image lifecycle polish (stale preview after delete, no client-side size/type validation) | Wire DeleteCatalogDrawer row actions; image validation + preview refresh | aw |
| R3-AW-3 | Customer-subscription drawer has no actions (cancel/pause/retry-billing) even in dunning | Status-conditional footer actions gated `subscription.manage` | aw |
| R3-AW-4 | Royalty drawer: stale after issue/payment, no void/PDF/overpayment clamp | Refetch on mutation + actions | aw |
| R3-AW-5 | Warehouse board: no auto-refresh, hardcoded hot-stage, ScanIn notFound precedence, no operator recorded | refetchInterval + fixes | aw + be |
| R3-AW-6 | No test-connection on integration settings | `POST /settings/{provider}/test` + button | be + aw |
| R3-AW-7 | No CSV export anywhere (DOC-7) | Client-side CSV on FilterableTable | aw |
| R3-AW-8 | Validation gaps (DL/insurance future dates, Aadhaar format, package credit ≥ price, coupon bounds, % fields) | Zod schema sweep | aw |
| R3-POS-4 | POS intake photos (DOC-9 counter side) | Capture at order create, reuse file-storage endpoints | pw + be |
| R3-POS-5 | Modal lacks focus-trap/scroll-lock; no offline indicator; SVG faux barcode unreliable on thermal printers; no thermal print CSS | a11y + `useNetworkStatus` banner + jsbarcode + print media CSS | pw |
| R3-CM-1 | No tracking poll/refetch — status updates require manual reopen; push tap doesn't invalidate queries | 20–30s refetchInterval on tracking + push-handler invalidation | cm |
| R3-CM-2 | Premium design pass: pull-to-refresh on all lists, empty-state personality, micro-haptics on chips/toggles, mutation loading states, a11y labels, typography tokens | per audit list | cm + ux |
| R3-RM-2 | Offline queue flushes only on task-detail focus; proof-photo upload not queued on failure | Flush in root layout; queue photo uploads | rm |
| R3-RM-3 | Premium design pass: ETA countdown/time-pressure, shift summary on off-duty, cumulative rating display, task-card animations, maps-app fallback picker | per audit list | rm + ux |
| R3-MOB-2 | customer-mobile eas.json missing; both apps' EAS_PROJECT_ID is slug not UUID (OTA 404s) | Add eas.json (mirror rider); UUID needs `eas project:init` by owner (documented blocker) | cm + rm |
| R3-NAV-1 | POS route gating: all staff see all POS routes | Permission-aware nav + guards (with R3-SEC-2 pos.* family) | pw |

### P2 — Low / backlog

SEC-4 analytics refresh scoping · SEC-5 matrix-edit priority ceiling (latent) · SEC-7 OAuth consent client name · OAuth refresh-token origin tagging (F6) · master OTP active in staging (informational — keep staging non-exposed) · POS keyboard shortcuts · earnings sparkline · Lottie celebration animations · dark mode (both apps — product decision) · rider real in-app map (needs dev build) · customer/rider/pos list-pagination lockstep · DOC-2 production ISubscriptionCharger (needs real Razorpay creds — prod config item) · MOB-2 Razorpay native SDK (needs dev build; backend order-creation endpoint can land first).

## Dispatch plan (2026-06-12)

- **Wave 1 (backend, parallel lanes):** BE-A Identity/security (R3-SEC-1/2/3, R3-BE-8) · BE-B Commerce/Orders/Worker (R3-BE-1/2/3/4/7, R3-WEB-2) · BE-C Gateway (R3-GW-1).
- **Wave 2 (web, parallel):** AW bundle (R3-WEB-1/3, R3-AW-1..8) · PW bundle (R3-POS-1/2/3, R3-POS-4/5, R3-NAV-1).
- **Wave 3 (mobile, parallel):** CM bundle (R3-BE-2/3 app side, R3-BE-6 UI, R3-CM-1/2, R3-MOB-1/2) · RM bundle (R3-RM-1/2/3, R3-MOB-1/2).
- **Wave 4 (QA):** live browser → Android → iOS; defects bounce to owners; full regression (backend tests + smoke 22 + mobile jest).
