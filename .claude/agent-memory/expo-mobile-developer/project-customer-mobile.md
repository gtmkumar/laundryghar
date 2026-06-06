---
name: project-customer-mobile
description: Customer Mobile app scaffold details — stack, service ports, auth flow, envelope shape, and what screens are built
metadata:
  type: project
---

Customer Mobile app scaffolded at `customer-mobile/` (repo root).

Stack: Expo SDK 52, TypeScript strict, NativeWind v4 (pinned 4.1.23), expo-router v4 (file-based), TanStack Query v5, Zustand v5, axios, expo-secure-store.

**Service ports (local dev defaults):**
- Identity: **5050** (not 5000 — app.config.ts has the correct default)
- Catalog: 5001
- Orders: 5002
- Commerce: 5005
- Engagement: 5007
All configurable via env vars (`IDENTITY_API_URL` etc.) surfaced through `app.config.ts` extra → `expo-constants`.

**Metro port:** Start on `--port 8088` (8081 is occupied by an unrelated project). Use `--port 8090` for rider.

**NativeWind/Reanimated pins:**
- NativeWind 4.1.23 — do NOT bump
- Reanimated 3.16.1 — do NOT bump
- `babel.config.js` uses `nativewind/babel` as a PRESET, not plugin; reanimated plugin intentionally omitted

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
- `/(app)/(tabs)/home` — services from Catalog + live CMS banners from Engagement
- `/(app)/(tabs)/price-list` — categories + price-list from Catalog
- `/(app)/(tabs)/my-orders` — paginated orders from Orders
- `/(app)/(tabs)/profile` — GET /me from Identity
- `/(app)/offers` — coupon list from `GET /api/v1/customer/coupons`; accepts optional `couponId` param to auto-scroll to highlighted coupon
- `/(app)/orders/[id]` — order detail + cancel
- `/(app)/orders/tracking/[id]` — order status history timeline

**Banner → Offers deep-link chain (verified E2E 2026-06-06):**
- Banner `couponId` field → `push('/(app)/offers', { params: { couponId } })`
- Banner `promotionId` (no coupon) → `push('/(app)/offers')` (list, no highlight)
- Banner `ctaDeeplink` starting with `/` → in-app `push(deeplink)`
- Banner `ctaDeeplink` with `http(s)://` → `Linking.openURL`
- Banner `ctaDeeplink` with custom scheme (e.g. `laundryghar://orders/new`) → `Linking.openURL` (added fix)
- `offers` is registered as a Stack.Screen (presentation: card) in `(app)/_layout.tsx`

**Validated DTO field names (backend JSON → TypeScript type):**
These were mismatched before 2026-06-06 E2E — the corrected names below are ground truth:
- `CouponDto`: `couponType` (not `discountType`), `maxDiscountAmount` (not `maxDiscount`), `validUntil` (not `expiresAt`)
- `CustomerMeResponse`: `customerId` (not `id`), `phone` (not `phoneE164`)
- `PriceListItemDto`: `basePrice` (not `price`), `displayLabel` (not `itemName`); no `categoryId` on item — bridge via `serviceId` → `ServiceDto.categoryId`
- `ServiceCategoryDto`: `displayOrder` (not `sortOrder`), `status: string` (not `isActive: boolean`)
- `ServiceDto`: `displayOrder` (not `sortOrder`), `status: string` (not `isActive: boolean`)

**OTP dev flow:**
- Code emitted via `LogWarning` to stdout → Aspire DCP captures in `*_out` temp file under `/var/folders/.../aspire-dcpeHR55r/`
- Find with: `grep -r "DEV-OTP" /var/folders/ 2>/dev/null | tail -5`
- Test phone +919876543210 is seeded/auto-provisioned on first OTP send

**Why:** First integration slice per BUILD_PLAN §2.3 / Wave 3 Agent J.
**How to apply:** When adding more screens, follow the pattern: `src/api/*.ts` → `src/hooks/use*.ts` → `app/(app)/...` route file. Use `ScreenLoader`/`ErrorState`/`EmptyState` for async states.
