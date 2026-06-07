---
name: tailwind-font-family-no-unloaded-fonts
description: Never list font names in tailwind.config.js fontFamily unless those fonts are actually loaded via expo-font/useFonts — unloaded named fonts silently break emoji rendering
metadata:
  type: feedback
---

If `tailwind.config.js` `fontFamily.sans` lists a named font (e.g. `['Inter', 'System']`) but no `.ttf`/`.otf` asset is bundled and no `useFonts`/`loadAsync` call exists, NativeWind injects that font-family into Text styles — which causes emoji to render as tofu "?" boxes on iOS because the system emoji font fallback is blocked.

**Why:** Fixed in customer-mobile `tailwind.config.js` — `fontFamily.sans/medium/bold` all listed `'Inter'` as primary but Inter was never loaded. Changed to `['System']` for all three.

**How to apply:** Check `assets/fonts/` and the root `_layout.tsx` for `useFonts` before adding a named font to the tailwind config. If the font isn't loaded, use `'System'` as the sole entry. Emoji-only `<Text>` nodes also need `style={{ fontSize: N }}` + `allowFontScaling={false}` instead of NativeWind `className="text-Nxl"` to ensure the system emoji font is selected on iOS 26 + Hermes.
