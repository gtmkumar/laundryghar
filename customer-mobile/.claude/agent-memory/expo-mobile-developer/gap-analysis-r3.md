---
name: gap-analysis-r3-cm
description: R3 gap analysis items completed/deferred for customer-mobile on 2026-06-12
metadata:
  type: project
---

Completed 2026-06-12.

**Why:** Wave 3 mobile gap closure per GAP_ANALYSIS_R3.md.

**How to apply:** QA must verify these on-device before marking the wave closed.

## Completed items

**R3-BE-2 (coupon at checkout):** CouponBar component in pay.tsx. Quick-pick chips from Commerce /coupons. Validate via Orders POST /customer/coupons/validate. Discount line shown in order summary. Fake wallet 10% discount removed. couponCode threaded into CreatePickupRequestRequest. Haptic on success/error.

**R3-BE-3 (reschedule):** New screen `app/(app)/orders/reschedule/[id].tsx`. useReschedulePickup mutation. Reschedule button on PickupTracking + PickupCard (pending/no_response/rescheduled statuses). 4xx (non-reschedulable) shows friendly alert.

**R3-BE-6 (DPDP consent UI):** ConsentModal in otp.tsx, shown only when isNewCustomer=true. Backend endpoint EXISTS (POST /customer/consents/grant on Catalog service). Grants purpose=service_delivery, privacyPolicyVersion=1.0. Best-effort call — navigation proceeds even on failure.

**R3-CM-1 (tracking freshness):** useOrderDetail + usePickupRequestDetail poll every 25s while focused + order is active. useOrderTracking also focus+status gated. Push tap handler already invalidates (was already implemented in lib/pushNotifications.ts).

**R3-MOB-1 (Android keyboard):** KeyboardAvoidingView on pay.tsx (wraps ScrollView), addresses.tsx (AddressForm), profile.tsx (screen-level).

**R3-MOB-2 (EAS config):** eas.json created at customer-mobile root, mirrors rider-mobile. TODO comment in app.config.ts for EAS_PROJECT_ID UUID.

**R3-CM-2 (polish):**
- Pull-to-refresh: home, wallet (FlatList), my-orders (already had it), offers (already had it)
- EmptyState enhanced with optional `action` CTA prop; my-orders uses it to drive into booking
- Haptics: coupon chip select, payment method selection, reschedule confirm, day/slot selection
- Typography tokens: `src/lib/typography.ts`
- a11y: already had payment radio roles and image accessibilityLabels
- Mutation loading: pay.tsx shows ActivityIndicator on confirming; reschedule screen same

## Backend dependencies verified present
- POST /customer/coupons/validate — EXISTS in CustomerOrderEndpoints.cs
- POST /customer/pickup-requests/{id}/reschedule — EXISTS
- POST /customer/consents/grant — EXISTS in CustomerEndpoints.cs (Catalog)
- CouponCode field on CreatePickupRequestRequest — EXISTS in PickupDtos.cs

## QA must verify on-device
1. Coupon validation: enter valid code → discount appears in summary → submit → pickup request has coupon stored
2. Coupon validation: enter invalid/expired code → inline error, no discount applied
3. Reschedule: from PickupTracking and my-orders PickupCard; confirm changes date in backend
4. Reschedule on non-reschedulable status (e.g. completed) → friendly error shown
5. DPDP consent modal: appears on first login only (isNewCustomer=true); cannot dismiss; requires checkbox
6. Android: keyboard does not cover coupon input on pay screen, address form fields, profile edit
7. Tracking polling: reopen tracking screen mid-order → updates within 25s
8. Pull-to-refresh: home, wallet, my-orders all show olive tint spinner
