# LaundryGhar OLMS — Handoff

_Last updated: 2026-06-06_

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

## 4. What changed in the latest session (2026-06-06)

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

## 6. Remaining backlog (prod-hardening)

- **Secrets → a real manager** (Key Vault / SSM): currently `ConnectionStrings__Default`,
  `Jwt__PrivateKey` (Identity), `Jwt__Authority` (services) come from env. See `backend/laundryghar/PRODUCTION_ENV.md`.
- **Remove AutoMapper 13.0.1** — flagged CVE (NU1903); it is unused (inline projections everywhere), so a low-risk delete.
- Possible follow-ups: deeper customer/rider mobile E2E; promotion/coupon linking in CMS banners;
  surface more analytics endpoints in admin-web.

## 7. Git policy

Git is **ask-gated** — commit/push only when explicitly requested. Commit trailer:
`Co-Authored-By: Claude Opus 4.8 (1M context)`. The repo `.gitignore` excludes `bin/obj`,
`node_modules`, `.expo`, mobile `ios/android`, `.env` (keeps `.env.example`), and `*.pem`.

## 8. Useful references

- Orchestrator working notes (detailed, per-bounded-context): `.claude/agent-memory/laundryghar-orchestrator/status.md`
- Production env contract: `backend/laundryghar/PRODUCTION_ENV.md`
- DB patches applied this session: `db/patches/harden_app_user_and_rls_bypass.sql`,
  `order_addons_brand_id_rls.sql`, `order_number_sequence.sql`, `fix_mv_customer_ltv_nulls.sql`
- AppHost: `backend/laundryghar/laundryghar.AppHost/Program.cs`
- RS256 key provider + JWKS: `laundryghar.Identity/Infrastructure/Auth/RsaJwtKeyProvider.cs`,
  `laundryghar.Identity/Endpoints/WellKnownEndpoints.cs`
