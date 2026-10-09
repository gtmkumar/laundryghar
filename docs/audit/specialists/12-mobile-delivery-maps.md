# 12 — Mobile Applications, Delivery Logistics and Business-Aware Maps Specialist

Agent key: `mobile-maps` · AREA code: `MOB` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw`

## Scope and method

**Question.** Are the customer and delivery-partner (rider) mobile apps, the admin dispatch UI and the backend logistics stack production-ready for a multi-tenant, multi-vertical SaaS? That covers assignment, scheduling, tracking, geocoding, maps, serviceability, and partner/location authorization.

**What I read, all repo-relative.**
- **customer-mobile:**
  - Config: `app.config.ts`, `eas.json`, `.env.example`, `src/constants/config.ts`, `google-services.json` and `GoogleService-Info.plist` (key *names* only).
  - API layer: `src/api/{client,auth,catalog,orders,fulfillment}.ts`, `src/lib/{tokenStorage,terminology}.ts`, `src/hooks/useCatalog.ts`.
  - Screens: `app/(app)/(tabs)/_layout.tsx`, `app/(app)/booking/{pickup,pay}.tsx`, `app/(app)/addresses.tsx`, `app/(app)/orders/tracking/[id].tsx`, `app/(app)/orders/[id].tsx`.
- **rider-mobile:**
  - Config: `app.config.ts`, `eas.json`, `src/constants/config.ts`.
  - API layer: `src/api/{client,tasks,rider,auth}.ts`.
  - Location: `src/hooks/useLocationTracking.ts`, `src/lib/{backgroundLocation,sendCurrentLocation}.ts`.
  - State: `src/store/{authStore,dutyStore,offlineQueueStore}.ts`, `src/hooks/useOfflineQueueFlush.ts`.
  - Screens: `app/_layout.tsx`, `app/(app)/_layout.tsx`, `app/(app)/tasks/[id].tsx`, `app/(app)/tasks.tsx`.
- **admin-web:** `src/components/map/{mapConfig,RiderMap}.ts(x)`, `src/hooks/useRiders.ts` (polling), `src/pages/orders/PickupDetailDrawer.tsx`, `src/pages/riders/*` (listing).
- **Backend, operations service:**
  - Endpoints: `operations.WebApi/Endpoints/Logistics/{RiderSelfEndpoints,RidersAdmin,RiderTasksAdmin,RiderAssignmentsAdmin}.cs` and `Endpoints/Orders/{CustomerOrderEndpoints,AdminPickupEndpoints,AdminDeliveryEndpoints}.cs`.
  - Rider self commands: `operations.Application/Logistics/RiderSelf/Commands/{BatchLocationPing,UpdateMyTaskStatus,VerifyTaskOtp,OfferActions}/*`.
  - Logistics helpers and ops queries: `Logistics/Common/{GeofenceEvaluator,RiderLoad}.cs`, `Logistics/RiderOps/Queries/{GetRiderTrack,GetRidersLive}`, `Logistics/RiderSelf/RiderTaskMapper.cs`, `Logistics/RiderSelf/Queries/GetMyTasksToday`.
  - Orders: `Orders/Pickup/Commands/{PickupCommands,CustomerPickupCommands}.cs`, `Orders/Delivery/Commands/DeliveryAssignmentCommands.cs`, `Orders/Delivery/Queries/DeliverySlotQueries.cs`, `Orders/Fare/Queries/GetFareQuoteQuery.cs`, `Orders/Orders/Queries/OrderQueries.cs`, `Orders/Orders/Commands/CancelOrderByCustomerCommand.cs`.
  - Catalog: `Catalog/Customer/Self/Queries/SelfQueries.cs` (serviceability).
- **Backend, commerce workers:** `commerce.Infrastructure/Worker/Services/{AutoDispatchService,PartitionMaintenanceService,RetentionSweepService}.cs`, `.../AutoDispatch/{RiderRanker,DispatchConfig}.cs`.
- **Backend, other:** `laundryghar.Utilities/Auth/RiderOnlyRequirement.cs`, `core.Application/Identity/Settings/*Maps*`, and `laundryghar.Gateway/appsettings.json`.
- **Database:**
  - Base schema: `database_scripts/05_bc5_logistics.sql`, `04_bc4_order_lifecycle.sql` (delivery_assignments, slots), `03_bc3_customer_catalog.sql` (customer_addresses), `99_cross_cutting_schema_qualified.sql` (partman).
  - Patches: `db/patches/{dispatch_offer_states,dispatch_permissions,rider_ping_partition_maintenance,rls_enable_logistics,dpdp_erasure_pipeline}.sql`.
  - Migrations: `db/migrations/0011_vertical_templates.up.sql`.
- **CI:** `.github/workflows/ci.yml` (the mobile job).

**Commands run.**
- Read-only `grep`, `sed`, `cat` and `ls` over the repo.
- In `scratchpad/mobile-maps/` (copies of the app folders; nothing installed in the repo):
  - `npm ci --ignore-scripts`. **rider-mobile failed with ERESOLVE**: react-dom@19.2.8 versus react@19.2.3, on npm 10.9.4 / Node 22.22. Both apps then installed with `--legacy-peer-deps`.
  - `npx jest --ci`. Rider: 8 suites, 91 tests pass. Customer: 11 suites, 170 tests pass.
  - `npx tsc --noEmit`. **Both apps fail** with `TS2882: Cannot find module or type declarations for side-effect import of '../global.css'` (`app/_layout.tsx:11`).

**What I could not verify, and why.**
- No .NET SDK and no running PostgreSQL/PostGIS/pg_partman, so no backend handler was executed. Every backend behaviour below comes from reading the code, and race conditions are reasoned, not reproduced.
- No iOS/Android device or simulator, so background-location, headless-task, push and permission prompts are not runtime-verified.
- I did not check whether `rls_enable_logistics.sql` is applied by the documented fresh build. That belongs to the tenancy/DB specialists.

## Current-state summary

**Two Expo SDK 56 apps exist and are wired to real APIs.** Both use React Native 0.85.3, React 19.2.3, expo-router ~56.2, TanStack Query 5, Zustand 5 and axios, with no demo fallback active (`FEATURES.bookingApi`/`riderTasksApi = true`).

**Customer app.**
- Phone-OTP, Google and PIN login, with tokens in the Keychain/Keystore via `expo-secure-store` (`src/lib/tokenStorage.ts`).
- Address CRUD by text form with a 6-digit pincode regex only.
- Catalog, cart and slot-based pickup scheduling (`POST /customer/pickup-requests`).
- Wallet/COD payment preference only; online pay is "coming soon" (`app/(app)/booking/pay.tsx:586-595`).
- Order/pickup status-timeline tracking, a parcel (point-to-point) flow, support tickets, and rating.
- There is **no map, no location permission and no geocoding/autocomplete** (`app.config.ts:29` `permissions: []`; no maps/location dependency in `package.json`).

**Rider app.**
- Admin-invited system user with phone OTP; KYC document upload.
- Duty toggle, today's tasks (30 s polling), start/arrive/collect/complete/fail, and server-side OTP verify.
- Proof photo and pickup inspection.
- Navigation handoff by deep link to Google Maps / Apple Maps (`app/(app)/tasks/[id].tsx:196-222`).
- Background GPS via `expo-location` + `expo-task-manager` (25 s / 30 m, Android foreground service, iOS `UIBackgroundModes: location`), with a foreground 25 s fallback.
- An AsyncStorage offline queue for status updates.
- The task-detail map is a placeholder (`tasks/[id].tsx:11-12`).

**Backend logistics execution paths traced.**
- **Rider ping:** rider-mobile → gateway `/logistics` (YARP, `laundryghar.Gateway/appsettings.json:19`) → `POST /api/v1/rider/location/ping` (`RiderSelfEndpoints.cs:57,126-145`; policy `RiderOnly`, which is claims only) → `BatchLocationPingHandler`. The handler:
  - self-resolves the rider by `UserId+BrandId`;
  - inserts `logistics.rider_location_pings` (geography, partitioned daily);
  - updates `riders.last_known_location/last_ping_at`;
  - runs `GeofenceEvaluator`, a 150 m haversine check that auto-flips `started→arrived` and stamps `dropped_at` (`BatchLocationPing.cs:36-99`, `GeofenceEvaluator.cs:43-111`).
- **Rider task status:** `PATCH /rider/tasks/{id}/status` → `UpdateMyTaskStatusHandler`. The handler is IDOR-guarded by rider+brand, but **sets `da.Status = cmd.Status` with no transition check** (`UpdateMyTaskStatus.cs:49-53,96`). On delivery completion it transitions the order to delivered and writes history, the COD payment and the outbox event in one transaction (`:152-260`).
- **Dispatch:**
  - **Auto:** `AutoDispatchService` (off by default). It polls pending pickup requests, ranks riders by franchise, then load, then haversine (`RiderRanker.cs:56-82`), and either push-assigns or creates a TTL offer (`AutoDispatchService.cs:110-288`).
  - **Manual:** `POST /admin/pickup-requests/{id}/assign` (`PickupCommands.cs:204-279`) and `POST /admin/delivery-assignments` (`DeliveryAssignmentCommands.cs:17-107`).
  - **Offers:** rider accept/decline offer endpoints exist (`OfferActions.cs`) but the rider app never calls them.
- **Admin live map:**
  - `GET /admin/riders/live` uses brand+franchise scope with 10-min stale detection (`GetRidersLive.cs:22-40`).
  - `GET /admin/riders/{id}/track` returns a brand+franchise-scoped breadcrumb (`GetRiderTrack.cs:25-48`).
  - admin-web polls every 20 s (`useRiders.ts:87,99`) and renders through a provider abstraction: OSM/Leaflet by default, Mapbox or Google when a key is set per brand (`mapConfig.ts:24-42`, `RiderMap.tsx`).
  - There is no WebSocket/SignalR anywhere; everything is polling.

**Maps infrastructure.** Map rendering exists **only in admin-web**. There is **no geocoding, reverse geocoding, autocomplete, routing, distance-matrix or ETA provider anywhere** (grep for googleapis/nominatim/geocod/distancematrix/osrm returned nothing). Crucially, **no application code ever writes a coordinate for a customer address, a store or a delivery assignment**: the only `CreatePoint` calls in the backend are in `BatchLocationPing.cs:53,85`. Consequences:
- geofence auto-arrival, distance-aware dispatch and coordinate-based rider directions are inert;
- the parcel fare quote throws for every app-created address (`GetFareQuoteQuery.cs:57-59`).

**Business type.**
- Vertical templates (`0011_vertical_templates.up.sql`) carry fulfilment mode, terminology and bundle, but **no location/map capability**.
- The mobile apps resolve tenant by a build-time `DEFAULT_BRAND_CODE` (default `LG-MAIN`) with a hardcoded name and bundle ID.
- The customer app shows both laundry and parcel flows regardless of vertical (`(tabs)/_layout.tsx:160-177`).
- Terminology and the tracking stages are server-driven.

## 1. Mobile Application Inventory

| App | Framework / SDK | Entry point | Build config | Key native capabilities | Status |
|---|---|---|---|---|---|
| customer-mobile (`com.laundryghar.customer`, v2.0.0) | Expo ^56, RN 0.85.3, React 19.2.3, TS ~6.0.3, expo-router ~56.2, NativeWind 4 | `expo-router/entry` → `app/_layout.tsx` | `app.config.ts` (EAS projectId = slug placeholder `:6`, OTA URL 404s), `eas.json` (dev/preview/prod; submit IDs empty `:48-59`). `google-services.json` has package `com.launddryghar.app` (typo) and the plist has bundle `com.laundrygahar.ios`; **neither matches nor is wired** (no `googleServicesFile`) | secure-store, notifications, auth-session, local-auth, updates, Sentry. **No location/maps** | Partially Supported (functional against the API; not release-ready) |
| rider-mobile (`com.laundryghar.rider`, v1.0.0) | Same stack + expo-location ~56.0.23, expo-task-manager ~56.0.25, expo-image-picker, expo-network | `expo-router/entry` → `app/_layout.tsx` (imports `@/lib/backgroundLocation` at module load `:33`) | `app.config.ts`: iOS location strings + `UIBackgroundModes:['location']` (`:21-32`); Android FINE/COARSE/BACKGROUND_LOCATION + FOREGROUND_SERVICE_LOCATION (`:41-55`). EAS projectId placeholder (`:6`). **No FCM config file at all.** Dev default gateway port 8080 (`src/constants/config.ts:19`) is stale against AppHost 5300 | background GPS, camera, push | Partially Supported |
| admin-web delivery management | React 19 + Vite; react-leaflet 5, `@vis.gl/react-google-maps` 1.8 | `src/pages/riders/RiderOpsView.tsx`, `PickupDetailDrawer.tsx` | Per-brand Maps settings (`PUT /admin/settings/maps`, `AdminSettings.cs:54`) | Live rider map, breadcrumb trail, assign pickup | Partially Supported |
| CI | `.github/workflows/ci.yml:65-86`: `npm ci` → `npm run typecheck` → `npm test` per app | — | Locally: rider `npm ci` ERESOLVE; both apps fail `tsc` (TS2882) | — | Not Supported (job would fail as committed; see SA-MOB-017) |

## 2. Customer Journey Audit

| Step | Screen | API | Backend handler | DB | Status | Evidence |
|---|---|---|---|---|---|---|
| Tenant resolution | (build constant) | `brandCode` in auth bodies | identity auth | `tenancy_org.brands` | Partially Supported: one brand per binary, set by env at build | `customer-mobile/src/constants/config.ts:49`, `src/api/auth.ts:68-111` |
| Login / registration | `(auth)/phone,otp,secure,unlock` | `/identity/api/v1/customer/auth/*` | core identity | identity_access | Fully Supported (not run) | `src/api/auth.ts`, `src/lib/tokenStorage.ts:16-50` |
| Business-type awareness | home, tracking | `GET /terminology`, `GET /fulfillment-config` | config queries | `vertical_terms` | Partially Supported (words and stages only; flows not gated) | `src/lib/terminology.ts:1-57`, `(tabs)/_layout.tsx:160-177` |
| Service discovery / pricing / cart | `booking/items`, `price-list` | `/catalog/...` | CustomerCatalogEndpoints | customer_catalog | Partially Supported (not deep-audited; Related area: VERT/FE) | `src/api/catalog.ts` |
| Address management | `addresses.tsx` | `POST/PUT/DELETE /customer/addresses` | catalog customer self | `customer_addresses` (`geo_location` never set) | Partially Supported: text only, no geo, no map pick, no geocode | `addresses.tsx:47-124`, `src/types/api.ts:235-254` |
| Map-based location pick / geocoding | — | — | — | — | Not Supported | no location/maps dependency; no `CreatePoint` for addresses |
| Serviceability check | (none calls it) | `GET /customer/serviceability?pincode=` | `CheckServiceabilityHandler` (store pincode or territory pincodes) | stores, territories | Partially Supported: API exists, client never calls it, pickup creation never enforces it | `SelfQueries.cs:89-116`; `useServiceability` is unused outside `src/hooks/useCatalog.ts:172-179` |
| Slot selection | `booking/pickup.tsx` | `GET /customer/delivery-slots?date=` | `GetAvailableSlotsHandler` | `delivery_slots` | Partially Supported: brand-wide, not zone/store aware; brand filter relies on RLS only | `pickup.tsx:3-8,202-228`, `DeliverySlotQueries.cs:47-66` |
| Checkout / book pickup | `booking/pay.tsx` | `POST /customer/pickup-requests` | `CustomerSchedulePickupHandler` (atomic slot capacity, idempotency index) | `pickup_requests`, `delivery_slot_bookings` | Partially Supported: **addressId ownership not checked** (SA-MOB-005); app sends no Idempotency-Key (SA-MOB-018) | `PickupCommands.cs:310-470`, `:103`; `pay.tsx:390-405` |
| Payment | `pay.tsx` | (wallet/COD preference only) | — | — | Partially Supported: online UPI/card "coming soon"; wallet top-up disabled | `pay.tsx:66-78,586-595`; `config.ts:116` |
| Parcel (logistics vertical) | `parcel/pickup,drop,vehicle,quote` | `POST /customer/fare/quote`, `POST /customer/orders/parcel` | `GetFareQuoteHandler` | addresses.geo_location | **Not Supported in practice**: quote throws when either address lacks geo, and nothing writes geo | `GetFareQuoteQuery.cs:44-59`; `src/api/orders.ts:96-107` |
| Status / tracking | `orders/tracking/[id].tsx` | `GET /customer/orders/{id}/tracking`, `GET /customer/pickup-requests/{id}` | `GetMyOrderTrackingHandler` (owner check) | `order_status_history` | Partially Supported: timeline only; pickup never shows `rider_dispatched`/`arrived` (SA-MOB-014) | `OrderQueries.cs:207-232`; `tracking/[id].tsx:1-13,57-66` |
| Rider assignment visibility / live tracking / ETA | — | — | — | — | Not Supported (no rider identity, location or ETA exposed to the customer) | `orders/[id].tsx:213` |
| Cancellation | order detail / pickups | `POST /customer/orders/{id}/cancel`, `POST /customer/pickup-requests/{id}/cancel` | `CancelOrderByCustomerHandler`; `CancelPickupByCustomerHandler` (cancels legs, frees slot and load) | orders, assignments | Partially Supported: an order cancel does not cancel its delivery legs (SA-MOB-016) | `CancelOrderByCustomerCommand.cs:35-111`; `CustomerPickupCommands.cs:345-427` |
| Delivery OTP shown to customer | order detail | `GET /customer/orders/{id}` (OTP only while out_for_delivery) | `OrderQueries.cs:125` | `orders.delivery_otp` | Not Supported in practice: OTP never generated (SA-MOB-003) | `CreateOrderCommand.cs:841-844` |
| Notifications | push | Expo push token register | ExpoPushChannelSender | push_tokens | Partially Supported: Android FCM not wired (SA-MOB-017) | `app.config.ts:89-101` |
| Support / refunds | `support/*` | `/customer/support/tickets` | support handlers; `OrderCancellationRefund.QueueAsync` | — | Partially Supported (not deep-audited) | `CancelOrderByCustomerCommand.cs:108-111` |
| Offline / errors | all | axios 15 s timeout, refresh-coalescing | — | — | Partially Supported (no offline queue; ErrorState with retry) | `src/api/client.ts` |

## 3. Delivery Partner Journey Audit

| Step | Screen | API | Backend handler | DB | Status | Evidence |
|---|---|---|---|---|---|---|
| Onboarding / invite | (admin) | `POST /admin/riders`, invite | `CreateRider`, `InviteRider` | `logistics.riders` (`user_id` globally UNIQUE, so one brand per rider) | Partially Supported | `05_bc5_logistics.sql:25` |
| KYC docs / approval | `documents.tsx` | `GET/POST /rider/documents`; admin `/verify`, `/vehicle/approve` | `UploadRiderDocument`, `VerifyRiderKyc` | rider docs | Partially Supported: auto-dispatch requires KYC verified + vehicle approved; **manual assign does not** | `AutoDispatchService.cs:147-148`; `PickupCommands.cs:214-218` |
| Login | `(auth)/login,otp` | `/identity/api/v1/auth/otp/*` | identity | users | Fully Supported (not run) | `rider-mobile/src/api/auth.ts:1-40` |
| Duty online/offline | `home.tsx` | `PATCH /rider/duty`, `GET /rider/me` | `SetRiderDuty` | `riders.is_on_duty` | Partially Supported: optimistic local, fire-and-forget server call; mismatch banner | `dutyStore.ts:91-116` |
| Offer accept / reject | — | `POST /rider/assignments/{id}/accept|decline` | `AcceptOfferHandler`/`DeclineOfferHandler` | `delivery_assignments` | **Not Supported end-to-end** (no app UI, no push, offers hidden from the task list) | `RiderSelfEndpoints.cs:64-65`; `RiderTaskMapper.cs:11-12`; rider app has no `accept` call |
| New-assignment notification | — | — | outbox `assignment.auto_assigned/offered` has no consumer | outbox_events | Not Supported (30 s polling while foregrounded) | `AutoDispatchService.cs:360,445`; `useRiderTasks.ts:57` |
| Task list / detail | `tasks.tsx`, `tasks/[id].tsx` | `GET /rider/tasks/today` | `GetMyTasksToday` (rider+brand) | assignments, orders, addresses | Fully Supported for listing | `GetMyTasksToday/*.cs:25-110` |
| Pickup/drop address, markers, route | `tasks/[id].tsx` | — | `RiderTaskMapper` (lat/lng from `da.GeoLocation ?? addr.GeoLocation`) | geo never written | Partially Supported: address text yes; coordinates effectively always null; map placeholder; no route | `RiderTaskMapper.cs:107-110`; `tasks/[id].tsx:11-12` |
| Navigation handoff | `tasks/[id].tsx` | `Linking.openURL(google.com/maps/dir … | search …)`, Apple Maps fallback | — | — | Partially Supported: the coordinates branch is dead in practice and falls back to address search | `tasks/[id].tsx:196-222,488-505` |
| Start / arrive | `tasks.tsx`, `tasks/[id].tsx` | `PATCH /rider/tasks/{id}/status` | `UpdateMyTaskStatusHandler` | assignments | Partially Supported: **no transition guard** (SA-MOB-001) | `UpdateMyTaskStatus.cs:41-53,96-100` |
| Geofence auto-arrive / drop | (background) | ping | `GeofenceEvaluator` | assignments, stores.geo_location | Not Supported in practice (needs `da.GeoLocation`/`stores.geo_location`, never written) | `GeofenceEvaluator.cs:83-106` |
| Pickup confirm / delivery confirm / OTP / POD | `tasks/[id].tsx` | `/verify-otp`, `/status completed`, `/proof-photo`, `/inspection` | `VerifyTaskOtpHandler`, `UpdateMyTaskStatusHandler`, `UploadProofPhoto`, `SubmitPickupInspection` | assignments | Partially Supported: OTP gate exists but OTPs are never generated, and verify has no attempt limit; photo is optional | `VerifyTaskOtp.cs:39-59`; `UpdateMyTaskStatus.cs:89-94` |
| Fail with reason | `tasks/[id].tsx` | `/status failed {reason,note}` | same | `cancellation_reason`, `notes` | Partially Supported (an offline replay drops the reason; SA-MOB-008) | `useOfflineQueueFlush.ts:51` |
| GPS / live tracking | (global) | `POST /rider/location/ping` | `BatchLocationPingHandler` | `rider_location_pings` (daily partitions) | Partially Supported (works foreground and background on dev/prod builds; see SA-MOB-009/010/011/012) | `backgroundLocation.ts:30-97`; `useLocationTracking.ts:21-66` |
| Offline / network interruption | banner + queue | replay of PATCH status / photo | — | AsyncStorage | Partially Supported (pings dropped while offline; poison-message stall) | `offlineQueueStore.ts`, `useOfflineQueueFlush.ts:35-64` |
| History / earnings / payouts / cash | `earnings`, `payouts`, `cash` | `/rider/tasks?date=`, `/rider/payouts`, `/rider/balance`, `/rider/payout-requests` | RiderSelf queries | assignments, payouts | Partially Supported (not deep-audited; Related area: FIN) | `RiderSelfEndpoints.cs:74-97` |
| Sign-out / deauthorisation | `profile.tsx` | `setOnDuty(false)` → stop background location; `/auth/logout` | `DeactivateRider` (admin) | riders | Partially Supported: client stops; **server does not stop a terminated rider** (SA-MOB-011) | `profile.tsx:66`; `RiderOnlyRequirement.cs` |

## 4. Business-Type Capability Matrix

| Vertical | Required map capability | Existing implementation | Missing | Config dependency | Status |
|---|---|---|---|---|---|
| Laundry (`process_deliver`) | Customer address pick, pickup/drop, rider assignment, live tracking | Text addresses; slot booking; manual and auto assignment; rider GPS + admin live map; Google/Apple Maps handoff by address text | Geocoding/map pin; coordinate capture; serviceability enforcement; customer live tracking/ETA; zone-aware slots; real geofence (geo never set) | `delivery_slots`, `territories.pincodes`, per-brand maps provider (admin only) | Partially Supported |
| Salon (`appointment`) | Salon location, nearby branches, directions, optional home-service tracking | None in the mobile apps: no store locator, no appointment booking screens; the salon strategy exists server-side only (Related area: VERT) | Branch list with coordinates, directions deep link, appointment UI, optional at-home tracking | Template `salon` → `appointment`; no location config | Not Supported |
| Hyperlocal marketplace | Customer address, vendor discovery by service area, coverage, delivery tracking | No vendor/marketplace model; serviceability by pincode only | Vendor geo index, service-area polygons, coverage checks, multi-vendor dispatch | No template exists (`vertical_templates` CHECK lists only laundry/salon/logistics/tiffin) | Not Supported |
| Logistics / courier (`point_to_point`) | Pickup/drop, route planning, driver location, shipment tracking, dispatch | Parcel screens; fare quote by haversine; vehicle tier; dispatch offers in the backend; rider GPS; admin breadcrumb | **Coordinates for addresses (the quote fails without them)**; route/ETA; customer shipment tracking; offer UI in the rider app | Template `courier` → `point_to_point`; fare settings | Not Supported in practice (the flow throws at quote) |

How the vertical reaches the clients today:
- **Words:** `GET /terminology`.
- **Tracking stages:** `GET /fulfillment-config`.
- **Admin map tiles:** `system_settings maps/provider`.

Nothing maps the business type to map capabilities, and no entitlement gates a map or tracking capability. Changing a brand's vertical changes nothing in the mobile apps' location behaviour. Both flows stay visible and the parcel endpoints are not entitlement-gated (`CustomerOrderEndpoints.cs:41,60`). Every "configuration" today is UI hiding or absent, with no backend enforcement.

## 5. Mapping Integration Audit

| Capability | Provider / SDK | Where | Status | Evidence |
|---|---|---|---|---|
| Map rendering, admin | Leaflet + OSM tiles (default); Mapbox raster through Leaflet; Google Maps JS via `@vis.gl/react-google-maps` | admin-web | Fully Supported (render only) | `mapConfig.ts:24-72`, `RiderMap.tsx` |
| Map rendering, rider | none (placeholder) | rider-mobile | Not Supported | `tasks/[id].tsx:11-12` |
| Map rendering, customer | none | customer-mobile | Not Supported | `package.json` |
| Geocoding / reverse / autocomplete / place pick | none | — | Not Supported | repo-wide grep (no provider calls) |
| Coordinate persistence for addresses/stores/legs | — | — | Not Supported (no writer; only pings write geo) | `BatchLocationPing.cs:53,85` are the only `CreatePoint` calls |
| Distance | haversine in code (fare quote, ranker, geofence) | backend | Partially Supported (straight-line only, and inputs are null) | `RiderRanker.cs:85-98`, `GeofenceEvaluator.cs:29-37`, `GetFareQuoteQuery.cs:61` |
| Route / ETA / distance matrix | none | — | Not Supported | — |
| Navigation handoff | Google Maps URL / Apple Maps URL | rider-mobile | Partially Supported | `tasks/[id].tsx:196-222` |
| Serviceability / zones | pincode equality against `stores.pincode` or `territories.pincodes` | backend | Partially Supported (no polygons, no PostGIS use, not enforced) | `SelfQueries.cs:95-115` |
| PostGIS usage | `GEOGRAPHY(POINT)` columns + GIST indexes; no spatial SQL queries | DB | Partially Supported (storage only) | `05_bc5_logistics.sql:76,117,134`, `03_bc3_customer_catalog.sql:111,128` |
| Real-time transport | HTTP polling (admin 20 s, rider tasks 30 s, rider profile 60 s) | all | Partially Supported | `useRiders.ts:87,99`; `useRiderTasks.ts:57` |
| Permissions foreground/background | rider: requested at duty-on, with fallback; customer: none needed | rider-mobile | Partially Supported (no pre-prompt or education screen; requests "Always" immediately) | `backgroundLocation.ts:70-74` |
| API keys | Map keys per brand in `kernel.system_settings` (`maps/provider`, `isEncrypted:false`) and returned by the admin settings GET; Firebase `API_KEY`/`current_key` committed in `customer-mobile/GoogleService-Info.plist` and `google-services.json` (public Firebase identifiers; key names only reported) | core settings; customer-mobile | Partially Supported (no referrer/package restriction evidence) | `UpdateMaps.cs:52`, `GetAdminSettings.cs:44` |
| Provider error handling / fallback | admin falls back to OSM when the key is missing | admin-web | Fully Supported (render) | `mapConfig.ts:34-41` |
| Quota / cost | No paid provider calls, so no cost today; OSM tile usage policy applies to production traffic | — | Not Verified | — |

**Are maps required?**
- **Laundry:** a plain address form plus pincode serviceability is enough for launch, *if* the geofence/distance features are disabled or fed by a one-time geocode on address save.
- **Logistics/parcel:** it is not enough, because the fare depends on coordinates.
- **Rider:** a deep-link handoff is sufficient. An in-app map is optional.

## 6. Delivery Workflow & Concurrency Audit

| Concern | Implemented behaviour | Gap | Status | Evidence |
|---|---|---|---|---|
| Assignment considers availability / KYC / load / location / service area | Auto: on-duty, active, KYC verified, vehicle approved, load < capacity, franchise preference, haversine to `LastKnownLocation`. Manual: rider in brand only | No `LastPingAt` freshness filter, no max radius or service area, location effectively null; manual assign ignores duty, KYC and status | Partially Supported | `AutoDispatchService.cs:143-162`, `RiderRanker.cs:56-82`, `PickupCommands.cs:214-218` |
| One job → one rider | Auto: check-then-insert without lock; offer: one live offer per pickup; accept: sibling "taken" check **outside** the transaction | No unique partial index on active `delivery_assignments(pickup_request_id)` or `(order_id, leg_type)`; no concurrency token; **manual assign/create have no existing-assignment or pickup-status check** | Not Supported | `04_bc4_order_lifecycle.sql:332-335`; `PickupCommands.cs:204-279`; `DeliveryAssignmentCommands.cs:29-107`; `OfferActions.cs:59-93` |
| Reassignment | None (a new row is added; the prior leg is not cancelled and its load is not decremented) | — | Not Supported | same |
| Status transition integrity | Allowed-set check only (`started/arrived/collected/completed/failed`) | Any to any, including cancelled/failed/completed → anything; repeat completes decrement load again | Not Supported | `UpdateMyTaskStatus.cs:34-42,96,263-264` |
| Idempotency of delivery completion side-effects | Gated on `o.DeliveredAt == null`; COD payment re-call guard | The gate does not check order status, so it can transition a cancelled order to delivered | Partially Supported | `UpdateMyTaskStatus.cs:152-257` |
| Slots respect capacity | Atomic `booked_count < capacity` UPDATE in a transaction; release on cancel | Not tied to store operating hours or the customer's zone; past slots of "today" are not filtered | Partially Supported | `PickupCommands.cs:388-404`; `DeliverySlotQueries.cs:51-61` |
| ETA | none | — | Not Supported | — |
| Cancellation propagation | Pickup cancel: legs cancelled, load decremented, slot released (excludes `offered` legs). Order cancel: legs untouched | Rider offline replay can revive a cancelled leg | Partially Supported | `CustomerPickupCommands.cs:321-322,395-407`; `CancelOrderByCustomerCommand.cs` |
| Customer tracking reflects authoritative status | Order history for orders; for pickups only `pending→assigned→completed` (started/arrived not propagated) | — | Partially Supported | `UpdateMyTaskStatus.cs:84-86,139-140,346-354` |
| Fresh location / stale detection | Admin board: stale after 10 min using `LastPingAt` | `LastPingAt` is client-supplied (`PingedAt`) | Partially Supported | `GetRidersLive.cs:22`; `BatchLocationPing.cs:79-87` |
| Tracking stops after completion / deauth | Client stops on off-duty and sign-out | Server accepts pings regardless of duty, assignment or rider status | Partially Supported | `BatchLocationPing.cs:42-47` |
| Retry / idempotency on the rider side | Dedup of identical `taskId+status` in the queue; server re-applies | Non-idempotent side effects (load decrement) per call | Partially Supported | `offlineQueueStore.ts:61-69` |

**Implemented vs. proposed.** Everything in the "Implemented behaviour" column is code that exists. The ranking improvements (ETA/route-aware ranking, zone filters) are *proposals*, not implemented.

## 7. Security & Privacy Audit (incl. explicit IDOR checks)

| Endpoint / data | Check performed (code read) | Result | Status |
|---|---|---|---|
| `PATCH /rider/tasks/{id}/status`, `/verify-otp`, `/proof-photo`, `/inspection` | Can rider X act on rider Y's or another brand's task by changing `{id}`? | No. Every handler loads `DeliveryAssignments` by `Id && RiderId == self && BrandId == jwt.brand` → 404 (`UpdateMyTaskStatus.cs:44-53`, `VerifyTaskOtp.cs:23-32`) | Verified (code) |
| `POST /rider/assignments/{id}/accept|decline` | Same IDOR | Guarded by rider + brand (`OfferActions.cs:36-49,118-131`) | Verified (code) |
| `GET /rider/tasks/today`, `/rider/tasks?date=` | Scope | Self rider + brand | Verified (code) |
| `POST /rider/location/ping` | Can a rider post as another rider? | No; the rider is resolved from the JWT. Coordinates, timestamp and batch size are unvalidated (SA-MOB-010) | Verified (code) |
| `GET /admin/riders/{id}/track`, `/admin/riders/live` | Cross-brand / cross-franchise | Brand filter + franchise filter when the actor is franchise-scoped; **not store-scoped** (a store manager sees every franchise rider's trail) | Partially Verified |
| `GET /admin/rider-tasks/{id}/proof-photo` | Brand scoping | `GetProofPhotoStreamQuery(id, brandId)` (handler not read) | Partially Verified |
| Customer reads rider location | Is any customer endpoint exposing rider location, before or after delivery? | No customer endpoint returns rider identity or location (`OrderQueries.cs:207-232`; `orders/[id].tsx:213`) | Verified (code) |
| `GET /customer/orders/{id}/tracking`, `/orders/{id}` | IDOR | `o.CustomerId == jwt.sub` (`OrderQueries.cs:221-223`) | Verified (code) |
| `POST /customer/pickup-requests` `addressId` | Can a customer use another customer's address? | **Yes.** `AddressId` is copied without an ownership check (`PickupCommands.cs:103`). The FK is `REFERENCES customer_addresses(id)` with no customer/brand match (`04_bc4_order_lifecycle.sql:262`; FK checks are not subject to RLS). The rider then receives that address and its `RecipientPhone` (`GetMyTasksToday`, `RiderTaskMapper.cs:128`) → SA-MOB-005 | Partially Verified (code; not reproduced) |
| `POST /customer/fare/quote` | IDOR | Addresses filtered by customer + brand (`GetFareQuoteQuery.cs:44-48`) | Verified (code) |
| `GET /customer/delivery-slots` | Brand scope | No brand predicate in the handler despite a comment claiming one; relies on RLS (`DeliverySlotQueries.cs:49-53`) → SA-MOB-020 | Partially Verified |
| Admin pickup assign / delivery assign | Cross-brand rider/order | Rider, order and pickup brand-checked; store scope checked (`PickupCommands.cs:207-218`, `DeliveryAssignmentCommands.cs:37-86`) | Verified (code) |
| DB defence in depth | RLS on logistics tables | `rls_brand` policy on riders and rider_location_pings (`db/patches/rls_enable_logistics.sql:29-69`); application of the patch not verified | Partially Verified |
| Token storage | Mobile | Keychain/Keystore via secure-store on both apps (`customer-mobile/src/lib/tokenStorage.ts:16-50`, `rider-mobile/src/store/authStore.ts:18-79`) | Verified (code) |
| Location retention (DPDP) | 14-day retention claim | Depends on pg_partman retention (setup is wrapped in `EXCEPTION WHEN OTHERS`), while partitions are created by a custom function that never drops them → SA-MOB-012 | Partially Verified |
| Rider access to customer PII after completion | Data minimisation | Historical tasks return name, phone and address indefinitely → SA-MOB-019 | Verified (code) |
| OTP brute force | Attempt limits | None (`OtpAttemptedAt` only stamped), 4-character codes, gateway limit 300/min/IP (`appsettings.json:26-29`) → part of SA-MOB-003 | Verified (code) |

## Findings

### SA-MOB-001 — Rider task status endpoint has no state machine: any leg can be moved to any status, reviving cancelled legs and forcing cancelled orders to "delivered"
- **Category:** Workflow integrity / authorization (business-logic).
- **Severity:** High.
- **Status:** Verified (code read end-to-end; not executed).
- **Evidence:** `backend/laundryghar/operations.Application/Logistics/RiderSelf/Commands/UpdateMyTaskStatus/UpdateMyTaskStatus.cs`:
  - `L34-42` checks only membership in `["started","arrived","collected","completed","failed"]`;
  - `L96` `da.Status = cmd.Status;` runs without reading the current `da.Status`;
  - `L148-257` delivery completion sets `o.Status = "delivered"`, hardcodes `FromStatus = "out_for_delivery"`, adds a COD `Payment` and an outbox event when `o.DeliveredAt == null`, without checking the order is `out_for_delivery` or not cancelled;
  - `L263-264` calls `RiderLoad.DecrementAsync` on every completed/failed call.
- **Supporting evidence:** `rider-mobile/src/store/offlineQueueStore.ts:9-11` claims the handler "guards against invalid transitions". The code contradicts this.
- **Observed behaviour:**
  - A rider can PATCH `completed` on a leg that was `cancelled` (customer cancel at `CustomerPickupCommands.cs:395-407`) or `failed`.
  - A rider can PATCH `started` on a completed leg.
  - A rider can complete a delivery leg of a cancelled order, which turns the order delivered and records COD cash.
  - Each repeated completed/failed decrements `riders.current_load`, which drives auto-dispatch capacity (`AutoDispatchService.cs:149`).
  - The rider app's offline replay (`useOfflineQueueFlush.ts:42-58`) triggers these paths naturally after a cancellation.
- **Reproduction / verification method:** Code trace. Integration test idea: create an order with a delivery leg, cancel the order, PATCH `completed` → expect 400. Today the code path yields 200 and `orders.status='delivered'`.
- **Impact:** Corrupt order lifecycle and finance (phantom COD payments, wrong `amount_paid`/`payment_status`), and load counter drift leading to over-assignment. It also undermines admin/customer consistency.
- **Recommended remediation (smallest safe change):** Add a per-leg transition table in the handler, for example: `assigned|accepted → started → arrived → (collected) → completed|failed`; terminal = `completed|failed|cancelled|expired|rejected`. Then:
  - return Conflict for illegal transitions and a 200 no-op for same-status repeats, decrementing load only on the first terminal entry;
  - before delivery completion, require the order's strategy to allow `→ delivered` from its current status;
  - use a conditional `UPDATE … WHERE status = @expected` (or an `xmin` concurrency token).
- **Regression tests required:** cancelled→completed rejected; completed→started rejected; double completed does not change load twice; completing a delivery leg of a cancelled order rejected.
- **Dependencies / priority:** P0.
- **Prior-doc cross-ref:** none found.

### SA-MOB-002 — One job can be held by two riders: no uniqueness, locking or status check on assignment creation; no reassignment semantics
- **Category:** Concurrency / dispatch.
- **Severity:** High.
- **Status:** Verified for the manual paths (deterministic). Suspected for the races (not reproduced).
- **Evidence:**
  - `operations.Application/Orders/Pickup/Commands/PickupCommands.cs:204-279` (`AssignPickupCommand`) never checks `pr.Status == "pending"` or any existing live assignment. It always inserts a new `DeliveryAssignment` and increments load.
  - `operations.Application/Orders/Delivery/Commands/DeliveryAssignmentCommands.cs:29-107` behaves the same for any order/pickup/leg.
  - `database_scripts/04_bc4_order_lifecycle.sql:332-335` has only non-unique indexes.
  - `commerce.Infrastructure/Worker/Services/AutoDispatchService.cs:313-315,396-400` re-checks then inserts with no lock.
  - `operations.Application/Logistics/RiderSelf/Commands/OfferActions/OfferActions.cs:59-71` runs its "taken" check before and outside `ExecuteInTransactionAsync` (`:73-93`).
  - `AutoDispatchService.cs:461-477` runs an expiry sweep that loads offered rows and overwrites them as `expired` with no concurrency token. This can race a concurrent accept, leaving the pickup `assigned` with an expired leg.
  - Admin UI hides assign unless `pending` (`admin-web/src/pages/orders/PickupDetailDrawer.tsx:26-27`). This is UI hiding only.
- **Observed behaviour:**
  - Calling admin assign twice (or assign after auto-dispatch) yields two active legs for one pickup. The first rider's leg is neither cancelled nor its load released.
  - Auto-dispatch plus manual assign, or two worker replicas, can double-insert.
- **Reproduction / verification method:** Code trace. Integration test: two sequential `POST /admin/pickup-requests/{id}/assign` → expect the second to fail. Today both succeed.
- **Impact:** Two riders dispatched to one customer, duplicate payouts/COD, and load drift.
- **Recommended remediation:**
  1. Add a partial unique index `ON delivery_assignments(pickup_request_id) WHERE status IN ('offered','assigned','accepted','started','arrived') AND pickup_request_id IS NOT NULL`, plus `(order_id, leg_type)` with the same predicate.
  2. In handlers, check `pr.Status == 'pending'` and map 23505 to 409.
  3. Add an explicit reassign command that cancels the old leg and adjusts load in one transaction.
  4. Make accept/expire conditional updates (`UPDATE … SET status='accepted' WHERE id=@id AND status='offered' AND offer_expires_at > now()`).
- **Regression tests required:** concurrent assign (Testcontainers), assign-after-assign 409, accept-vs-expire race, reassign moves load.
- **Dependencies / priority:** P0. Depends on SA-MOB-001 for status vocabulary.

### SA-MOB-003 — Proof-of-delivery OTP is never generated, so the OTP gate is inert; verification has no attempt limit
- **Category:** Security / workflow.
- **Severity:** High.
- **Status:** Verified (repo-wide search for writers of `DeliveryOtp`/`PickupOtp` / `delivery_otp`; DB triggers searched).
- **Evidence:**
  - The only writes in the codebase are the partner-dispatch `PickupOtp = req.PickupOtp` (`PartnerDispatch/Commands/AssignPartnerDispatch/AssignPartnerDispatch.cs:67`).
  - Order OTP is read at `UpdateMyTaskStatus.cs:90-94`, `VerifyTaskOtp.cs:39-48` and `RiderTaskMapper.cs:93-94`.
  - The DDL `04_bc4_order_lifecycle.sql:44-45` has no default.
  - `VerifyTaskOtp.cs:43-59` only stamps `OtpAttemptedAt`, with no counter or lockout.
  - The customer sees OTP only when it exists (`CreateOrderCommand.cs:841-844`).
- **Observed behaviour:** `requiresOtp` is false for all app-created orders. Riders complete deliveries without any customer handshake, and the photo is optional. If OTPs were seeded, there is no brute-force protection.
- **Reproduction / verification method:** Code search (`grep -rn "DeliveryOtp *=\|PickupOtp *="`).
- **Impact:** No proof of handover, which is a dispute and fraud vector for COD/high-value items. The "OTP-verified delivery" product claim is not met.
- **Recommended remediation:**
  - Generate CSPRNG 4–6 digit pickup/delivery OTPs when the order enters `pickup_scheduled`/`out_for_delivery` (strategy transition effect).
  - Add `otp_attempts` with a lockout after N attempts per leg.
  - Expose the OTP to the customer by push/SMS.
- **Regression tests required:** order out_for_delivery has OTP; complete without verify → 400; 6th wrong attempt → locked.
- **Dependencies / priority:** P1.

### SA-MOB-004 — No geocoding or coordinate capture anywhere: geofence, distance-aware dispatch, coordinate navigation and parcel fare quote are inert or broken
- **Category:** Maps / location infrastructure.
- **Severity:** High (it blocks the logistics vertical).
- **Status:** Verified.
- **Evidence:**
  - The only `CreatePoint` calls are `BatchLocationPing.cs:53,85`.
  - No assignment to `GeoLocation` exists for `CustomerAddress`, `Store`, `Warehouse` or `DeliveryAssignment` (grep of `GeoLocation *=`; entities in `laundryghar.SharedDataModel/Entities/*`).
  - The customer address request has no lat/lng (`customer-mobile/src/types/api.ts:235-254`), and the customer app has no location dependency.
  - `GetFareQuoteQuery.cs:57-59` throws `BusinessRuleException` when either point is null.
  - `GeofenceEvaluator.cs:83,100` uses `leg.GeoLocation` and the store points.
  - `AutoDispatchService.cs:127,215-216` handles null coordinates, which ranks without distance.
  - In `rider-mobile/app/(app)/tasks/[id].tsx:489-505` the coordinates branch needs `task.lat/lng`.
- **Observed behaviour:**
  - The parcel flow fails at quote for every customer.
  - Geofence auto-arrive and store-drop never fire.
  - Dispatch is load-only.
  - Rider directions fall back to text search.
  - Only seed data (`db/patches/seed_rider_ops_demo.sql:65`) has coordinates, so demos look functional.
- **Reproduction / verification method:** Code search plus trace.
- **Impact:** Point-to-point/courier is not operable, distance-based payouts and fares are meaningless, and the "auto status on arrival" claim is false.
- **Recommended remediation (smallest):**
  1. Add optional `latitude/longitude` to the create/update address DTO and persist them as a Point.
  2. In customer-mobile, add an `expo-location` "use my current location" button and/or a pin-drop on `react-native-maps` (dev-build/config plugin required).
  3. Add a server-side `IGeocoder` port with one provider adapter, called on address save when lat/lng are absent. Store the provider, accuracy and timestamp.
  4. Copy address geo into `delivery_assignments.geo_location` at assignment time.
  5. Allow stores to be pinned in admin.
- **Regression tests required:** address saved with coordinates → quote succeeds; leg created copies geo; geofence flips within 150 m.
- **Dependencies / priority:** P0 for the logistics vertical; P1 for laundry.

### SA-MOB-005 — Customer can attach another customer's address to a pickup request (IDOR); the rider is dispatched there with that address's phone
- **Category:** Authorization / IDOR / privacy.
- **Severity:** Medium.
- **Status:** Partially Verified (code read; not reproduced).
- **Evidence:**
  - `operations.Application/Orders/Pickup/Commands/PickupCommands.cs:52-103` sets `AddressId = req.AddressId` with no lookup. Neither `CustomerSchedulePickupHandler` (`:336-470`) nor the endpoint (`CustomerOrderEndpoints.cs` SchedulePickup) validates ownership.
  - `database_scripts/04_bc4_order_lifecycle.sql:262` declares `address_id UUID NOT NULL REFERENCES customer_addresses(id)`.
  - The rider task resolves the address from `pr.AddressId` (`GetMyTasksToday/*.cs:73-101`) and shows `CustomerPhone: addr?.RecipientPhone ?? c?.PhoneE164` (`RiderTaskMapper.cs:128`).
- **Observed behaviour:** With a guessed or leaked address UUID, a customer can make a pickup appear at a victim's address. The rider calls the victim (address recipient phone).
- **Reproduction / verification method:** POST a pickup with a foreign `addressId` (UUIDs are random, which limits exploitation to leaked IDs).
- **Impact:** Harassment/abuse vector, wrong-address dispatch, and possible cross-brand reference.
- **Recommended remediation:** In `CustomerSchedulePickupHandler`, verify `CustomerAddresses.Any(a => a.Id == req.AddressId && a.CustomerId == cmd.CustomerId && a.BrandId == cmd.BrandId && a.DeletedAt == null)` and return 404 otherwise. Apply the same in admin create (address must belong to `req.CustomerId`).
- **Regression tests required:** foreign addressId → 404; own address → 201.
- **Dependencies / priority:** P1.

### SA-MOB-006 — Offer→accept dispatch mode is not wired end-to-end (no rider UI, offers hidden from the task list, no notification)
- **Category:** Feature completeness / dispatch.
- **Severity:** Medium.
- **Status:** Verified.
- **Evidence:**
  - The endpoints exist (`RiderSelfEndpoints.cs:64-65`).
  - The rider app has no call to `/accept` or `/decline` (grep of `rider-mobile/src`, `rider-mobile/app`).
  - `RiderTaskMapper.cs:11-12` `OpenStatuses = ["assigned","accepted","started","arrived"]` excludes `offered`.
  - The `assignment.offered` outbox event (`AutoDispatchService.cs:445`) has no consumer (grep).
  - The mode can be enabled by a platform admin (`db/patches/dispatch_offer_states.sql`, `dispatch_permissions.sql`, `DispatchConfig.cs:160-170`).
- **Observed behaviour:** Enabling `offer_accept` means every offer expires unseen and pickups fall back to push after `MaxOfferRounds`, with added latency.
- **Reproduction / verification method:** Code trace.
- **Impact:** Configurable but non-functional dispatch mode, and misleading platform settings.
- **Recommended remediation:**
  - Either block enabling `offer_accept` until it ships, or add: an offer card in rider home/tasks (include `offered` in a separate query), accept/decline calls, and push on `assignment.offered`.
  - In both cases fix the race in SA-MOB-002.
- **Regression tests required:** rider sees the offer; accept → task appears; decline → re-offered.
- **Dependencies / priority:** P2.

### SA-MOB-007 — Riders get no push notification for new or changed assignments; the app learns of work only by 30 s polling while foregrounded
- **Category:** Real-time / notifications.
- **Severity:** Medium.
- **Status:** Verified (code search).
- **Evidence:**
  - Riders register push tokens (`RiderSelfEndpoints.cs:58,147-156`; `RiderPushToken.cs:32-62`).
  - No notification is enqueued for a rider recipient on assign/auto-assign/cancel (grep of `RecipientType` "rider"; `AutoDispatchService.cs:354-371` writes an outbox event with no consumer; `PickupCommands.cs:259-264` writes no notification).
  - Polling: `rider-mobile/src/hooks/useRiderTasks.ts:57`.
- **Observed behaviour:** A newly assigned or cancelled job is invisible until the rider opens the app.
- **Impact:** Slow pickups and missed cancellations (compounds SA-MOB-001).
- **Recommended remediation:** Map `assignment.auto_assigned`, manual assign and leg cancellation to a notification for the rider's push tokens through the existing `ExpoPushChannelSender`.
- **Regression tests required:** assign → notification row for the rider recipient.
- **Dependencies / priority:** P1. Depends on SA-MOB-017 (FCM config) for Android delivery.

### SA-MOB-008 — Rider offline queue treats server rejections as "offline", poisons itself, and drops failure reasons; location pings are not queued
- **Category:** Mobile resilience / data integrity.
- **Severity:** Medium.
- **Status:** Verified (code).
- **Evidence:**
  - `rider-mobile/app/(app)/tasks/[id].tsx:360-406`: any error, including `ApiError("Incorrect OTP.")` (400 from `VerifyTaskOtp.cs:59`), goes to `enqueue({status:'completed'})` with the message "No connection right now". The same pattern appears in `markArrived` (`:346-355`).
  - `rider-mobile/src/hooks/useOfflineQueueFlush.ts:42-58` replays a `failed` item via `updateTaskStatus` (reason/note discarded) and `break`s on the first error, so a permanently rejected item blocks every later item forever.
  - `rider-mobile/src/lib/backgroundLocation.ts:49-51` and `sendCurrentLocation.ts:43-45` drop pings when offline.
- **Observed behaviour:** A wrong OTP shows "offline" and queues a completion that the server will always reject (once OTPs exist). After that, every later queued update stalls. Failure reasons are lost.
- **Impact:** Silent data loss, riders believing tasks are complete when they are not, and gaps in tracking history.
- **Recommended remediation:**
  - Enqueue only on network or 5xx errors (`!error.response || status >= 500`).
  - On 4xx, show the server message and drop or mark the item failed.
  - Replay `failed` via `failTaskStatus`.
  - Continue past permanently failed items.
  - Optionally buffer the last N pings.
- **Regression tests required:** jest: 400 not enqueued; poison item skipped; failed replay carries reason.
- **Dependencies / priority:** P1.

### SA-MOB-009 — Background location task may run without hydrated auth and trigger `logout()`, wiping stored tokens
- **Category:** Mobile auth / background execution.
- **Severity:** Medium.
- **Status:** Suspected (device behaviour not observed).
- **Evidence:**
  - `rider-mobile/src/lib/backgroundLocation.ts:30-52`: the task posts through `logisticsClient`.
  - `rider-mobile/src/api/client.ts:136-186`: no token means no Authorization header, then 401, then `_getRefreshToken()` returns null, then `throw`, then `_onAuthFailure()`.
  - `rider-mobile/src/store/authStore.ts:54-70,84-89`: `logout()` clears state and **deletes the SecureStore tokens**.
  - Tokens are hydrated only inside the root component effect (`rider-mobile/app/_layout.tsx:268-279`, `void hydrate()` at `:275`), which does not run when the OS launches JS headlessly for a background location event.
- **Observed behaviour (expected):** After the OS kills the app during a shift, the next background location delivery can sign the rider out silently.
- **Reproduction / verification method:** Android dev build: go on duty, swipe the app away, move more than 30 m, reopen. Expect the login screen.
- **Impact:** Riders silently logged out mid-shift, and tracking stops.
- **Recommended remediation:** In the task, read tokens directly from SecureStore if the store is not hydrated, and never call `onAuthFailure` from a headless context (skip the ping on 401).
- **Regression tests required:** jest unit with an unhydrated store → no logout.
- **Dependencies / priority:** P2.

### SA-MOB-010 — Location ping ingestion is unvalidated and not gated by duty/assignment; client timestamps drive staleness and partition routing
- **Category:** Input validation / privacy / integrity.
- **Severity:** Medium.
- **Status:** Verified (code).
- **Evidence:**
  - `operations.Application/Logistics/RiderSelf/Dtos/RiderSelfDtos.cs:6-16` has no validators (no validator exists for `LocationPingInput`).
  - `BatchLocationPing.cs:42-47` resolves the rider without checking `IsOnDuty`/`Status`, takes `PingedAt = p.PingedAt` (client time) at `:56`, and sets `LastPingAt = latest.PingedAt` at `:87`.
  - `RiderSelfEndpoints.cs:126-145` accepts an unbounded `List<LocationPingInput>`.
  - `GeofenceEvaluator.cs:24-25` uses a 150 m radius with no accuracy filter.
- **Observed behaviour:**
  - Lat/lng out of range or NaN reach NTS/PostGIS, which likely surfaces as a 500.
  - Future or past timestamps fall into the DEFAULT partition or fail when no partition exists.
  - A device with a skewed clock appears fresh or stale on the admin board.
  - Off-duty and terminated riders' locations are stored.
  - Large batches cause heavy inserts.
  - Spoofed coordinates auto-flip legs to `arrived`.
- **Impact:** Privacy (tracking outside shift), DoS surface, misleading live board, and geofence manipulation.
- **Recommended remediation:**
  - Add a FluentValidation validator: lat ∈ [-90,90], lng ∈ [-180,180], batch ≤ 50, `PingedAt` within [now-15m, now+2m] (else clamp to server time), accuracy ≤ 200 m for geofence evaluation.
  - Reject or ignore pings when `!IsOnDuty || Status != active`.
  - Store server receive time in `created_at` and use it for staleness.
- **Regression tests required:** validator unit tests; off-duty ping → 204/ignored.
- **Dependencies / priority:** P1.

### SA-MOB-011 — Deactivating a rider does not stop tracking, task access or open legs
- **Category:** Authorization lifecycle.
- **Severity:** Medium.
- **Status:** Partially Verified (session/refresh-token revocation path not traced; Related area: IAM).
- **Evidence:**
  - `operations.Application/Logistics/Riders/Commands/DeactivateRider/*.cs:40` sets only `rider.Status = Terminated`, leaving `IsOnDuty` and open legs untouched.
  - `laundryghar.Utilities/Auth/RiderOnlyRequirement.cs` checks claims only (`token_use`, `user_type`).
  - Rider-self handlers resolve by `UserId+BrandId` with no status filter (`BatchLocationPing.cs:43-46`, `UpdateMyTaskStatus.cs:44-47`).
- **Observed behaviour:** Until the access token expires (and longer, if refresh is not revoked), a terminated rider can ping, view tasks and complete legs with COD side-effects.
- **Impact:** Ex-partner retains operational access and customer PII.
- **Recommended remediation:**
  - On deactivate: set `IsOnDuty=false`, cancel or flag open legs for reassignment, and revoke refresh tokens.
  - Add `r.Status == "active"` (or not terminated) to the rider self-resolve in all rider-self handlers via one shared helper.
- **Regression tests required:** terminated rider → 403/404 on ping and status.
- **Dependencies / priority:** P1.

### SA-MOB-012 — 14-day location retention is not reliably enforced
- **Category:** Privacy (DPDP) / data lifecycle.
- **Severity:** Medium.
- **Status:** Partially Verified (no DB to run).
- **Evidence:**
  - `database_scripts/99_cross_cutting_schema_qualified.sql:75-91` configures partman retention inside `EXCEPTION WHEN OTHERS … RAISE NOTICE 'skipped'`. pg_partman is not installed in this environment.
  - `db/patches/rider_ping_partition_maintenance.sql:27-126` creates daily partitions (and a DEFAULT dance) but never drops any.
  - `commerce.Infrastructure/Worker/Services/RetentionSweepService.cs:19-21` defers to partman.
  - The only partman scheduler is a macOS launchd note (`ops/backup/README.md:15`, `db/tools/run_partman_maintenance.sh`); nothing exists in `deploy/`.
  - Customer erasure nulls address geo (`CustomerErasureService.cs:177`). No rider-side erasure of pings was found.
- **Observed behaviour:** Unless partman is installed, configured and scheduled in production, pings accumulate indefinitely. Rows in DEFAULT are never purged.
- **Impact:** DPDP purpose/storage-limitation exposure for precise location data.
- **Recommended remediation:** Extend the SECURITY DEFINER function, or add a sibling, to `DROP` partitions older than the configured retention and `DELETE` DEFAULT rows older than retention. Call it from `PartitionMaintenanceService`. Add a rider erasure path.
- **Regression tests required:** integration test: partition older than 14 days is dropped.
- **Dependencies / priority:** P1.
- **Related area:** DB/PRIV.

### SA-MOB-013 — Serviceability, zones and service areas are not enforced anywhere in the booking or dispatch path
- **Category:** Delivery-zone / operating-region control.
- **Severity:** Medium.
- **Status:** Verified (code).
- **Evidence:**
  - `operations.Application/Catalog/Customer/Self/Queries/SelfQueries.cs:89-116` checks pincode equality only.
  - `customer-mobile/src/hooks/useCatalog.ts:172-179` defines `useServiceability`, but it has no caller.
  - `CustomerSchedulePickupHandler` / `CreatePickup` (`PickupCommands.cs:52-120,336-470`) perform no serviceability check.
  - Slots are brand-wide, and the slot's store becomes the pickup store (`PickupCommands.cs:364-373`; `pickup.tsx:202-206`).
  - `RiderRanker` has no maximum distance (`RiderRanker.cs:56-82`).
  - There are no polygon columns or spatial queries despite PostGIS.
- **Observed behaviour:** Customers anywhere can book. The assigned store is whichever store owns the chosen slot, possibly far from the customer, and riders can be auto-assigned regardless of distance.
- **Impact:** Unfulfillable orders and wrong-store routing. A multi-tenant region definition is missing.
- **Recommended remediation:**
  1. Enforce `CheckServiceability(address.pincode)` server-side in pickup and parcel creation.
  2. Resolve the store from the serviceable territory/store before slot selection, and filter slots by that store.
  3. Later, add an optional `service_area GEOGRAPHY(POLYGON)` on stores/territories with `ST_Covers`.
- **Regression tests required:** unserviceable pincode → 422; slots filtered by resolved store.
- **Dependencies / priority:** P1.

### SA-MOB-014 — Customer tracking is a status timeline only, and pickup progress never reflects rider start or arrival
- **Category:** Tracking consistency.
- **Severity:** Medium.
- **Status:** Verified.
- **Evidence:**
  - `GetMyOrderTrackingHandler` returns history only (`OrderQueries.cs:207-232`).
  - The customer tracking screen expects `rider_dispatched`/`arrived` (`customer-mobile/app/(app)/orders/tracking/[id].tsx:57-66`).
  - The constant exists (`SharedDataModel/Enums/PickupRequestStatus.cs:7`), but nothing writes it.
  - `UpdateMyTaskStatus.cs` only advances the pickup request on `completed` (`:135-141`); on `collected` the pickup target is `null` (`:84-85`).
- **Observed behaviour:** The pickup shows `assigned` until the drop at the store. There is no rider location or ETA.
- **Impact:** Customer/rider/admin views diverge, generating support load.
- **Recommended remediation:**
  - Map leg `started→pickup rider_dispatched` and `arrived→arrived` in `UpdateMyTaskStatusHandler` and the geofence.
  - Later, expose a customer-scoped "rider approaching" endpoint, available only while the leg is `started`/`arrived` for the caller's own request, returning a coarse location and rider first name. Stop it on terminal states.
- **Regression tests required:** started leg → pickup status `rider_dispatched`.
- **Dependencies / priority:** P2.

### SA-MOB-015 — Tenant and business type are build-time constants in the mobile apps; vertical does not drive mobile flows or map capabilities, and the backend does not gate vertical-specific endpoints
- **Category:** Multi-tenant / multi-vertical configuration.
- **Severity:** Medium.
- **Status:** Verified.
- **Related area:** VERT, ONB.
- **Evidence:**
  - `customer-mobile/app.config.ts:10,19,27,57` hardcodes name and bundle/package and takes the brand code from env. `rider-mobile/app.config.ts:10,19,39,81` does the same.
  - `customer-mobile/app/(app)/(tabs)/_layout.tsx:160-177` shows both laundry and parcel.
  - `CustomerOrderEndpoints.cs:41,60` has no entitlement or vertical gate on `/orders/parcel` and `/fare/quote`.
  - `db/migrations/0011_vertical_templates.up.sql` has no location capability fields.
  - Maps settings are admin-only (`core.Application/Identity/Settings/Commands/UpdateMaps/UpdateMaps.cs:52`).
- **Observed behaviour:**
  - Each tenant needs its own binary build with code edits to the name and bundle ID.
  - A laundry tenant's customers can create parcel orders.
  - A salon tenant gets laundry/parcel UI.
  - Map behaviour cannot vary per tenant.
- **Impact:** Not SaaS-ready for multiple verticals on mobile. The UI is the only boundary, and the UI isn't even hiding the flows.
- **Recommended remediation:**
  - Parameterise `app.config.ts` from a per-tenant JSON (name, slug, bundle ID, icon, brand code) used by EAS build profiles, rather than an app per tenant in code.
  - Have the app fetch `GET /app-config` at boot, carrying `verticalKey`, `fulfillmentModes[]` and `capabilities{addressGeo, parcel, liveTracking, storeLocator}`.
  - Enforce the same capabilities server-side through the existing entitlement policy on parcel, fare and pickup endpoints.
- **Regression tests required:** laundry-only brand → `/orders/parcel` 403; app hides the parcel entry.
- **Dependencies / priority:** P1.

### SA-MOB-016 — Customer order cancellation does not cancel or release the order's delivery legs
- **Category:** Workflow propagation.
- **Severity:** Medium.
- **Status:** Partially Verified (code; which states carry legs depends on the vertical).
- **Evidence:**
  - `operations.Application/Orders/Orders/Commands/CancelOrderByCustomerCommand.cs:35-111` updates the order, history, outbox and refund only.
  - Compare `CustomerPickupCommands.cs:395-407`, which cancels legs and decrements load.
  - Cancellable statuses: `StateMachineStrategyBase.cs:62-63` (`placed`, `pickup_scheduled`).
- **Observed behaviour:** For orders, including parcel orders, that already have a leg, the rider keeps the job. Combined with SA-MOB-001, the rider can still "complete" it.
- **Impact:** Wasted trips and phantom deliveries.
- **Recommended remediation:** Reuse the pickup-cancel block: cancel active legs by `order_id`, decrement load, and notify the rider (SA-MOB-007).
- **Regression tests required:** cancel order → legs cancelled.
- **Dependencies / priority:** P2.

### SA-MOB-017 — Mobile release/CI readiness: rider `npm ci` fails, both typechecks fail, EAS/OTA/submit and FCM are unconfigured
- **Category:** Build / release.
- **Severity:** Medium.
- **Status:** Verified (run locally on Node 22.22 / npm 10.9.4).
- **Related area:** DEVOPS.
- **Evidence:**
  - `.github/workflows/ci.yml:65-86` runs `npm ci` and `npm run typecheck`.
  - Locally, `rider-mobile` `npm ci` fails with `ERESOLVE` (react-dom@19.2.8 peer react@^19.2.8 versus root react@19.2.3).
  - `tsc --noEmit` in both apps fails at `app/_layout.tsx:11` (TS2882 `../global.css`).
  - `app.config.ts:6` in both apps sets EAS projectId to the slug (the OTA URL 404s; comment at `:3-5`).
  - `eas.json:48-59` has empty submit credentials.
  - The customer Firebase files do not match the app IDs (`google-services.json` package `com.launddryghar.app`, plist `com.laundrygahar.ios`) and are not referenced by `app.config.ts`.
  - The rider app has no FCM file.
  - The rider dev gateway port is 8080 (`rider-mobile/src/constants/config.ts:19`) while the AppHost binds 5300 (`laundryghar.AppHost/AppHost.cs:40-53`).
- **Observed behaviour:** The CI mobile job fails as committed (inferred from local reproduction). Android push in standalone builds will not work. OTA updates are not deliverable.
- **Impact:** No green pipeline and no store-ready binaries.
- **Recommended remediation:**
  - Align React to 19.2.8 (or pin react-dom) and regenerate the lockfiles.
  - Add `declare module '*.css'` to `nativewind-env.d.ts`.
  - Run `eas project:init`.
  - Wire `android.googleServicesFile`/`ios.googleServicesFile` with correct IDs per app (and per tenant build).
  - Fix the rider dev port.
- **Regression tests required:** CI green on the mobile matrix.
- **Dependencies / priority:** P1.

### SA-MOB-018 — Customer app creates pickups without an Idempotency-Key despite server support
- **Category:** Retry safety.
- **Severity:** Low.
- **Status:** Verified.
- **Evidence:**
  - `customer-mobile/src/api/orders.ts:129-137` sends no header.
  - `app/(app)/booking/pay.tsx:390-405` does not pass a key.
  - The server supports both the header and the body key (`CustomerOrderEndpoints.cs` SchedulePickup; `PickupCommands.cs:343-356`).
- **Observed behaviour:** A 15 s timeout followed by a re-tap creates duplicate pickups and slot bookings.
- **Recommended remediation:** Generate a UUID per checkout session (bookingStore) and send `Idempotency-Key`.
- **Regression tests required:** jest: header sent; repeated submit reuses the key.
- **Dependencies / priority:** P2.

### SA-MOB-019 — Riders retain indefinite access to customer PII for historical tasks
- **Category:** Privacy / data minimisation.
- **Severity:** Low.
- **Status:** Verified (code).
- **Evidence:** `GET /rider/tasks?date=` (`RiderSelfEndpoints.cs:84,307-319`) maps through `RiderTaskMapper` with `CustomerName`, `CustomerPhone` (`RiderTaskMapper.cs:120-130`) and the address line for any past date.
- **Impact:** Ex-customer contact details are available to partners long after service, contrary to DPDP minimisation.
- **Recommended remediation:** Mask phone and address for completed or failed legs older than N hours in `RiderTaskMapper` (earnings needs only order number, amount and time).
- **Regression tests required:** past-date task DTO has masked PII.
- **Dependencies / priority:** P2.

### SA-MOB-020 — Customer slot listing has no in-handler brand predicate (RLS-only), contrary to its comment
- **Category:** Tenant isolation (defence in depth).
- **Severity:** Low.
- **Status:** Partially Verified (RLS application not verified).
- **Related area:** TEN.
- **Evidence:**
  - `operations.Application/Orders/Delivery/Queries/DeliverySlotQueries.cs:49-53`: the comment claims "explicit brand predicate is the in-handler defense-in-depth", but the query has none.
  - The endpoint passes no brand (`CustomerOrderEndpoints.cs:292-296`).
- **Impact:** If RLS is not active for this connection, customers see other brands' store slots and IDs.
- **Recommended remediation:** Pass `u.BrandId` and filter `s.BrandId == brandId`.
- **Regression tests required:** cross-brand slot not returned.
- **Dependencies / priority:** P2.

### SA-MOB-021 — Map provider keys stored unencrypted and echoed to every settings reader
- **Category:** Secrets handling.
- **Severity:** Low.
- **Status:** Verified (code).
- **Evidence:** `core.Application/Identity/Settings/Commands/UpdateMaps/UpdateMaps.cs:52` (`isEncrypted: false`); `GetAdminSettings.cs:44` returns `GoogleApiKey`/`MapboxToken` (Read permission at `AdminSettings.cs:50`).
- **Impact:** These are browser keys, intrinsically public once used, but without HTTP-referrer or URL restrictions (not verifiable from the repo) they can be abused for quota and billing.
- **Recommended remediation:** Document that keys must be referrer-restricted, and mask them in the GET except for `settings.manage`.
- **Dependencies / priority:** P3.

## Positive controls verified
- **Rider self-service IDOR guards.** Every rider-self command and query filters by the JWT-derived rider (`UserId`→`Riders.Id`) and `BrandId` (`UpdateMyTaskStatus.cs:44-53`, `VerifyTaskOtp.cs:23-32`, `OfferActions.cs:36-49,118-131`, `BatchLocationPing.cs:43-47`). There are no rider IDs in paths for self data (`RiderSelfEndpoints.cs:29-42`).
- **Server-side OTP comparison.** The code never leaves the server, and the customer sees it only while out_for_delivery (`VerifyTaskOtp.cs:9-59`; `CreateOrderCommand.cs:841-844`).
- **Delivery-completion side effects** are transactional and gated on first completion; the COD payment has a re-call guard (`UpdateMyTaskStatus.cs:152-260`).
- **Slot capacity** is atomic with an idempotency unique-index fallback that rolls back the capacity increment (`PickupCommands.cs:376-470`).
- **Pickup cancellation** propagates to legs, slot and rider load in one transaction (`CustomerPickupCommands.cs:345-427`).
- **Auto-dispatch eligibility** includes KYC verified + vehicle approved + capacity, with franchise preference, and only a platform row may enable offer mode (`AutoDispatchService.cs:143-162`; `DispatchConfig.cs:160-170`).
- **Admin live board** is brand+franchise scoped with a 10-min stale indicator and derives load from open legs rather than the counter (`GetRidersLive.cs:22-104`). The track query is capped at 1,500 points and brand+franchise scoped (`GetRiderTrack.cs:20-48`).
- **Rider background location** is implemented correctly for the managed workflow: config plugin, iOS usage strings, Android foreground-service notification, task defined at module load, stop on off-duty/sign-out, and foreground fallback (`rider-mobile/app.config.ts:21-55,117-126`; `backgroundLocation.ts`; `useLocationTracking.ts`; `profile.tsx:66`).
- **Tokens** are in the Keychain/Keystore on both apps. Refresh is coalesced and the auth-call 401 loop is avoided (`client.ts:155-186`).
- **Partition auto-provisioning** for pings, via a SECURITY DEFINER function plus a worker, avoids the earlier "no partition" 400s (`rider_ping_partition_maintenance.sql`, `PartitionMaintenanceService.cs`).
- **The admin map provider abstraction** degrades to key-less OSM and respects the Google ToS (no Google tiles in Leaflet) (`mapConfig.ts:1-72`).
- **Mobile unit tests** pass locally: customer 170/170, rider 91/91.

## Open questions / not verified
- Whether `rls_enable_logistics.sql`, `dispatch_offer_states.sql` and `rider_ping_partition_maintenance.sql` are applied by `db/build_from_scratch.sh` / the migration runner in production.
- Whether refresh tokens are revoked when a rider is deactivated or a user disabled (IAM specialist).
- Whether a DEFAULT partition exists for `rider_location_pings` in production, and whether partman v5 retention will drop function-created partitions.
- Device behaviour of the headless background task (SA-MOB-009), iOS "Always" permission flow, and App Store review of background location justification.
- `GetProofPhotoStream` handler scope and storage location of proof/KYC photos (Related area: storage/privacy).
- Rider earnings/payout and COD settlement correctness (Related area: FIN).
- Whether any external process (POS, MCP, partner API) sets address coordinates outside the code paths searched.

## Recommended target architecture (fits the existing modular monolith)

The proposal keeps the current services (core / operations / commerce + worker). There is no new microservice, no app-per-tenant codebase and no plugin framework.

1. **Shared location infrastructure (operations.Application/Location, new folder).**
   - Add an `IGeocoder` port with one adapter (Google *or* an OSM/Nominatim-compatible provider), behind per-brand settings that already exist (`maps/provider`).
   - Add `GeoPoint` value handling; add `ServiceAreaResolver` (pincode now, polygon later via PostGIS `ST_Covers`).
   - Add `DistanceService` (haversine now; a provider distance matrix behind the same interface later).
   - All coordinate writes go through this module.
2. **Tenant location configuration.** Reuse `kernel.system_settings` for the provider and keys. Extend `territories`/`stores` with optional `service_area` polygons and an `operating_hours` link. Add a `brand capabilities` projection: vertical template defaults merged with entitlement add-ons. It is served by one `GET /app-config` and enforced by the existing permission/entitlement policies on endpoints.
3. **Vertical-specific map workflows** live with the fulfilment strategies already in `operations.Application/Fulfillment/*`. Each strategy declares its required location capabilities, for example `RequiresAddressGeo`, `RequiresStoreDrop` (already present) and `SupportsLiveTracking`. Endpoints check the capability instead of `if (vertical == …)`.
4. **Customer vs partner experiences.** These remain two Expo apps, with tenant branding injected at build from per-tenant JSON via EAS profiles, and behaviour from `/app-config` at runtime. Customer:
   - address pin/geocode;
   - serviceability gate;
   - capability-driven flows;
   - an optional "rider approaching" card.

   Rider:
   - offer card;
   - push;
   - deep-link navigation (in-app map optional).
5. **Backend assignment/tracking services** are refactored inside operations:
   - an `AssignmentService` (create/reassign/cancel with unique-index protection, status state machine, load accounting);
   - a `TrackingService` (validated ping ingestion, duty/assignment gating, retention, customer-scoped tracking view).

   AutoDispatch keeps running in the worker but calls the same `AssignmentService` rules (shared static helpers, as `RiderLoad` already is).
6. **ABAC/RBAC policies**, as server-side checks:
   - **Customer address:** `address.customer_id == sub && address.brand_id == jwt.brand`, on every write and reference (pickup, parcel, quote).
   - **Partner location write:** `rider.user_id == sub && rider.brand_id == jwt.brand && rider.status == active && rider.is_on_duty`.
   - **Partner location read:** admin with `rider.read` within brand → franchise → **store** scope; customer only for their own active leg (`started|arrived`), coarse, and never after a terminal state.
   - **Assignment mutation:** rider may only transition their own leg along the state machine. Admin `pickup.assign`/`delivery.assign` within scope, with the target rider in the same brand/franchise and active plus KYC verified.
   - **Tracking history:** admin within scope and ≤ the retention window. Riders see their own history with customer PII masked after completion.

## 8. Implementation roadmap

| # | Item (finding IDs) | Depends on | Acceptance criteria | Tests | Complexity |
|---|---|---|---|---|---|
| R1 | Leg state machine + idempotent terminal handling + order-status guard on completion (SA-MOB-001, SA-MOB-016) | — | Illegal transitions → 400; repeats are no-ops; load changes once; cancelled order cannot become delivered; order cancel cancels legs | Unit (transition table), integration (cancel→complete) | M |
| R2 | Assignment uniqueness and reassign (SA-MOB-002) | R1 | Partial unique indexes; 409 on duplicate; reassign cancels the old leg atomically; accept/expire via conditional UPDATE | Testcontainers concurrency tests | M |
| R3 | Address ownership check (SA-MOB-005) | — | Foreign address → 404 on pickup/parcel/admin create | Integration | S |
| R4 | Coordinate capture + geocoder port + copy geo to legs + store pinning (SA-MOB-004) | — | Address save stores a point (client or geocoder); quote works; geofence fires; dispatch ranks by distance | Unit (geocoder fake), integration | L |
| R5 | Serviceability enforcement + store resolution before slots (SA-MOB-013) | R4 (optional for polygons) | Unserviceable → 422; slots filtered to the resolved store; dispatch max radius configurable | Integration | M |
| R6 | OTP generation + attempt lockout (SA-MOB-003) | R1 | OTP present at out_for_delivery/pickup_scheduled; lockout after N attempts; customer receives the OTP | Unit + integration | M |
| R7 | Ping validation, duty/status gating, server-time staleness (SA-MOB-010, SA-MOB-011) | — | Validator rejects bad input; off-duty/terminated pings ignored; deactivate turns duty off and flags legs | Unit + integration | S–M |
| R8 | Retention enforcement + rider erasure (SA-MOB-012) | — | Partitions older than 14 days dropped by the worker; DEFAULT purged | Integration (PG) | S |
| R9 | Rider offline queue fixes + headless auth safety (SA-MOB-008, SA-MOB-009) | — | 4xx not queued; poison skipped; reasons kept; no logout from background | Jest | S |
| R10 | Rider push for assign/cancel + offer UI (or disable offer mode) (SA-MOB-006, SA-MOB-007) | R2, R11 | Rider is notified within seconds; offers visible and actionable | Integration + manual device | M |
| R11 | Mobile CI/release config (SA-MOB-017, SA-MOB-018) | — | CI mobile matrix green; EAS project IDs; FCM wired; Idempotency-Key sent | CI | S |
| R12 | Capability-driven app config + server-side vertical/entitlement gates + per-tenant build config (SA-MOB-015) | R4 | `/app-config` returns capabilities; parcel endpoints 403 when not entitled; app hides flows; tenant JSON drives `app.config.ts` | Integration + jest | L |
| R13 | Customer pickup progress + optional "rider approaching" view (SA-MOB-014) | R1, R7 | Pickup shows dispatched/arrived; coarse rider location only during the active own leg; stops at terminal | Integration (IDOR tests) | M |
| R14 | PII masking for historical rider tasks; slot brand predicate; map key masking (SA-MOB-019, SA-MOB-020, SA-MOB-021) | — | Masked past tasks; brand-filtered slots; key masked for read-only admins | Unit/integration | S |

## Verdict inputs

| # | Question | Status | Justification |
|---|---|---|---|
| M1 | Functional customer mobile app exists? | Partially Supported | Real API-backed auth, booking, slots, tracking timeline and support. Online payment is "coming soon"; the parcel flow is broken (no geo); not release-configured (SA-MOB-004/017). |
| M2 | Functional delivery-partner app exists? | Partially Supported | Real tasks, status, OTP verify, photos, KYC docs, background GPS and handoff navigation. No offer UI, no push for assignments, offline-queue defects (SA-MOB-006/007/008). |
| M3 | Each app resolves tenant and business type correctly? | Partially Supported | Tenant is a build-time brand code; vertical is used only for terminology and tracking stages, not flows (SA-MOB-015). |
| M4 | Business-appropriate map capabilities configured automatically? | Not Supported | Templates have no location capabilities; per-brand map settings are admin tile provider only. |
| M5 | Different mapping workflows without mixing vertical logic? | Not Supported | There are no vertical mapping workflows. The customer app shows laundry and parcel side by side, and the backend has no capability gates. |
| M6 | Geocoding / address selection / routing / navigation / live tracking individually implemented & verified? | Partially Supported | Geocoding: Not Supported. Map address pick: Not Supported. Routing/ETA: Not Supported. Navigation handoff: Partially (text-search fallback). Live tracking: Partially (rider→admin only, polling; none for customers). |
| M7 | Delivery zones / operating regions / serviceability? | Not Supported | A pincode check exists but is never called or enforced; there are no polygons or radius limits (SA-MOB-013). |
| M8 | Assignment, scheduling and status consistent across customer/partner/admin? | Not Supported | No leg state machine, duplicate assignments possible, pickup progress not propagated, order cancel not propagated (SA-MOB-001/002/014/016). |
| M9 | Customer and partner location data protected by backend authz + tenant isolation? | Partially Supported | Rider self and admin endpoints are brand(+franchise) scoped, and customers cannot read rider location. Gaps: address IDOR, no store-level scope on tracking, terminated riders keep access (SA-MOB-005/011). |
| M10 | Background tracking, permissions, privacy, battery, network failures handled? | Partially Supported | Correct expo-location/TaskManager setup at 25 s / 30 m Balanced. Gaps: headless logout risk, no ping buffering, unvalidated ingestion, retention not enforced (SA-MOB-009/010/012). |
| M11 | Duplicate assignments and repeated status updates prevented under concurrency/retries? | Not Supported | No unique/lock/concurrency token; repeat completes re-decrement load; the offline replay can revive cancelled legs (SA-MOB-001/002). |
| M12 | Minimum changes to deliver the intended customer and partner experiences | — | (1) Leg state machine + assignment unique indexes (R1/R2). (2) Address ownership check (R3). (3) Persist coordinates via a device pin or geocoder and copy them to legs (R4). (4) Enforce serviceability and resolve the store before slots (R5). (5) Generate OTPs with attempt limits (R6). (6) Validate and gate pings and enforce retention (R7/R8). (7) Fix the rider offline queue and headless auth (R9). (8) Rider push on assign/cancel (R10). (9) Green mobile CI, EAS and FCM config (R11). (10) Capability-based `/app-config` with backend entitlement gates for parcel/vertical flows (R12). |
