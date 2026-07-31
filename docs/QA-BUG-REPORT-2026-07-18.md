# QA Bug Report — Live browser role-based test — 2026-07-18

> **Fix status (same day):** BUG-1…BUG-6 and BUG-8 fixed in admin-web (tsc clean, verified live in-browser where noted). BUG-7 mitigated with an explicit "awaiting itemisation" empty state (backend total-validation left as a product decision). A ninth bug found during fixing (BUG-9, step-up token loses claims) is fixed in `core.WebApi/Program.cs` — **requires a core-host restart to take effect**. Per-bug status notes inline below.

**Scope:** admin-web (localhost:5174) against live standalone hosts (Core 5056 / Operations 5015 / Commerce 5242), canonical `laundry_ghar_db`.
**Roles exercised:** `platform_admin` (admin@laundryghar.local), `store_admin` (storeadmin@laundryghar.local), `warehouse_supervisor` (warehouse@laundryghar.local), plus negative tests (rider, suspended user, wrong password).
**Coverage:** all 24 routed screens as platform_admin (dashboard, tenancy, orders, warehouse board incl. step-up OTP, customers, riders ×4, support, items, catalog, packages, coupons incl. create+archive CRUD, CMS, promotions, subscriptions, cashbook, expenses, analytics, royalty, platform plans/billing, access control, settings); scoped sweeps + forbidden deep-links for the other two roles.

**What passed:** every screen loads with zero console errors and zero unexpected API failures as platform_admin; coupon create/archive round-trip; warehouse-board step-up OTP (master OTP accepted in dev); deep-link 403 page for unauthorized routes; login rejects wrong password / suspended user / rider with a uniform 401 (no user enumeration); rider cannot password-login (no password set, OTP is mobile-only).

---

## BUG-1 · HIGH · Warehouse supervisor locked out of their primary screen (client permission map out of sync)

- **Repro:** log in as `warehouse@laundryghar.local` / `Warehouse@123` → redirected to `/warehouse/board` → full-page "You don't have access to this module", no sidebar, no way onward.
- **Root cause:** `admin-web/src/lib/routePermissions.ts` maps `/warehouse/board` → `garment.read`, a permission code that **does not exist** in `identity_access.permissions`. The server's `identity_access.modules` table maps the route to `fulfillment.read`, which `warehouse_supervisor` has.
- **Expected:** warehouse supervisor lands on a working garment board.
- **Fix:** change the map entry to `fulfillment.read`.
- **Status: FIXED** (`routePermissions.ts`) — verified live: warehouse login now lands on the board and receives the legitimate step-up OTP prompt. Completing step-up then exposed BUG-9 (below).

## BUG-2 · HIGH · Spurious "You don't have permission" toast on every page for non-admin roles

- **Repro:** log in as `store_admin` or `warehouse_supervisor` → red toast "You don't have permission to perform this action." on login and on every page load.
- **Root cause:** the shell (Topbar store counter + brand store) unconditionally fetches `GET /core/api/v1/admin/stores?pageSize=100` and `GET /core/api/v1/admin/brands` — both 403 for these roles (`store_admin` has `stores.read` but the list endpoint wants `stores.list`) — and the axios 403 interceptor toasts globally.
- **Side effect:** topbar shows **"All 0 stores"** for a store admin whose store demonstrably exists.
- **Fix:** gate these background fetches on the user's permissions (skip when not granted) and don't toast for passive shell queries.
- **Status: FIXED** — permission-gated the fetches in `Topbar`, `Sidebar`, `BrandSwitcher` (new `useBrands` enabled param), `useAccessControl` (roles/franchises), `usePickupRequests` (pickup.read), `OrdersPage` + `NeedsActionPanel` + `DashboardPage` (stores.list / customer.read). Store-count text hidden without stores.list. Verified live: store_admin sees no toast and no "All 0 stores".

## BUG-3 · HIGH · Failed login shows no error — form silently resets

- **Repro:** on `/login` enter a wrong password → POST returns 401 → page hard-reloads back to a pristine login form; no "invalid credentials" message ever appears.
- **Root cause:** `passwordLogin()` in `admin-web/src/api/auth.ts` does not pass `_skipAuthRetry`, so the 401 enters the interceptor's refresh-and-retry path; the refresh fails → `clearAuth()` + `window.location.replace('/login')` wipes the error state. (`stepUpSend`/`stepUpVerify` already opt out.)
- **Fix:** pass `SKIP_AUTH_RETRY` on `passwordLogin` (and `logout`).
- **Status: FIXED** — also mapped the 401 to the friendly i18n message in `LoginPage` (was raw "Request failed with status code 401"). Verified live: wrong password now shows the error banner in place.

## BUG-4 · MEDIUM · Dead "Fabrics" nav item silently lands on the dashboard

- **Repro:** platform_admin → Catalogue ▸ Fabrics → URL flips to `/catalog/fabrics` → wildcard route bounces to `/` (dashboard) with the Fabrics item still highlighted.
- **Root cause:** the navigator seed (`identity_access.modules`) contains `/catalog/fabrics` (`catalog.fabric.manage`), but the frontend router has no such route; the catch-all `*` → `/` swallows it.
- **Fix:** route `/catalog/fabrics` to the Pricing page's Fabric multipliers tab (or add a dedicated page).
- **Status: FIXED** — added the `/catalog/fabrics` route (renders CatalogPage with the Fabric multipliers tab preselected) + its `catalog.fabric.manage` entry in the route-permission map.

## BUG-5 · MEDIUM · Raw UUIDs shown where names belong

- **Order drawer** (any role): Summary shows `Customer: 09ac0fd6-418e-4d2c-9c5d-e5cc54f1d099`, `Store: db417624-…` — the list card right next to it resolves both names fine.
- **Analytics ▸ Overview** "Top 5 customers by lifetime value": CUSTOMER ID column is a raw UUID, segment `—`.
- **Roles without `customer.read`** (warehouse supervisor): order cards and dashboard "needs action" items render truncated UUIDs like `...f1d099 · ...2232` as the customer/store line.
- **Fix:** reuse the resolved names in the drawer; join names client-side for analytics; for unprivileged roles show the order code / store code instead of UUID fragments.
- **Status: MOSTLY FIXED** — order drawer now receives resolver props from OrdersPage (falls back to the id only when unresolvable); analytics Top-5 joins names client-side (header renamed "Customer"). Remaining known gap: roles without `customer.read`/`stores.list` (warehouse) still see truncated-id fallbacks on cards — by design until a scoped name endpoint exists.

## BUG-6 · MEDIUM · store_admin dashboard shows misleading zeros

- **Repro:** log in as store_admin → dashboard KPIs: Orders today 0, Revenue ₹0, "No revenue data", store leaderboard empty — while the same store shows 5 active orders / ₹55 today to platform_admin, and the store admin's own Orders screen lists them.
- **Root cause:** KPI cards come from commerce analytics endpoints the role can't call; failures render as zeros instead of a no-access state.
- **Fix:** hide (or replace with an informative empty state) the analytics-backed cards when the role lacks `analytics.read`; derive "orders today / pending pickup" from the ops orders API the role *can* call.
- **Status: FIXED** — deeper root cause found: the dashboard gated ALL queries on `brandStore.activeBrandId`, which only platform admins ever set; brand-scoped users (JWT brand) had every query disabled. Now gates on `useEffectiveBrandId()`; analytics-backed cards show "—  · needs analytics access" without `analytics.read`. Verified live: store_admin sees real Pending Pickup/In Wash/Out for Delivery counts and a populated Needs-Action list.

## BUG-7 · LOW · Order accepted with 0 items and a non-derivable total (data integrity)

- **Evidence:** `LG-2026-LGS-MUM-001-000006` — 0 items, subtotal ₹0, add-ons ₹0, tax ₹0, **grand total ₹80**, amount due ₹80. Drawer shows no Items section at all.
- **Risk:** totals that don't derive from lines make refunds/settlement unauditable. If pickup-first orders legitimately start item-less, the drawer should say "awaiting itemisation" rather than show contradictory totals; the API should reject a nonzero total with zero lines otherwise.
- **Status: MITIGATED (display)** — drawer now renders an explicit "No items recorded yet — awaiting itemisation" empty state instead of omitting the section. Whether the API should hard-reject zero-line nonzero-total orders is a product decision (pickup-first flows may itemise after weighing) — left open.

## BUG-8 · LOW · Coupon validity date off by one (UTC rendering)

- **Repro:** create a coupon "now" (18 Jul 2026 IST, after midnight) → list shows validity starting **17 Jul 2026**.
- **Root cause:** date-only rendering of a UTC timestamp; IST (+05:30) dates shift back one day for times before 05:30.
- **Fix:** render validity dates in the tenant timezone (`Asia/Kolkata`).
- **Status: FIXED** — root cause was the *default* start date being computed via `toISOString().slice(0,10)` (UTC date). Added `localIsoDate()` util; coupon + promotion drawer defaults and the dashboard's from/to date-range params now use local dates.

## BUG-9 · HIGH (found while fixing) · Step-up verify mints a token with NO permissions — locks out non-admin staff

- **Repro (API-verified):** login as `warehouse@laundryghar.local` → token carries full `permissions`/`scope_type`/`brand_id` claims → `POST /auth/otp/send` (sensitive_action) + `POST /auth/step-up/verify` → upgraded token contains ONLY `user_type` + `amr`/`stepup_at`; `permissions`, `scope_type/scope_id`, `brand_id`, `scope_nodes` all gone. The client swaps in the upgraded token → user instantly loses every screen until re-login.
- **Root cause:** `/api/v1/auth/step-up/verify` runs **authenticated**, so the pre-auth RLS-bypass allow-list in `core.WebApi/Program.cs` never applies; `ScopeResolver.BuildTokenClaimsAsync` then queries `identity_access.user_scope_memberships` under tenant RLS, sees zero rows, and mints empty claims. Platform admins were immune (TenantResolutionMiddleware always grants them `bypass_rls`) — which is why the admin's warehouse-board step-up worked while the warehouse supervisor's bricked their session.
- **Status: FIXED in code, pending restart** — `Program.cs` now sets `bypass_rls` for `/api/v1/auth/step-up` (same self-keyed-query isolation rationale as the pre-auth paths). **Core host must be restarted** (`bash scripts/run-stack.sh` restart or kill/rerun core.WebApi) and the flow re-verified: warehouse login → warehouse board → Send code → 123456 → board should load with claims intact.

---

## Observations (not defects, for product review)

1. **Topbar breadcrumb is static** — every screen shows "OPERATIONS · DASHBOARD / Good morning, …" with global Today/Export controls; only some pages add their own breadcrumb below.
2. **store_admin sees HQ staff in Access control** — the People tab lists an HQ operations manager (scope "All stores") with an actions menu, and an Invite user button; Franchises tab data 403s. Verify intended scope for `users.list` at store level.
3. **Customers page "name" column falls back to the customer code** (e.g. `APLS8K3Q53`) when no profile name exists — reads oddly next to a real name column; consider a placeholder.
4. Automation note: browser-extension synthetic clicks intermittently didn't register in one session (sign-out appeared broken); verified app-level handler works — not an app bug.

## Environment notes

- Commerce host initially failed to boot due to a concurrent-build race on `SharedDataModel.dll` when all three hosts `dotnet run` simultaneously from a cold build — retry succeeded. Consider pre-building the solution once in `run-stack.sh` before launching hosts.
