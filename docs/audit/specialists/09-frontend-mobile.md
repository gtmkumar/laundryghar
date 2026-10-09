# 09 — Frontend, Mobile and UX Architecture Specialist

## Scope and method

**Inspected:** all four clients: `admin-web/` (React 19 + Vite + TanStack Query + Zustand), `pos-web/` (same stack), `customer-mobile/` (Expo 56 / expo-router / NativeWind) and `rider-mobile/` (Expo 56). Coverage: routing, API clients, stores, auth and token handling, brand and vertical context, navigation and permission gates, feature screens, i18n, tests, and build and deploy wiring (`admin-web/Dockerfile`, `.github/workflows/{ci,release}.yml`, `deploy/docker-compose.yml`). For the multi-tenant and Q13 questions I followed client calls into the backend handlers they hit: `GetNavigator`, `ScopeResolver`, `FabricTypesAdmin`, `CreateParcelOrderCommand`, `Auth.cs` logout/refresh, `AdminSettings`, `AdminWhiteLabel`, `Signup`, `SalonAppointmentStrategy` and `HttpContextCurrentUser.TryGetBrandId`.

**Commands run.** I copied the apps without `node_modules` (tar) into `scratchpad/frontend/`. The repo was not modified.
- `pos-web`: `npm ci --ignore-scripts` succeeded, `tsc -b` exited 0, `eslint .` gave 0 errors and 2 warnings.
- `admin-web`: `npm ci` succeeded, `tsc -b` exited 0, `eslint .` gave 0 errors and 12 warnings. I did not run `vite build`.
- `customer-mobile`: `npm ci` succeeded and `jest` passed 11 suites / 170 tests. `tsc --noEmit` **failed** with TS2882 (`app/_layout.tsx(11,8)`, side-effect import of `../global.css`).
- `rider-mobile`: `npm ci` **failed with ERESOLVE** (lockfile has `react-dom@19.2.8`, which needs `react@19.2.8`, but the root pins `react@19.2.3`). With `--legacy-peer-deps`, `jest` passed 8 suites / 91 tests. `tsc --noEmit` **failed** with the same TS2882 error.
- Environment: Node v22.22.0 and npm 10.9.4, the same major versions as CI (`node-version: 22`).
- `gh run list` / `gh run view 36294076412` (last CI run on `main`, 2026-09-27): `rider-mobile typecheck + tests: failure`, `customer-mobile typecheck + tests: failure`, admin-web / backend / migration lint: success. I could not download the job logs (403 from the log host).

**Not verified:** none of the apps were run against a live backend; there is no .NET SDK, no running DB and no Docker. All runtime behaviour below comes from reading code. I did not run the admin Playwright e2e (`admin-web/e2e/saas-billing.mjs`) because it needs the full stack. I did not run Expo or device builds, and I did not do accessibility testing with tools (only static checks).

## Current-state summary

**Four separate clients, no storefront.**
- `admin-web` is one console for both the platform operator and brand, franchise, store and warehouse staff. Which one a user gets depends on the `user_type` JWT claim and the server navigator.
- `pos-web` is the counter app for store staff.
- `customer-mobile` is the consumer app. It also builds for web: `src/lib/tokenStorage.ts` falls back to `localStorage` there.
- `rider-mobile` is the driver app.
- There is **no public, brand-branded web storefront** and **no business-signup client**. I searched all four clients for `/api/v1/signup`, brand creation, hostname-to-brand resolution and "storefront", and found nothing.

**How the clients reach the backend.** Each web app builds one axios instance per service from build-time `VITE_*_URL` values that point at gateway prefixes (`admin-web/src/api/client.ts:26-34`, `:275-283`). Both mobile apps derive gateway URLs from `app.config.ts` `extra` values or the dev host (`customer-mobile/src/constants/config.ts:43-50`). Every request sends `Authorization: Bearer`. The web apps also send `X-Brand-Id`, taken from the JWT `brand_id` claim, or from the platform admin's switcher selection when the claim is absent (`admin-web/src/api/client.ts:109-146`). On the server, `TenantResolutionMiddleware` turns that header into `brand_id_override`, which `HttpContextCurrentUser.TryGetBrandId` reads (`laundryghar.Utilities/Services/HttpContextCurrentUser.cs:131-137`).

**Admin path, traced end to end:**
1. `Sidebar` calls `useNavigator`, which calls `GET /identity/api/v1/admin/navigator`. The gateway forwards it to core `Navigator.GetNavigator` (`RequireAuthorization`), which dispatches `GetNavigatorQueryHandler`.
2. The handler reads the active modules, resolves the brand vertical from `TryGetBrandId()`, applies the vertical gate (`VerticalKey.IsAvailableTo`), then the entitlement gate (only when `Entitlement:Enforced`, which is `true` in `core.WebApi/appsettings.json:12-14`), then the permission gate (`core.Application/Identity/AccessControl/Queries/GetNavigator/GetNavigator.cs:25-88`).
3. Route-level gating in the browser is a hand-maintained static map (`admin-web/src/lib/routePermissions.ts:24-51`) enforced by `RequirePermission`, using permissions decoded from the JWT (`hooks/usePermissions.ts:64-87`).
4. The server answers a plan-based denial with 402 and a step-up requirement with 403; the client turns these into an upgrade toast or an OTP dialog (`api/client.ts:178-223`).

**Auth state per client:**
- **admin-web:** the access token is persisted in `localStorage` (`stores/authStore.ts:75-85`). The refresh token is kept only in memory plus the HttpOnly `lg_refresh` cookie (path `/identity/api/v1/auth/refresh`, SameSite=Strict, `core.WebApi/Endpoints/Identity/Auth.cs:39-51`).
- **pos-web:** both tokens are persisted in `localStorage`.
- **Both mobile apps:** `expo-secure-store` on native.
- **Brand context:**
  - Web staff: the JWT `brand_id` claim, or the persisted switcher selection for `platform_admin`.
  - Mobile: a **build-time constant** `DEFAULT_BRAND_CODE` (default `LG-MAIN`), sent as `brandCode` on OTP, Google and PIN login and on public engagement calls (`customer-mobile/src/api/auth.ts:68-111`, `src/api/engagement.ts:31-36`).

**Vertical awareness:**
- admin-web reads the terminology pack (`GET /api/v1/terminology`) and fulfilment-config status labels.
- The admin order *actions* and *filters* are still the hardcoded laundry state machine.
- The customer, rider and POS apps each ship a `lib/terminology.ts`, but it is never called. Their UI copy is laundry-specific.
- POS order detail and customer/rider tracking do use the backend fulfilment config.

**How real the screens are.** Almost every screen in all four apps calls real endpoints; I found no `src/data` fixtures used in admin-web or POS. Mock or placeholder content is limited to:
- `customer-mobile/src/data/demoItems.ts`, used as a *production* fallback.
- `rider-mobile/src/data/demoTasks.ts`, used only when `FEATURES.riderTasksApi=false` (currently `true`).
- Customer UPI/card payment and wallet top-up ("coming soon").
- The rider notifications screen (static).
- `admin-web/src/pages/ComingSoonPage.tsx`, which exists but is not routed anywhere.

## Feature inventory

### admin-web (routes in `admin-web/src/App.tsx:55-121`)

| Feature | Screen path | Real API? (endpoint) | Status | Evidence |
|---|---|---|---|---|
| Login (password + Google), accept invite | `/login`, `/accept-invite` | identity `/api/v1/auth/password/login`, `/auth/google`, `/auth/accept-invite` | Working | `src/api/auth.ts:19-47`, `src/api/invite.ts` |
| Data-driven sidebar | shell | identity `GET /api/v1/admin/navigator` | Working (see SA-FE-005 for stale cache) | `hooks/useNavigator.ts:6-14`, `GetNavigator.cs:25-88` |
| Dashboard | `/` | analytics `/api/v1/admin/.../dashboard` (analyticsClient) | Working (dev); broken in Docker image (SA-FE-001) | `src/api/analytics.ts` |
| Tenancy: stores, franchises, warehouses, delivery slots | `/tenancy` | identity `/api/v1/admin/stores`, `/warehouses`, `/franchises` | Working | `src/api/tenancy.ts` |
| Brand create/edit (vertical, branding) | — | backend `POST/PUT /api/v1/admin/brands` exists | **Missing UI** (list/get only) | `src/api/tenancy.ts:20-34`; `core.WebApi/Endpoints/Identity/AdminBrands.cs:28-29` |
| Franchise onboarding wizard | `/access-control` drawer | identity `/api/v1/admin/franchises/onboarding/*` | Working | `src/api/onboarding.ts:12-40` |
| Staff/user mgmt: people, roles, invites, overrides, memberships, ABAC policies | `/access-control` | identity `/api/v1/admin/access/*`, `/admin/users/*`, `/admin/policies` | Working | `src/api/accessControl.ts`, `users.ts`, `policies.ts` |
| Licensing / entitlements / platform invoices | `/access-control?tab=modules`, `/platform-plans`, `/platform-billing` | identity `/api/v1/admin/entitlements/*`, finance `/platform-plans` | Working | `src/api/entitlements.ts`, `finance.ts` |
| Items, catalog, price matrix, fabrics, add-ons, value slabs | `/items`, `/catalog`, `/catalog/fabrics` | catalog `/api/v1/admin/items`, `/fabric-types`, `/add-ons` … | Working (laundry-shaped) | `src/api/catalog.ts` |
| Orders list, detail, status, cancel, invoice, notes, pickups, ops queues | `/orders` | orders `/api/v1/admin/orders/*`, `/pickup-requests` | Partial: laundry-only status actions (SA-FE-004) | `pages/orders/orderStatus.ts:43-63`, `OrderDetailDrawer.tsx:504` |
| Warehouse board | `/warehouse/board` | warehouse client | Working (dev); laundry-specific (garment scan) | `src/api/warehouse.ts` |
| Customers, support inbox | `/customers`, `/support` | catalog `/admin/customers`, logistics support | Working | `src/api/catalog.ts`, `support.ts` |
| Riders, verification, payouts, incentives | `/riders/*` | identity + logistics `/api/v1/admin/riders/*` | Working (dev) | `src/api/riders.ts`, `riderPayouts.ts`, `incentives.ts` |
| Packages, coupons, promotions, customer subscriptions | `/packages`, `/coupons`, `/promotions`, `/subscriptions` | commerce `/api/v1/admin/*` | Working (dev) | `src/api/commerce.ts`, `subscriptions.ts` |
| CMS: banners, slides, app config, notification templates/outbox/logs | `/cms` | engagement `/api/v1/admin/*` | Working (dev) | `src/api/engagement.ts` |
| Cash book, expenses, royalty | `/cashbook`, `/expenses`, `/royalty` | finance `/api/v1/admin/*` | Working (dev) | `src/api/finance.ts` |
| Settings: email, SMS, WhatsApp, payments, maps, fare, dispatch, payout, provisioning, business rules, custom domains | `/settings` | identity `/api/v1/admin/settings/*`, catalog business-settings, `/admin/brands/{id}/domains` | Working; custom domains have no consumer (SA-FE-008) | `src/api/settings.ts`, `brandDomains.ts` |
| Placeholder | — | — | `ComingSoonPage` defined but not routed | `pages/ComingSoonPage.tsx:5-20` |

### pos-web (routes in `pos-web/src/App.tsx:55-91`)

| Feature | Screen path | Real API? | Status | Evidence |
|---|---|---|---|---|
| Login / refresh | `/login` | identity `/api/v1/auth/*` | Working; refresh token in `localStorage` (SA-FE-003) | `stores/authStore.ts:64-82` |
| New walk-in order: customer lookup/create, catalog, price resolve, coupon, payment, receipt, tags | `/new-order` | catalog `/admin/customers`, `/admin/price-list…/resolve`; orders `POST /admin/orders`; commerce offline payment | Working (laundry-shaped: "Garment Tags") | `src/api/{customers,catalog,orders,payments}.ts`, `pages/pos/NewOrderPage.tsx:61,437` |
| Orders list/detail, status advance, invoice | `/orders`, `/orders/:id` | orders `/api/v1/admin/orders/*` | Working; uses server `allowedTransitions` with a laundry fallback | `pages/orders/OrderDetailPage.tsx:50`, `lib/utils.ts:136-170` |
| Cash book | `/cash-book` | finance `/api/v1/admin/cash-books/*` | Working | `src/api/finance.ts` |
| Offline behaviour | global | — | Partial: banner, submit blocked, cart persisted per store; no offline order queue | `hooks/useNetworkStatus.ts`, `components/shared/OfflineBanner.tsx`, `stores/cartStore.ts` |
| Deployability | — | — | **No Dockerfile; not in CI, release or compose** | `grep -c pos-web` = 0 in `.github/workflows/*.yml`, `deploy/docker-compose.yml` |

### customer-mobile (`customer-mobile/app/**`)

| Feature | Screen path | Real API? | Status | Evidence |
|---|---|---|---|---|
| Onboarding slides | `(auth)/onboarding` | engagement `GET /public/onboarding-slides?brandCode=` | Working (hardcoded fallback slides) | `src/api/engagement.ts:42-55`, `app/(auth)/onboarding.tsx:33-49` |
| Phone OTP, Google, link phone, PIN set/unlock, biometrics | `(auth)/*` | identity `/customer/auth/otp/*`, `/google`, `/pin`, `/pin/verify`, `/phone/link/*` | Working; Apple sign-in "coming soon" | `src/api/auth.ts:68-209`, `app/(auth)/phone.tsx:147` |
| Home (banners, recent orders) | `(tabs)/home` | engagement banners; orders | Working | `src/hooks/useEngagement.ts` |
| Laundry booking: items, pickup slot, pay, confirm | `booking/*` | catalog `/customer/catalog/price-list`, `/delivery-slots`, `POST /customer/pickup-requests` | Partial: `DEMO_ITEMS` fallback in production (SA-FE-010) | `app/(app)/booking/items.tsx:158-184`, `pay.tsx:362-400` |
| Online payment UPI/card | `booking/pay` | `initiatePayment` API wrapper exists but is unused | **Placeholder** | `app/(app)/booking/pay.tsx:586-594` |
| Parcel (point-to-point) booking | `parcel/*` | `/customer/fare/quote`, `POST /customer/orders/parcel` | Working; shown to every brand (SA-FE-006) | `src/api/orders.ts:98-125`, `app/(app)/(tabs)/_layout.tsx:162-180` |
| My orders, detail, tracking, reschedule, cancel, rate | `(tabs)/my-orders`, `orders/*` | `/customer/orders/*`, `/fulfillment-config/` | Working; tracking is vertical-aware | `app/(app)/orders/tracking/[id].tsx:476-487` |
| Wallet balance/transactions, loyalty | `(tabs)/wallet` | `/customer/wallet/*`, `/customer/loyalty/balance` | Partial: top-up is "coming soon" (`FEATURES.walletTopUp=false`) | `src/constants/config.ts:116`, `app/(app)/(tabs)/wallet.tsx:57-59` |
| Packages purchase / my packages | — | hooks `useAvailablePackages` / `useMyPackages` exist | **No screen** | `src/hooks/useCommerce.ts:51-65`; no reference under `app/` |
| Offers, addresses, profile, account deletion, consents, support tickets, notifications (derived from orders), help, price list | various | `/customer/coupons`, `/addresses`, `/profile`, `/account/deletion-request`, `/support/tickets` | Working | `src/api/*.ts` |

### rider-mobile (`rider-mobile/app/**`)

| Feature | Screen path | Real API? | Status | Evidence |
|---|---|---|---|---|
| OTP login (staff lane) | `(auth)/login`, `otp` | identity `/auth/otp/send`, `/auth/otp/verify`, `/auth/refresh` | Working | `src/api/auth.ts:32-100` |
| Duty toggle, today's tasks and assignments, location ping/background tracking | `home`, `tasks` | logistics `/rider/duty`, `/rider/tasks/today`, `/rider/assignments/today`, `/rider/location/ping` | Working; demo fallback only when flag off (`riderTasksApi: true`) | `src/api/tasks.ts:34-41`, `src/constants/config.ts:34-35` |
| Task detail: OTP verify, proof photo, pickup inspection | `tasks/[id]`, `inspection/[id]`, `delivered` | `/rider/tasks/{id}/status`, `/verify-otp`, `/proof-photo`, `/inspection` | Working; inspection is laundry "garment condition" | `src/api/tasks.ts:1-12`, `src/api/inspection.ts` |
| Offline status queue | global | replays PATCH status | Partial (AsyncStorage queue, global key) | `src/store/offlineQueueStore.ts:16-50` |
| Cash/COD, earnings, payouts, incentives, documents/KYC, profile, support | various | `/rider/cash/summary`, `/payouts`, `/payout-requests`, `/incentives`, `/documents`, `/rider/me`, `/rider/support/tickets` | Working | `app/(app)/*.tsx` hooks |
| Notifications | `notifications` | none | **Placeholder** (static list) | `app/(app)/notifications.tsx:1-26` |

### Client state model (auth / tenant / vertical / permissions / entitlements)

| Concern | admin-web | pos-web | customer-mobile | rider-mobile |
|---|---|---|---|---|
| Access token | `localStorage` `lg-admin-auth` | `localStorage` `lg-pos-auth` | SecureStore (native), `localStorage` (web) | SecureStore |
| Refresh token | memory + HttpOnly cookie | **`localStorage`** | SecureStore | SecureStore |
| Brand source | JWT `brand_id`, otherwise persisted switcher (`lg-admin-brand`) | JWT, otherwise switcher | build-time `DEFAULT_BRAND_CODE` | build-time code (engagement only); staff JWT |
| Vertical | `useActiveVertical` (store brand, otherwise `GET /admin/brands/{id}`) + `/terminology` | fulfilment config per order `jobType` | fulfilment config (tracking only) | fulfilment config (task detail) |
| Permissions | JWT `permissions` claim parsed client-side; server navigator | JWT parsed (`usePermissions`) | n/a (customer lane) | n/a |
| Entitlements | server navigator + 402 handling; licensing tab | 402 not specially handled | none | none |
| Navigation | **server-driven** (navigator API), plus a static route→permission map | hardcoded 3 routes | hardcoded tabs (laundry + parcel) | hardcoded |

## Findings

### SA-FE-001 — admin-web production image bakes only 3 of its 9 API base URLs
- Category: Build/Deploy correctness (Related area: OPS)
- Severity: High
- Status: Verified (code and config read; the image was not built or run)
- Evidence:
  - `admin-web/src/api/client.ts:26-34,275-283`: nine `VITE_*_URL` values (identity, catalog, orders, engagement, analytics, commerce, warehouse, logistics, finance).
  - `admin-web/Dockerfile:24-31`: only `ARG VITE_IDENTITY_URL`, `VITE_CATALOG_URL` and `VITE_ORDERS_URL`.
  - `.github/workflows/release.yml:44-52`: the same three build args.
  - `deploy/docker-compose.yml:102-109`: the same three.
  - `admin-web/.dockerignore` excludes `.env` and `.env.*`.
  - `admin-web/deploy/nginx.conf:16-18`: `try_files $uri $uri/ /index.html`.
- Observed behaviour:
  - In the released or compose-built image, `engagementClient`, `analyticsClient`, `commerceClient`, `warehouseClient`, `logisticsClient` and `financeClient` get `baseURL: undefined`.
  - Their requests go to the admin nginx origin. A GET there returns `index.html` (the `unwrap` call then fails); a POST returns 405/404.
  - Screens affected: dashboard (analytics), CMS, coupons/packages/promotions/subscriptions, warehouse board, riders/support/incentives/payouts, cash book/expenses/royalty/platform plans. The Google button is also always hidden because `VITE_GOOGLE_CLIENT_ID` is not passed.
- Reproduction / verification: `grep -n "ARG VITE" admin-web/Dockerfile`, then compare with `grep VITE_ admin-web/src/api/client.ts`.
- Impact: most of the back office is non-functional in the only shipped deployment path. Local dev works because `.env` has all nine values, which hides the problem.
- Recommended remediation: add the six missing `ARG`/`ENV` lines plus `VITE_GOOGLE_CLIENT_ID` to the Dockerfile, release.yml and compose. Better still, inject one `window.__CONFIG__` at container start, which also allows runtime per-environment config.
- Regression tests required: a CI step that builds the image and greps `dist/assets/*.js` for each gateway prefix (`/engagement`, `/analytics`, …); a smoke e2e of the dashboard and CMS against compose.
- Dependencies / priority: P0 before any production deploy.

### SA-FE-002 — admin-web logout does not end the session after any page reload
- Category: Session management / Security (Related area: AUTH)
- Severity: High
- Status: Partially Verified (full code path traced client and server; not executed)
- Evidence:
  - `admin-web/src/stores/authStore.ts:75-85`: the refresh token is deliberately not persisted, so it is `null` after a reload.
  - `admin-web/src/components/layout/Topbar.tsx:43-53`: `if (refreshToken) await logout(refreshToken)`, so the `/logout` call is skipped when the in-memory token is gone.
  - `core.WebApi/Endpoints/Identity/Auth.cs:185-210`: the server logout would clear the cookie even without a token, but the client never calls it. Even when it is called, the cookie is scoped to the refresh path only (`Auth.cs:51`), so `/logout` cannot read it to revoke the token family.
  - `admin-web/src/components/layout/ProtectedRoute.tsx:42-67`: when the access token is missing, the app silently refreshes via the cookie.
- Observed behaviour: an admin logs in, reloads the tab (or opens a new one) and clicks Logout. The local state is cleared and they land on `/login`. Visiting `/` then triggers the cookie refresh, which succeeds, so they are signed in again without credentials. The refresh family stays valid until it expires.
- Reproduction / verification: read the three code sites above. Runtime check: log in, reload, log out, navigate to `/`, and observe the network sequence `POST /identity/api/v1/auth/refresh 200`.
- Impact: on shared or counter computers, logout is ineffective. Combined with SameSite=Strict this is not cross-site exploitable, but the next person at the machine gets the previous admin's session (which could be a `platform_admin`).
- Recommended remediation: always `POST /logout` with `withCredentials: true`, without the `if`. Widen the cookie path to `/identity/api/v1/auth` (refresh and logout), or add a cookie-reading `/auth/refresh/logout` under the same path, so the server can revoke the family and not just delete the cookie.
- Regression tests required: Playwright: login, reload, logout, goto `/`, expect `/login`. Backend: an integration test that logout via cookie revokes the family, so a later refresh returns 401.
- Dependencies / priority: P0/P1.

### SA-FE-003 — pos-web stores the refresh token in localStorage, and its refresh path ignores the HttpOnly cookie
- Category: Token storage security (Related area: SEC)
- Severity: High
- Status: Verified
- Evidence:
  - `pos-web/src/stores/authStore.ts:64-82`: `persist(...)` with `name: 'lg-pos-auth'` and `localStorage`, and **no `partialize`**, so `accessToken`, `refreshToken` and `user` are all written to disk.
  - `pos-web/src/api/client.ts:136-144`: the 401 interceptor throws `'No refresh token available'` unless the body token exists, and does not send `withCredentials`.
  - Compare `admin-web/src/stores/authStore.ts:77-85`, which explicitly avoids this pattern for XSS reasons.
- Observed behaviour: any XSS on the POS origin (or a browser extension, or someone at a shared counter tablet) can read a long-lived refresh token and mint access tokens until it expires or is rotated.
- Impact: counter tablets are exactly the shared, poorly managed devices where this risk is highest. The two staff web apps also behave inconsistently.
- Recommended remediation: mirror admin-web: add `partialize` that excludes `refreshToken`, and switch the interceptor refresh to `refreshAccessToken()` (which uses the cookie, `client.ts:179-191`).
- Regression tests required: a unit test that the persisted `lg-pos-auth` JSON has no `refreshToken`; an e2e that a hard reload still refreshes through the cookie.
- Dependencies / priority: P1.
- Prior-doc cross-ref: GAP_ANALYSIS_R3 R3-POS-2 fixed the expiry check only.

### SA-FE-004 — Admin order management hardcodes the laundry state machine and ignores the server's `allowedTransitions`
- Category: Multi-vertical correctness
- Severity: High
- Status: Verified (client code plus backend strategy read)
- Evidence:
  - `admin-web/src/pages/orders/orderStatus.ts:13-63`: a laundry ladder, with a comment pointing at a non-existent `laundryghar.Orders/.../OrderStateMachine.cs`.
  - `pages/orders/OrderDetailDrawer.tsx:504`: `advanceableTargets(order.status)`.
  - `pages/orders/OrdersPage.tsx:33,122`: the status filter is `ORDER_STATUS_LIST`.
  - Backend: `operations.Application/Fulfillment/Salon/SalonAppointmentStrategy.cs:47-66` uses `booked→confirmed→checked_in→in_service→completed`.
  - The server already returns `AllowedTransitions` (`operations.Application/Orders/Orders/Dtos/OrderDtos.cs:137`). POS uses it (`pos-web/src/pages/orders/OrderDetailPage.tsx:50`); admin-web's type does not even declare it.
- Observed behaviour: for a salon (`booked`), logistics or recurring order, `ALLOWED_TRANSITIONS[status]` is `undefined`, so no action buttons appear and the order cannot be progressed from the admin console. The status filter offers only laundry statuses. Status *labels* are vertical-aware (`useStatusLabeler`), so the screen looks correct but cannot be operated.
- Impact: Q9. Order processing for any non-laundry vertical is not usable in the main business console.
- Recommended remediation: add `allowedTransitions` to `OrderDto` in `types/api.ts` and use it, as POS does. Build the filter from `useFulfillmentConfig()` stages plus terminal statuses. Keep the laundry map only as a fallback.
- Regression tests required: component tests for the drawer with a salon fixture (`booked` shows Confirm / Cancel); an e2e against a seeded salon brand.
- Dependencies / priority: P1, a prerequisite for any non-laundry tenant.

### SA-FE-005 — Brand switching and logout do not scope or clear the client cache, so data from one brand shows under another
- Category: Tenant isolation (client), data correctness
- Severity: Medium
- Status: Verified (code); not executed
- Evidence:
  - `admin-web/src/components/layout/BrandSwitcher.tsx:61-67`: `setActiveBrand` only, with no `queryClient` invalidation.
  - `hooks/useOrders.ts:17-24` and `hooks/useAnalytics.ts:15-23`: query keys without a brand. Catalog keys likewise (`hooks/useCatalog.ts:104-166`).
  - `App.tsx:46-52`: `refetchOnWindowFocus: false`, `staleTime: 30_000`.
  - `hooks/useNavigator.ts:9`: key `['navigator', accessToken]`, but the server navigator varies by `X-Brand-Id` (`GetNavigator.cs:37-45`, `HttpContextCurrentUser.cs:131-137`).
  - `Topbar.tsx:49-51`: logout clears the stores but not the `QueryClient`, and `api/client.ts:261-262` (forced logout) clears auth but not `lg-admin-brand`.
  - `hooks/useActiveVertical.ts:21,37`: the stored brand's vertical wins over the JWT brand.
- Observed behaviour:
  - A platform admin on `/orders` who switches from brand A to brand B keeps seeing A's orders, because the key is unchanged and nothing triggers a refetch. Mutations then go out with `X-Brand-Id: B` against A's ids.
  - The sidebar keeps A's vertical modules (e.g. laundry "Fabrics" on a salon brand) for up to 5 minutes.
  - After a forced logout, the next user's terminology can come from the previous user's stored brand.
  - (Only some hooks include `brandId`: access-control, entitlements, business settings, the POS catalog.)
- Impact: operators act on the wrong tenant's data. The server is still authoritative (RLS / brand filters), so this is a confusion and integrity risk, not a direct leak.
- Recommended remediation: include `useEffectiveBrandId()` in every brand-scoped query key, or call `queryClient.clear()` in `setActiveBrand` and on every logout path; key the navigator on `[accessToken, brandId]`; call `clearBrand()` in the 401 forced-logout path.
- Regression tests required: a hook test that switching brand changes the keys or clears the cache; an e2e that switches brand and asserts the order list re-fetches with the new header.
- Dependencies / priority: P1.

### SA-FE-006 — Vertical restrictions are client/navigation-only; the API does not enforce them (Q13)
- Category: Authorization depth (Related area: AUTHZ / ENT)
- Severity: Medium
- Status: Partially Verified (handlers and token resolver read; no live call)
- Evidence:
  - `GetNavigator.cs:72-80`: vertical gate on *navigation*.
  - `db/patches/phase2_slice_b_fabric_module.sql:45-46`: the `fabrics` module has `vertical_key='laundry'` and gates `catalog.fabric.manage`.
  - `operations.WebApi/Endpoints/Catalog/FabricTypesAdmin.cs:21-27`: the endpoints require only `permission:catalog.read` / `catalog.fabric.manage`.
  - `ScopeResolver.cs:185-240`: token permissions are filtered by *entitlement* only. Grepping it for `vertical` finds nothing.
  - Customer side: `customer-mobile/app/(app)/(tabs)/_layout.tsx:162-180` always offers "Parcel".
  - `operations.Application/Orders/Orders/Commands/CreateParcelOrderCommand.cs:48-95` has no vertical or feature check (it checks the quote token, address ownership and store only).
- Observed behaviour: a non-laundry brand admin who holds the permission (granted by role) can call the laundry fabric APIs directly. Any brand's customer can create point-to-point parcel orders, whatever the brand's vertical or plan. The only "restriction" is that the menu is hidden.
- Positive control: *entitlement* restrictions are enforced server-side (permissions are stripped from the token when `Entitlement:Enforced=true`, `appsettings.json:12-14`, and denials return 402). The `/settings` gate is also enforced server-side (`AdminSettings.cs:48-64`).
- Impact: the "one primary vertical per business" target is not enforced at the API.
- Recommended remediation: add a vertical/feature endpoint filter (resolve `brands.vertical_key` and the feature from module metadata) to vertical-specific endpoint groups and to `CreateParcelOrder` / `FareQuote`. On the client, show the parcel FAB only when the brand config says so.
- Regression tests required: an integration test that a salon brand calling `POST /admin/fabric-types` gets 403/402; that a non-logistics brand customer calling `POST /customer/orders/parcel` is denied.
- Dependencies / priority: P1.

### SA-FE-007 — No business self-signup UI and no brand management UI, although the backend endpoints exist
- Category: Feature completeness (Q9 onboarding)
- Severity: Medium
- Status: Verified (searched all four clients)
- Evidence: backend `core.WebApi/Endpoints/Identity/Signup.cs:24-33` (`/api/v1/signup/templates|start|complete`, anonymous) and `AdminBrands.cs:28-29` (`POST`/`PUT` brands). Client: `admin-web/src/api/tenancy.ts:20-34` has `getBrands` / `getBrandById` only. `grep -rn "signup"` across the clients finds only a Google button label (`GoogleSignInButton.tsx:41`).
- Observed behaviour: a new business cannot onboard itself through any UI. A platform admin cannot create a brand, choose its vertical or edit its branding from admin-web. Franchise onboarding inside an existing brand *is* implemented.
- Impact: the target SaaS onboarding funnel has no front door, so tenant creation needs API calls or SQL.
- Recommended remediation: a minimal signup SPA route (template → OTP → complete) using the existing endpoints, and a platform-admin "Brands" tab (create/edit: name, vertical, logo, colours, plan).
- Regression tests required: an e2e of signup against a seeded template; a test of the admin brand create form.
- Dependencies / priority: P1 for SaaS go-live.

### SA-FE-008 — Branding and white-label: brand is fixed per build, theme is hardcoded, custom domains have no consumer (Q8)
- Category: Multi-tenant branding
- Severity: Medium
- Status: Verified
- Evidence:
  - `customer-mobile/app.config.ts:6,10,19,27,57`: name "Laundry Ghar", bundle id `com.laundryghar.customer`, colour `#4A552A`, `EAS_PROJECT_ID = 'laundryghar-customer'` (a placeholder; its own TODO says OTA returns 404), and `defaultBrandCode` from env.
  - `customer-mobile/eas.json`: profiles are dev/preview/production only, with no per-brand profile. The rider app is the same (`rider-mobile/app.config.ts:10,19,39,81`).
  - `core.WebApi/Endpoints/Identity/AdminWhiteLabel.cs:21-41` serves per-brand app config, with no client or script consumer.
  - Custom domains can be registered and verified (`admin-web/src/api/brandDomains.ts:9-40`), but no client resolves brand from hostname and there is no storefront.
  - Web theme: CSS variables hardcoded in `admin-web/src/index.css:20-28`, and "Laundry Ghar" is hardcoded in `Sidebar.tsx:214` and `LoginPage.tsx:159`.
- Observed behaviour: one mobile binary maps to one brand code chosen at build time. Shipping brand B's app requires editing `app.config.ts`. No client loads a theme or logo at runtime. No brand-specific branding is shown to brand staff in admin-web or POS.
- Impact: Q8 client side is Not Supported. The per-brand storefront and white-label mobile apps in the target do not exist.
- Recommended remediation (smallest): make `app.config.ts` read name, slug, bundle id, colours, icons and brand code from an env/JSON file per brand (generated from `/admin/white-label/apps/{app}`), and add per-brand EAS profiles. For web, load brand theme tokens at runtime after login (`GET /admin/brands/{id}`) into CSS variables.
- Regression tests required: config snapshot tests per brand profile; a visual check of themed admin-web.
- Dependencies / priority: P2 (after SA-FE-007).

### SA-FE-009 — Server terminology is wired only in admin-web; customer, rider and POS ship hardcoded laundry copy
- Category: Laundry coupling / multi-vertical UX
- Severity: Medium
- Status: Verified
- Evidence:
  - `customer-mobile/src/lib/terminology.ts`, `rider-mobile/src/lib/terminology.ts` and `pos-web/src/lib/terminology.ts` all say words "come from `GET /api/v1/terminology`", but no API call and no non-test import exist in any of the three apps (a grep for `terminology` outside the lib and tests returns nothing).
  - Copy: `customer-mobile/src/i18n/locales/en.json:3,88,92,114,165-166,188` ("What needs washing?", "garment", "20% off your first wash", "Laundry pickup"); 21 laundry-term lines in customer `en.json`.
  - POS: `components/print/GarmentTags.tsx`, `pages/orders/OrderDetailPage.tsx:154` ("Garment Tags").
  - Rider: inspection is laundry "garment condition" (`src/api/tasks.ts:9`).
  - Laundry-only admin pages: `pages/catalog/FabricMultipliersTab.tsx`, `pages/warehouse/WarehouseBoardPage.tsx`, `components/warehouse/*`.
- Impact: a salon or courier tenant sees laundry vocabulary, which the strategy doc names as a top product risk. The code looks vertical-ready only because it carries dead helper modules.
- Recommended remediation: add a `useTerminology()` hook (copy the admin version) to the three apps and route item nouns through `itemNoun()`. Move vertical-specific strings into terminology keys.
- Regression tests required: render tests with a salon pack asserting no "garment" or "wash" text appears.
- Dependencies / priority: P2.

### SA-FE-010 — Customer booking falls back to hardcoded demo garments and prices in production
- Category: Mock data in production path
- Severity: Medium
- Status: Verified (client); whether the server accepts such a request is Not Tested
- Evidence: `customer-mobile/app/(app)/booking/items.tsx:27,158-184`: `if (live.length > 0) return live; return DEMO_ITEMS.map(...)`, not gated by `__DEV__`. `src/data/demoItems.ts:13-21` (Shirt ₹170 … Coat ₹480). `app/(app)/booking/pay.tsx:376-386` then submits `itemId: null` and `estimatedUnitPrice` set to the demo price.
- Observed behaviour: when a brand's price list is empty, or the request fails (`priceList` is undefined), the customer is shown fabricated laundry items and prices and can schedule a pickup with them. For any non-laundry brand this always shows laundry garments.
- Impact: a misleading price promise and wrong data in pickup requests.
- Recommended remediation: gate the fallback with `__DEV__`; in production render `ErrorState` / `EmptyState` with retry.
- Regression tests required: a component test for an empty price list in production mode, which should show the empty state with no demo items.
- Dependencies / priority: P1.

### SA-FE-011 — Client quality gates: mobile CI is red, pos-web is outside CI/CD, web apps have no unit tests
- Category: Testing / CI (Related area: OPS, QA)
- Severity: Medium
- Status: Partially Verified (reproduced locally in scratch copies with the same Node major version; CI conclusion read via `gh`, logs not downloadable)
- Evidence:
  - `rider-mobile` `npm ci` gives ERESOLVE (`react-dom@19.2.8` vs `react@19.2.3`, lockfile `rider-mobile/package-lock.json:14602-14607`).
  - customer and rider `tsc --noEmit` give TS2882 at `app/_layout.tsx:11`. `tsconfig.json` includes `expo-env.d.ts`, but that file is gitignored (`customer-mobile/.gitignore:5`) and only generated by `expo start`.
  - CI run 36294076412 on `main`: both mobile jobs failed.
  - `.github/workflows/ci.yml:47-86` covers admin-web lint and build plus the mobile jobs; pos-web is absent from ci.yml, release.yml and docker-compose.
  - No `*.test.*` files exist in admin-web or pos-web. `admin-web/e2e/saas-billing.mjs` is a single runner-less Playwright script that needs a live stack and is not in CI.
  - The mobile jest suites (customer: 11 suites / 170 tests; rider: 8 / 91, pass) cover pure libs and stores (API client refresh, cart/booking stores, terminology, fulfilment tracking, version gate, offline queue), not screens.
- Impact: regressions in POS and admin UI logic (the SA-FE-004 class of bug) cannot be caught. Mobile PRs merge on a red pipeline.
- Recommended remediation: regenerate the rider lockfile (or align `react`/`react-dom`); commit a stub `expo-env.d.ts` (`/// <reference types="expo/types" />`) or add a `*.css` module declaration; add a pos-web job (lint, build) and a Dockerfile; add Vitest plus React Testing Library to the web apps, starting with `orderStatus`/`allowedTransitions`, `routePermissions` and the client interceptors.
- Regression tests required: as above.
- Dependencies / priority: P1.

### SA-FE-012 — Customer payments, wallet top-up and packages are not implemented in the UI
- Category: Feature completeness
- Severity: Low
- Status: Verified
- Evidence: `customer-mobile/app/(app)/booking/pay.tsx:586-594` ("UPI & Card — coming soon"; `initiatePayment` in `src/api/commerce.ts:149-156` has no caller); `src/constants/config.ts:116` (`walletTopUp: false`); `useAvailablePackages` / `useMyPackages` (`src/hooks/useCommerce.ts:51-65`) are not referenced under `app/`; `rider-mobile/app/(app)/notifications.tsx:1-26` is a static placeholder.
- Impact: customers can pay only by wallet or COD; prepaid packages exist in admin but cannot be bought.
- Recommended remediation: wire the existing initiate/verify APIs behind the Razorpay SDK, and add a packages screen.
- Dependencies / priority: P2.

### SA-FE-013 — admin-web `/settings` gate drifts from server authorization; the route map is hand-synced
- Category: Client/server authz drift
- Severity: Low
- Status: Verified
- Evidence: `admin-web/src/hooks/usePermissions.ts:46-51` and `components/layout/RequirePermission.tsx:24-31` gate on `user_type in (platform_admin, brand_admin)`, but the server now gates on `permission:settings.read` / `settings.manage` (`core.WebApi/Endpoints/Identity/AdminSettings.cs:31-64`). `lib/routePermissions.ts:1-23` says the server modules table must be mirrored manually because the navigator DTO omits `requiredPermission` (`GetNavigator.cs:85`).
- Impact: a custom role granted `settings.manage` is blocked by the UI; the stale comments contradict R3-SEC-3, which is fixed server-side. The security impact is nil because the server is authoritative.
- Recommended remediation: gate on `hasPermission('settings.read')`. Add `requiredPermission` to `NavItemDto` and derive route gates from it.
- Dependencies / priority: P3.

### SA-FE-014 — WebMCP exposes customer search and order-status mutation to in-browser AI agents in production builds
- Category: Security hardening
- Severity: Low
- Status: Verified (code); a browser with the API was not available
- Evidence: `admin-web/src/App.tsx:123-125` calls `initWebMCP(router)` unconditionally; `src/lib/webmcp.ts:83-215` registers `search_customers` (returns names and phones), `list_orders`, `get_order`, `update_order_status` and `open_admin_page`. There is no `import.meta.env.DEV` guard (unlike `Agentation`, `App.tsx:136`).
- Impact: in browsers that enable WebMCP, any agent (including one steered by prompt injection from page content) can read customer PII and change order states with the admin's privileges.
- Recommended remediation: gate behind `import.meta.env.DEV`, or behind an explicit opt-in setting plus a confirmation step for mutations.
- Dependencies / priority: P2.

### SA-FE-015 — Web routers have no error boundary
- Category: Failure recovery
- Severity: Low
- Status: Verified (grep: no `errorElement` or `ErrorBoundary` in `admin-web/src` or `pos-web/src`; both mobile apps wrap the app in `ErrorBoundary`, `customer-mobile/app/_layout.tsx:23,308`)
- Impact: a render error, or a lazy-chunk 404 after a redeploy (all routes use `lazy()`), shows React Router's default "Unexpected Application Error" with no recovery.
- Recommended remediation: add a root `errorElement` with a reload button and handling for chunk-load errors.
- Dependencies / priority: P3.

### SA-FE-016 — Shared-device residue after logout (POS cart PII, rider offline queue, mobile query caches)
- Category: Session hygiene / privacy
- Severity: Low
- Status: Verified (code)
- Evidence: `pos-web/src/stores/cartStore.ts:32,70` persists the selected `AdminCustomerDto` to `localStorage`, and `Topbar.tsx:16-27` logout does not clear the cart. `rider-mobile/src/store/offlineQueueStore.ts:16` uses a global AsyncStorage key that logout (`src/store/authStore.ts:54-70`) does not clear. No client calls `queryClient.clear()` on logout (grep).
- Impact: the next staff member sees the previous customer's details in the POS basket. Rider B's session replays rider A's queued status PATCHes; the server should reject them, but they are noise. Earlier users' cached lists flash briefly.
- Recommended remediation: clear the cart, offline queue and query cache in every logout path.
- Dependencies / priority: P3.

## Positive controls verified
- **Server-driven navigation** with vertical, entitlement and permission filtering (`GetNavigator.cs:25-88`). Entitlements are also enforced at the token (`ScopeResolver.cs:185-240`) and surfaced as **402** with an actionable toast (`admin-web/src/api/client.ts:178-187`).
- **Step-up (OTP) re-auth** for high-risk actions is single-flight across concurrent 403s with a single retry (`admin-web/src/api/client.ts:196-223`; `src/api/auth.ts:89-120` uses `_skipAuthRetry` correctly).
- **admin-web refresh token** is kept out of `localStorage` and uses an HttpOnly, SameSite=Strict, path-scoped cookie (`authStore.ts:77-85`, `Auth.cs:39-51`). There is proactive refresh and a refresh mutex with queueing (`ProtectedRoute.tsx:42-67`, `client.ts:234-266`).
- **Mobile token storage** uses SecureStore on native (`customer-mobile/src/lib/tokenStorage.ts:33-54`, `rider-mobile/src/store/authStore.ts:46-50`). Customer endpoints derive the customer and brand from the token, never from the URL (`CustomerOrderEndpoints.cs:20-24,134-143`).
- **The route-level 403 page** stops forbidden screens from mounting (`RequirePermission.tsx`). The shared states are used widely under `pages/`: `LoadingState` in 34 files, `ErrorState` in 37 and `ForbiddenState` in 27 (there are 29 `*Page.tsx` files), and mutation errors are never silent (`App.tsx:39-45`).
- **POS reliability:** the cart is persisted and scoped per store, there is an offline banner, submit is blocked while offline, server `allowedTransitions` are preferred, and catalog query keys include `brandId` (`pos-web/src/hooks/useCatalog.ts:64-99`).
- **Rider:** an offline status queue with replay on foreground, background location, polling for task updates, and real APIs on all task flows.
- **Vertical-aware tracking** in customer, rider and POS via `GET /fulfillment-config` (`customer-mobile/app/(app)/orders/tracking/[id].tsx:476-487`).
- **Builds and tests:** admin-web and pos-web `tsc -b` are clean and eslint has 0 errors. Customer jest passes 170 tests and rider jest passes 91 (run locally in scratch).
- **XSS:** no `dangerouslySetInnerHTML`, `innerHTML` or `eval` in any client (grep).

## Open questions / not verified
- Runtime behaviour of every flow; nothing was run against a backend. The SA-FE-002 resurrection and SA-FE-005 stale data should be confirmed with Playwright against compose.
- Whether `POST /customer/pickup-requests` accepts `itemId: null` demo lines (SA-FE-010); I did not read the server handler.
- Whether `FareQuote` (as distinct from `CreateParcelOrder`) has any feature gate; I did not read it.
- The exact failure text of the last CI run (log download was forbidden); local reproduction stands in for it.
- Accessibility was assessed statically only: `aria`/`accessibilityLabel` appear in 28 of 145 admin tsx files, 9 of 33 POS, 44 of 54 customer and 23 of 31 rider. No axe or screen-reader testing was done. Responsive breakpoints are used in only about 21 admin page files; admin is effectively desktop-first. I did not visually test either.
- iOS and Android builds, OTA (the EAS project id is a placeholder), and Expo web deployment.

## Verdict inputs
- **Q8 (branding/storefront, client side): Not Supported.** There is no storefront client, no runtime theming, and mobile brand is a build-time constant with hardcoded name, bundle id and colours. The custom-domain and white-label endpoints have no client consumer (SA-FE-008).
- **Q9 (web, mobile, admin, order mgmt, user mgmt genuinely implemented): Partially Supported.** For laundry, all four apps are real and API-backed: user/staff/role management, franchise onboarding, admin order management, POS, customer booking/tracking and rider flows. Against the multi-vertical SaaS target:
  - The admin Docker image breaks six service clients (SA-FE-001).
  - Non-laundry orders cannot be operated in admin (SA-FE-004).
  - There is no signup or brand management UI (SA-FE-007).
  - Laundry copy and demo data leak into other verticals (SA-FE-009, SA-FE-010).
  - Payments are placeholders (SA-FE-012).
- **Q13 (restrictions only client-side anywhere?): Yes, partially.** Permissions, `/settings` and entitlements are enforced server-side (Verified). **Vertical** restrictions exist only in the navigator and client UI: the laundry fabric APIs and the customer parcel order are not vertical- or feature-gated at the API (SA-FE-006, Partially Verified).
