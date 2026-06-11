---
name: project-rider-mobile
description: Expo SDK 52 rider Partner v2 app at rider-mobile/; OTP auth, olive/cream/gold brand, gap fixes R2
metadata:
  type: project
---

`rider-mobile/` at repo root. Expo SDK 52 + TypeScript + NativeWind + expo-router v4 + TanStack Query + Zustand + axios.

**Why:** Production rider app for LaundryGhar "Partner v2" redesign.

**Brand colors:** olive (#4A552A), cream (#F3EEE3), gold (#DBAC3D).

**Auth:** 6-digit OTP via POST /auth/otp/send + /auth/otp/verify. Password login also supported on login screen.

**Key stores:** authStore (SecureStore tokens), dutyStore (AsyncStorage, local optimistic source of truth), offlineQueueStore (AsyncStorage), taskOverrideStore (session-only).

**API clients:** logisticsClient (port 5004), identityClient (5050), engagementClient (5007).

**Feature flags:** FEATURES.riderTasksApi, pushNotifications, crashReporting, otaUpdates, versionGate — all in src/constants/config.ts.

**Screens (app/(app)/):** home, tasks, tasks/[id], delivered, profile, earnings, cash, inspection/[id] (DOC-9), notifications (MOB-19).

**Packages not yet installed (added to package.json, need `npx expo install`):**
- expo-haptics ~14.0.1 — type stub at src/types/expo-haptics.d.ts
- expo-network — type stub at src/types/expo-network.d.ts

**GAP_ANALYSIS_R2 Round A implemented (2026-06-11):**
- MOB-5: Camera proof photo — action sheet (Take/Library), expo-image-picker config plugin, iOS NSCamera/NSPhotoLibrary permissions, Android CAMERA + READ_MEDIA_IMAGES.
- DOC-9: Garment inspection screen (app/(app)/inspection/[id].tsx) + API (src/api/inspection.ts). Front photo required, back optional; condition flags; best-effort, pickup not gated.
- MOB-19: Real notifications screen (notifications.tsx) — informative empty state; home bell routes there instead of fake Alert.
- MOB-15: Earnings DayRow tappable — expands to show tasks. Today from cache; historical = pending backend date-range endpoint.
- MOB-9: expo-haptics — success on OTP verified + task complete; error on OTP mismatch; impact(medium) on duty toggle.
- MOB-10: Skeleton shimmer primitive (src/components/ui/Skeleton.tsx, Reanimated). HomeScreenSkeleton + TasksListSkeleton replace ScreenLoader.
- MOB-16: useNetworkStatus (5s poll, expo-network), OfflineBanner — wired at (app) layout level.
- MOB-21: dutyStore.hydrate() reconciles local vs server; syncMismatch banner on home screen.

**tsc:** clean. `npx expo config` parses without errors.

**Deferred (out of scope this round):**
- MOB-11: react-native-maps live task map — needs dev build + map key.
- MOB-15 historical drill-down: date-range tasks endpoint not yet on backend.

**Key conventions:**
- Identity: localhost:5050; Logistics: 5004; Engagement: 5007.
- SecureStore keys: `lg_rider_access_token`, `lg_rider_refresh_token`.
- AsyncStorage key for duty: `lg_rider_duty_v1`.
- `@/*` alias resolves to `src/*`.

[[project-customer-mobile]]
