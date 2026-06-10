---
name: lg-palette-contrast
description: WCAG AA contrast ratios for LaundryGhar brand color tokens (admin-web + pos-web) and which combos fail
metadata:
  type: project
---

LaundryGhar brand tokens defined in `admin-web/src/index.css` (`--lg-green #5C6E2E`, `--lg-amber #E6A23C`, `--lg-cream #F7F5EF`). pos-web has NO LG tokens — it uses stock Tailwind blue-600/gray.

Computed WCAG 2.1 contrast ratios (normal text needs >= 4.5:1):

PASS: lg-green on white 5.64, lg-green on cream 5.17, lg-green-hover on white 7.53, white-on-lg-green button 5.64, blue-600 on white 5.17 (pos), all status pills (amber-800/red-800/green-800/yellow-800 on their -100 tints all 6.3-6.8), red-600 on white 4.83, text-gray-500 on white 4.83, gray-600/700 on white 7.5+/10+.

FAIL (flagged): lg-amber #E6A23C as TEXT on white = 2.19 and white-on-amber = 2.19 — never use amber for text or as a button fill with white text; it is decorative/accent only. text-gray-400 on white = 2.54 and on cream = 2.33 — fails for body text (OK for purely decorative icons/placeholders). text-gray-500 on cream = 4.43 (just under AA; fine for large text).

**Why:** Task #28 web a11y pass. Brand-color decisions (amber) were left as recommendations, not auto-fixed, since they need design sign-off. text-gray-400 body text was fixed case-by-case (241 occurrences in admin, mostly decorative — no blanket swap).
**How to apply:** When adding text, never use lg-amber or text-gray-400 for readable copy on white/cream. Use text-gray-500+ for muted body text. For amber emphasis use a darker token (e.g. amber-800 on amber-100 tint).
