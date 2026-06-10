---
name: project-a11y-task28
description: Task #28 mobile accessibility pass — a11y labels, roles, states added to both mobile apps; a11y locale namespace added to all 4 locale files
metadata:
  type: project
---

Task #28 mobile half COMPLETE (web half handled by sibling agent — do NOT mark task done).

**What was added:**
- `a11y` namespace added to all 4 locale files (customer-mobile en/hi, rider-mobile en/hi) with ~14 keys each. All accessibilityLabel values on translated screens now use t('a11y.*') — never hardcoded English strings.
- customer-mobile: tab bar roles changed to `accessibilityRole="tab"`, FAB label t-based, OtpInput (display-only) wrapped with group accessible label, BrandSplash marked `accessibilityElementsHidden`, StarRow uses t-based labels with `accessibilityRole="radio"`, tracking rating widget stars get role+state, all back/close/add buttons get role, language switcher uses a11y keys, short tab label keys added to home/orders/wallet/profile sections of en.json + hi.json.
- rider-mobile: duty toggle changed from `accessibilityRole="button"` to `accessibilityRole="switch"` + `accessibilityState={{ checked: isOnDuty }}`, tasks content tabs get `accessibilityRole="tab"` + selected state, all back/close/call/message/directions/photo buttons get t-based labels, language switcher uses a11y keys, BrandSplash marked hidden.
- Both Button primitives already had full role/state/label pass-through before this task — no changes needed.
- Both Keypad primitives already had accessibilityRole="button" + accessibilityLabel on each key.
- Both Stepper primitives already had role/label on +/-.
- Addresses form: checkbox/radio labels already correct; add/close/back buttons updated.

**Gates passed:** tsc --noEmit 0 errors in both apps; 115 tests customer-mobile, 63 tests rider-mobile, all green; npx expo export --platform ios clean.

**Contrast findings (report-only, no palette changes):**
- gold-400 (#DBAC3D) on cream (#FEFAF4): ~2.8:1 — below WCAG AA 4.5:1 for normal text; OK for large text (≥18pt bold).
- ink-muted (#7B7A6C) on cream: ~4.2:1 — marginally below AA for small body text.
- olive-100 text on cream: not used as text color.
- text-olive-700 (#4A552A) on white: ~7.1:1 — passes AAA.
- text-white on olive-600/700: passes AAA.

**Why:** WCAG 2.1 AA compliance + VoiceOver/TalkBack usability for laundry service users.
**How to apply:** When adding new pressable icons or status chips, always add t('a11y.*') label. New decorative illustrations/gradients should get importantForAccessibility="no-hide-descendants".
