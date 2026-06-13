---
name: android-emulator-testing
description: Gotchas for driving customer-mobile/rider-mobile on the Android emulator via adb + Expo Go
metadata:
  type: feedback
---

Rules learned live-testing Expo apps on emulator-5554 (AVD `snap_pixel`, 1080x2340):

- NEVER `lsof -ti:8081 | xargs kill` — qemu holds a client TCP connection to Metro, so this kills the emulator itself. Kill Metro by its own pid only.
- Start Metro with `npx expo start --offline` (CI=1 makes expo demand an EAS login and exit). Launch with `adb shell am start -a android.intent.action.VIEW -d "exp://10.0.2.2:8081" host.exp.exponent`.
- Expo Go APK is cached at `~/.expo/android-apk-cache/` — fresh AVD boots need `adb install -r` of it.
- In-app modals (Add address sheet): both ESC (keyevent 111) and BACK (keyevent 4) dismiss the WHOLE modal and silently discard the form. Never send them while a modal is open; tap the next field directly, the Save button stays reachable.
- Pressing BACK past the app root drops into Expo Go's project list, which shows OTHER Metro servers on the LAN (e.g. a "SnapAccount" project) — blind taps can launch the wrong app. Recover with force-stop + the exp:// deep link.
- Read every screenshot before tapping; screenshots may come back scaled (multiply coords by stated factor to get 1080-wide physical px for `input tap`).
- Dev OTP login: phone field accepts `input text 9999000077`, master OTP 123456 typed on the app's own keypad (not the IME). `input text` drops spaces — use `%s`.
- Screenshots over ~1.5 MB are rejected by the image API ("media removed"); downscale first with `sips -Z 1000 shot.png --out small.png`, then multiply tap coords by the scale factor.
- Returned screenshots vary in scale (1x, 2x, 4x, or odd factors with an explicit "multiply by" note) — always compute physical 1080-wide coords before tapping.
- Emulator camera works for in-app capture: viewfinder shutter bottom-center, then a checkmark confirm screen; virtual scene needs no setup. `adb emu geo fix <lng> <lat>` sets GPS.
- Backend access tokens (admin and rider) expire in roughly 15 min — re-login before any API verification batch instead of reusing a saved token.

**Why:** Each of these burned 5-15 min of recovery during the 2026-06-13 customer-mobile live test (emulator killed once, address form lost twice, wrong app launched once).
**How to apply:** Any time driving an Expo app on the Android emulator via adb.
