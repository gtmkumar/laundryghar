---
name: gap-r2-customer-mobile
description: GAP Analysis Round 2 fixes applied to customer-mobile — DOC-3, MOB-1/3/4/6/7/8/9/10/12/13/17 + premium polish
metadata:
  type: project
---

GAP R2 work shipped in customer-mobile on 2026-06-11. tsc clean, 115 tests green.

**DOC-3 (Delete Account):**
- API: `requestAccountDeletion`, `getAccountDeletionRequest`, `cancelAccountDeletion` in `src/api/catalog.ts`
- Hooks: `useRequestAccountDeletion`, `useAccountDeletionRequest`, `useCancelAccountDeletion` in `src/hooks/useCatalog.ts`
- UI: `DeleteAccountSection` component in `app/(app)/(tabs)/profile.tsx` — shows grace-period card when pending, or menu item to initiate
- FAQ text updated in `app/(app)/help.tsx` to match actual flow (Profile → scroll → Delete account)
- i18n keys added: `profile.deleteAccount`, `deletionPendingTitle/Message`, `cancelDeleteTitle/Message/Confirm`, `deleteRequested*`, `deleteRequestCancelled*`

**MOB-3 (Profile email):**
- `useCustomerProfile()` added to `src/hooks/useCatalog.ts` (queries Catalog `/customer/profile`)
- `profile.tsx` reads `catalogProfile?.email` instead of hardcoded `''`
- Mutation `onSuccess` invalidates `['customer','profile']` so email refreshes immediately

**MOB-1 (Live pickup slots):**
- `app/(app)/booking/pickup.tsx` completely replaced: `SlotGrid` calls `useDeliverySlots(undefined, dayIso)`
- storeId omitted (unknown pre-order); endpoint returns brand-level slots
- 7-day picker (was 5); slot UUID is real from backend, stored in `BookingSlot.windowStart/windowEnd`
- `BookingSlot` type extended with optional `windowStart`/`windowEnd` in bookingStore.ts

**MOB-1 stub removal (pay.tsx):**
- Removed `slotHourMap` stub; now uses `slot.windowStart ?? '09:00:00'` / `slot.windowEnd ?? '21:00:00'`
- `slotId` sent as real UUID (no more `startsWith('s-')` guard)

**MOB-4 (Wallet guard):**
- `pay.tsx`: wallet option disabled + opacity-50 when `wallet.balance < total`
- "Insufficient balance" banner above Pay button; Pay button disabled/grayed
- Warning haptic on empty cart, error haptic on booking failure

**MOB-6/MOB-19 (Notifications):**
- Home bell navigates to `/(app)/(tabs)/my-orders` (was `/offers`)
- `useOrderDetail` + `usePickupRequestDetail` now have `refetchInterval: 30_000`
- Push foreground handler (`handleForegroundNotification`) invalidates order/pickup queries
- `setPushQueryClient(queryClient)` called at module level in `app/_layout.tsx`

**MOB-7 (Delivery OTP):**
- `OrderDto` type extended with optional `deliveryOtp?: string | null`
- `DeliveryOtpCard` component in tracking/[id].tsx: large selectable code card, tap-to-copy, appears when `status === 'out_for_delivery' && order.deliveryOtp`
- Backend note: `deliveryOtp` field exists on the Order entity but NOT yet surfaced in the customer `OrderDto` — backend team needs to add it to `GetMyOrderByIdQuery` response

**MOB-8 (Booking reset):**
- `confirm.tsx` "Back to Home" calls `resetBooking()` + `clearCart()` before navigate
- `items.tsx` `useEffect([], [])` resets booking + cart if `confirmed` is stale from prior booking

**MOB-9 (Haptics):**
- `expo-haptics` installed (npm install --legacy-peer-deps)
- `src/lib/haptics.ts` created: `hapticTap`, `hapticImpact`, `hapticSuccess`, `hapticError`, `hapticWarning`
- Success haptic on confirm mount, impact on pay press, error on booking failure, warning on empty cart

**MOB-10 (Skeletons):**
- `src/components/ui/Skeleton.tsx` created: `Skeleton` primitive (reanimated shimmer), `SkeletonHomeScreen`, `SkeletonOrderList`
- Home + my-orders tabs use skeleton instead of `ScreenLoader`

**MOB-12 (Home interactions):**
- Address chip now `onPress → /(app)/addresses`
- `ServiceTile` passes `{ serviceId: id }` params to `/(app)/booking/items` (items.tsx ready to receive but doesn't filter yet — serviceId param plumbed, filtering is deferred)
- Dead "Dry Clean" filter chip removed from `items.tsx`

**MOB-17 (i18n tracking banners):**
- All hardcoded English strings in `tracking/[id].tsx` replaced with `t()` keys
- New keys: `tracking.bannerDelivered/InProcess/ReadyBy/PickupDate/ItemsCollected/OrderAtStore/PickupCancelled/NoResponse`

**Premium polish:**
- `confirm.tsx`: `DetailRow` with `copyable` prop — order number shows copy icon, copies to clipboard (ToastAndroid on Android)
- WhatsApp row upgraded to tappable Pressable (opens `wa.me`), olive card with forward arrow
- i18n: `confirm.whatsappTitle` added

**Why (re deliveryOtp backend dep):** field exists on entity but OrderDto doesn't expose it to customer. Card renders `null` until backend adds the field — no breakage, graceful no-op.

**How to apply:** When touching customer-mobile pickup/booking flow, remember real slot UUIDs are now stored. If a backend change surfaces `deliveryOtp` in CustomerOrderDto, the UI auto-activates.
