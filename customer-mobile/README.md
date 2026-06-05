# Laundry Ghar — Customer Mobile App

React Native + Expo SDK 52 + TypeScript.

---

## Quick start

```bash
cd customer-mobile
npm install
npx expo start          # Expo Go / dev build
```

iOS simulator: `npx expo start --ios`
Android emulator: `npx expo start --android`

Type-check only (no backend needed):
```bash
npx tsc --noEmit
```

---

## Environment / service URLs

Service base URLs are configured in `app.config.ts` under `extra` and resolved at
runtime via `expo-constants`. To override for local dev, set env vars before
`expo start`:

| Variable            | Default                  | Service              |
|---------------------|--------------------------|----------------------|
| `IDENTITY_API_URL`  | `http://localhost:5000`  | Identity (auth/OTP)  |
| `CATALOG_API_URL`   | `http://localhost:5001`  | Catalog (services, price-list, profile) |
| `ORDERS_API_URL`    | `http://localhost:5002`  | Orders (pickup, tracking) |
| `COMMERCE_API_URL`  | `http://localhost:5005`  | Commerce (packages, wallet, loyalty) |
| `DEFAULT_BRAND_CODE`| `LG-MAIN`                | Brand resolution      |

Example (macOS/Linux):
```bash
IDENTITY_API_URL=https://dev.laundryghar.com:5000 \
CATALOG_API_URL=https://dev.laundryghar.com:5001 \
npx expo start
```

For physical device dev builds, replace `localhost` with your machine's LAN IP.

---

## Auth flow

1. **Onboarding** (`/(auth)/onboarding`) — static carousel, skip → Phone.
2. **Phone entry** (`/(auth)/phone`) — validates Indian mobile, calls
   `POST {Identity}/api/v1/customer/auth/otp/send`.
3. **OTP entry** (`/(auth)/otp`) — 6-digit code, calls
   `POST {Identity}/api/v1/customer/auth/otp/verify` → receives
   `{ accessToken, refreshToken, isNewCustomer }`.
4. Tokens stored in **expo-secure-store** via Zustand auth store.
5. All subsequent requests carry `Authorization: Bearer <accessToken>`.
6. On 401: interceptor attempts one token refresh via
   `POST {Identity}/api/v1/customer/auth/refresh`, retries original request.
   On second 401: logout (clear tokens, redirect to onboarding).
7. Logout: calls `POST {Identity}/api/v1/customer/auth/logout` (best-effort),
   clears SecureStore.

Brand is resolved server-side from the `brand_id` claim inside the JWT.
Send `brandCode: "LG-MAIN"` in OTP requests (default from config).

---

## API client architecture

`src/api/client.ts` creates one `AxiosInstance` per microservice with shared:
- `Authorization: Bearer` request interceptor
- 401 → refresh → retry response interceptor (coalesced, no duplicate refresh calls)

`unwrapSingle<T>()`, `unwrapList<T>()`, `unwrapPaginated<T>()` helpers normalise
the `{ status, data, message }` envelope. All throw `ApiError` on `status=false`.

Per-service modules:
- `src/api/auth.ts` — Identity service
- `src/api/catalog.ts` — Catalog service
- `src/api/orders.ts` — Orders service
- `src/api/commerce.ts` — Commerce service

---

## Screens implemented

| Route | Wired to |
|-------|----------|
| `/(auth)/onboarding` | Static slides (CMS TBD) |
| `/(auth)/phone` | `POST /customer/auth/otp/send` |
| `/(auth)/otp` | `POST /customer/auth/otp/verify` |
| `/(app)/(tabs)/home` | `GET /customer/catalog/services` |
| `/(app)/(tabs)/price-list` | `GET /customer/catalog/categories` + `GET /customer/catalog/price-list` |
| `/(app)/(tabs)/my-orders` | `GET /customer/orders` (paginated) |
| `/(app)/(tabs)/profile` | `GET /customer/auth/me` |
| `/(app)/orders/[id]` | `GET /customer/orders/{id}` + cancel |
| `/(app)/orders/tracking/[id]` | `GET /customer/orders/{id}/tracking` |

---

## State management

- **Auth**: Zustand (`src/store/authStore.ts`) — tokens in SecureStore, customer info in memory.
- **Server state**: TanStack Query v5 — query keys in `src/hooks/useCatalog.ts` + `useOrders.ts`.

---

## Adding a new screen

1. Add the route file under `app/`.
2. Add typed API function in the relevant `src/api/*.ts` module.
3. Add a TanStack Query hook in `src/hooks/`.
4. Use `ScreenLoader`, `ErrorState`, `EmptyState` for async states.

---

## EAS build profiles (to be configured)

```json
// eas.json
{
  "build": {
    "development": { "developmentClient": true, "distribution": "internal" },
    "preview":     { "distribution": "internal" },
    "production":  {}
  }
}
```

Set service URLs as EAS secrets:
```bash
eas secret:create --scope project --name IDENTITY_API_URL --value https://api.laundryghar.com
```
