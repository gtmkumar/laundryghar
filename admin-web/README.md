# Laundry Ghar — Admin Web

React 19 + Vite + TypeScript management console for Platform/Brand/Franchise/Store admins.

## Tech stack

| Concern | Library |
|---|---|
| Framework | React 19 + Vite 8 |
| Language | TypeScript 5 (strict) |
| Routing | React Router v7 |
| Server state | TanStack Query v5 |
| Client state | Zustand v5 (persisted to localStorage) |
| Forms | React Hook Form + Zod |
| HTTP | Axios |
| Styling | Tailwind CSS v4 + shadcn/ui components (Radix primitives) |
| Icons | Lucide React |

## Setup

```bash
cd admin-web
cp .env.example .env        # edit service URLs if needed
npm install
npm run dev                 # dev server on http://localhost:5173
npm run build               # production build
npx tsc --noEmit            # standalone type check
```

## Environment variables

All service base URLs are configured via `.env`:

```
VITE_IDENTITY_URL=http://localhost:5000
VITE_CATALOG_URL=http://localhost:5001
VITE_ORDERS_URL=http://localhost:5002
VITE_WAREHOUSE_URL=http://localhost:5003
VITE_LOGISTICS_URL=http://localhost:5004
VITE_COMMERCE_URL=http://localhost:5005
VITE_FINANCE_URL=http://localhost:5006
```

## Dev credentials

```
Email:    admin@laundryghar.local
Password: Admin@123
```

## How auth works

1. `POST {IDENTITY_URL}/api/v1/auth/password/login` with `{ identifier, password }` returns `{ accessToken, refreshToken, expiresInSeconds }`.
2. `accessToken` is a 15-minute JWT carrying `user_type`, `permissions`, and optionally `brand_id`.
3. Both tokens are persisted to `localStorage` via Zustand's `persist` middleware (key: `lg-admin-auth`).
4. Every axios request attaches `Authorization: Bearer <accessToken>` via a request interceptor.
5. On a 401 response, the interceptor silently calls `POST /api/v1/auth/refresh` with the stored `refreshToken`. On success, tokens are rotated, the pending request is retried once, and queued requests are drained. On failure, auth is cleared and the user is redirected to `/login`.
6. `POST /api/v1/auth/logout` is called on sign-out (best-effort — clears client state regardless).

## How X-Brand-Id works

The backend uses PostgreSQL RLS scoped by `brand_id`. Platform admins can act across brands and must send `X-Brand-Id: <uuid>` on brand-scoped admin endpoints.

- **platform_admin**: The topbar shows a Brand Switcher dropdown populated from `GET /api/v1/admin/brands`. The selected brand id is persisted in `localStorage` (key: `lg-admin-brand`) and sent as `X-Brand-Id` on every request via the axios interceptor.
- **brand_admin, franchise_owner, store_admin, etc.**: Their `brand_id` is embedded in the JWT. The interceptor reads it from the decoded payload and sends it automatically; the manual switcher is hidden.

## Source structure

```
src/
  api/
    client.ts         Axios instances + interceptors + unwrap() helper
    auth.ts           Identity auth endpoints
    tenancy.ts        Identity admin tenancy endpoints (brands, stores, franchises)
    catalog.ts        Catalog admin endpoints (categories, services, price-lists)
    orders.ts         Orders admin endpoints
  components/
    ui/               Primitive UI components (Button, Input, Badge, Card, Select, Label)
    layout/           AppShell, Sidebar, Topbar, BrandSwitcher, ProtectedRoute
    shared/           DataTable, Pagination, PageHeader, LoadingState, ErrorState
  hooks/
    useTenancy.ts     TanStack Query hooks for tenancy data
    useCatalog.ts     TanStack Query hooks for catalog/pricing data
    useOrders.ts      TanStack Query hooks for orders data
  pages/
    auth/LoginPage    RHF + Zod login form
    DashboardPage     Quick-nav cards
    tenancy/          Stores + Franchises tabbed list
    catalog/          Service Categories + Services + Price Lists tabbed list
    orders/           Orders list with status filter
  stores/
    authStore.ts      Zustand auth state (accessToken, refreshToken, user claims)
    brandStore.ts     Zustand active brand context
  types/
    api.ts            TypeScript interfaces mirroring backend DTOs
    schemas.ts        Zod schemas (login form)
  lib/
    utils.ts          cn(), formatCurrency(), formatDate(), formatDateTime()
```

## API contracts wired (first slice)

| Screen | Service | Endpoint |
|---|---|---|
| Login | Identity :5000 | `POST /api/v1/auth/password/login` |
| Token refresh | Identity :5000 | `POST /api/v1/auth/refresh` |
| Brand switcher | Identity :5000 | `GET /api/v1/admin/brands` |
| Tenancy — Stores | Identity :5000 | `GET /api/v1/admin/stores` |
| Tenancy — Franchises | Identity :5000 | `GET /api/v1/admin/franchises` |
| Catalog — Categories | Catalog :5001 | `GET /api/v1/admin/service-categories` |
| Catalog — Services | Catalog :5001 | `GET /api/v1/admin/services` |
| Catalog — Price Lists | Catalog :5001 | `GET /api/v1/admin/price-lists` |
| Orders | Orders :5002 | `GET /api/v1/admin/orders` |

All list endpoints support `?page=&pageSize=` pagination. The `unwrap()` helper in `src/api/client.ts` unwraps the `{ status, data, message }` envelope and throws on error.
