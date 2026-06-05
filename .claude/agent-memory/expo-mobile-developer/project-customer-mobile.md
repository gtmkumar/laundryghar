---
name: project-customer-mobile
description: Customer Mobile app scaffold details — stack, service ports, auth flow, envelope shape, and what screens are built
metadata:
  type: project
---

Customer Mobile app scaffolded at `customer-mobile/` (repo root).

Stack: Expo SDK 52, TypeScript strict, NativeWind v4, expo-router v4 (file-based), TanStack Query v5, Zustand v5, axios, expo-secure-store.

**Service ports (local dev defaults):**
- Identity: 5000  
- Catalog: 5001  
- Orders: 5002  
- Commerce: 5005  
All configurable via env vars (`IDENTITY_API_URL` etc.) surfaced through `app.config.ts` extra → `expo-constants`.

**Default brand code:** `LG-MAIN` (body field on OTP send/verify; server falls back to config).

**API envelope shape (all services):**
- `{ status: boolean, data?: T, message?: { errorTypeCode, errorMessage, responseMessage } }`
- Paginated: `data = { list: T[], hasPreviousPage, hasNextPage }`
- `unwrapSingle`, `unwrapList`, `unwrapPaginated` helpers in `src/api/client.ts`

**Auth flow (OTP-only, no password):**
1. `POST /api/v1/customer/auth/otp/send` — body `{ phone, brandCode? }`
2. `POST /api/v1/customer/auth/otp/verify` → `{ accessToken, refreshToken, isNewCustomer }`
3. Tokens stored in SecureStore (keys: `lg_access_token`, `lg_refresh_token`)
4. 401 → refresh via `POST /api/v1/customer/auth/refresh` body `{ refreshToken }` → new accessToken
5. Concurrent refreshes coalesced (single in-flight promise)
6. Logout: `POST /api/v1/customer/auth/logout` body `{ refreshToken }`

**Customer identity in JWT:** `sub` claim = customerId, `brand_id` claim = brandId. Server self-filters all endpoints by token — never send customerId in URL for customer-facing routes.

**Route structure (expo-router):**
- `/(auth)/onboarding`, `/(auth)/phone`, `/(auth)/otp`
- `/(app)/(tabs)/home` — services from Catalog
- `/(app)/(tabs)/price-list` — categories + price-list from Catalog
- `/(app)/(tabs)/my-orders` — paginated orders from Orders
- `/(app)/(tabs)/profile` — GET /me from Identity
- `/(app)/orders/[id]` — order detail + cancel
- `/(app)/orders/tracking/[id]` — order status history timeline

**Why:** First integration slice per BUILD_PLAN §2.3 / Wave 3 Agent J.
**How to apply:** When adding more screens, follow the pattern: `src/api/*.ts` → `src/hooks/use*.ts` → `app/(app)/...` route file. Use `ScreenLoader`/`ErrorState`/`EmptyState` for async states.
