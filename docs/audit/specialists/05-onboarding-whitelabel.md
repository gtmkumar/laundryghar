# 05 — Onboarding, White-Label and Application Provisioning Specialist

## Scope and method

**What I looked at.** I traced the self-serve provider funnel and white-label surfaces from the HTTP edge to the database:

- Backend (core):
  - `core.WebApi/Endpoints/Identity/{Signup,AdminBrands,AdminBrandDomains,AdminProviderOnboarding,AdminWhiteLabel,AdminSettings}.cs`
  - `core.WebApi/Endpoints/Engagement/PublicEngagement.cs`
  - `core.WebApi/Program.cs` (the pre-auth RLS bypass list)
  - `core.Application/Identity/{Signup,ProviderOnboarding,WhiteLabel,TenancyOrg/Brands,TenancyOrg/BrandDomains,TenancyOrg/Terminology,AccessControl,Entitlements}/**`
  - `core.Application/Identity/Auth/Common/{ScopeResolver,CustomerBrandResolver}.cs`
  - `core.Infrastructure/Services/BrandResolver.cs`
- Backend (shared and other services):
  - `laundryghar.Utilities/{Middlewares/TenantResolutionMiddleware,Caching/OutputCaching,Auth/FeatureNotInPlan}.cs`
  - `laundryghar.ServiceDefaults/Extensions.cs`
  - `laundryghar.Gateway/Program.cs`
  - `operations.Application/Orders/{Orders/Commands/CreateOrderCommand,Invoices/**}`
  - `commerce.Infrastructure/Worker/Services/NotificationMappingService.cs`
- Database:
  - Migrations `db/migrations/0002`, `0003`, `0004`, `0011`, `0017`, `0018`, `0019`, `0032`
  - Patches `db/patches/phase0_multi_vertical.sql`, `rls_proposal.sql`, `_applied_rls_bc1_bc2.sql`, `seed_notification_lifecycle_templates.sql`
  - `database_scripts/01_bc1_tenancy_org.sql`
- Clients:
  - `admin-web/src/{App.tsx,components/layout/Sidebar.tsx,index.css,hooks/useActiveVertical.ts,pages/settings/**}`
  - `customer-mobile/{app.config.ts,eas.json,src/constants/config.ts,src/api/*}`
  - `rider-mobile/app.config.ts`
  - `pos-web/index.html`
- Deploy and tests:
  - `deploy/docker-compose.yml`, `backend/laundryghar/PRODUCTION_ENV.md`
  - Tests under `tests/core.Tests/{Signup,WhiteLabel,Identity}` and `tests/operations.IntegrationTests/Rbac/BrandResolverHostTests.cs`

**Commands run.** All were read-only: `grep`, `sed -n`, `cat -n` and `find` across the repository.

**What I could not verify.**
- No .NET SDK is available, so nothing was built or executed.
- I tried to stand up a throw-away PostgreSQL 16 cluster in my scratch directory to reproduce the `tenancy_org.brands` RLS and `ensure_brand_subdomain` behaviour. It failed: the sandbox denied the `postgres` OS user access to the scratch path, and the socket path exceeded 107 bytes. So no SQL was executed.
- Runtime behaviour that depends on YARP library defaults (the Host header rewrite) or on live RLS is therefore reported from code and config reading, not reproduction. Those findings are labelled `Partially Verified`.

## Current-state summary

### 1. Self-serve signup exists, backend only

The traced path:

1. `GET /api/v1/signup/templates`, `POST /start` and `POST /complete` are anonymous. `/start` and `/complete` carry the `auth` rate limit (`Signup.cs:L26-33`).
2. `/complete` is placed on the pre-auth **blanket** RLS-bypass list (`core.WebApi/Program.cs:L641-646`).
3. `CompleteSignupCommandHandler` then does the following, in order:
   - Checks that the template key is a public `vertical_templates` row (`CompleteSignup.cs:L74-76`).
   - Verifies and consumes the OTP (`L79`, `L243-272`).
   - Refuses a phone number that already has an account (`L84-85`).
   - Inside one transaction (`L97-235`), creates:
     - a `brands` row with `vertical_key = template.VerticalKey`, a generated code, and fixed INR / IN / Asia/Kolkata settings (`L106-129`);
     - an owner `users` row typed `brand_admin` (`L132-156`);
     - a brand-scope `brand_admin` membership (`L163-172`);
     - an "OWN" franchise (`L184-194`);
     - `brand_feature` rows expanded from the template's default bundle, filtered by vertical (`TemplateProvisioner.cs:L53-79`);
     - catalogue categories and items without prices (`L86-143`);
     - a 14-day `trialing` platform subscription, but only if the bundle row exists (`L200-225`).
4. The response carries the brand id and code. **No token is returned**, and **no client calls these endpoints**: there is no signup route in `admin-web/src/App.tsx:L57-116`, and nothing matches `signup/` in any client.

### 2. A provider onboarding wizard exists, backend only

- `GET/POST /api/v1/admin/provider-onboarding{,/draft,/skip,/go-live}` (`AdminProviderOnboarding.cs:L26-34`).
- Step status is **derived** from real rows by the SECURITY DEFINER function `kernel.brand_onboarding_facts` (`0017_onboarding_progress.up.sql`).
- "Go live" calls `kernel.ensure_brand_subdomain`. That inserts `<code>.laundryghar.app` as verified and primary, with `ssl_status='pending'` (`0017:L115-170`; `OnboardingCommands.cs:L148-159`).
- No admin-web page calls `/provider-onboarding`. The admin-web "onboarding" UI is **franchise** onboarding (`admin-web/src/api/onboarding.ts`), which is a different flow.

### 3. Custom domains: schema, verification and resolver exist, but the path is not reachable

Pieces that exist:
- The `brand_domains` table, with TXT verification and global `UNIQUE(domain)` (`0002`).
- `kernel.resolve_brand_domain` (`0003`).
- The admin CRUD and DNS-TXT verification endpoints (`AdminBrandDomains.cs:L32-45`).
- The admin-web `CustomDomainsPanel`.
- `BrandResolver` puts Host first (`BrandResolver.cs:L67-96`).

Why the Host lookup never fires:
- `BrandResolver` is used only by `PublicEngagement` (CMS: slides, app-config, banners).
- The Gateway routes by **path only** (`Gateway/Program.cs:L45-55`). It does not preserve the original Host.
- The services' forwarded-headers middleware accepts only `X-Forwarded-For` and `X-Forwarded-Proto` (`ServiceDefaults/Extensions.cs:L273-283`).
- So behind the Gateway, core sees `Host: core` (`deploy/docker-compose.yml:L87-89`).

Other missing pieces:
- No SSL issuance: `ssl_status` is never written, and T-12 is blocked on OQ-6 (`0018:L1-15`).
- No storefront or PWA app exists in the repo to serve on a custom domain.

### 4. Branding

- `tenancy_org.brands` already has `logo_url`, `favicon_url`, `primary_color`, `secondary_color`, `accent_color` and URL columns (`database_scripts/01_bc1_tenancy_org.sql:L42-70`).
- The only write path is `UpdateBrand`, and it accepts only `LogoUrl`, a free-form string (`BrandDtos.cs:L20-27`; `UpdateBrand.cs:L15-35`).
- There is no write path at all for the colour columns.
- `brands` is `rls_admin_only` (`rls_proposal.sql:L315-341`, enabled in `_applied_rls_bc1_bc2.sql:L42`), so a brand owner cannot even read their own row through EF.
- Every client hardcodes LaundryGhar branding:
  - admin-web: `Sidebar.tsx:L214`, `index.css:L8-24`
  - pos-web: `index.html:L8`
  - customer-mobile: `app.config.ts:L3-30` and the build-time `DEFAULT_BRAND_CODE` (`app.config.ts:L56`)

### 5. White-label mobile (T3)

- `GET /admin/white-label/apps/{app}` returns the name, slug, bundle id, package, primary colour and icon URL for an Expo build (`GetAppConfig.cs:L46-97`).
- Nothing consumes it. `app.config.ts` is hardcoded, there is no build pipeline, and `eas.json` submit credentials are empty. T-22 is blocked on OQ-7.

### 6. Business type

The schema enforces one vertical:
- `brands.vertical_key` is a single `NOT NULL` column with a CHECK over {laundry, salon, logistics, tiffin} (`phase0_multi_vertical.sql:L29-37`, widened in `0004`).
- A trigger forbids changing it once orders exist (`phase0_multi_vertical.sql:L75-99`).
- Signup accepts exactly one `TemplateKey` string (`SignupDtos.cs:L26-32`).

Downstream, the vertical has little effect:
- It is consulted only by the navigator, role listing, terminology and bundle application.
- Each of those reads `tenancy_org.brands` through EF under RLS. For non-platform users that read returns null, so every one of them **fails open** (SA-ONB-003).
- Order creation ignores the vertical entirely (SA-ONB-004).

### What "provisioning" means today

| Meaning | Implemented? |
|---|---|
| Enabling modules (`brand_feature` from the template bundle; token-mint entitlement filter) | **Yes**, server-side |
| Creating tenant config (brand row, owner, membership, own franchise, catalogue seed, trial) | **Yes**, for self-signup only |
| Publishing a storefront | **No** — no storefront app; the "go live" sub-domain is a DB row only |
| Building a branded app | **No** — config endpoint only; no build or submit |
| Dedicated instance | **No**, and not needed (see the delivery-model comparison) |

## Onboarding step matrix

| Onboarding step | Exists? | Server-enforced? | Evidence | Gap |
|---|---|---|---|---|
| 1. Business signs up | Backend yes; UI no | Yes: OTP proof, rate limit, generated brand code | `Signup.cs:L26-33`; `CompleteSignup.cs:L74-85,L243-272`; `Program.cs:L641-646` | No client screen. No handler or integration tests (SA-ONB-008, SA-ONB-010). |
| 2. Select exactly one primary business type | Yes (single `TemplateKey`) | Yes: public-template check plus DB CHECK on a single column | `SignupDtos.cs:L26-32`; `CompleteSignup.cs:L74-76`; `0011:L20-23` | Admin `CreateBrand` cannot set a vertical and defaults to laundry (SA-ONB-005). "Hyperlocal marketplace" is not a vertical. Tiffin is not public. |
| 3. Company name, logo, branding, business info | Name, phone, email and GSTIN only | Name required; GSTIN unvalidated | `SignupDtos.cs:L26-32`; `CompleteSignup.cs:L106-128,L304-307` | No logo, colours or theme at signup or afterwards (SA-ONB-002). GSTIN never reaches invoices (SA-ONB-007). |
| 4. Validate vertical + plan | Vertical yes; plan implicit | Partially | `CompleteSignup.cs:L200-225`; `TemplateProvisioner.cs:L53-79` | No plan choice. If the bundle is missing, the brand silently gets no subscription and no features (SA-ONB-010). |
| 5. Provision tenant config | Yes, for self-signup | Yes (single transaction) | `CompleteSignup.cs:L97-235` | Back-office path provisions nothing (SA-ONB-005). No notification templates, CMS, app config, hours or price list seeded (SA-ONB-006). |
| 6. Storefront/web, mobile, admin, order mgmt, customer mgmt, user mgmt | Admin, order, customer and user mgmt: yes (shared, laundry-shaped). Storefront: no. Mobile: one hardcoded build. | Admin, order and user mgmt are RBAC-gated | `admin-web/src/App.tsx:L57-116`; `customer-mobile/app.config.ts:L8-30` | No storefront. "Go live" is not reachable (SA-ONB-001). Salon orders run the laundry pipeline (SA-ONB-004). |
| 7. Configure services, staff, locations, pricing, hours, booking rules | Yes, via the existing admin pages; the wizard only reports status | Yes (existing endpoints) | `AdminProviderOnboarding.cs:L12-21`; `GetOnboardingState.cs:L46-75` | No UI for the wizard. The catalogue step is "done" with unpriced items. No hours, pricing or booking-rule steps. `go-live` ignores prerequisites (SA-ONB-011). |
| 8. Access by membership, role, permission, attribute, entitlement, business type | Membership, role, permission and entitlement: yes. Business type: nav only. | Entitlement yes (token mint). Business type **no** | `ScopeResolver.cs:L185-240`; `GetNavigator.cs:L37-80`; `SetBrandFeature.cs:L20-53` | Vertical gating fails open for tenant users and is absent from API authorization (SA-ONB-003). |

## White-label capability matrix

| Capability | Where stored | How served | How clients load it | Status |
|---|---|---|---|---|
| Tenant name | `brands.name` | `BrandDto` (admin-only RLS); `kernel.brand_app_identity` for the white-label endpoint | admin-web shows a hardcoded "Laundry Ghar" (`Sidebar.tsx:L214`) | Partial |
| Logo / favicon | `brands.logo_url` / `favicon_url` (TEXT) | `GetAppConfig` icon and splash only | Not loaded by any client. No upload endpoint (item images do use brand-prefixed storage: `ItemImageCommands.cs:L39-45`) | Not supported |
| Colours / theme | `brands.primary/secondary/accent_color` | `GetAppConfig.PrimaryColor` only | Build-time Tailwind/CSS constants (`admin-web/src/index.css:L8-24`; `customer-mobile/app.config.ts:L3`) | Not supported (no write path) |
| Custom domain | `brand_domains` | `BrandResolver` (PublicEngagement only) | No storefront. Host is not forwarded by the Gateway | Not reachable (SA-ONB-001) |
| Storefront config | none | none | none | Not supported |
| Mobile branding | `app.config.ts` per app (build-time) | `GetAppConfig` (unused) | `DEFAULT_BRAND_CODE` env at build (`app.config.ts:L56`) | Build-time per brand; no factory |
| Navigation | `identity_access.modules` | `GetNavigator` (server RBAC + entitlement + vertical) | admin-web Sidebar is data-driven (`Sidebar.tsx:L183,L220`) | Supported; vertical gate fails open (SA-ONB-003) |
| Terminology | `vertical_terms` | `GET /terminology` | admin-web and pos-web fetch it | Falls back to laundry for tenant users (SA-ONB-003) |
| Notification templates | `engagement_cms.notification_templates` (per brand) | `NotificationMappingService` | n/a | Only LG-MAIN is seeded; fallback text says "Laundry Ghar" (SA-ONB-006) |
| Invoices / receipts | `invoices` snapshot from store + franchise | `InvoicePdfRenderer` | PDF | Supplier per tenant; footer, SAC and GSTIN wrong for new tenants (SA-ONB-006, SA-ONB-007) |
| Email / SMS / WhatsApp sender | per-brand settings (`AdminSettings.cs:L46-69`; `SettingsStore.ResolveBrandIdAsync`) | channel senders | n/a | Exists. Default `FromName` is "Laundry Ghar" (`EmailSettings.cs:L19`) |

## Delivery-model comparison (evidence-based)

| Option | Fit with what exists | Cost | Security | Maintainability | Recommendation |
|---|---|---|---|---|---|
| **Configuration-driven multi-tenant (one deployment, brand rows + RLS + features)** | It is the existing architecture: `brand_id` + RLS, `brand_feature` entitlements, server-driven nav, per-brand settings, `brand_domains`. | Lowest; scales with rows | RLS and token entitlement already enforced. Must fix fail-open vertical reads (SA-ONB-003). | One codebase | **Primary model.** Close the gaps (branding write path, Host forwarding, vertical enforcement, template seeding). |
| **Modular verticals inside the same deployment** | Already present: `vertical_templates`, `IFulfillmentStrategy` (Laundry, Logistics, Salon, Recurring), vertical-tagged modules, features and roles. | Low | Same as above | Good if strategy selection is wired to the brand vertical (SA-ONB-004) | **Adopt.** Wire brand vertical → fulfilment mode → strategy. |
| **White-label builds (mobile only)** | `GetAppConfig` already derives per-brand identifiers. Expo/EAS supports per-brand `app.config.ts` from env/JSON. | Per-brand store fees and review cycles; CI minutes per build | Brand identity is build-time, but API data is still RLS-isolated. Signing keys must be held per brand. | Same source, N build profiles | **Only for T3 (store listings).** Generate `app.config` from `GetAppConfig` at build. Never fork source. Runtime-theme the shared app for T1. |
| **Dedicated deployment per tenant** | Nothing in the code needs it. RLS and brand scoping are already the isolation boundary. | Highest (N stacks, N DBs, N migrations) | Strongest blast-radius isolation | Worst: N upgrade paths | **Not recommended** unless a contract requires data residency. |

## Findings

### SA-ONB-001 — "Go live" and custom domains are database rows that no request path can reach

- **Category:** Provisioning / Routing / White-label
- **Severity:** High
- **Status:** Partially Verified (code and config read end-to-end; YARP's default Host rewrite is library behaviour, not executed)
- **Evidence:**
  - `backend/laundryghar/laundryghar.Gateway/Program.cs:L45-55` (`MakeRoute`): routes match on path only. The only transform is `PathPattern`, with no `RequestHeaderOriginalHost`.
  - `laundryghar.Gateway/Program.cs:L283-290,L310-316`: comments claim X-Forwarded-Host is forwarded.
  - `laundryghar.ServiceDefaults/Extensions.cs:L267-283`: `ForwardedHeaders = XForwardedFor | XForwardedProto` only, so `Request.Host` is never rewritten from X-Forwarded-Host.
  - `deploy/docker-compose.yml:L87-89`: identity and engagement clusters are `http://core:8080`, so core sees Host `core`.
  - `core.Infrastructure/Services/BrandResolver.cs:L67-71,L106-142`: the Host lookup uses `context.Request.Host.Host`.
  - `BrandResolver` is consumed only by `core.WebApi/Endpoints/Engagement/PublicEngagement.cs:L61,L80,L99`.
  - Customer login uses `core.Application/Identity/Auth/Common/CustomerBrandResolver.cs:L20-53`: X-Brand-Id, then body `brandCode`, then a default. It never looks at Host.
  - `db/migrations/0017_onboarding_progress.up.sql:L115-170` (`ensure_brand_subdomain`): inserts a verified, primary row with `ssl_status='pending'`.
  - `0018_domain_health.up.sql:L1-15`: SSL issuance is blocked on OQ-6.
  - No storefront or PWA project exists in the repo (searched for "storefront" and "pwa" across the repo).
  - Gateway CORS is a static `Cors:AllowedOrigins` list (`Gateway/Program.cs:L164-197`).
- **Observed behaviour:**
  - `POST /provider-onboarding/go-live` returns `<code>.laundryghar.app` and the wizard marks the brand "live".
  - In a real request through the Gateway, the Host is the upstream name, so `resolve_brand_domain('core')` returns null and the result is negatively cached for 30 s.
  - No certificate is issued, there is nothing to serve on that host, and a browser app on the domain would be CORS-blocked.
  - `BrandResolverHostTests` pass only because they set `ctx.Request.Host` on a `DefaultHttpContext` (`tests/operations.IntegrationTests/Rbac/BrandResolverHostTests.cs:L175`). They never go through the Gateway.
- **Reproduction:** Deploy with compose and call `GET https://<custom-domain>/engagement/api/v1/public/app-config` with no `brandCode`. Expected result: LG-MAIN's config, not the custom domain's brand. Not executed here.
- **Impact:**
  - Business: "live on a sub-domain in minutes" is reported as achieved when it is not.
  - Docs: `docs/TASKS.md` T-09 and T-17 are marked `Done`, and the T-17 acceptance item "brand reachable on its sub-domain" is contradicted by the code.
- **Recommended remediation (smallest safe change):**
  1. Add a YARP `RequestHeaderOriginalHost=true` transform, or add `ForwardedHeaders.XForwardedHost` with `AllowedHosts` tied to verified domains.
  2. Use Host in `CustomerBrandResolver` as well.
  3. Make CORS dynamic for verified domains.
  4. Pick an option for OQ-6, preferably Cloudflare-for-SaaS or Caddy on-demand TLS with an `ask` endpoint backed by `resolve_brand_domain`.
  5. Do not mark a brand "live" until a storefront exists and `ssl_status='active'`.
- **Regression tests required:** Gateway-level integration test (Host header in → brand resolved upstream); a CORS test for a verified domain.
- **Dependencies / priority:** P1. Depends on the OQ-6 decision and on a storefront.
- **Prior-doc cross-ref:** `docs/TASKS.md` T-09, T-12, T-17; `PLATFORM_STRATEGY.md` §4.

### SA-ONB-002 — Tenant branding (logo, colours, theme) has no usable write path and no client consumes it

- **Category:** White-label
- **Severity:** High
- **Status:** Partially Verified (code verified; the RLS read-as-empty behaviour is taken from the policy source plus measurements recorded in migration comments, not reproduced)
- **Evidence:**
  - `database_scripts/01_bc1_tenancy_org.sql:L42-70`: the branding columns exist.
  - `core.Application/Identity/TenancyOrg/Dtos/BrandDtos.cs:L20-27`: `UpdateBrandRequest` has `LogoUrl` only, no colours or favicon.
  - `Brands/Commands/UpdateBrand/UpdateBrand.cs:L15-35`: `FindAsync` on `Brands`, which is `rls_admin_only` (`db/patches/rls_proposal.sql:L315-341`; enabled in `_applied_rls_bc1_bc2.sql:L42`). A brand owner holds `brands.update` (`core.Infrastructure/Seeders/IdentitySeeder.cs:L515-516`) but gets 404.
  - A repo-wide grep shows `PrimaryColor` is assigned nowhere.
  - `SignupDtos.cs:L26-32`: no branding inputs at signup.
  - admin-web has no branding panel (`pages/settings/SettingsPage.tsx:L6-17`) and is hardcoded "Laundry Ghar" (`components/layout/Sidebar.tsx:L214`; `index.css:L8-24`).
  - pos-web hardcodes its title (`pos-web/index.html:L8`).
  - Mobile is hardcoded at build time (`customer-mobile/app.config.ts:L3-30`).
- **Observed behaviour:**
  - A provider cannot set a logo or colours.
  - A platform admin can set only a raw logo URL. There is no upload endpoint and no per-tenant asset path for logos.
  - No surface renders tenant branding.
- **Impact:** Step 3 of the target flow ("logo, branding") and tier T2 are not deliverable. A self-signed-up owner cannot edit even their own brand name.
- **Recommended remediation:**
  1. Add a brand-self `PUT /admin/brand/branding` that reads and writes via SECURITY DEFINER or a narrow RLS policy (`id = current_brand_id()`). It should cover name, logo, favicon and colours, with hex validation.
  2. Add a logo upload that reuses `IFileStorageProvider` with the brand prefix.
  3. Add a public `GET /public/branding` (Host or brandCode).
  4. Have admin-web set CSS variables at runtime and have the mobile apps theme at runtime for the shared app.
- **Regression tests required:** owner can update their own brand and cannot update another; colour validation; the public branding endpoint resolves per brand.
- **Dependencies / priority:** P1.
- **Prior-doc cross-ref:** `PLATFORM_STRATEGY.md` §4 T2; `docs/TASKS.md` T-22 notes.

### SA-ONB-003 — Business-type (vertical) restrictions fail open for tenant users and are not enforced in API authorization

- **Category:** Authorization / Vertical enforcement
- **Severity:** High
- **Related area:** AUTHZ, ENT
- **Status:** Partially Verified (code paths verified; RLS empty-read not executed here)
- **Evidence:**
  - These paths read `brands.vertical_key` through EF under tenant RLS (`brands` is `rls_admin_only`):
    - `core.Application/Identity/AccessControl/Queries/GetNavigator/GetNavigator.cs:L42-45`
    - `GetAccessRoles/GetAccessRoles.cs:L25-28`
    - `GetRoles/GetRoles.cs:L23`
    - `TenancyOrg/Terminology/GetTerminology.cs:L46-55`
    - `GrantMembership/GrantMembership.cs:L197-201,L247-258`
  - Only platform admins get a bypass (`laundryghar.Utilities/Middlewares/TenantResolutionMiddleware.cs:L35-41`).
  - `VerticalKey.IsAvailableTo` returns `true` when the brand vertical is null (`SharedDataModel/Enums/VerticalKey.cs:L34-37`).
  - admin-web `useActiveVertical` falls back to laundry when `getBrandById` fails (`admin-web/src/hooks/useActiveVertical.ts:L20-37`; `GetBrandById` is plain EF).
  - `SetBrandFeature.cs:L20-53` licenses any active feature with no vertical check.
  - `ScopeResolver.cs:L185-240`: the token filter applies features but not vertical.
  - `GrantMembership.cs:L64-72` checks the feature gate only, not the role's vertical.
- **Observed behaviour:**
  - For a non-platform user, the vertical resolves to null. The nav shows every vertical-tagged module (still limited by entitlement), role lists include laundry-only roles, terminology is laundry for a salon or courier, and the user's home `vertical_key` is overwritten with NULL on primary grants.
  - Nothing at request time stops a brand of vertical X from calling another vertical's endpoints if a feature is licensed manually.
- **Impact:** Target requirement 8 ("restricted by … business type") is not server-enforced. Terminology leakage, which `PLATFORM_STRATEGY` §12 lists as a top risk, occurs for every non-laundry tenant.
- **Recommended remediation:**
  1. Add `kernel.brand_vertical(uuid)` (SECURITY DEFINER, as with `brand_status`), or mint `vertical` into the JWT at `ScopeResolver`.
  2. Use it in all the readers above.
  3. Reject vertical-mismatched features in `SetBrandFeature` and roles in `GrantMembership`.
  4. Strip vertical-mismatched modules in the token filter.
- **Regression tests required:** salon brand_admin → navigator excludes laundry modules; terminology is salon; `SetBrandFeature(warehouse)` on salon → 422; `GrantMembership(warehouse_staff)` on salon → 422.
- **Dependencies / priority:** P1.
- **Prior-doc cross-ref:** The "brands-RLS trap" is recorded six times in migration comments (`0009:L6-20`, `0017`, `0019`, `0021`, `GetAppConfig.cs:L60-62`). These callers were missed.

### SA-ONB-004 — The salon template is public, but orders always run the laundry pipeline (template fulfilment mode is never applied)

- **Category:** Vertical provisioning / Order management
- **Severity:** High
- **Related area:** ORD
- **Status:** Verified (code)
- **Evidence:**
  - `db/migrations/0011_vertical_templates.up.sql:L63-89`: `salon` has `fulfillment_mode='appointment'` and is public. The comment at `L26-28` and `CompleteSignup.cs:L72-73` both say a template must never be offered if it "cannot take an order".
  - `operations.Application/Orders/Orders/Commands/CreateOrderCommand.cs:L618-623`: the mode is `isParcel ? PointToPoint : ProcessDeliver`, and the brand or template is never consulted.
  - `CreateOrderCommand.cs:L641-647`: the comment admits `VerticalKey` is left at its entity default.
  - `SharedDataModel/Entities/OrderLifecycle/Order.cs:L44`: that default is `Laundry`.
  - `SalonAppointmentStrategy` is registered (`operations.Application/DependencyInjection.cs:L31-40`) but nothing resolves `appointment`: only `CreateOrderCommand:L623` and `CreateParcelOrderCommand:L93` call `Resolve`.
- **Observed behaviour:** A salon that signs up gets salon catalogue rows (`catalog_kind=service`), but every order is stored `vertical_key='laundry'` and `fulfillment_mode='process_deliver'`.
- **Impact:** Business-type selection does not change the core workflow, and vertical analytics and partitions are mislabelled.
- **Recommended remediation:**
  1. Resolve the brand's vertical, and from it the template mode, in `CreateOrderCommand` (via the SECURITY DEFINER lookup from SA-ONB-003).
  2. Set `Order.VerticalKey` and `FulfillmentMode` from it.
  3. Until the appointment flow has an entry point, set `vertical_templates.is_public=false` for `salon`.
- **Regression tests required:** order on a salon brand gets `vertical_key=salon` and `mode=appointment`; laundry is unchanged.
- **Dependencies / priority:** P1.

### SA-ONB-005 — Back-office `CreateBrand` creates an unprovisioned tenant with an implicit vertical

- **Category:** Provisioning consistency
- **Severity:** Medium
- **Status:** Verified (code)
- **Evidence:**
  - `core.Application/Identity/TenancyOrg/Brands/Commands/CreateBrand/CreateBrand.cs:L17-49` and `Dtos/BrandDtos.cs:L9-18`: no `VerticalKey` in the request.
  - `Brand.VerticalKey` defaults to `Laundry` (`SharedDataModel/Entities/TenancyOrg/Brand.cs:L16`).
  - No owner, membership, own franchise, `brand_feature` rows, subscription or catalogue are created. Compare `CompleteSignup.cs:L97-235`.
  - Endpoint: `AdminBrands.cs:L28`.
- **Observed behaviour:** A platform-created brand is always "laundry". With `Entitlement:Enforced=true` (`core.WebApi/appsettings.json:L12-14`) it has only core features, and no one can log in to it until memberships are granted by hand.
- **Impact:** There are two provisioning paths that produce different shapes. Sales-led onboarding is error-prone, and the vertical cannot be chosen.
- **Recommended remediation:** Make `CreateBrand` require a `TemplateKey` and reuse `TemplateProvisioner`, plus owner invite, franchise and trial, through a shared `BrandProvisioner` service. Keep a single code path.
- **Regression tests required:** Admin-created brand has the template vertical, features and own franchise. A request without a template returns 422.
- **Dependencies / priority:** P2.

### SA-ONB-006 — New tenants' customer notifications and invoices carry LaundryGhar/laundry branding (white-label leak)

- **Category:** White-label / Notifications / Documents
- **Severity:** Medium
- **Status:** Verified (code)
- **Evidence:**
  - Signup seeds no notification templates (`TemplateProvisioner.cs:L38-49`; `CompleteSignup.cs:L196-228`).
  - The only template seed targets LG-MAIN (`db/patches/seed_notification_lifecycle_templates.sql:L27-33`).
  - `commerce.Infrastructure/Worker/Services/NotificationMappingService.cs:L258-282`: when a template is missing it falls back to `BuildFallbackBody`, and `L423-437` hardcodes "Your Laundry Ghar order…" and "Update from Laundry Ghar."
  - The invoice footer hardcodes "Laundry & Dry-Cleaning Services" and the SAC is always `999712` (`operations.Application/Orders/Invoices/InvoicePdfRenderer.cs:L41-45`; `InvoiceTaxCalculator.cs:L18`; `GenerateInvoiceCommand.cs:L156`).
  - Staff invite emails say "invited to the Laundry Ghar admin console" (`core.Application/Identity/Settings/EmailTemplates.cs:L14-69`), and the default `FromName` is "Laundry Ghar" (`EmailSettings.cs:L19`).
- **Observed behaviour:** Customers of a self-signed-up "Priya Salon" receive SMS, WhatsApp and push messages naming "Laundry Ghar", and tax invoices describe laundry services.
- **Impact:** Brand confusion and incorrect tax descriptions for non-laundry verticals.
- **Recommended remediation:**
  1. Seed per-brand notification templates from per-vertical platform defaults in `TemplateProvisioner`.
  2. Make the fallback body use the brand name.
  3. Store the SAC code and service description on `vertical_templates` (or per brand).
  4. Template the staff emails with the brand name.
- **Regression tests required:** status event for a new brand → body contains the brand name and not "Laundry Ghar"; salon invoice SAC and description are correct.
- **Dependencies / priority:** P2.

### SA-ONB-007 — GSTIN captured at signup never reaches invoices

- **Category:** Provisioning / Compliance
- **Severity:** Medium
- **Status:** Verified (code)
- **Evidence:**
  - `CompleteSignup.cs:L122` and `L304-307`: the GSTIN goes to `brands.config.gstin`.
  - The "OWN" franchise is created without a GSTIN (`L184-194`; `Franchise.Gstin` exists at `SharedDataModel/Entities/TenancyOrg/Franchise.cs:L19`).
  - Invoices read `franchise.Gstin` (`operations.Application/Orders/Invoices/Commands/GenerateInvoiceCommand.cs:L80-87`), and the renderer prints "GSTIN: Unregistered / Composition" when it is null (`InvoicePdfRenderer.cs:L61-64`).
- **Observed behaviour:** A GST-registered provider's invoices state that they are unregistered.
- **Impact:** GST compliance defect on every self-signed-up registered business.
- **Recommended remediation:** In `CompleteSignup`, set `Franchise.Gstin` from the request after validating the 15-character GSTIN pattern.
- **Regression tests required:** a signup with a GSTIN produces an invoice showing that GSTIN.
- **Dependencies / priority:** P1 (small fix).

### SA-ONB-008 — No client implements signup, the provider wizard, branding or the white-label app config; mobile is one hardcoded build

- **Category:** Application provisioning / UX completeness
- **Severity:** Medium
- **Status:** Verified (searched all four clients)
- **Evidence:**
  - No `signup`, `provider-onboarding` or `white-label` calls exist in `admin-web/src`, `pos-web/src`, `customer-mobile/{src,app}` or `rider-mobile/{src,app}`.
  - `admin-web/src/App.tsx:L57-116` has only `/login`, `/accept-invite` and the authenticated pages.
  - `customer-mobile/app.config.ts:L8-30` hardcodes the name, slug, bundle id and package, and the brand comes from the build-time `DEFAULT_BRAND_CODE` (`L56`; `src/constants/config.ts:L49`).
  - `customer-mobile/eas.json` has empty submit credentials and a placeholder EAS project id (`app.config.ts:L4-6`).
  - `GetAppConfig.cs:L46-97` output has no consumer.
- **Observed behaviour:** The onboarding funnel can only be exercised with raw API calls. White-label mobile apps would require hand-edited configs.
- **Impact:** Target steps 1, 3 and 6 are not deliverable to an end user.
- **Recommended remediation:**
  1. Add an anonymous signup page in admin-web (or a small separate onboarding SPA) and a wizard panel that reads `/provider-onboarding`.
  2. For T3, make `app.config.ts` read a JSON produced from `GetAppConfig` (`BRAND_CONFIG_PATH`) with per-brand EAS profiles.
  3. Do not fork source per brand.
- **Regression tests required:** e2e signup → OTP → login → wizard shows steps.
- **Dependencies / priority:** P2.

### SA-ONB-009 — The vertical cannot be changed through any governed path, and a raw change would not re-provision

- **Category:** Business-type lifecycle
- **Severity:** Low
- **Status:** Verified (code)
- **Evidence:**
  - No command assigns `Brand.VerticalKey` except signup (repo-wide grep; `CompleteSignup.cs:L114`).
  - The DB trigger blocks changes only once orders exist (`db/patches/phase0_multi_vertical.sql:L75-99`).
  - `TemplateProvisioner` runs only at signup.
- **Observed behaviour:** Before the first order, an operator with SQL access can change the vertical. Features, catalogue `catalog_kind`, roles and templates then stay those of the old vertical. After the first order, the change is impossible.
- **Impact:** A low risk today, because there is no API. It becomes a hazard once support tooling is built.
- **Recommended remediation:** A platform-only `ChangeBrandVertical` command that is allowed only before the first order and re-runs feature expansion. Document it as a platform-admin action, with an audit row.
- **Regression tests required:** a change after orders exist returns 409; a change before orders re-expands features.
- **Dependencies / priority:** P3.

### SA-ONB-010 — Signup has no plan choice, silently tolerates a missing plan, and has no handler or integration tests

- **Category:** Provisioning robustness / Test coverage
- **Severity:** Medium
- **Status:** Verified (code); tests searched
- **Evidence:**
  - `CompleteSignup.cs:L200-225`: if `template.DefaultBundleCode` is null, or its `module_bundle` row is absent, no subscription is created and no error is raised. `TemplateProvisioner.cs:L56` also returns 0 features in that case.
  - The plan is never caller-selectable (`SignupDtos.cs:L26-32`).
  - The duplicate-phone and brand-code checks are check-then-insert (`L84-85`, `L279-301`). The unique constraints (`users.phone_e164 UNIQUE`, `database_scripts/02_bc2_identity_access.sql:L23`; `brands.code UNIQUE`) would surface concurrent duplicates as unmapped DB errors.
  - Tests: only pure helpers are covered (`tests/core.Tests/Signup/TemplateProvisionerTests.cs:L17-76`; `WhiteLabel/AppIdentifierTests.cs`). There is no test of `CompleteSignupCommandHandler`, the rate limit, duplicate handling, or `GoLive`/`GetOnboardingState`. `docs/TASKS.md` T-16 lists those tests as acceptance criteria and its Evidence field is "_(empty)_" while its Status is `Done`.
- **Observed behaviour:** Under a template or bundle mismatch, a brand can be created with no plan and no licensed features.
- **Impact:** Silent, unbillable or empty tenants. Regressions in the tenant-creation endpoint, which is the platform's most abuse-prone surface, would not be caught.
- **Recommended remediation:**
  1. Fail the signup (500 plus an alert) when a public template has no resolvable bundle.
  2. Optionally accept a `planCode` validated against the vertical's bundles.
  3. Map 23505 to 409.
  4. Add handler and integration tests.
- **Regression tests required:** As listed in T-16, plus missing bundle → error and concurrent duplicate phone → 409.
- **Dependencies / priority:** P2.

### SA-ONB-011 — `go-live` ignores wizard prerequisites, and adding a primary custom domain can strand the brand off its verified host

- **Category:** Provisioning correctness
- **Severity:** Low
- **Status:**
  - Prerequisite bypass: Verified (code)
  - Primary-demotion collision: Suspected (SQL reasoning; not executed)
- **Evidence:**
  - `OnboardingCommands.cs:L148-159`: `GoLive` does not consult `brand_onboarding_facts`, so it succeeds with zero locations or items despite `TASKS.md` T-17 stating "two steps cannot be skipped".
  - The catalogue step is "done" with seeded **unpriced** items (`GetOnboardingState.cs:L53-58`).
  - `AddBrandDomain.cs:L60-73` demotes the existing (verified) primary when an unverified domain is added as primary. That drops `PrimaryDomain` (the facts query requires a verified primary).
  - A later `ensure_brand_subdomain` takes the `ON CONFLICT … SET is_primary=true` path (`0017:L154-159`), which would collide with `idx_brand_domains_one_primary` (`0002:L58-60`).
- **Impact:** A brand can be shown as "live" while unable to take a priced order. Re-going-live can error.
- **Recommended remediation:**
  1. In `GoLive`, require the location and catalogue facts, plus at least one priced item.
  2. In `AddBrandDomain`, never demote a verified primary in favour of an unverified domain. Promote only after verification.
- **Regression tests required:** go-live with no location → 422; add an unverified primary → the existing verified primary is kept.
- **Dependencies / priority:** P3.

### SA-ONB-012 — White-label app identifiers can collide between brands

- **Category:** White-label mobile
- **Severity:** Low
- **Status:** Verified (code + existing test data)
- **Evidence:**
  - `core.Application/Identity/WhiteLabel/Queries/GetAppConfig.cs:L89-90,L106-114`: `IdentifierSegment` strips all non-alphanumerics.
  - `tests/core.Tests/WhiteLabel/AppIdentifierTests.cs:L23-41` asserts `LG-MAIN → lgmain` and `7 → b7`.
  - Brand codes are unique only as given (`brands.code UNIQUE`), so `LG-MAIN` and `LGMAIN`, or `7` and `B7`, map to the same `com.laundryghar.<seg>.customer`.
- **Impact:** Two tenants could get the same bundle id. Store bundle ids are immutable after first submission.
- **Recommended remediation:** Persist a per-brand `app_identifier` with a UNIQUE constraint, allocated once (with a suffix on collision) instead of being derived on every call.
- **Regression tests required:** two colliding codes → distinct identifiers.
- **Dependencies / priority:** P3 (before T-22 ships).

## Positive controls verified

- **Single vertical per tenant at the schema level:** `brands.vertical_key` is `NOT NULL` with a CHECK (`phase0_multi_vertical.sql:L29-37`; `0004`). Signup accepts one `TemplateKey` and validates it against public templates (`CompleteSignup.cs:L74-76`). The API cannot submit multiple or invalid verticals.
- **Signup safety:**
  - OTP is proven before any write (`CompleteSignup.cs:L79,L243-272`), with attempt counting.
  - The brand code is server-generated, so a caller cannot squat a tenant id (`L279-301`).
  - The whole provisioning runs in one transaction (`L97-235`).
  - The rate limit is applied (`Signup.cs:L31-32`).
  - The duplicate-phone answer happens only after OTP, so there is no membership oracle (`Signup.cs:L42-49`).
- **Vertical-aware feature expansion at signup:** shared tiers do not license laundry-only features to other verticals (`TemplateProvisioner.cs:L58-65`). The same applies to `ApplyBundleToBrand` (`ApplyBundleToBrand.cs:L29-44`, platform-admin path with bypass).
- **Entitlements are enforced server-side at token mint:** permissions are stripped for unlicensed features and `ent_off` produces 402 (`ScopeResolver.cs:L185-240`; `FeatureNotInPlan.cs`; `appsettings.json:L12-14`). This gates the white-label and domain endpoints behind the Enterprise features (`0019`).
- **Domain hijack protections:**
  - Global `UNIQUE(domain)` (`0002`).
  - Only verified rows resolve, through a narrow SECURITY DEFINER with a pinned `search_path` (`0003:L31-57`).
  - TXT verification is scoped by `(BrandId, DomainId)` and never un-verifies (`VerifyBrandDomain.cs`).
  - `ensure_brand_subdomain` refuses another brand's hostname (`0017:L154-159`).
- **Server-driven navigation:** admin-web's Sidebar renders `/navigator` (`Sidebar.tsx:L183,L220`), gated by permission and entitlement (`GetNavigator.cs:L56-80`).
- **Default roles:** global system roles and presets are visible to all tenants (`0013`, `0030`), so no per-tenant role seeding is needed. Role grants respect feature gates (`GrantMembership.cs:L64-72`).
- **Per-brand configuration isolation:**
  - Settings (email, SMS, WhatsApp, payments) are resolved per brand (`AdminSettings.cs:L46-69`).
  - Notification templates are keyed by `brand_id`.
  - Shared output cache keys include the tenant identity plus the X-Brand-Id header (`OutputCaching.cs:L44-94`), and ASP.NET's default key also includes the host.
  - Item images are stored under a brand-prefixed key (`ItemImageCommands.cs:L39-45`).
- **Wizard progress is derived from real rows rather than stored flags** (`0017`; `GetOnboardingState.cs:L36-101`).

## Open questions / not verified

- Live RLS behaviour (brands reading as empty for `app_user`) was not reproduced; the local Postgres attempt was blocked by the sandbox. Evidence is the policy source plus repeated measurements recorded in `0009`, `0017`, `0019` and `0021`.
- YARP Host-forwarding defaults are taken from library behaviour; the Gateway was not run.
- Whether production fronts the Gateway with a Host-aware edge (for example nginx `proxy_set_header Host`) is unknown. `deploy/README.md` leaves the reverse proxy to the operator, and even then `ForwardedHeaders` ignores X-Forwarded-Host.
- What happens when the trial ends (dunning, suspension) is outside this area; see migration `0021` and ADR-010.
- `InviteUser` and `AcceptInvite` flows were not traced end to end; I only confirmed that they exist (`admin-web/src/pages/access-control/InviteUserModal.tsx`, `pages/auth/AcceptInvitePage.tsx`).
- OQ-6 (SSL vendor) and OQ-7 (who submits store builds) are open product decisions that block T2 and T3.

## Verdict inputs

| Question | Status | Justification |
|---|---|---|
| **Q5** — Can a business onboard and get a configured, provisioned tenant? | Partially Supported | Backend self-signup provisions brand, owner, features, catalogue and trial atomically. There is no UI, branding is not configurable, the back-office path is unprovisioned, and notification and GSTIN gaps remain. |
| **Q6** — White-label (logo, colours, domains, storefront, branded apps) | Not Supported | There is no write path for colours, no client reads branding, the custom-domain path is unreachable behind the Gateway, there is no SSL automation and no storefront, and mobile is a single hardcoded build. |
| **Q8** — Exactly one primary business type, enforced server-side | Partially Supported | Single column, CHECK and single-template signup are enforced. Vertical-based access, nav and terminology fail open for tenant users, are absent from API authorization, and orders ignore the vertical. |
| **Q9** — Web, mobile, admin, order mgmt and user mgmt genuinely implemented (onboarding angle) | Partially Supported | The admin console, order, customer and user management and the POS exist, but they are shared and laundry-shaped. There is no storefront, mobile is one LaundryGhar-branded app, and salon orders run the laundry pipeline. |
