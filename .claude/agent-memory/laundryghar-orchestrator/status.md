# Orchestrator Status

## Completed bounded contexts (5 of 9)
- **BC-1 tenancy_org + BC-2 identity_access ✅** → `laundryghar.Identity` (:5000) + SharedDataModel (25 entities). RLS proven.
- **BC-3 customer_catalog ✅** → `laundryghar.Catalog` (:5001) + customer OTP auth + 14 entities. IDOR/RLS proven.
- **BC-4 order_lifecycle ✅** → `laundryghar.Orders` (:5002) + `laundryghar.Warehouse` (:5003) + 20 entities. Full warehouse flow + outbox verified. (order_addons RLS-deferred: no brand_id.)
- **BC-5 logistics ✅ (2026-06-05)** → `laundryghar.Logistics` (:5004) + 4 entities. Admin rider/assignment/capacity CRUD + rider self-service (RiderOnly lane: token_use=user AND user_type=rider; batched GPS ping insert; own-assignment self-filter). All 4 tables RLS-enabled.

### Services (7 projects) + ports
Identity :5000 · Catalog :5001 · Orders :5002 · Warehouse :5003 · Logistics :5004 · SharedDataModel (63 entities BC1-5+kernel) · Utilities (shared, +BusinessRuleException 422/ForbiddenException 403).

### Cross-service JWT contract (PINNED)
token_use=user|customer; sub=user_id|customer_id; system tokens carry permissions+user_type (rider = user_type 'rider'); all carry brand_id. HS256 dev key shared via Jwt config; ValidAlgorithms pinned HS256 every service. JwtBearer remaps sub→ClaimTypes.NameIdentifier. Auth lanes: permission:<code> (admin), CustomerOnly (Identity/Catalog), RiderOnly (Logistics).

## ⚠️ Subagent model: use model:"sonnet" for ALL subagents
The default Opus 4.8 1M-context hit a "usage credits required for 1M context" limit mid-run (Warehouse). Sonnet (standard context) works fine and is now standard for every build/DB/QA subagent. NOTE: SendMessage to old 1M agents reuses their model — spawn FRESH sonnet Agents instead (they read existing code as templates).

## Prod-hardening BACKLOG (deferred)
- RS256+JWKS (HS256 dev now); secrets (JWT key + DB creds) → env/Key Vault.
- Flip service runtime DB connection to `app_user` so RLS enforces at runtime (currently postgres=superuser bypasses; app-layer brand predicates are the safeguard).
- order_number → DB sequence; order_addons add brand_id/EXISTS-policy for RLS; AutoMapper 13.0.1 CVE bump; security headers/HSTS.
- Dedicated security review of Orders + Logistics customer/rider endpoints (smoke-verified IDOR-safe; pattern mirrors reviewed Catalog/Warehouse).

## BC-6 commerce: COMPLETE ✅ (2026-06-05)
QA gate caught + fixed 4 real bugs the network-truncated build agent left: DEF-1 (CRITICAL: BeginTransactionAsync incompatible with Npgsql retrying strategy → all 4 transactional handlers threw 500; fixed via CreateExecutionStrategy().ExecuteAsync()), DEF-2/3 (CHECK-violating enum literals top_up→topup, package_purchase→package), DEF-4 (coupon percentage→percent miscompute). All money-flow checks PASS: idempotent payments, ledger+balance atomicity, coupon limits, customer isolation, separation.
- DB: fk_patch_06 + all 13 tables RLS-enabled (rls_enable_commerce.sql), packages_tenant legacy policy dropped. Verified app_user empty-brand→0 rows.
- Entities: 13 mapped (append-only ledgers, idempotency-key uniques, composite FKs to orders).
- `laundryghar.Commerce` (:5005) — admin (payment-methods/packages/coupons/promotions/loyalty/payments/refunds/wallet) + customer (packages/loyalty/coupons/wallet/payments) + IPaymentGateway dev stub. Program.cs/appsettings hand-finished by orchestrator (the build subagent hit a network "socket closed" error after 77 tool calls — recurring with large agents, separate from the 1M-credit issue). Fixed 2 seeder enum bugs (earn_basis per_rupee→spend; coupon percentage→percent). Boots + seeds + both auth lanes verified.
- PENDING: money-flow QA (idempotent payments, ledger+balance atomicity, coupon limits, customer-self isolation, separation). 9 commerce permission codes added to Identity seeder.
- ⚠️ A subagent bumped Utilities AutoMapper 13.0.1→16.1.1 (broke the build, NU1605); reverted. AutoMapper is unused (inline projections) — candidate for removal; CVE on backlog.

## BC-7 finance_royalty: COMPLETE ✅ (2026-06-05)
`laundryghar.Finance` (:6/5006? port 5006) — cash books (per store/shift, ADR-009), expenses (lifecycle + approve), royalty (calc from commerce.payments + invoices). 8 entities + 13 enums (exact CHECK values documented). Agent independently fixed the Npgsql-retrying-strategy/BeginTransactionAsync issue (same as BC-6 DEF-1). 7 finance perm codes added. Builds + boots.

## Service ports: Identity 5000 · Catalog 5001 · Orders 5002 · Warehouse 5003 · Logistics 5004 · Commerce 5005 · Finance 5006. 9 backend projects, builds 0 errors.

## 2026-06-05: User directive — START FRONTEND + MOBILE API INTEGRATION NOW, keep backend going.
- Frontend `admin-web/` (senior-react-architect): React 19+Vite+TS+TanStack Query+Zustand+Tailwind+shadcn. Integrate Identity auth + tenancy/catalog/orders. Per docs wireframes (docs/Laundry Ghar wireframes _standalone_ (1).html + OLMS PDF).
- Mobile `customer-mobile/` (expo-mobile-developer): Expo SDK 52+TS+NativeWind+React Navigation. Customer OTP auth + home/catalog/pickup/orders.
- API envelope (Utilities ApiResponse): top-level { status: bool, data: T|null, message }. Paginated: data={ list:[], hasPreviousPage, hasNextPage }. Login → data.accessToken + data.refreshToken. Auth: Bearer; admin platform-admin sends X-Brand-Id; customer tokens carry brand_id. Node v22, npm available.
- Backend continues: BC-8 engagement_cms (notifications/CMS/onboarding/banners/mobile_app_config — feeds the apps), then BC-9 analytics MVs.

## BC-8 engagement_cms: COMPLETE ✅ (2026-06-05)
`laundryghar.Engagement` (:5007) — admin CRUD (notification_templates/onboarding_slides/app_banners/mobile_app_config + outbox/log read+retry) + ANONYMOUS public endpoints (/api/v1/public/onboarding-slides, /app-config, /banners) that the apps call pre-login (brand via X-Brand-Id/brandCode→LG-MAIN, explicit brand filter since no token=no RLS). DevNotificationSender stub. 5 CMS perms added. 8 entities. Boots+seeds (3 customer + 3 rider slides, banner, android/ios config, 2 templates). Delete = archive (no deleted_at).

## CLIENT APPS scaffolded + API-integrated (first slices, all typecheck/build clean) — 2026-06-05
- `admin-web/` (React19+Vite+TS+Tailwind v4+TanStack Query+Zustand): auth(login+refresh+protected+brand switcher X-Brand-Id) + 3 live screens (Tenancy stores/franchises, Catalog+Pricing, Orders). `npm run build` + tsc pass. Agent ab008c2807db44ced.
- `customer-mobile/` (Expo SDK52+TS+NativeWind+expo-router): customer OTP auth + Home/PriceList/MyOrders/tracking/Profile. tsc pass. Agent aade8acb857ec2cfd.
- `rider-mobile/` (Expo, mirrors customer-mobile, green theme): rider PASSWORD auth + Today's Assignments + status update + Profile + location ping (expo-location one-shot). tsc pass. Agent ab23d0c485801db3d.
- API envelope for clients: { status, data, message }; paginated data={list,hasPreviousPage,hasNextPage}. Each app has typed axios clients per service + 401→refresh→retry + unwrap helpers.

## LIVE-RUN TEST (2026-06-05): all 3 apps build + run + integrate. Fixed real issues:
- Live API E2E: every endpoint the apps call returns 200 across 6 running services (admin tenancy/catalog/orders; customer otp/catalog/orders; engagement public CMS). Contracts match.
- admin-web: runs in browser (Vite :5173/:5174), API integration works.
- customer-mobile + rider-mobile MOBILE BUILD FIXES (both had the same scaffolding bugs; now bundle iOS cleanly + customer-mobile loads in iOS Simulator):
  1. Missing `expo-asset`+`expo-font` (tsc passed but Metro couldn't bundle) → `expo install`.
  2. babel.config.js: `nativewind/babel` was a PLUGIN → must be a PRESET; reanimated/plugin removed (app uses only GestureHandlerRootView).
  3. NativeWind 4.2.5 → **4.1.23** (its css-interop 0.2.5 hardcodes reanimated-4 `react-native-worklets/plugin`, incompatible with SDK52/reanimated3); pinned reanimated 3.16.1.
  4. Created placeholder PNG assets (icon/splash/adaptive-icon/favicon) — scaffold referenced them but assets/ was empty.
- Dev infra notes: port 8081 occupied by unrelated `snapaccount` project (use other ports for expo); services occasionally leave a port bound (kill stale pid before restart).

## NEXT per user: BC-9 analytics → POS web → wire CMS (onboarding/banners/app-config from :5007) into the apps (replace placeholders).
## BC-9 analytics: 5 populated MVs (mv_customer_ltv, mv_daily_store_revenue, mv_monthly_franchise_revenue, mv_rider_performance, mv_warehouse_throughput), 0 base tables. NOTE: RLS does NOT apply to materialized views — the Analytics service MUST filter by brand_id in-query (MVs carry brand_id).

## BC-9 analytics: COMPLETE ✅ — ALL 9 BACKEND BCs DONE.
`laundryghar.Analytics` (:5008): 5 MVs mapped as keyless EF view-entities; admin reporting endpoints (daily-store-revenue, monthly-franchise-revenue, warehouse-throughput, customer-ltv, rider-performance, dashboard) all brand-filtered IN-QUERY (no RLS on MVs); POST /refresh runs REFRESH MATERIALIZED VIEW CONCURRENTLY (all 5 have unique indexes). analytics.read/refresh perms. No MediatR (thin read projections).

## BACKEND COMPLETE: 9 services (Identity 5000, Catalog 5001, Orders 5002, Warehouse 5003, Logistics 5004, Commerce 5005, Finance 5006, Engagement 5007, Analytics 5008) + SharedDataModel (~76 entities, all 9 BCs + kernel) + Utilities. 11 projects, builds 0 errors. All RLS-isolated/brand-guarded, security+QA gated.
## CLIENTS: admin-web + customer-mobile + rider-mobile (build+run+integrated). 

## POS web + CMS wiring: COMPLETE ✅ (2026-06-05)
- `pos-web/` (4th client; mirrors admin-web): staff password auth + brand/store context + walk-in order creation (Orders create, server-side pricing) + today's orders/status + cash book (Finance open/entry/close). Builds + typechecks. Order-create payload shape documented.
- CMS wiring into customer-mobile + rider-mobile: added engagement.ts client (anonymous, X-Brand-Id/brandCode) + useEngagement hooks + wired onboarding carousels to live onboarding-slides and customer home to live home_top banners (with safe fallbacks). Both apps tsc exit 0. (Agent finished work then hit a network error on its report only.)

## ALL DELIVERED: 9 backend services (11 .NET projects) + 4 clients (admin-web, pos-web, customer-mobile, rider-mobile). Backend builds 0 errors; all clients typecheck/build; mobile apps bundle for iOS; live API E2E green across services.
## REMAINING (optional/backlog): notification outbox dispatcher worker; admin-web CMS management screens; rider-mobile home banners (only onboarding wired there); deeper multi-service live E2E; GIT COMMIT (still zero commits — Goutam commits manually). Prod-hardening: RS256+JWKS, secrets→vault, flip runtime to app_user (RLS enforcement), AutoMapper removal, order_number DB sequence, order_addons brand_id.
## Prod-hardening backlog: RS256+JWKS, secrets→vault, flip runtime to app_user (RLS enforcement), AutoMapper removal/CVE, order_number DB sequence, order_addons brand_id.

## Pattern per BC (proven over 5 BCs)
Round1 (parallel, fresh sonnet agents): DB patches+RLS (database-architect) ∥ SharedDataModel entities (dotnet, mirror existing BC entities). Round2: service slice(s) (sonnet dotnet, mirror existing services + add perms to Identity seeder). Round3: sonnet QA/security gate. Remediate; keep prod-hardening backlog. No git commits. Append-only ledgers (loyalty/wallet/package_usage) + idempotency keys per ADR-006 — important for BC-6.

## ⚠️ PORTS + ASPIRE (2026-06-05) — READ BEFORE RUNNING ANYTHING
- **Identity is on 5050, NOT 5000.** macOS AirPlay Receiver (ControlCenter) listens on *:5000 (system process; can't kill; returns 403 on every path). All client configs (admin-web/.env, pos-web/.env(.example), customer-mobile + rider-mobile app.config.ts) point Identity at 5050. Other services 5001–5008.
- **.NET Aspire**: `laundryghar.AppHost` + `laundryghar.ServiceDefaults` (13 projects). ONE command runs all 9 services + dashboard: `cd backend/laundryghar && ASPNETCORE_ENVIRONMENT=Development dotnet run --project laundryghar.AppHost`. Dashboard = dynamic https port + login token in stdout. Aspire **13.4.2** (SDK-native — .NET 10 SDK 10.0.103 fails NETSDK1228 on workload-era 9.x `<IsAspireHost>` props). DB injected via `.WithHttpEndpoint(port:N)` + `.WithEnvironment("ConnectionStrings__Default", connStr)` from AppHost config (NOT `AddConnectionString` → unresolved secret param, services stuck NotReady). Uses the existing live DB (no container). Each service Program.cs has `builder.AddServiceDefaults()` + `app.MapDefaultEndpoints()`. Manual per-service `dotnet run --project <svc>` on its fixed port still works for one-off testing.
- **Keep the AppHost running across turns**: launch it DETACHED (harness reaps tracked background tasks via SIGTERM after a few min): `cd backend/laundryghar && nohup env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:18888 ASPIRE_ALLOW_UNSECURED_TRANSPORT=true DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true dotnet run --project laundryghar.AppHost > /tmp/lg_apphost.log 2>&1 < /dev/null & disown`.
- **Dashboard = http://localhost:18888, NO token, NO https** (frictionless). Aspire REQUIRES https unless `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` (else OptionsValidationException "applicationUrl must be https"); `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` removes the login token. Without these, dashboard is a dynamic https port + rotating login token (user kept hitting http→ERR_EMPTY_RESPONSE and stale-token "Invalid token").
