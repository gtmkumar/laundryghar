---
name: project-pos-web-scaffold
description: POS Web app scaffolded at pos-web/; build and typecheck clean; staff auth + walk-in order + orders list/detail + cash book wired to real API contracts
metadata:
  type: project
---

POS Web scaffold created at `/Users/gtmkumar/Documents/source/laundryghar/pos-web/` (first slice complete).

**Why:** Store walk-in counter POS for staff (store_staff, store_admin); touch-tablet optimized.

**How to apply:** Mirror this app's patterns for any new POS-specific screens. Same api/client interceptor pattern as admin-web.

## Stack
React 19 + Vite + TS + TanStack Query + Zustand + React Router + Tailwind v4 + shadcn-style UI + RHF + Zod + Axios. Node v22.

## Ports (verified from backend endpoints)
- Identity: 5000, Catalog: 5001, Orders: 5002, Finance: 5006

## API contracts used
- Staff login: `POST {Identity}/api/v1/auth/password/login {identifier,password}` → TokenResponse
- Order create: `POST {Orders}/api/v1/admin/orders` with CreateOrderRequest (server resolves prices); channel='walkin'
- Order status: `PATCH {Orders}/api/v1/admin/orders/{id}/status {toStatus,reason,notes,customerNotified}`
- Cash books: `GET/POST {Finance}/api/v1/admin/cash-books`, POST entries, POST close
- Catalog: service-categories, services, items, pricing/resolve

## Architecture decisions
- Bottom tab navigation (not sidebar) — thumb-friendly for landscape tablets
- posStore (Zustand) holds active StoreDto separate from brandStore
- JWT store_id auto-selects store for store-scoped staff; dropdown shown for admins
- Same 401→refresh→retry interceptor as admin-web; financeClient added as 4th service client
- localStorage keys: lg-pos-auth, lg-pos-brand, lg-pos-store (distinct from admin-web's lg-admin-* keys)

## Build status
`npm run build` and `npx tsc --noEmit` both pass with 0 errors.

See [[project-admin-web-scaffold]] for the template this mirrors.
