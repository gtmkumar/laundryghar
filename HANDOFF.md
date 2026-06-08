# LaundryGhar OLMS — Handoff

_Last updated: 2026-06-08 (people employment + KYC + bank details)_

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

| Service | Port | Schema(s) |
|---|---|---|
| Identity | **5050** | tenancy_org, identity_access |
| Catalog | 5001 | customer_catalog |
| Orders | 5002 | order_lifecycle |
| Warehouse | 5003 | order_lifecycle (garments) |
| Logistics | 5004 | logistics |
| Commerce | 5005 | commerce |
| Finance | 5006 | finance_royalty |
| Engagement | 5007 | engagement_cms |
| Analytics | 5008 | analytics (materialized views) |
| Worker | – | drains outbox / notifications |

`SharedDataModel` (≈76 EF entities, all bounded contexts + kernel) and `Utilities`
(API envelope, exceptions) are shared libraries. `ServiceDefaults` wires Aspire (OTel,
health). **Identity is on 5050, not 5000** (macOS AirPlay squats 5000).

## 2. How to run

Prereqs: .NET 10 SDK, Node 22, the `laundry_ghar_db` Postgres running on `localhost:5432`,
`psql` at `/opt/homebrew/opt/postgresql@16/bin/psql`.

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
on `user_profiles`, editable from the Access-control person drawer (see §4).

## 4. What changed in recent sessions

### 2026-06-08 (latest) — people employment + KYC + bank details

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
+ `UpdateUserRequest` carry the new fields; `UpdateUserHandler` now (1) **creates a profile
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
**Follow-up if wanted:** gate *required* PAN+bank on contractual/consultant types before
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
+ live location-permission prompt) → tasks list (real stats: Today 6/12, Earned ₹525) →
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
   placeholders, so blank = preserve, typing = overwrite. KYC *status* is still NOT editable here
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
- **Rider per-order task API (unblocks rider-mobile v2 live data).** Add a rider-facing
  route group in Logistics: `GET /api/v1/rider/tasks/today` (delivery_assignments ⋈ order for
  the authed rider, returning the `RiderTask` shape in `rider-mobile/src/types/api.ts`),
  `POST /rider/tasks/{id}/start|arrive`, and a `POST /rider/tasks/{id}/complete` that verifies
  the customer's `delivery_otp`. Also a rider duty toggle (`POST /rider/duty`). Then flip
  `FEATURES.riderTasksApi=true` in rider-mobile. Until then the app shows labelled demo tasks.
- **rider-mobile live map** — `tasks/[id]` uses a stylised map placeholder; a real map needs
  `react-native-maps` (dev build + Maps key, not Expo Go).
- **admin-web stores the refresh token in `localStorage`** (`src/stores/authStore.ts`) — XSS→token-theft
  risk (security review Medium). Proper fix = move the refresh token to an `HttpOnly; Secure; SameSite`
  cookie set by Identity's `/auth/refresh`, and stop persisting it client-side. Not yet done (touches
  Identity auth contract). Mobile already uses `expo-secure-store` correctly.
- ~~**No seeded riders**~~ ✅ ADDRESSED (2026-06-08): the admin `/riders` screen now onboards riders
  end-to-end (Identity invite → `logistics.riders` profile) and there are named demo riders in the DB,
  so rider-mobile login is now E2E-testable. KYC approve flips the linked login invited→active.
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
  `rider_verify_permission.sql` (rider.verify perm + grants; rider.manage→franchise_owner)
- Rider onboarding: admin-web `pages/riders/`, `api/riders.ts`, `hooks/useRiders.ts`,
  `hooks/usePermissions.ts`; Logistics `Application/Riders/`; Identity `InviteRiderCommand`
- AppHost: `backend/laundryghar/laundryghar.AppHost/Program.cs`
- RS256 key provider + JWKS: `laundryghar.Identity/Infrastructure/Auth/RsaJwtKeyProvider.cs`,
  `laundryghar.Identity/Endpoints/WellKnownEndpoints.cs`
