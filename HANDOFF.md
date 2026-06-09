# LaundryGhar OLMS — Handoff

_Last updated: 2026-06-09 (customer-mobile v2 redesign + live-test bug-fix sweep; Rider Ops initiative — Phases 1-4 + Maps settings)_

## 1. What this is

LaundryGhar is an Online Laundry Management System built as **.NET 10 microservices**
over a single canonical **PostgreSQL 16** database, with four client apps. Multi-tenant
by **brand**, isolated at the database layer via **Row-Level Security (RLS)**.

- **Repo:** https://github.com/gtmkumar/laundryghar (private, branch `main`)
- **Backend:** `backend/laundryghar/` — 9 services + a background Worker, orchestrated by a
  .NET Aspire AppHost. Solution: `laundryghar.slnx`.
- **Clients (repo root):** `admin-web/`, `pos-web/` (React 19 + Vite + TS + TanStack Query +
  Zustand + Tailwind), `customer-mobile/`, `rider-mobile/` (Expo SDK 52 + expo-router + NativeWind).
- **Docs/wireframes:** `docs/`. DB schema + patches: `db/patches/`, `database_scripts/`.

### Services & ports

| Service    | Port     | Schema(s)                      |
| ---------- | -------- | ------------------------------ |
| Identity   | **5050** | tenancy_org, identity_access   |
| Catalog    | 5001     | customer_catalog               |
| Orders     | 5002     | order_lifecycle                |
| Warehouse  | 5003     | order_lifecycle (garments)     |
| Logistics  | 5004     | logistics                      |
| Commerce   | 5005     | commerce                       |
| Finance    | 5006     | finance_royalty                |
| Engagement | 5007     | engagement_cms                 |
| Analytics  | 5008     | analytics (materialized views) |
| Worker     | –        | drains outbox / notifications  |

`SharedDataModel` (≈76 EF entities, all bounded contexts + kernel) and `Utilities`
(API envelope, exceptions) are shared libraries. `ServiceDefaults` wires Aspire (OTel,
health). **Identity is on 5050, not 5000** (macOS AirPlay squats 5000).

## 2. How to run

Prereqs: .NET 10 SDK, Node 22, the `laundry_ghar_db` Postgres running on `localhost:5432`,
`psql` at `/opt/homebrew/opt/postgresql@18/bin/psql`.

**Whole backend + dashboard, one command (run detached so it survives terminal/session):**

```bash
cd backend/laundryghar
nohup env ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://localhost:18888 \
  ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
  DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
  dotnet run --project laundryghar.AppHost > /tmp/lg_apphost.log 2>&1 < /dev/null & disown
```

- **Aspire dashboard: http://localhost:18888** (no token, plain HTTP — the env vars above make it frictionless).
- Health check any service: `curl http://localhost:<port>/health/ready`.
- Clients: `cd admin-web && npm install && npm run dev` (Vite :5173). Mobile: `npx expo start`.

**DB credentials**

- Runtime principal: **`app_user` / `app_user`** (non-superuser → RLS enforced).
- Admin/seeding principal: **`postgres` / `postgres`** (superuser; Dev seeding only).
- Connection: `Host=localhost;Port=5432;Database=laundry_ghar_db`.

**Admin login (seeded):** `POST :5050/api/v1/auth/password/login`
`{"identifier":"admin@laundryghar.local","password":"Admin@123"}` → returns an RS256 access token.

## 3. Current state

All 9 backend bounded contexts are built, security/QA-gated, and verified live end-to-end.
All 4 clients build/typecheck; mobile apps bundle for iOS. The full stack runs from one
Aspire command and was last validated green across every service under the `app_user` runtime.
**Admin rider onboarding** (Identity invite → Logistics profile, KYC approve/reject, franchise
self-service, full-profile edit) is built, security-gated, and verified live + in-browser.
**People** (HQ + franchise employees) now also carry optional employment / KYC / bank details
on `user_profiles`, and their **primary role can be changed** from the Access-control person
drawer (fix a wrongly-assigned role) — see §4.
The **Rider Ops initiative (Phases 1-4)** is complete (see §4): a live tracking board, geofence
auto-status + drop-at-laundry round-trip, COD cash + settlement reconciliation, and a configurable
payout engine — plus a provider-pluggable Maps setting (OSM default; Google/Mapbox via Settings).
Last full clean rebuild + run validated green: all 9 HTTP services + Worker healthy.
The **customer-mobile app was redesigned to "v2"** (warm cream/olive/gold, bottom tabs + FAB,
full pickup booking flow) and **live-tested on iOS + Android** — 6 runtime bugs found & fixed
(see §4). First customer-mobile commit `237b8ff` predates those fixes, so the v2 fixes are uncommitted.

## 4. What changed in recent sessions

### 2026-06-09 (latest) — customer-mobile "v2" redesign + live-test bug-fix sweep

Full visual + flow redesign of **`customer-mobile/`** to the v2 mockups (`customerappscreen/`):
warm **cream/olive/gold** palette (copied from rider-mobile's `tailwind.config.js`), a **custom
bottom tab bar with a centre gold "+" FAB** (Home · Orders · [+] · Wallet · Profile), and a full
**pickup booking flow**. Builds clean (`tsc` 0), `expo export` bundles all screens, runs on both
platforms against the live backend. **The data layer was already real and is unchanged** (customer
OTP login, catalog, orders/tracking, wallet/loyalty/coupons, engagement CMS).

**Two commits exist** for customer-mobile: `237b8ff` (the v2 build — committed mid-session, BEFORE
testing) and then the **6 bug-fixes below are UNCOMMITTED** (working tree).

**New/changed screens** (`app/`): `(auth)/onboarding|phone|otp` redesigned; `(app)/(tabs)/{home,
my-orders,wallet,profile}` + custom `(tabs)/_layout`; **booking flow** `(app)/booking/{items,
pickup,pay,confirm}`; `(app)/{price-list,offers}` + `orders/[id]` + `orders/tracking/[id]`
(real lifecycle timeline for UUID orders, demo timeline for the `LG-#####` ids the local booking
produces). New UI primitives in `src/components/` (`BrandSplash`, `ui/{Button,Card,Badge,Chip,
Stepper,OtpInput,Keypad,Avatar}`), stores `src/store/{cartStore,bookingStore}.ts`, helpers
`src/lib/{format,serviceMeta}.ts`, `src/data/demoItems.ts`. Added `expo-linear-gradient`; bumped to
v2.0.0; same `DEV_HOST`-from-`hostUri` + no-hardcoded-`localhost`-in-`extra` config fixes as
rider-mobile.

**⚠️ Booking is local-state only.** There is **no single customer "create order with items"
endpoint** (orders are admin/POS-created after pickup+weighing). So the booking flow runs on
local `cartStore`/`bookingStore`, gated by `FEATURES.bookingApi` (=`false`); Pay finalises
locally (generates an `LG-#####` number, no server write), so a placed booking does **not** appear
in the real Orders list. Razorpay (UPI/card) isn't wired (no native SDK) — those settle as
pay-on-delivery. Wire a real create-order/pickup endpoint + flip the flag to make it persist.

**Live E2E test (Android emulator primary, iOS parity) — 6 bugs found & fixed** (all delegated to
the `expo-mobile-developer` agent; `tsc` clean):

1. **Onboarding hero + home promo banner blank** — CMS image URLs are unreachable
   `https://cdn.laundryghar.com/...` and `<Image>` had no error fallback. Fixed: `onError` →
   olive-blob (onboarding) / text-card (home banner). Verified iOS+Android.
2. **Centre FAB clipped on Android** — fixed with `overflow:visible` on the tab-bar wrapper +
   `paddingTop:28` + FAB `top:-56`. Verified Android.
3. **Pay screen crash** "Maximum update depth exceeded" — `useCartStore((s)=>s.list())` returned a
   NEW array each render → zustand v5 / `useSyncExternalStore` infinite loop. Fixed (drop the
   unstable selector; **rule: never return fresh arrays/objects from a zustand selector** — select
   raw `s.lines` and derive with `useMemo`). Verified Android (full pay→confirm→track works).
4. **OTP failure showed raw "Request failed with status code 401"** — `verifyOtp` (`src/api/auth.ts`)
   now catches axios errors → friendly message.
5. **Auth-refresh DEADLOCK (critical)** — the SAME bug rider-mobile already fixed, present because
   `customer-mobile/src/api/client.ts` lacked the `isAuthCall` guard: the 401 interceptor refreshes
   via `/customer/auth/refresh` **through the same axios instance** whose interceptor awaits the
   module-shared `refreshPromise`; when refresh itself 401s it re-enters and self-awaits → hangs
   forever, **freezing every screen** (no bounce to login). Fixed by adding
   `const isAuthCall = (originalRequest.url ?? '').includes('/auth/')` to the 401 guard. **Verified
   on iOS:** an expired session now cleanly lands on onboarding instead of an infinite spinner.
6. **Price-list rows showed raw UUID** "Item 90ead3a5" — label fallback is now
   `displayLabel ?? notes ?? 'Garment'`; booking items no longer drops live rows missing a label.

**Data gaps flagged for the backend/seed (NOT app bugs):** `customer_catalog.price_list_items`
have null `display_label` (so price list shows generic names + booking falls back to demo items);
Engagement `onboarding-slides`/`banners` carry placeholder `cdn.laundryghar.com` image URLs that
don't resolve on devices. Fix the seed for richer demos.

**Dev OTP for mobile testing:** read the live code from the Aspire dcp `_out` file
(`/var/folders/.../T/aspire-dcp*/<guid>_out`, grep `[DEV-OTP]`) — the identity stdout is an
in-memory pipe (no disk log), the OTP hash is unsalted SHA256 but **DB hash-reversal is blocked by
the auto-classifier**. Codes expire in 5 min. Test customer `+919800000050`. (Tooling: `adb input
tap` reliable on Android; idb tap intermittent on iOS 26.3 bridgeless; the emulator OOM-crashes
with 2 RN Metros + iOS sim + backend — give it `-memory 3072`; a second project "SnapAccount" runs
on :8088 and Expo Go auto-reconnects to it after a reboot; customer Metro runs `--offline` on :8083.)

### 2026-06-09 — Rider Ops initiative (Phases 1-4) + Maps settings

A four-phase "rider operations" build answering: who's en route / on site, how many pickups &
deliveries, the laundry round-trip (collect → drop at store), COD cash collected, and rider
earnings. All committed + pushed to `main`; full stack rebuilt & validated green.

1. **Phase 1 — Admin live ops board** (`590a716`, committed earlier with Maps). Read-only Logistics
   endpoints (`GET /admin/riders/live` · `/{id}/track` · `/{id}/stats`, all `permission:rider.read`)
   derive ops-status (offline/idle/on_the_way/arrived) from `delivery_assignments`, location from
   `riders`, trail from `rider_location_pings`. admin-web: a **Roster | Live map | Cash** tab set on
   the Riders page (`?view=`); live map is **react-leaflet + OSM** behind a provider abstraction.
   **Maps setting**: Identity `maps` setting + `PUT /settings/maps`; admin-web Settings → Maps panel
   (OSM/Google/Mapbox + keys). Google/Mapbox tile _renderers_ are a follow-up (render on OSM for now).
2. **Phase 2 — drop-at-laundry + geofence** (`e963e69`). `delivery_assignments.collected_at`/
   `dropped_at`; `GeofenceEvaluator` runs on each GPS ping (started→arrived within 150 m of the
   customer; stamps the store drop for a collected pickup). rider-mobile streams pings every ~25s
   on duty (`useLocationTracking`) and the pickup is a two-step round-trip (collect → drop).
3. **Phase 3 — COD cash + settlement** (`dab0198`). COD captured when a delivery completes with a
   balance due (inferred — no COD flag on orders); `logistics.rider_settlements` + `rider.settle`
   permission; admin **Cash** tab reconciles outstanding cash and records settlements (one-step,
   admin-recorded; finance cash-book posting is a follow-up).
4. **Phase 4 — configurable payout engine** (`84abfe0`). `delivery_assignments.payout_amount`;
   rates are configurable in **Settings → Operations → Rider payouts** (shared
   `SharedDataModel/Common/RiderPayoutSettings` formula+defaults; Identity owns the setting,
   Logistics reads the same `kernel.system_settings` row). Payout computed at completion; rider
   `Earnings` = Σ payout_amount, shown on the live board.

**DB patches** (idempotent): `rider_verify_permission.sql`, `rider_drop_at_store.sql`,
`rider_cod_settlement.sql`, `rider_payout.sql`, `seed_rider_ops_demo.sql` (dev demo). Apply to any
environment with `db/patches/apply_rider_ops_patches.sh` (DB\_\* env vars; `--with-demo` opt-in;
needs a privileged role). There is **no other environment reachable from the repo** — staging/prod
are deploy-time env-var secrets (`ConnectionStrings__Default`), so the script is run by the deploy.

**Caveat:** anything firing through a live **rider session** (geofence ping→arrived, two-step
pickup, COD-at-delivery, payout-at-completion) is built + logic/DB-verified but not live-fired —
rider login is OTP-gated and a session wasn't established; exercise via the rider app on duty.

### 2026-06-09 — change a user's role (fix a wrongly-assigned role)

A user's role **is** their _primary_ `user_scope_membership` (the green badge on the People
list). Until now it could only be set at **Invite** time — there was no way to correct a wrong
role in the UI (the backend's guarded grant/revoke existed but nothing called it).

**Backend (Identity, `RoleCommands.cs` + `AdminUserEndpoints.cs`):** new
`POST /api/v1/admin/users/{id}/change-role` (`{roleId, scopeType, scopeId}`, permission
`memberships.grant`) → `ChangePrimaryRoleCommand`. **Replace semantics:** it grants the new
role as primary (which demotes the old primary's flag) and then **revokes the previous primary
membership(s)** (`revoked_reason = "Replaced via change-role"`). It delegates to
`GrantMembershipCommand`, so it reuses **every** guard (can't grant above your own rank,
platform-admin-only roles, brand-scope must cover the target) — and those throw **before**
anything is revoked, so a denied attempt changes nothing.

**Frontend (admin-web, `PersonDetailDrawer`):** new **Role & access** section + **Change role**
picker (role `<select>` grouped by tier; a **Franchise** select appears only for
franchise-scoped roles; Replace role / Cancel). Gated on `memberships.grant`. New
`useChangeUserRole` hook + `changeUserRole` API + `ChangeRolePayload`/`MembershipDto` types.
Also fixed the misleading drawer note (it claimed the ⋯ menu handled roles — it never did;
the ⋯ menu is status only: activate / suspend / reactivate).

**Verified live on the shared DB** (user explicitly authorized the real mutation): changed a
seeded _Support Lead_ → _Finance Manager_ (new `finance_manager` primary+active, old
`support_lead` revoked with reason; People API immediately showed "Finance Manager"), then
reverted → "Support Lead", then **restored the seeded user to its exact original single row**
(no test residue). Also: no-auth → 401; bogus roleId → 422 "Role not found" with **zero
mutation** (guard runs before any write). `tsc` 0, backend builds clean, picker verified in
Playwright (0 page errors). **Not yet committed.**

### 2026-06-08 — rider per-order tasks API (live data for rider-mobile v2)

Closed the "rider tasks API" backlog item: the rider task list is no longer demo data.

**Backend (Logistics, new `RiderSelf/RiderTaskQueries.cs` + `LogisticsEndpoints.cs`):**

- `GET /api/v1/rider/tasks/today` → the rider's `order_lifecycle.delivery_assignments`
  joined to orders/customers/addresses, mapped to a `RiderTaskDto`, ordered by
  `sequence_number` then window (completed sink to the bottom; open work + today's completed).
- `PATCH /api/v1/rider/tasks/{id}/status` → `started|arrived|completed|failed` (stamps the
  matching timestamp).
- `POST /api/v1/rider/tasks/{id}/verify-otp` → **server-side** OTP check against the order's
  `delivery_otp`/`pickup_otp`.
- **Security:** the OTP value is **never returned to the device** — the DTO carries only
  `requiresOtp`/`otpVerified`. A delivery/return leg with an OTP is **blocked from `completed`
  until `otp_verified=true`** (returns 400 "Delivery OTP must be verified before completing").
  Wrong code → 400. All self-filtered by rider id + brand (cross-rider → 404).
- `payout` is a transparent server-side **estimate** (₹40 + ₹7/km, +₹20 express, nearest ₹5) —
  there is no real per-leg payout model yet.

**App (rider-mobile):** `FEATURES.riderTasksApi=true`; `src/api/tasks.ts` maps the DTO and adds
`verifyTaskOtp`/`updateTaskStatus`; `tasks/[id]` confirm flow now verifies server-side
(`verifyTaskOtp` → `updateTaskStatus('completed')`) instead of client-comparing; list polls
every 30s + pull-to-refresh for auto-populate; `taskOverrideStore` is now just an optimistic
overlay (stopped fabricating a customer rating). `RiderTask` gained `requiresOtp`/`otpVerified`/
`sequenceNumber`; `TaskLegType` gained `return`.

**Follow-up fixes (same day, after first test pass):**

- **OTP now required for BOTH legs** (pickup _and_ delivery) — the customer reads the code at
  each handoff. `requiresOtp` is true for any leg whose order has the matching
  `pickup_otp`/`delivery_otp`; the complete-gate + app OTP field/labels updated accordingly.
- **Fixed long-order-number ↔ badge overlap** in the task cards — order number now `flex-1`
  with `ellipsizeMode="middle"` (`#LG-20…002314`), badges are `shrink-0`; OTP + COD/Express
  render side-by-side.
- **Fixed the auth refresh deadlock** (root cause of stuck spinners/loaders): `refreshPromise`
  is shared module-wide and `refreshAccessToken` hits `/auth/refresh` through the same axios
  instance whose interceptor awaits `refreshPromise` — so a 401 on refresh made the refresh
  await _itself_ → infinite hang (expired-token requests never settled; "Confirm" spun forever;
  no bounce to login). `client.ts` now **skips the 401-refresh dance for any `/auth/*` URL**, so
  refresh failures reject cleanly → `onAuthFailure` → `logout` → guard redirects to login.
  Verified live: a stale token now lands on the login screen. (`logout` also clears local state
  before the best-effort network revoke.)
- **Android dev base URL** (two bugs) — `config.ts` now derives the dev API host from how the
  device reached Metro (`Constants.expoConfig.hostUri` → `localhost` on iOS sim, `10.0.2.2` on the
  Android emulator, LAN IP on a physical device). Traps fixed: (a) do **not** import
  `react-native`'s `Platform` in that early module — it crashes Android New-Arch with a
  "[runtime not ready] PlatformConstants" redbox; (b) `app.config.ts` must **not** hardcode
  `extra.identityApiUrl='http://localhost:5050'` (it always won over the derived host, so Android
  hit `localhost` → Network Error) — those are now env-only. Backend binds loopback-only, so the
  emulator must use `10.0.2.2` (LAN IP wouldn't reach it).
- **Verified on Android emulator** (`snap_pixel`, Expo Go SDK52): full OTP login via
  `http://10.0.2.2:5050` (send + verify 200) → home + task list with real data, order-number
  ellipsis + OTP/COD badges render cleanly, Go-on-duty location prompt. iOS verified the
  pickup + delivery server-OTP confirm end-to-end (no spinner hang).

**Verified — full live E2E in the iOS sim** (idb-driven, real backend): fresh OTP login →
home shows real rider + "4 tasks · Sector 45 · ₹58 avg" → tasks list (sequenced, OTP/COD tags)
→ pickup confirm (no OTP, `PATCH 200`) → "Picked up!" (earnings recompute, count decrements) →
delivery: enter OTP `4283` → `POST verify-otp 200` then `PATCH completed 200` → "Delivered!"
(₹410 total, next task shown, 2 left). Backend also curl-validated incl. the security gate.
`tsc` clean; Logistics built clean; AppHost restarted (healthy). **Not yet committed.**

> ⚠️ Seeding test tasks needs explicit DB authorization (the auto-classifier blocks writes to
> the shared dev DB) — script at `/tmp/seed_rider_tasks.sql` seeds 9 assignments for rider
> `+919800000001` (5 open + 4 completed) and the orders' OTP/addresses. Re-runnable.
> Rider-mobile runs **two Metros**; point Expo Go at the `--offline` one (`exp://127.0.0.1:8081`).

### 2026-06-08 — people employment + KYC + bank details

People (HQ employees, franchise owners, franchise staff) are employees too, so they now
carry the same **employment / KYC / payout** shape Riders already had. All fields are
**optional, never required, and available for everyone** (per product decision — a
contractual hire and a full-timer see identical fields; nothing is enforced yet).

**Where it lives:** all new fields sit on the existing 1-to-1 `identity_access.user_profiles`
row (NOT the `users` table) — mirroring how Rider carries them inline:
`employment_type` (`full_time|part_time|contractual|consultant|intern`, CHECK-constrained),
`pan_number`, `aadhaar_number_masked`, `kyc_status` (`pending|verified|rejected`),
`kyc_verified_at`, `bank_account_name`, `bank_account_number`, `bank_ifsc`, `upi_id`.

**Schema:** added to the canonical `database_scripts/02_bc2_identity_access.sql` (fresh
installs) **and** shipped as an idempotent patch `02_patch_user_employment_kyc.sql`
(ALTER … ADD COLUMN IF NOT EXISTS + guarded CHECKs) — **already applied to the live DB**.
There are no EF migrations in this repo; schema is hand-written SQL.

**Backend (Identity):** `UserProfile` entity + `UserProfileConfiguration` extended; `UserDto`

- `UpdateUserRequest` carry the new fields; `UpdateUserHandler` now (1) **creates a profile
  row if the person has none** (verified 0→1 on first save), (2) treats **empty string as
  clear, null/omitted as leave-unchanged**, (3) upper-cases PAN/IFSC, and (4) auto-stamps
  `kyc_verified_at` on the pending→verified transition (clears it on the reverse). The list
  (`GetUsers`) and create projections keep working via defaulted `UserDto` params.

**Frontend (admin-web):** `PersonDetailDrawer` gained **Employment**, **KYC & documents**,
and **Bank & payout** sections in both view and edit modes, with one Edit toggle (in the
header) and a sticky Save/Cancel footer. `AdminUserDetail` / `UpdateUserPayload` types +
`UserEmploymentType` / `UserKycStatus` added in `types/api.ts`.

**Verified live:** GET returns the fields; PUT persists (PAN/IFSC upper-cased, verified-at
stamped); empty-string clears; partial update leaves siblings intact; verified→pending
clears the timestamp; profile auto-created for a profile-less user (throwaway user, then
hard-deleted). Drawer renders all sections, edit shows selects + IFSC/UPI + Save, 0 page
errors (Playwright). Backend built clean, `tsc` 0, AppHost restarted (Identity healthy).
**Follow-up if wanted:** gate _required_ PAN+bank on contractual/consultant types before
activation (currently all optional).

### 2026-06-08 — rider-mobile "Partner v2"

Focus: a full visual + flow redesign of **`rider-mobile/`** to the v2 mockups (warm
cream / olive / gold palette, stack-based navigation, **no bottom tabs**). Builds clean
(`tsc` 0 errors), **bundles for both iOS and Android** (`expo export`), and was
**live-tested end-to-end in the iOS simulator** (idb-driven) against the running backend.
Not yet committed.

**Live E2E verified (iOS sim, real backend):** OTP login with a seeded rider
(`+919800000001` → real `/auth/otp/send` + `/auth/otp/verify`; dev code read from Aspire
dcp `*_out` logs, `[DEV-OTP] … code=`) → home offline→**Go on duty** (olive "You're online"

- live location-permission prompt) → tasks list (real stats: Today 6/12, Earned ₹525) →
  delivery detail → enter delivery OTP `4283` → **Confirm delivered** → success screen
  (₹140 this task, ₹525 total, next task) → back to tasks (list correctly re-segments to
  Done) → pickup detail (no OTP) → Profile (all real `/rider/me` fields). Two **emoji-tofu
  bugs found & fixed**: the 🇮🇳 flag in login and the 📋 in the home tasks pill rendered as
  `?` boxes on the simulator (and unreliably on Android) — replaced with a **drawn tricolor
  flag** (`IndiaFlag` in `login.tsx`) and an **Ionicons `clipboard-outline`** respectively.
  (Benign pre-existing warning still present: `client.ts ↔ auth.ts` require cycle.)

**New design system** (`tailwind.config.js`): `cream` / `olive` / `gold` / `ink` token
scales. New/updated UI primitives in `src/components/`: `BrandSplash` (gradient splash,
mockup #1), `ui/Button` (gold `primary`, olive `confirm`, `secondary` outline + icon props),
`ui/OtpInput` (segmented cells, `editable=false` display-only mode), `ui/Keypad` (custom
numeric pad), `ui/Avatar` (initials). `expo-linear-gradient` added (Expo-Go-safe).

**Screens (route tree, all under `app/`):**

- `(auth)/login.tsx` — phone entry → **`POST /auth/otp/send`** (`identifierType:"phone"`,
  `purpose:"login"`). **OTP login is real** — the generic system-user OTP flow issues rider
  tokens (verified in Identity `OtpVerifyHandler`).
- `(auth)/otp.tsx` — **6-digit** code (backend `OtpSendHandler.CodeLength=6`; mockup shows 4)
  via custom keypad → **`POST /auth/otp/verify`** → tokens → `/(app)/home`. 30 s resend timer.
- `(app)/home.tsx` — **Go on duty** circle (offline/online states), "Before you ride"
  checklist, "tasks waiting" pill, gated "View today's tasks" CTA. Going on duty is
  client-side (`src/store/dutyStore.ts`, AsyncStorage) **plus** best-effort real effects:
  sends a `/rider/location/ping` and activates today's `scheduled` shift assignment
  (`PATCH /rider/assignments/{id}/status → active`).
- `(app)/tasks.tsx` — olive header (zone + live stats), Tasks/Done tabs, job cards
  (first pending expanded with Call/Start).
- `(app)/tasks/[id].tsx` — stylised map placeholder, customer card (call/SMS), **delivery
  OTP** entry (validates the 4-digit code), garments/payment row, Confirm.
- `(app)/delivered.tsx` — success summary (rating, this-task + running earnings, next task).
- `(app)/profile.tsx` — restyled identity/stats/details + logout.

**⚠️ Backend gap (the one thing not wired to real APIs):** there is **no rider-facing
per-order task endpoint**. The backend models jobs as `logistics.delivery_assignments`
joined to an order (with `pickup_otp`/`delivery_otp` on the order), but exposes no
`/api/v1/rider/tasks*` route group, no rider duty toggle, and no delivery-OTP verify
endpoint. So the task-list / delivery-OTP / delivered flow runs on a **clearly-labelled
demo set** (`src/data/demoTasks.ts`, served by `src/api/tasks.ts`) with a UI banner saying
so. It's a single seam: implement `GET /api/v1/rider/tasks/today` returning the `RiderTask`
shape (see `src/types/api.ts`) **and** a delivery-complete + OTP-verify endpoint, then flip
`FEATURES.riderTasksApi` in `src/constants/config.ts` to `true`. Session-local completions
live in `src/store/taskOverrideStore.ts` (delete once the mutation is real). The shift-based
`/rider/assignments/today` data still feeds the home duty side-effects and is real.

**Removed:** the old `(app)/(tabs)/` group (assignments/location/profile tabs),
`(app)/assignments/[id]`, and `(auth)/onboarding.tsx`. `src/api/engagement.ts` +
`useEngagement` remain but are now unused (kept for future banners).

### Earlier on 2026-06-08 — admin rider onboarding

Focus: an admin **rider onboarding** vertical slice on the `/riders` screen, plus a follow-up
that brought the edit form to full field-parity with onboarding. A rider = a `User`
(`user_type='rider'`) **always tied to a franchise**; the operational profile is a separate
`logistics.riders` row. Onboarding is **frontend-orchestrated** across two services (no new
service-to-service plumbing). Commits on `main` (newest first):

1. **Rider edit field-parity** (`63fe186`) — `PUT /riders/{id}` + admin-web edit drawer now also
   edit EmploymentType, VehicleType, Aadhaar(masked), PAN, Bank a/c/IFSC/holder, and UPI (previously
   only status/store/vehicle no+model/DL/insurance/capacity). The handler applies **only non-null
   fields**, so a partial form never clobbers what it didn't send. **Sensitive PII (Aadhaar/PAN/bank/UPI)
   is never returned in `RiderDto`** — those edit inputs start blank with "Leave blank to keep"
   placeholders, so blank = preserve, typing = overwrite. KYC _status_ is still NOT editable here
   (verify/reject only). `UpdateRiderValidator` whitelists the employment/vehicle enums. Verified live:
   new fields persist, omitting PAN keeps it, invalid enum → HTTP 422.
2. **Admin rider onboarding + KYC + franchise self-service** (`c115405`) — Identity gained a narrow
   `POST /access-control/riders/invite` (gated `permission:rider.manage`, not broad `users.create`) that
   forces the actor's franchise for franchise-scoped callers; admin-web orchestrates invite→profile.
   New `permission:rider.verify` + Logistics `POST /riders/{id}/verify` (KYC→verified **and** flips the
   linked login invited→active) and `/reject` (reason → rider Metadata). **Scoped single-approval**:
   franchise users act only on own-franchise riders, super-admins on any — enforced server-side in
   every rider handler via `_user.FranchiseId`. `/riders` screen: search (name/email/phone/code),
   franchise+KYC+status filters, sortable columns, View/Edit drawers, Approve/Reject actions. Security
   review closed a cross-brand IDOR (CreateRider now verifies userId/franchise/store all belong to the
   brand) and a KYC-bypass (KYC status removed from the editable PUT contract). DB patch:
   `db/patches/rider_verify_permission.sql`. Also extracted a shared `ActionMenu` (portal + flip-up)
   that fixed the row-action menu being clipped on the last/second-last rows (Riders **and** People).

### Earlier this session (2026-06-07 → 08) — list pagination sweep

1. **Pagination + infinite scroll across admin lists** (`31b3059`) — every admin-web list now
   loads 100 rows then fetches the next page on scroll, via a reusable
   `useInfiniteScroll` hook (IntersectionObserver sentinel, prefetches ~400px early) +
   `useInfiniteQuery`. Converted: Access-Control **Franchises** & **People**, **Orders**,
   **Catalog** (categories/services/price-lists), **Tenancy** (stores/franchises), **CMS**
   (templates/slides/banners/app-config/outbox/notification-logs/whatsapp-logs). Backend:
   `access-control/franchises` now paginated + **newest-first** (`UpdatedAt desc, CreatedAt desc`);
   `access-control/people` returns `{ counts, people: PaginatedList<PersonDto> }` so chip counts
   stay full-set accurate while the table pages. `PaginatedList<T>` util gained `Map()` (project a
   page while keeping metadata), in-memory `Create()`, and public `TotalCount`/`PageNumber`.
   Frontend conversions were **additive** — new `*Infinite` hooks alongside the flat ones, so
   Sidebar/Topbar/Dashboard (which read `useStores`/`useOrders` for counts/charts) are untouched.
   Verified live: Orders 100→300 rows on scroll; People 27 total with correct chips.
2. **Onboarding "Update" label** (`304839b`) — DetailsForm Save button reads **Update** once the
   step has saved data, **Save** otherwise.
3. **SaveButton success state + KYC completion** (`9d15727`) — Save buttons flash **Saved ✓**; the
   "Business & KYC" step is now done on **GSTIN + address** (PAN is optional, no longer gates it →
   progress advances on save).
4. **Toggle accessibility/layout fix** (`d1a1692`) — rewrote the Email/SMTP toggle as a flex switch
   (no more absolute-positioned knob overlapping its label) + `role="switch"`/`aria-checked`.

### What was deliberately NOT paginated (don't "fix" these)

- **Analytics tables** (daily/monthly revenue, warehouse throughput) — they also feed the Dashboard
  revenue **chart**, which needs the full date range; they're already date-bounded.
- **Roles tab** — a permission **matrix**, not a scrollable list.
- **`admin/roles`, `admin/roles/permissions`, `admin/orders/{id}/notes`** — no consumers in any client.
- **Customer/rider list endpoints** (catalog, packages, coupons, addresses, slots, tracking) — consumed
  by `customer-mobile`/`rider-mobile`; changing their response shape to `PaginatedList` is a breaking
  change that needs coordinated mobile updates. See backlog.

### Prior session (2026-06-06)

5 commits on `main` (newest first):

1. **RS256 + JWKS** (`27f2f4f`) — replaced the shared HS256 secret. Identity signs with an RSA
   private key and publishes the public key at `/.well-known/jwks.json`; the 8 other services
   verify via `Authority`/JWKS (no shared secret; auto key-rotation). Dev key auto-generated to
   `bin/.../keys/dev-jwt-signing.pem` (gitignored); prod fails closed without `Jwt__PrivateKey`.
2. **Analytics dashboard fixes** (`e2fefbc`) — found via cross-service E2E: a `Task.WhenAll`
   on one DbContext (concurrency crash) and `mv_customer_ltv` NULL aggregates.
3. **Anonymous-endpoint RLS regression fix + feature screens** (`b1e41a0`) — the app_user flip
   had broken customer OTP login + all public CMS endpoints (brand-by-code lookup returned 0
   under RLS); fixed with scoped bypass middleware. Plus admin-web CMS screens + rider-mobile banners.
4. **Security hardening** (`21b0e52`) — runtime flipped to `app_user` so RLS is enforced;
   fixed a dead `rls_bypass()`; `order_addons` brand-scoped; atomic `order_number`; secrets→env.
5. **Initial commit** (`27686a2`).

## 5. ⚠️ Critical gotchas (read before running or changing anything)

- **Runtime runs as `app_user` → RLS is ENFORCED.** Services using `postgres` would silently
  bypass RLS. Each request sets `app.current_brand_id` via `RlsConnectionInterceptor` (from the
  JWT `brand_id` claim). A query with no brand context returns **0 rows** — that is correct, not a bug.
- **`kernel.rls_bypass()` reads `app.bypass_rls`** and accepts `on/true/1/yes/t`. Platform-admin
  cross-brand reads and the Worker rely on it. Anonymous/pre-auth endpoints (Engagement
  `/api/v1/public/*`, Identity `/api/v1/customer/auth/*`) set `Items["bypass_rls"]=true` so their
  brand-by-code lookups work — **any new anonymous brand-scoped endpoint must do the same** or it
  returns 0 rows.
- **Seeding runs on the `Admin` (postgres) connection** (`SeedingSupport.CreatePrivilegedContext`),
  Development only. Don't provision `ConnectionStrings__Admin` in production.
- **Auth is RS256/JWKS.** You can no longer mint test tokens with a shared key. Get a token via a
  real login. `platform_admin` tokens carry **no `brand_id`**, so admin endpoints need an
  **`X-Brand-Id: <brandId>`** header; `platform_admin` also bypasses permission checks and gets RLS bypass.
- **Permissions claim is space-separated**; lanes are `permission:<code>`, `CustomerOnly`, `RiderOnly`.
- **JWT contract (pinned):** `token_use` = `user|customer`; `sub` = user/customer id; `iss` =
  `laundryghar-identity`; `aud` = `laundryghar-services`. RS256, `kid` in header.
- **Keep the AppHost detached** — the harness SIGTERMs tracked background tasks after a few minutes;
  use the `nohup … & disown` recipe above.
- **Order creation is admin/POS only** (`POST :5002/api/v1/admin/orders`, `permission:orders.create`,
  needs brand context). Customers create via pickup requests, not direct orders.
- **Brand "Brand Two" is soft-deleted** (EF `DeletedAt` filter hides it) — APIs showing 1 brand is correct.
- **Backend code changes need a FULL AppHost restart.** Aspire's dcp does **not** auto-restart a
  manually-killed resource (the public port is a dcp reverse-proxy, so the port stays "held" while
  returning errors). To pick up a `.cs` change: stop the AppHost launcher + orphaned dcp procs, then
  relaunch with the detached command in §2. Vite (admin-web) hot-reloads on its own.
- **List pagination pattern.** Backend list endpoints return `PaginatedList<T>` →
  `{ list, hasPreviousPage, hasNextPage, totalCount, pageNumber }` wrapped in `PaginatedListResponse<T>`;
  build it with `PaginatedList<T>.CreateAsync(query, page, pageSize, ct)` (or `.Create(list, …)` for
  in-memory sets, `.Map(selector)` to shape DTOs after paging). Admin-web consumes these with
  `useInfiniteQuery` (`getNextPageParam` from `hasNextPage`) + the `useInfiniteScroll` hook
  (`src/hooks/useInfiniteScroll.ts`); see `useAccessFranchises`/`FranchisesTab` as the reference.

## 6. Remaining backlog (prod-hardening)

- ~~**Secrets → a real manager**~~ ✅ DONE (2026-06-06, cloud-agnostic abstraction): added
  `ISecretsProvider` in `laundryghar.ServiceDefaults/Secrets/` (`EnvironmentSecretsProvider`
  = no-op default, `FileSecretsProvider` = Docker/k8s secret-mount dir with `__`→`:` keys +
  64 KiB size guard), selected by `Secrets:Provider` (`env`|`file`), layered into config inside
  `AddServiceDefaults` (all 10 projects, zero per-service edits). Documented seams (throw, no SDK
  added) for `azure-keyvault`/`aws-secretsmanager`/`vault` in `SecretsProviderFactory` + PRODUCTION_ENV.md.
  Dev is byte-for-byte unchanged (env no-op). 7 xUnit tests in `laundryghar.ServiceDefaults.Tests`.
  **To go live on a cloud:** implement the seam, set `Secrets__Provider` + provider config in prod env.
- ~~**Remove AutoMapper 13.0.1** (CVE NU1903)~~ ✅ DONE (2026-06-06): removed from all 8 csprojs,
  6 `AddAutoMapper` calls, 2 global usings, and the dead `ProjectToListAsync` helper. Build 0 errors.
- ~~**Analytics in admin-web**~~ ✅ DONE: `/analytics` section (dashboard + daily/monthly revenue,
  warehouse throughput, customer LTV, rider performance) + "Refresh MVs" button. (Fixed a latent
  Analytics 500: `mv_warehouse_throughput.avg_tat_hours` is NULL when nothing has completed a
  turnaround — entity `AvgTatHours` made `decimal?`, admin-web table guards null.)
- ~~**CMS banner ↔ promotion/coupon linking**~~ ✅ DONE: backend was already modelled (FKs, entity,
  commands); added admin-web promotion/coupon pickers in the banner form + a customer-mobile **Offers**
  screen (`app/(app)/offers.tsx`, lists `GET /customer/coupons`) that banner taps deep-link into.
- ~~**Deeper mobile E2E**~~ ✅ DONE: live-API E2E in the simulator fixed **6 latent customer-mobile
  runtime bugs** (tsc-clean but broken) — DTO field drift on `CouponDto`/`CustomerMeResponse`/
  `PriceListItemDto`/service+category, a broken price-list category filter, and the banner
  custom-scheme deep-link being dropped. Both apps bundle for iOS.

### Still open

- ~~**Rider per-order task API**~~ ✅ DONE (2026-06-08, see §4): `GET /rider/tasks/today`,
  `PATCH /rider/tasks/{id}/status`, `POST /rider/tasks/{id}/verify-otp` shipped + flag flipped,
  live-verified. **Still open follow-ups:** (a) **auto-dispatcher** — assignments are still
  created manually by admin/store staff; no order→rider matching or capacity enforcement
  (`Rider.CurrentLoad`/`DailyDeliveryCapacity` exist but are unused). (b) a **rider duty toggle**
  endpoint (going on/off duty is still client-side in `dutyStore`; the app can't set the
  backend `riders.is_on_duty`). (c) ~~real per-leg payout~~ ✅ DONE — Phase 4 configurable payout
  engine (§4). (d) proof-of-delivery photo/signature upload (`DeliveryAssignment` has the S3
  columns, no endpoint).
- **Rider Ops (Phases 1-4) follow-ups** (see §4): (i) **rider-session live E2E** of geofence
  auto-arrival / two-step pickup / COD-at-delivery / payout-at-completion — built + verified by
  logic/DB/preview but not fired through a real OTP-gated rider login; (ii) **background GPS** in
  rider-mobile (today foreground-only while on duty — geofence needs continuous pings); (iii)
  **finance cash-book posting** on COD settlement (logistics-only today); (iv) **Google/Mapbox tile
  renderers** (the setting selects them; the board still renders on OSM until wired).
- **rider-mobile live map** — `tasks/[id]` uses a stylised map placeholder; a real map needs
  `react-native-maps` (dev build + Maps key, not Expo Go). (Admin live map IS real — Leaflet/OSM.)
- **admin-web stores the refresh token in `localStorage`** (`src/stores/authStore.ts`) — XSS→token-theft
  risk (security review Medium). Proper fix = move the refresh token to an `HttpOnly; Secure; SameSite`
  cookie set by Identity's `/auth/refresh`, and stop persisting it client-side. Not yet done (touches
  Identity auth contract). Mobile already uses `expo-secure-store` correctly.
- ~~**No seeded riders**~~ ✅ ADDRESSED (2026-06-08): the admin `/riders` screen now onboards riders
  end-to-end (Identity invite → `logistics.riders` profile) and there are named demo riders in the DB,
  so rider-mobile login is now E2E-testable. KYC approve flips the linked login invited→active.
- **customer-mobile booking doesn't persist** (see §4) — the v2 pickup flow (items→pickup→pay→confirm)
  runs on local cart/booking state behind `FEATURES.bookingApi=false`; there's no customer
  create-order/pickup-with-items endpoint, so a "placed" booking never reaches the real Orders list
  and the track screen shows a demo timeline. To make it real: add a customer endpoint that creates a
  pickup request (or order) from the cart, wire `src/store/{cartStore,bookingStore}` → API, flip the
  flag, and replace the local `LG-#####` id with the server order. Razorpay (UPI/card) also needs a
  native SDK (dev build, not Expo Go); wallet/COD can settle without it.
- **Customer/rider + pos-web list pagination** not yet done — the admin-web sweep (§4) stopped at the
  admin clients because the customer/rider list endpoints back the Expo apps and a shape change is
  breaking. To finish "paginate every list": convert those endpoints to `PaginatedList<T>` **and**
  update `customer-mobile`/`rider-mobile`/`pos-web` consumers in lockstep (verify each app bundles).
- Banner link targets are now allowlist-validated server-side (`AppBannerRules`: relative path /
  http(s) / `laundryghar://` only; create **and** update) and the mobile only hands the app's own
  `laundryghar://` scheme to `Linking.openURL`.

## 7. Git policy

Git is **ask-gated** — commit/push only when explicitly requested. Commit trailer:
`Co-Authored-By: Claude Opus 4.8 (1M context)`. The repo `.gitignore` excludes `bin/obj`,
`node_modules`, `.expo`, mobile `ios/android`, `.env` (keeps `.env.example`), and `*.pem`.

## 8. Useful references

- Orchestrator working notes (detailed, per-bounded-context): `.claude/agent-memory/laundryghar-orchestrator/status.md`
- Production env contract: `backend/laundryghar/PRODUCTION_ENV.md`
- DB patches applied: `db/patches/harden_app_user_and_rls_bypass.sql`,
  `order_addons_brand_id_rls.sql`, `order_number_sequence.sql`, `fix_mv_customer_ltv_nulls.sql`,
  `rider_verify_permission.sql`, **Rider Ops:** `rider_drop_at_store.sql`,
  `rider_cod_settlement.sql`, `rider_payout.sql`, `seed_rider_ops_demo.sql` (dev demo)
- **Apply Rider Ops patches to any env:** `db/patches/apply_rider_ops_patches.sh` (idempotent;
  `DB_HOST/PORT/USER/PASS` env vars; `--with-demo` opt-in; needs a privileged role)
- Rider onboarding: admin-web `pages/riders/`, `api/riders.ts`, `hooks/useRiders.ts`,
  `hooks/usePermissions.ts`; Logistics `Application/Riders/`; Identity `InviteRiderCommand`
- Rider Ops: Logistics `Application/{RiderOps,RiderCod,Payout}/` + `RiderSelf/GeofenceEvaluator.cs`;
  admin-web `pages/riders/{RiderOpsView,RiderCashView}.tsx`, `components/map/`,
  `pages/settings/{MapsPanel,PayoutPanel}.tsx`; rider-mobile `hooks/useLocationTracking.ts`;
  shared `SharedDataModel/Common/RiderPayoutSettings.cs`
- AppHost: `backend/laundryghar/laundryghar.AppHost/Program.cs`
- RS256 key provider + JWKS: `laundryghar.Identity/Infrastructure/Auth/RsaJwtKeyProvider.cs`,
  `laundryghar.Identity/Endpoints/WellKnownEndpoints.cs`
