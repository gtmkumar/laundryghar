---
name: project-admin-web-scaffold
description: Initial scaffold of admin-web React app for Laundry Ghar — stack choices, folder layout, API integration design, and build status
metadata:
  type: project
---

Admin-web was scaffolded at `/Users/gtmkumar/Documents/source/laundryghar/admin-web/` as the first frontend integration slice.

**Stack**: React 19 + Vite 8 + TypeScript 5 strict + React Router v7 + TanStack Query v5 + Zustand v5 + Axios + Tailwind CSS v4 (via @tailwindcss/vite plugin) + Radix UI primitives + Lucide React + React Hook Form + Zod.

**Build status**: `npm run build` and `npx tsc --noEmit` both pass clean as of scaffold date (2026-06-05).

**Why:** First integration slice only — auth, 3 data screens, typed foundation. No git commits created per spec.

**How to apply:** When extending admin-web, follow the folder structure already established. Add new screens under `src/pages/`, new API calls under `src/api/`, and new TanStack Query hooks under `src/hooks/`.

Key architectural decisions:
- One axios instance per backend service (identityClient, catalogClient, ordersClient), each with shared interceptors.
- `unwrap()` / `unwrapPaginated()` helpers centralize envelope unwrapping.
- Zustand stores use `persist` to localStorage. `authStore` key = `lg-admin-auth`, `brandStore` key = `lg-admin-brand`.
- `X-Brand-Id` header is set from JWT `brand_id` claim first, then from manual brand switcher (platform admins only).
- Token refresh: single-flight with pending-queue pattern; clears auth + redirects on repeated 401.
- `@/*` path alias maps to `src/*`; configured in both vite.config.ts and tsconfig.app.json (using ignoreDeprecations:"6.0" for TS6 baseUrl deprecation).
- Tailwind v4 uses `@import "tailwindcss"` in index.css, not a tailwind.config file.
- chunk-size warning on build is expected (single entry, no code-splitting yet); not an error.
