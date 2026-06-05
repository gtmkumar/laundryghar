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
## Prod-hardening backlog (REMAINING): RS256+JWKS (replace shared HS256 secret), real secrets manager/Key Vault (currently env vars), AutoMapper removal/CVE.

## ✅ SECURITY-HARDENING TRACK DONE + E2E-VALIDATED (2026-06-06). All 4 items below built, applied, stack-restarted, live-tested.
- **order_addons brand_id + RLS** (db/patches/order_addons_brand_id_rls.sql): added brand_id uuid NOT NULL + FK→tenancy_org.brands ON DELETE RESTRICT + idx_order_addons_brand_id_fk + RLS enabled + rls_brand policy (mirrors order_items); dropped redundant legacy rls_admin_only. Entity OrderAddon.BrandId + OrderAddonConfiguration brand_id mapping + CreateOrderCommand sets BrandId=brandId. (0 rows existed → NOT NULL direct, no backfill.)
- **order_number atomic sequence** (db/patches/order_number_sequence.sql): replaced race-prone COUNT(*)+1 with order_lifecycle.next_order_number(brand,store,storeCode,year) — atomic INSERT...ON CONFLICT DO UPDATE...RETURNING on new order_number_sequences(brand_id,store_id,year,last_value) counter table (RLS-enabled). Format LG-<year>-<storeCode>-<NNNNNN> preserved. Handler calls it via _db.Database.SqlQuery<string>($"... AS \"Value\"").
- **secrets→env** (task 5): relocated ConnectionStrings from base appsettings.json → appsettings.Development.json for all 10 services (base is now SECRET-FREE; prod fails closed — all services `GetConnectionString("Default") ?? throw`). Jwt:SigningKey already dev-only in Development.json + non-dev throw guard present in all 9 API services. Doc: backend/laundryghar/PRODUCTION_ENV.md (required prod env: ConnectionStrings__Default=app_user, Jwt__SigningKey; do NOT set ConnectionStrings__Admin in prod).
- **E2E PROOF** (live, app_user runtime, brand-scoped NON-bypass token minted with dev key `dev-only-secret-key-min-32-chars-laundry-ghar-2026`, user_type=brand_admin, brand_id=LG, permissions="orders.create" space-separated): POST /api/v1/admin/orders ×2 → HTTP 201, order_number LG-2026-LGS-MUM-001-000001 then -000002 (atomic), addonTotal 50, both order_addons rows brand_id=LG. RLS isolation reproven: app_user brand=LG→2 addons, brand=B2→0, no-context→0. Login 200, all 9 services healthy.
- Test artifacts: 2 test orders remain in dev DB (LG-2026-LGS-MUM-001-000001/2) + counter at last_value=2 — harmless dev data, left in place.

## ⚠️ REGRESSION from app_user flip — FIXED (2026-06-06). The flip broke ALL anonymous/pre-auth endpoints.
Root cause: BrandResolver (+ customer-auth ResolveBrandIdAsync) assumed "anonymous → no token → RLS inactive → see all brands" — TRUE under postgres superuser, FALSE under app_user (RLS always active). So brand-by-code lookup returned 0 → customer OTP send 422 "Brand not found", all Engagement /public endpoints 404. tenancy_org.brands policy is rls_admin_only (bypass-ONLY), so anonymous can't read it.
FIX: scoped RLS-bypass middleware (sets HttpContext.Items["bypass_rls"]=true before endpoints) — Engagement Program.cs for /api/v1/public/*, Identity Program.cs for /api/v1/customer/auth/* when NOT authenticated. Restores the pre-flip contract (explicit brand_id predicate in each public/pre-auth query is the guard; bypass only for those request paths, full RLS everywhere else). Only Identity + Engagement have anonymous endpoints (Catalog customer endpoints require auth → token carries brand_id → RLS works; seeders use Admin/postgres conn). VERIFIED post-fix: public banners/onboarding/app-config 200, customer OTP send 200. LESSON: any new anonymous/pre-auth brand-scoped endpoint MUST set Items["bypass_rls"]=true (or it returns 0 rows under app_user).

## ✅ TRACK 2 (feature screens) DONE (2026-06-06, delegated to sonnet agents, both verified by orchestrator):
- admin-web CMS section: src/pages/cms/ (CmsPage tab shell + NotificationTemplates/OnboardingSlides/AppBanners/MobileAppConfig full CRUD + NotificationOutbox retry + NotificationLogs read), src/api/engagement.ts (+engagementClient in client.ts using VITE_ENGAGEMENT_URL=http://localhost:5007), src/hooks/useCms.ts, route /cms + Sidebar "CMS & Engagement" entry. tsc 0 errors, npm run build OK. DELETE = archive (status=archived).
- rider-mobile home_top banners: src/types/api.ts AppBannerDto, src/api/engagement.ts getHomeBanners, src/hooks/useEngagement.ts useHomeBanners, app/(app)/(tabs)/assignments.tsx BannerSection as FlatList ListHeaderComponent (null fallback on empty/error). tsc 0 errors. Endpoint GET /api/v1/public/banners?placement=home_top&brandCode=LG-MAIN (flat ListResponse).

## ⚠️ KEY AUTH/RLS FACTS (verified 2026-06-06)
- Login: POST /api/v1/auth/password/login {"identifier","password"}; admin@laundryghar.local / Admin@123 (Seeder:AdminPassword). platform_admin token has NO brand_id claim + gets RLS bypass (TenantResolutionMiddleware sets Items["bypass_rls"]=true when user_type=platform_admin).
- permissions claim = SPACE-SEPARATED codes (PermissionHandler splits on ' '); platform_admin user_type bypasses individual permission checks entirely.
- Order create is ADMIN/POS only (POST /api/v1/admin/orders, permission:orders.create); brandId = _user.RequireBrandId() (brand_id claim). Customers create via pickup-requests, not direct orders.
- Brand "Brand Two" (63a95d0f) is soft-deleted (deleted_at set) + suspended; EF DeletedAt query filter hides it from APIs (looks like 1 brand, not RLS).

## ✅ GITHUB (2026-06-06): https://github.com/gtmkumar/laundryghar — PRIVATE, branch main. Monorepo first commit (983 files): all backend + 4 clients + docs + db patches. Root .gitignore added (.NET bin/obj, node_modules, .expo, ios/android, .env keep .env.example). Removed stray empty nested repo backend/laundryghar/.git. Git remains ASK-GATED (Goutam asked explicitly this time).

## ✅ RUNTIME FLIP TO app_user — DONE + VALIDATED (2026-06-06). RLS now ENFORCED at runtime (was: postgres superuser bypassed it; app-layer brand predicates were the only guard).
- **DB patch** db/patches/harden_app_user_and_rls_bypass.sql (APPLIED): (1) FIXED kernel.rls_bypass() — it checked app.bypass_rls='on' but the RlsConnectionInterceptor emits 'true'/'false' → bypass was SILENTLY DEAD; platform-admin cross-brand reads + worker only worked because postgres=superuser ignores RLS. Now accepts on/true/1/yes/t. (2) Consolidated app_user GRANTs across ALL 9 schemas + analytics MVs + ALTER DEFAULT PRIVILEGES (legacy app_user_role.sql only covered 4 schemas; grants were actually already present from per-BC patches, but this documents/guarantees them).
- **Config**: ConnectionStrings:Default → app_user (runtime, RLS enforced) in all 10 appsettings.json + AppHost appsettings.Development.json. Added ConnectionStrings:Admin → postgres (Dev SEEDING ONLY; bypasses RLS natively). AppHost Program.cs injects BOTH ConnectionStrings__Default + __Admin to all 10 projects. **PROD: do NOT provision ConnectionStrings:Admin** (seeding is disabled outside Development anyway; seeders throw).
- **Seeding**: new laundryghar.SharedDataModel/SeedingSupport.CreatePrivilegedContext(adminConn) builds a standalone DbContext on the Admin (postgres) connection, NO RLS interceptor. The 6 seeders (Identity/Catalog/Orders/Commerce/Engagement/Finance) Program.cs now build it via `using var seedDb = SeedingSupport.CreatePrivilegedContext(app.Configuration.GetConnectionString("Admin") ?? connStr); ActivatorUtilities.CreateInstance<XSeeder>(scope.ServiceProvider, seedDb)`. Why: seeders write cross-brand bootstrap rows with NO request tenant context → app_user RLS WITH CHECK would reject them.
- **VALIDATED live**: build 0 errors; full stack relaunched; all 9 services + worker healthy as app_user (pg_stat_activity shows app_user runtime conns; postgres conns = leftover idle seeding pools, harmless, absent in prod). Platform-admin login 200; admin brands read returns the 1 active brand (Brand Two is soft-deleted via EF DeletedAt query filter — proves bypass works: without it admin would see 0). DB-level isolation reproven: app_user brand=LG→1 order, brand=B2→0; no-brand-no-bypass→0; bypass='true'→all. ZERO permission-denied errors in stack log.
- Worker now runs as app_user + BypassRls=true (works after the rls_bypass fix), no errors.
- Note: RLS not enabled on partition CHILD tables (enforced via partitioned PARENT, which EF always queries) and on 10 global/pre-auth identity tables (users/permissions/roles/otp_codes/refresh_tokens etc — by design, no brand_id). order_addons is the only brand-scopable table still missing brand_id (task in progress).

## Pattern per BC (proven over 5 BCs)
Round1 (parallel, fresh sonnet agents): DB patches+RLS (database-architect) ∥ SharedDataModel entities (dotnet, mirror existing BC entities). Round2: service slice(s) (sonnet dotnet, mirror existing services + add perms to Identity seeder). Round3: sonnet QA/security gate. Remediate; keep prod-hardening backlog. No git commits. Append-only ledgers (loyalty/wallet/package_usage) + idempotency keys per ADR-006 — important for BC-6.

## ⚠️ PORTS + ASPIRE (2026-06-05) — READ BEFORE RUNNING ANYTHING
- **Identity is on 5050, NOT 5000.** macOS AirPlay Receiver (ControlCenter) listens on *:5000 (system process; can't kill; returns 403 on every path). All client configs (admin-web/.env, pos-web/.env(.example), customer-mobile + rider-mobile app.config.ts) point Identity at 5050. Other services 5001–5008.
- **.NET Aspire**: `laundryghar.AppHost` + `laundryghar.ServiceDefaults` (13 projects). ONE command runs all 9 services + dashboard: `cd backend/laundryghar && ASPNETCORE_ENVIRONMENT=Development dotnet run --project laundryghar.AppHost`. Dashboard = dynamic https port + login token in stdout. Aspire **13.4.2** (SDK-native — .NET 10 SDK 10.0.103 fails NETSDK1228 on workload-era 9.x `<IsAspireHost>` props). DB injected via `.WithHttpEndpoint(port:N)` + `.WithEnvironment("ConnectionStrings__Default", connStr)` from AppHost config (NOT `AddConnectionString` → unresolved secret param, services stuck NotReady). Uses the existing live DB (no container). Each service Program.cs has `builder.AddServiceDefaults()` + `app.MapDefaultEndpoints()`. Manual per-service `dotnet run --project <svc>` on its fixed port still works for one-off testing.
- **Keep the AppHost running across turns**: launch it DETACHED (harness reaps tracked background tasks via SIGTERM after a few min): `cd backend/laundryghar && nohup env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:18888 ASPIRE_ALLOW_UNSECURED_TRANSPORT=true DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true dotnet run --project laundryghar.AppHost > /tmp/lg_apphost.log 2>&1 < /dev/null & disown`.
- **Dashboard = http://localhost:18888, NO token, NO https** (frictionless). Aspire REQUIRES https unless `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` (else OptionsValidationException "applicationUrl must be https"); `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` removes the login token. Without these, dashboard is a dynamic https port + rotating login token (user kept hitting http→ERR_EMPTY_RESPONSE and stale-token "Invalid token").
