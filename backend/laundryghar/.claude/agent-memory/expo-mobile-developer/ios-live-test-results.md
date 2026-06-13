---
name: ios-live-test-results
description: iOS sim live test 2026-06-13 — customer + rider full flows PASS; rider login on iOS KICKED the Android session (single-session); COD cod_amount gap reconfirmed
metadata:
  type: project
---

iOS Simulator live test passed 2026-06-13 (both apps, Expo Go 2.32.18, iPhone 17 Pro, consolidated backend). Customer: onboarding→OTP login→home (greeting QA)→price list (4 real items)→booked PKP-2026-5B37-000007 (Sun 14 Jun 9-11 AM, Shirt·Laundry Wash ₹50 COD)→tracking date correct→notifications→logout, all PASS, no iOS layout defects (safe areas, keypad, scroll all clean). Rider: OTP login→on-duty→task card real data→start→arrived→inspection WITH photo via library→COD collect→drop→complete; earnings ₹80→₹120; 5 GPS pings landed in logistics.rider_location_pings from iOS.

Findings:
- SINGLE-SESSION: logging the rider into iOS kicked the Android rider app back to the sign-in screen (verified via adb screenshot). Whether kick-on-new-login is intended needs a product decision; customers may multi-device.
- COD gap reconfirmed on a fresh assignment (681690f8): admin assign leaves cod_amount NULL, complete leaves cod_collected_at NULL (open task #16).
- Rider duty state did not carry from Android (iOS started OFF duty though Android run left it ON) — possibly cleared when the Android session was kicked, or device-local.
- Cosmetic-only console warnings in both apps: require cycle src/api/client.ts <-> src/api/auth.ts, expo-notifications Expo Go limitation, new-arch always-on notice.

QA data end-state: PKP-2026-5B37-000007 completed (nothing left to cancel); rider 9829cf7b ON duty, 3 tasks done today ₹120; customer +919999000077 logged OUT on iOS; rider logged IN on iOS, logged OUT on Android.

**Why:** Closes the iOS half of the platform-parity pass that [[customer-mobile-android-test]] and [[rider-mobile-android-test]] left pending.
**How to apply:** Treat iOS as parity-verified; remaining mobile work is the COD persistence fix and the single-session product decision. See [[ios-simulator-testing]] for how to drive the sim.
