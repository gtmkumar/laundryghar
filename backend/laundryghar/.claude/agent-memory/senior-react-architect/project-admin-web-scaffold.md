---
name: project-admin-web-scaffold
description: admin-web and pos-web confirmed login E2E status, bugs fixed, and architectural notes
metadata:
  type: project
---

Admin-web was scaffolded at `/Users/gtmkumar/Documents/source/laundryghar/admin-web/` and pos-web at `/Users/gtmkumar/Documents/source/laundryghar/pos-web/`.

**Stack**: React 19 + Vite 8 + TypeScript 5 strict + React Router v7 + TanStack Query v5 + Zustand v5 + Axios + Tailwind CSS v4 (via @tailwindcss/vite plugin) + Radix UI primitives + Lucide React + React Hook Form + Zod.

**Build status**: Both apps built and dev servers confirmed running (2026-06-07 E2E test run).

**E2E login results (2026-06-07)**:
- admin-web: PASS — login → dashboard → BrandSwitcher renders, brands loaded from Identity. No errors.
- pos-web: PASS (after frontend fixes) — login → /new-order → NewOrderPage renders, "No store selected" warning shown. Zero catalog 401s, zero spurious refresh calls.

## Fixed Frontend Bugs (pos-web, 2026-06-07)

### Bug 1 — useCatalog.ts: catalog queries fired without brand context
All three hooks (`useServiceCategories`, `useServices`, `useItems`) lacked `enabled` guards.
They fired on mount for platform_admin with no `brand_id` in JWT and no `activeBrandId` in store,
producing guaranteed 401s ("Brand context required. Pass X-Brand-Id header.") from Catalog :5001.
Fix: `useEffectiveBrandId()` helper reads brand_id from JWT first, falls back to brandStore;
`enabled: !!brandId` added to all three hooks.

### Bug 2 — client.ts: 401 interceptor triggered token refresh on brand-context errors
The interceptor treated ALL 401s as token-expiry and called `POST /auth/refresh`.
Brand-context 401s caused a storm: 3 catalog queries × multiple retry = 12+ refresh calls per load.
Fix: detect `errorMessage.UnauthorizedAccessException` containing "brand context" in response body;
skip refresh and propagate directly.

## Key Architectural Notes (applies to both apps)

- platform_admin JWT has NO `brand_id` claim — brand context must come from X-Brand-Id header (BrandSwitcher → brandStore).
- All other user types (brand_admin, store_admin, store_staff) carry brand_id in JWT; X-Brand-Id is derived automatically.
- Catalog service (port 5001) strictly enforces brand context on ALL /api/v1/admin/* endpoints.
- One axios instance per backend service; stores are `persist`-ed to localStorage.
- `authStore` key = `lg-admin-auth` (admin-web), `lg-pos-auth` (pos-web); `brandStore` key = `lg-admin-brand` / `lg-pos-brand`.
- Token refresh: single-flight with pending-queue pattern; clears auth + redirects to /login on repeated true auth 401.
- `@/*` path alias maps to `src/*`; Tailwind v4 uses `@import "tailwindcss"` in index.css.
- pos-web Button component has a `size="touch"` variant (h-14, rounded-xl) for tablet POS use.
- Confirm login creds: `admin@laundryghar.local` / `Admin@123` (platform_admin) — works for both apps.
