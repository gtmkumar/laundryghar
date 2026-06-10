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

## Counter workflow (Task #12 — shipped)
Full walk-in counter flow added on NewOrderPage + OrderDetailPage. Build/typecheck clean; lint clean on all new files.

Backend gaps found (frontend degrades gracefully around each):
- **No admin create-customer endpoint.** Catalog `AdminCustomerEndpoints` is GET/PUT/DELETE only — no POST. New-customer form ships disabled with a note; customers self-register via app OTP. Lookup uses `GET :5001/api/v1/admin/customers?search=` (matches phone/name/code; `AdminCustomerDto` carries `lifetimeOrders` for hints).
- **No admin/POS offline-payment endpoint.** Commerce `/api/v1/admin/payments` is GET + POST /refunds only; customer-lane initiate/verify is Razorpay + needs a customer JWT. POS payment capture is therefore recorded as a **Finance cash-book `order_payment` entry** (direction +1, paymentMode cash|upi|card) against today's OPEN cash book. PaymentModal requires an open book (won't auto-open a drawer).
- **CreateOrderRequest has no coupon field.** Coupon input ships disabled with "coming soon" tooltip.

Working integrations:
- **Weight entry:** `ServiceDto.pricingModel === 'per_kg'` is the marker (enum also has per_item/per_sqft/per_pair/per_side/flat). Weight-mode cart lines take a decimal kg input instead of +/- qty.
- **Invoice PDF:** `POST :5002/.../orders/{id}/invoice` (gated to ready|delivered|closed → 422 otherwise) then `GET .../invoice.pdf` (404 if none). `useOpenInvoicePdf` best-effort generates, fetches as authed Blob, opens via object URL. Verified live: 422 on `received`, 201+valid PDF on billable.
- **Receipt + garment tags:** browser `window.print()` against a `.print-area` shown only via `@media print` in index.css (80mm @page). Components in src/components/print/. Counter receipt is an HTML slip ("tax invoice follows on delivery"), NOT the PDF. Tags expand by qty (per-item) or 1-per-kg-line; Barcode component copied from admin-web.

Patterns: print idiom = mount one print payload + `setTimeout(window.print, 50)`; Modal in components/shared (no Radix Dialog); customerLabel() helper in lib/utils (displayName→name→phone→code).

See [[project-admin-web-scaffold]] for the template this mirrors.
