---
name: project-eslint-setup
description: ESLint wiring for customer-mobile + rider-mobile (Expo SDK 52); eslint 8 + eslint-config-expo 8 legacy .eslintrc, and the npm install constraint
metadata:
  type: project
---

Both Expo SDK 52 apps (customer-mobile, rider-mobile) use **eslint@^8.57.0 + eslint-config-expo@^8.0.1** with a legacy **`.eslintrc.js`** (`extends: ['expo']`), NOT flat config. `lint` script is `eslint src --ext .ts,.tsx`.

**Why:** For SDK 52, `eslint-config-expo@8.0.1` ships only a legacy `.eslintrc`-format config (single `default.js` main, `extends`/`overrides`/`globals` object — no `/flat` export; `/flat` and defineConfig support only arrived in eslint-config-expo v9 for newer SDKs). So eslint 8 + `.eslintrc.js` is the idiomatic pairing. `latest` dist-tag of eslint-config-expo (56.x) is for newer SDKs — do not pull it.

**How to apply:** `npm install` in these apps fails on a pre-existing peer conflict (`@react-native/jest-preset@0.86.0` wants react 19, project pins react 18.3.1). Always install with `--legacy-peer-deps` — the existing node_modules trees were built that way. Lint runs green in both. `npm test` = 130 (customer) / 80 (rider). `npm run typecheck` = `tsc --noEmit`, both clean. Note: rider's `src/types/api.ts` previously had a duplicated block of 4 CMS interfaces (AppBannerDto/OnboardingSlideDto/MobileAppConfigDto/AppSettingsConfigValue) that TS merged silently but `import/export` lint flagged — removed the redundant second copy. See [[project-rider-mobile]] [[project-customer-mobile]].
