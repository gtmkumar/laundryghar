---
name: project-gap-r3-rm
description: R3 gap analysis RM bundle — what was done, deferred, and key design decisions
metadata:
  type: project
---

R3-RM-1 (inspection entry-point): was already wired in tasks/[id].tsx (line 409). Added session-local `inspectionDone` state + badge: the "Inspect garments" Pressable turns green with a checkmark when visited; confirm flow remains unblocked whether inspection is skipped.

R3-MOB-1 (Android keyboard): Added `KeyboardAvoidingView` (behavior `padding` iOS / `height` Android) + `Platform` imports to: inspection/[id].tsx (wrapping ScrollView + submit bar), tasks/[id].tsx failure-reason modal. Login screen was already wrapped. OTP screen uses custom Keypad (no system keyboard). Profile screen has no TextInputs.

R3-RM-2 (offline robustness):
  - app/(app)/_layout.tsx now calls `flushOfflineQueue()` on: (a) auth hydration (isHydrated + accessToken effect), (b) offline→online transition via `useNetworkStatus` + `prevConnected` ref.
  - `useOfflineQueueFlush` already handles AppState 'active' — no second listener added to layout (would double-fire).
  - Proof photo offline queue: `uploadProofPhoto` failure now calls `enqueue({ taskId, status: '__photo__', note: '<uri>|<mime>' })`. The flush hook detects `status === '__photo__'` and calls `uploadProofPhoto` instead of `updateTaskStatus`. Sentinel string exported as `OFFLINE_PHOTO_STATUS` from useOfflineQueueFlush.

R3-RM-3 premium polish (all done):
  a. Shift summary modal: home.tsx replaces plain Alert.alert with a Modal showing time online (derived from `onDutySince` in dutyStore), tasks completed, earned today, pending count. Confirm/Stay online buttons. `formatElapsed(ms)` helper.
  b. ETA countdown on task detail: `computeEta` useCallback ticks every 60s via setInterval. Only shows when windowEnd is set and window is <60 min away. Amber at ≤15 min, red when past.
  c. Cumulative rating: profile.tsx shows `ratingCount` in the stat card label (e.g. "Rating (42)"). Both `ratingAverage` and `ratingCount` are already on RiderDto from /rider/me.
  d. Task card LayoutAnimation: tasks.tsx enables `UIManager.setLayoutAnimationEnabledExperimental` on Android; `switchTab()` calls `LayoutAnimation.configureNext(easeInEaseOut)` before setTab.
  e. Maps fallback: `openDirections()` in tasks/[id].tsx wraps `Linking.openURL` in try/catch; on failure shows Alert.alert action sheet with Google Maps / Apple Maps / Copy address options.

Notification badge (Low): bell has no badge number (correct — no real feed yet). notifications.tsx empty state is clear and honest. No changes needed.

**Why:** R3 gap analysis wave-3 RM bundle, 2026-06-12.
**How to apply:** When touching any of these areas in future sessions, verify these patterns are still in place before adding new logic.
