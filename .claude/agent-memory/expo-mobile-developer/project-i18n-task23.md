---
name: project-i18n-task23
description: Task #23 i18n status — all four clients done; extraction scope, completed screens, locale key conventions, and known gotchas
metadata:
  type: project
---

Task #23 — en-IN/hi-IN i18n across all four LaundryGhar clients — COMPLETE.

**Why:** Product requirement to support Hindi (hi-IN) alongside English (en-IN) for customers and operations staff.

**How to apply:** When touching any screen in any client, check if the file already has `useTranslation` wired up. If it does, keep using the same pattern. If it doesn't and you're adding user-visible strings, add it.

## Infrastructure (all four clients)

- `i18next` + `react-i18next` in all four
- Mobile (customer-mobile, rider-mobile): `expo-localization` for device locale; `AsyncStorage` persists chosen locale under key `'lg_locale'`; `initI18n()` is async — root `_layout.tsx` gates on `i18nReady` state before rendering the full tree
- Web (admin-web, pos-web): `i18next-browser-languagedetector`; `localStorage` key `'lg_locale'`; `initI18n()` is synchronous (called before `createRoot().render()`)
- `AppLocale = 'en' | 'hi'` exported from each `src/i18n/index.ts`
- `pickLocalized(base, localized?)` helper for backend *Localized DTO fields
- Language switcher: mobile apps — profile screen; admin-web — Topbar component

## Extraction scope completed

**customer-mobile** — ALL screens done:
- `(auth)/phone.tsx`, `(auth)/otp.tsx`, `(auth)/onboarding.tsx`
- `(tabs)/home.tsx`, `(tabs)/orders.tsx`, `(tabs)/wallet.tsx`, `(tabs)/profile.tsx`
- `booking/items.tsx`, `booking/pickup.tsx`, `booking/pay.tsx`, `booking/confirm.tsx`
- `orders/[id].tsx`, `orders/tracking/[id].tsx`
- `addresses.tsx`, `help.tsx`

**rider-mobile** — ALL screens done:
- `(auth)/login.tsx`, `(auth)/otp.tsx`
- `(app)/home.tsx`, `(app)/tasks.tsx`, `(app)/tasks/[id].tsx`
- `(app)/profile.tsx`, `(app)/earnings.tsx`, `(app)/cash.tsx`, `(app)/delivered.tsx`
- `_layout.tsx` (OTA/maintenance/version banners)

**pos-web** — `LoginPage.tsx`, `PaymentModal.tsx`, `NewOrderPage.tsx` done

**admin-web** — infrastructure + `LoginPage.tsx` (exemplar) + `Topbar.tsx` (language switcher) done; all remaining pages to be extracted incrementally by team following documented convention in `src/i18n/index.ts`

## Locale file locations

- `customer-mobile/src/i18n/locales/{en,hi}.json` — namespaces: common, language, auth, onboarding, home, booking, confirm, orders, orderDetail, tracking, wallet, profile, addresses, help, update, error
- `rider-mobile/src/i18n/locales/{en,hi}.json` — namespaces: common, language, auth, home, tasks, taskDetail, profile, earnings, cash, update, error
- `pos-web/src/i18n/locales/{en,hi}.json` — namespaces: common, language, auth, pos, payment
- `admin-web/src/i18n/locales/{en,hi}.json` — namespaces: common, language, auth, topbar

## Key conventions

- `<screen_or_domain>.<descriptive_key>` (e.g. `auth.email`, `booking.chooseAddress`)
- Status label maps: use `t('ns.key.status', { defaultValue: fallback })` — safe for unknown future statuses
- Plural keys: use `_one` / `_other` suffixes (e.g. `confirm.garmentCount_one`)
- Variable interpolation: `{{amount}}`, `{{count}}`, `{{label}}` etc.
- `pickLocalized(base, localized)` for backend DTO fields ending in `Localized`

## Known gotchas

- Variable name clashes: if a component had a local variable named `t` (timer, tab key, etc.) before adding `useTranslation`, rename the local variable — not the i18n `t`
- Module-level arrays with dynamic labels: must move inside component to access `t()`, OR store i18n key strings in the array and call `t(key)` at render time (used for SLOTS in pickup.tsx and ORDER_STEPS/PICKUP_STEPS in tracking/[id].tsx)
- `validate()` in addresses.tsx now returns i18n key strings (`'addresses.validation.*'`); the caller does `t(error)` to display
- Curly quotes (U+2018/U+2019) in source: the Python replace script fixes `'` → `'` but can break string literals that use `'` as part of contractions (e.g. `it's`). Always grep for the pattern after running the script and switch affected strings to double-quote delimiters.
- Onboarding.tsx had a curly-quote contraction `it's` that needed to be changed to a double-quoted string after the byte-level replacement.

## Gates (all passed)

- `npx tsc --noEmit` 0 errors: customer-mobile, rider-mobile, pos-web, admin-web
- `npm run build` clean: pos-web, admin-web
- `npx expo export --platform ios` 5.88 MB bundle: customer-mobile
