---
name: ios-simulator-testing
description: Gotchas for driving customer-mobile/rider-mobile on the iOS Simulator via fb-idb + Expo Go (taps, permissions, PHPicker, black-screen launch)
metadata:
  type: feedback
---

Rules learned live-testing Expo apps on the iPhone 17 Pro simulator (402x874 pt) with fb-idb (`/opt/anaconda3/bin/idb` + brew `idb_companion`):

- Automation that works: `idb ui describe-all --udid <UDID>` returns the AX tree with frames in POINTS; tap element centers with `idb ui tap x y`. Never tap from screenshot pixel coords — `sips -Z 1000` output scale varies (460- or 345- or 230-wide) and estimates were off by 50+ pt. `idb ui text "..."` types into the focused field.
- First `simctl openurl booted exp://localhost:8081` can leave Expo Go on a pure black screen indefinitely (AX tree = 1 bare Application node) even when Metro has the bundle cached. Fix: `simctl terminate booted host.exp.Exponent`, then re-open the exp:// URL — loads in ~10s.
- PHPicker (Choose from library) runs out-of-process: idb's AX tree sees NOTHING. Tap photos by screen-coordinate estimate (scale = 402 / screenshot-width).
- iOS Simulator has no camera; the rider inspection flow works via "Choose from photo library" (sim stock photos). Expect TWO permission layers: Expo Go's own per-experience dialogs (foreground location, then background location, sometimes repeated) plus iOS system dialogs (notifications, photo "Allow Full Access"). `simctl privacy grant location-always host.exp.Exponent` pre-clears only the OS layer.
- Rider apps' Start/Call buttons inside the task card are NOT individually AX-exposed (whole card = one GenericElement) — tap by visual position; also an a11y gap worth fixing.
- zsh does not word-split unquoted `$coords` in `for c in $coords` — emit one tap command per line or use explicit taps.
- `log show --predicate 'process == "Expo Go"' | grep -iE "console.error|redbox"` is the console-error channel when Metro stdout isn't yours.

**Why:** Each burned 5-15 min during the 2026-06-13 iOS live test (black screen ate the most; wrong-coord taps silently no-opped on "Send OTP").
**How to apply:** Any time driving Expo apps on the iOS simulator. See [[android-emulator-testing]] for the Android twin.
