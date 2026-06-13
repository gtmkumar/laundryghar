---
name: customer-mobile-android-test
description: Customer-mobile Android RE-TEST (2026-06-13 PM) — all 9 fixed defects PASS live (names, date, COD default, back-stack, cancel API, notifications, greeting, mask, logout); one cosmetic wallet-flash note
metadata:
  type: project
---

Customer-mobile Android targeted RE-TEST against the consolidated backend passed 2026-06-13 (evening). C1-C9 all PASS live on emulator:
real price-list names (Shirt · Dry Cleaning etc.) with real itemId/serviceId/displayLabel persisted on the pickup; pickupDate stored 2026-06-14 for a tomorrow booking (no off-by-one, tracking shows "Pickup on 14 Jun 2026"); COD auto-selected when wallet can't cover; hardware back from confirmation lands on tabs; POST /customer/pickup-requests/{id}/cancel → 200 status=cancelled (no in-app cancel UI yet — tracking screen only offers Reschedule); bell → Notifications screen; greeting "QA"; OTP mask 99 ●●●● 0077; logout → login with no onboarding.

Cosmetic note (not filed): the payment screen renders ~1 frame with wallet selected + red insufficient-balance banner before the useEffect in app/(app)/booking/pay.tsx flips to COD once the wallet query resolves. Settled state is correct.

API gotchas hit during the retest: customer OTP verify body field is `code` (not `otp`); CreatePickupRequestRequest.ServicesRequested is Guid[] — passing a slug string gives a bare 400 "Malformed request body".

QA data state after retest: customer +919999000077 "QA Customer Android"; PKP-2026-5B37-000004 and -000005 cancelled; PKP-2026-5B37-000006 (created via API for the rider retest) ended status=completed.

**Why:** Defect-fix wave for customer-mobile is verified closed on Android; iOS pass still pending.
**How to apply:** Treat these defects as regression-test points only. See [[android-emulator-testing]] for emulator driving and [[rider-mobile-android-test]] for the rider half.
