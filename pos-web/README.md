# Laundry Ghar POS Web

Touch-optimized walk-in counter POS for store staff and store admins.
Same stack as `admin-web/`: React 19 + Vite + TypeScript + TanStack Query + Zustand + React Router + Tailwind CSS v4 + shadcn-style UI + React Hook Form + Zod + Axios.

---

## Quick start

```bash
# Node v22 required
cp .env.example .env   # fill in service URLs (see below)
npm install
npm run dev            # starts on http://localhost:5173
```

Build for production:

```bash
npm run build          # tsc -b && vite build (0 type errors)
npx tsc --noEmit       # standalone typecheck
```

---

## Service URLs

| Service  | Port | Purpose                            |
| -------- | ---- | ---------------------------------- |
| Identity | 5000 | `VITE_IDENTITY_URL` — staff auth   |
| Catalog  | 5001 | `VITE_CATALOG_URL` — items/pricing |
| Orders   | 5002 | `VITE_ORDERS_URL` — order CRUD     |
| Finance  | 5006 | `VITE_FINANCE_URL` — cash books    |

Configure all four in `.env` (copy from `.env.example`).

---

## Auth

POS uses **staff password login** — the same endpoint as admin-web:

```
POST {IDENTITY_URL}/api/v1/auth/password/login
{ "identifier": "...", "password": "..." }
→ { status: true, data: { accessToken, refreshToken, ... } }
```

JWT claims for store staff include `brand_id`, `store_id`, and `franchise_id` — no manual header selection needed.

**Platform admins** must select a brand and a store via the topbar switchers (same X-Brand-Id flow as admin-web).

Dev credentials: `admin@laundryghar.local` / `Admin@123` (platform_admin — select brand + store in topbar).

---

## Screens

### New Walk-in Order (`/new-order`)
1. Enter customer ID / phone.
2. Select service category → service → tap items from the touch grid.
3. Adjust quantities in the cart panel.
4. Submit → `POST /api/v1/admin/orders` with `channel: "walkin"` — server resolves prices.
5. Confirmation screen shows order number + totals. One-tap to view order or start a new one.

### Today's Orders (`/orders`)
- Lists all orders for the active store filtered to today's date.
- Tap an order to open the detail view.

### Order Detail (`/orders/:id`)
- Full order info: line items, totals, status history.
- **Status advance buttons** — calls `PATCH /api/v1/admin/orders/{id}/status` for allowed transitions (e.g. `placed → received`, `qc → ready`).

### Cash Book (`/cash-book`)
- Open today's cash book (`POST /api/v1/admin/cash-books`).
- Add cash-in / cash-out entries (`POST /api/v1/admin/cash-books/{id}/entries`).
- Running summary (opening balance, total in, total out, expected closing).
- Close end-of-day (`POST /api/v1/admin/cash-books/{id}/close`).

---

## Architecture decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| State management | Zustand persist | Same as admin-web; small, zero boilerplate |
| Server state | TanStack Query | Caching + invalidation + mutation lifecycle |
| Auth token storage | localStorage via zustand/middleware persist | Consistent with admin-web |
| API client | Axios with interceptors | Bearer token, X-Brand-Id, 401→refresh→retry — identical to admin-web |
| Navigation | Bottom tab bar | Thumb-friendly on landscape tablets |
| Touch targets | min 48px h, rounded-xl | WCAG 2.5.5 / Apple HIG |
| Brand context | Same as admin-web: JWT brand_id first, brandStore fallback | No code duplication of logic |
| Store context | posStore (Zustand persist) | POS-specific; separate from admin-web concerns |

---

## File structure

```
pos-web/
├── src/
│   ├── api/
│   │   ├── client.ts       # Axios factory + interceptors + unwrap helpers
│   │   ├── auth.ts         # passwordLogin, logout, refreshTokens
│   │   ├── tenancy.ts      # getBrands, getStores
│   │   ├── catalog.ts      # categories, services, items, price resolve
│   │   ├── orders.ts       # getOrders, getOrderById, createOrder, updateOrderStatus
│   │   └── finance.ts      # getCashBooks, openCashBook, addEntry, closeCashBook
│   ├── stores/
│   │   ├── authStore.ts    # JWT tokens + parsed claims (lg-pos-auth key)
│   │   ├── brandStore.ts   # Active brand for platform admins (lg-pos-brand key)
│   │   └── posStore.ts     # Active store for POS session (lg-pos-store key)
│   ├── hooks/
│   │   ├── useTenancy.ts   # useBrands, useStores
│   │   ├── useCatalog.ts   # useServiceCategories, useServices, useItems, usePriceResolve
│   │   ├── useOrders.ts    # useOrders, useOrder, useCreateOrder, useUpdateOrderStatus
│   │   └── useCashBook.ts  # useCashBooks, useCashBook, useOpenCashBook, useAddCashBookEntry, useCloseCashBook
│   ├── components/
│   │   ├── layout/
│   │   │   ├── AppShell.tsx      # Topbar + main + BottomNav
│   │   │   ├── Topbar.tsx        # Brand switcher + Store switcher + user + logout
│   │   │   ├── BottomNav.tsx     # Touch bottom tab bar
│   │   │   ├── BrandSwitcher.tsx # Platform-admin brand picker
│   │   │   ├── StoreSwitcher.tsx # Store picker / fixed label for store-scoped staff
│   │   │   └── ProtectedRoute.tsx
│   │   ├── ui/               # button, input, label, card, badge, select
│   │   └── shared/           # LoadingState, ErrorState
│   ├── pages/
│   │   ├── auth/LoginPage.tsx
│   │   ├── pos/NewOrderPage.tsx
│   │   ├── orders/OrdersPage.tsx
│   │   ├── orders/OrderDetailPage.tsx
│   │   └── cashbook/CashBookPage.tsx
│   ├── types/
│   │   ├── api.ts      # All DTO + request types matching backend contracts
│   │   └── schemas.ts  # Zod schemas for forms
│   ├── lib/utils.ts    # cn, formatCurrency, formatDate, orderStatusColor, nextStatuses
│   ├── App.tsx         # Router + QueryClientProvider
│   └── main.tsx
```
