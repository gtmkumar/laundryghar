---
name: customer-mobile-conventions
description: Stack, folder layout, design tokens, state patterns for customer-mobile
metadata:
  type: project
---

Expo SDK 52, expo-router v4, NativeWind v4, TanStack Query v5, Zustand v5, TypeScript strict.

**Design:** cream/olive/gold palette. `bg-cream`, `text-ink`, `text-ink-muted`, `text-ink-faint`, `bg-olive-700`, `text-gold-400`. Typography tokens in `src/lib/typography.ts`.

**Folder layout:**
- `app/(auth)/` — onboarding, phone, otp
- `app/(app)/(tabs)/` — home, my-orders, wallet, profile
- `app/(app)/booking/` — items, pickup, pay, confirm
- `app/(app)/orders/[id].tsx`, `tracking/[id].tsx`, `reschedule/[id].tsx`
- `src/api/` — one file per microservice (auth, catalog, commerce, orders, engagement, pushNotifications)
- `src/hooks/` — useCatalog, useCommerce, useOrders, useEngagement
- `src/store/` — authStore, bookingStore, cartStore
- `src/lib/` — format, haptics, typography, pushNotifications, sentry, otaUpdates, versionGate
- `src/types/api.ts` — ALL DTO types

**State:** Zustand v5 selector rule — never return fresh arrays/objects from selectors. Server state via TanStack Query.

**API clients:** `src/api/client.ts` — one axios instance per service (identityClient, catalogClient, ordersClient, commerceClient, engagementClient). 401 interceptor with isAuthCall guard (must be preserved). Config from `src/constants/config.ts`.

**Haptics:** `src/lib/haptics.ts` — hapticTap, hapticImpact, hapticSuccess, hapticError, hapticWarning.

**Feature flags:** `FEATURES` in `src/constants/config.ts` — bookingApi=true, walletTopUp=false, pushNotifications=true.

**EAS:** `eas.json` at project root mirrors rider-mobile pattern (dev/preview/production + channels). EAS_PROJECT_ID slug needs real UUID via `eas project:init` by owner.

**i18n:** `src/i18n/locales/en.json` + `hi.json`. Always add new keys to both files.

**R3-CM-1 tracking:** useOrderDetail + usePickupRequestDetail use focus-aware refetchInterval (25s while focused + active status). useOrderTracking takes optional orderStatus param.

**R3-BE-2 coupon flow:** POST /customer/coupons/validate on Orders service (:5002). ValidateCouponForPickupRequest { couponCode, estimatedSubtotal } → CouponPreviewResult { valid, discountPreview, reason }. CouponCode passes through CreatePickupRequestRequest. Wallet 10% fake discount removed — wallet is payment method only.

**R3-BE-3 reschedule:** `app/(app)/orders/reschedule/[id].tsx`. POST /customer/pickup-requests/{id}/reschedule. Reschedulable statuses: pending | no_response | rescheduled. Entry from PickupTracking + PickupCard on my-orders.

**R3-BE-6 DPDP consent:** shown as a modal on otp.tsx when backend returns isNewCustomer=true. Calls POST /customer/consents/grant (Catalog service, /customer/consents/grant). Best-effort — navigation proceeds regardless. Privacy policy version = "1.0".

**R3-MOB-1 keyboard:** KeyboardAvoidingView wraps addresses form (AddressForm), pay.tsx ScrollView, profile screen.
