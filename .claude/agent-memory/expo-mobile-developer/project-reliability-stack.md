---
name: project-reliability-stack
description: Task #24 reliability stack — Sentry, expo-updates OTA, version gate wired into both mobile apps
metadata:
  type: project
---

Task #24 shipped the reliability stack to both customer-mobile and rider-mobile (Expo SDK 52).

## Packages
- `@sentry/react-native ~6.10.0` — SDK-52 compatible, installed via `npx expo install`
- `expo-updates ~0.27.5` — SDK-52 compatible, installed via `npx expo install`

## Feature flags added to FEATURES in both constants/config.ts
- `crashReporting: true`
- `otaUpdates: true`
- `versionGate: true`

## New files (identical pattern in both apps)
- `src/lib/sentry.ts` — `initialiseSentry()`, `captureError()`, `withSentry` re-export
- `src/lib/otaUpdates.ts` — `checkAndFetchOtaUpdate()` returning restart fn or null
- `src/lib/versionGate.ts` — `evaluateVersionGate(rows)` → `{kind: 'none'|'force'|'soft'}`
- `src/components/ErrorBoundary.tsx` — class-based; retry bumps key; reports to Sentry
- `.env.example` — documents EXPO_PUBLIC_SENTRY_DSN; no real .env committed

## app.config.ts changes (both)
- Added `updates: { url: https://u.expo.dev/<projectId>, checkAutomatically: 'ON_LOAD', fallbackToCacheTimeout: 0 }`
- Added `runtimeVersion: { policy: 'appVersion' }`
- Added `sentryDsn: process.env.SENTRY_DSN` to extra
- Added `'@sentry/react-native'` to plugins array

## Boot order in root _layout.tsx (both apps)
1. `initialiseSentry()` — at module-load time, covers first-render crash
2. `bootstrapApiAuth()` — axios interceptors
3. hydration effects (auth/duty/queue stores)
4. OTA check — after isHydrated, via `checkAndFetchOtaUpdate()`, non-blocking, shows banner
5. `AppConfigGate` — maintenance + force/soft version gate using `evaluateVersionGate()`
6. `ErrorBoundary` — wraps Stack, reports caught errors to Sentry
7. Root default export wrapped with `withSentry()`

## Version gate DB state (2026-06-10)
- customer app_settings: `min_version="1.0.0"`, `force_update_version="0.9.0"` → no gate active (both below customer v2.0.0)
- rider app_settings: NO ROWS seeded yet → gate silently passes
- `store_url` key not seeded → force-update modal has no "Update Now" button until seeded
- Expected contract: `{"min_version":"X","force_update_version":"Y","store_url":"https://...","maintenance_mode":false}`

## Sentry DSN sources (priority order)
1. `Constants.expoConfig.extra.sentryDsn` (EAS secret SENTRY_DSN → app.config.ts)
2. `process.env.EXPO_PUBLIC_SENTRY_DSN` (.env.local / CI)
- No DSN → fully disabled; __DEV__ → always disabled; flag off → disabled

## Gates passed
- `npx tsc --noEmit` 0 errors in both apps
- `npx expo export --platform ios` in customer-mobile: clean (5.69 MB bundle)
