---
name: project-qa-r3-live-browser
description: QA-R3 live run findings (2026-06-12/13) — admin-web+pos-web smoke AND Android emulator customer-mobile+rider-mobile QA; defects, verified behaviors, gotchas.
metadata:
  type: project
---

# QA-R3 Android Emulator Run (2026-06-12/13)

## Customer Mobile + Rider Mobile — Checklist Results

Emulator: emulator-5554 (snap_pixel API 35). Customer Metro :8083, Rider Metro :8082 --offline. Backend healthy.

### Customer Items
| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | Onboarding skip | PASS | 75_customer_launch.png |
| 2 | Home screen loads | PASS | 91_cust_home_logged.png — services grid, Grand Launch Offer, CTA |
| 3 | Booking / slot picker | FAIL | CUST-BUG-01 — slot picker permanently "Something went wrong" |
| 4 | Place booking | BLOCKED | CUST-BUG-01 |
| 5 | Reschedule | BLOCKED | CUST-BUG-01 |
| 6 | Tracking 25s poll | BLOCKED | No order placed |
| 7 | Profile edit name | Code-PASS | profile.tsx hooks-before-returns correct; EditForm firstName/lastName mutation wired. App test blocked by CUST-BUG-03 |
| 8 | Address stale cache | FAIL | CUST-BUG-02 — staleTime 60s, no refetchOnWindowFocus |
| 9 | Offers empty state | Code-PASS | offers.tsx renders EmptyState "No offers right now" when list.length===0 |
| 10 | DPDP consent new user | Code-PASS | otp.tsx isNewCustomer→ConsentModal. DB: customer 94a99275 created for +919800000077. App blocked by rate-limit constraint |

### Rider Items
| # | Item | Result | Notes |
|---|------|--------|-------|
| 11 | Login | PASS | +919876543211 (NOT +919800000001 — that number doesn't exist). 65_rider_after_otp.png |
| 12 | Duty toggle | PASS | 66_rider_duty_toggle.png |
| 13 | Tasks list | PASS | Empty state correct, API returns []. 68_rider_tasks.png |
| 14 | Pickup inspection | NOT TESTED | No assignments in DB |
| 15 | ETA pill | NOT TESTED | No active tasks |
| 16 | Off-duty shift summary | PASS | 66_rider_duty_toggle.png — time/tasks/earnings/pending all shown |
| 17 | Earnings drill-down | PASS | 72_earnings_expanded.png — #QA-A1-RETEST-001 +50 expands correctly |
| 18 | Offline mode | PASS | 74_offline_tasks.png — amber banner "You are offline — task updates will retry when connected" |

### Defects

**CUST-BUG-01 — MAJOR:** Slot picker always errors. `useDeliverySlots` in `src/hooks/useOrders.ts`: `enabled: !!storeId && !!date` but pickup screen passes `storeId=undefined`. Fix: `enabled: !!date`.

**CUST-BUG-02 — MINOR:** Address picker stale cache. `useAddresses` staleTime=60_000, no refetchOnWindowFocus. Fix: reduce staleTime or add refetchOnWindowFocus:true.

**CUST-BUG-03 — CRITICAL:** `my-orders.tsx` line 226 — `useMemo` after early returns at lines 218-219. React hooks violation. Crashes tab navigator on Orders tab visit. Confirmed by user screenshot. Blast radius: crashes Profile + Wallet tabs too.

**CUST-BUG-04 / RIDER-BUG-01 — MINOR:** 429 rate-limit displayed as "That code is incorrect or has expired" (fallback string when 429 body is empty). Fix: `src/api/auth.ts` verifyOtp catch — check `err.response?.status === 429` explicitly.

**RIDER-BUG-02 — COSMETIC:** Tasks header shows "RIDER · YOUR ZONE ZONE" — zone name duplicated. Likely `${zoneLabel} ZONE` concatenation bug.

### Key Test Data
- Existing customer: +919800000050
- New customer created this session: +919800000077 (id: 94a99275, 2026-06-13)
- Actual rider: +919876543211, rider code QA-RIDER-001
- WRONG number in original checklist: +919800000001 (does not exist in DB)
- DB name: laundry_ghar_db, schema: identity_access.users (not "laundryghar" / "Users")

### QA Infra Constraint
Identity auth rate limiter: 10 req/60s per IP (FixedWindow). Emulator (10.0.2.2→localhost) and curl share the same bucket. Exhausts quickly in automated QA. Fix for dev: raise PermitLimit to 30 or exempt 127.0.0.1 in appsettings.Development.json.

### Critical Gotchas
- Re-apply software renderer after every force-stop: `adb shell setprop debug.hwui.renderer skiasoft`
- adb device coords 1080x2340; screenshots display at 923x2000 (multiply by 1.17)
- Customer OTP payload: {phone, brandCode} — NOT {identifier, identifierType, purpose}
- Rider OTP payload: {identifier, identifierType:"phone", purpose:"login"}
- OTP resend cooldown: 60s (ResendCooldownSeconds in OtpSettings.cs)

---

# QA-R3 Live Browser Run (2026-06-12)

Wave-4 live QA against admin-web :5173 + pos-web :5174 after full gap-analysis remediation round.

**Why:** Verifying Wave-2 (admin-web/pos-web) remediation completeness before release decision.
**How to apply:** Start next QA cycle from these findings, not from scratch.

## Confirmed PASS

- All 15 admin module routes render cleanly, no console errors
- FormDrawer: Escape closes (R3-AW-1 done), body.overflow=hidden confirmed
- Catalog items ActionMenu: Edit + Delete present; edit drawer opens with image section
- Catalog categories/services: Delete IS in ActionMenu (via dispatchEvent probe) — R3-AW-2 done
- Dashboard KPIs render; 401 from useStores is transient race, clears after brand resolves
- Settings page loads fully; Payments panel shows masked secrets; webhook URL visible (R3-WEB-2 done)
- POS: 3 date presets + search field on Orders (R3-POS-1 done)
- POS Offline: banner appears/dismisses correctly (R3-POS-5 useNetworkStatus done)
- POS new-order: 3 catalog items render after store+service selection

## Defects Requiring Fix

### DEF-R3-1: Dashboard useStores 401 race (P3/Minor, owner: aw)
- `DashboardPage.tsx:506` — `useStores({ pageSize: 100 })` NOT gated by `enabled=Boolean(activeBrandId)`
- Fires before brand auto-select resolves, hits 401, clears in ~2s
- Fix: add `enabled` prop matching the other dashboard queries

### DEF-R3-2: Catalog Categories tab click triggers edit drawer (P2/Major, owner: aw)
- Default tab is `useState<Tab>('categories')` — clicking "Service Categories" tab button opens CategoryEditDrawer for first row
- Blocks automation and may confuse users clicking the tab
- Overlay blocks subsequent interaction; ESC via document.dispatchEvent closes it
- Root cause: tab button click at x≈1228,y=123 somehow propagates to first row onRowClick

### DEF-R3-3: R3-AW-3 Subscription drawer actions — unverified (P1, needs manual check)
- Automation cannot trigger `<tr>` React onClick — verified via Playwright limitation
- DataTable uses React synthetic onClick on TR, not triggered by Playwright click or dispatchEvent
- Manual check required: click a customer-subscription row → does detail drawer open with Cancel/Pause?

### DEF-R3-4: POS Cash Book — close flow needs open book with entries (P2 WARN/by-design)
- "Close & Lock Cash Book" button is disabled when no entries or book empty
- To test: select store → open book → add entry → close
- R3-POS-3 variance UI IS in code (expectedClosing, varianceReason) — confirmed by code review

### DEF-R3-5: POS E2E — customer mandatory, automation incomplete (WARN)
- Place Order disabled until customer selected via CustomerLookupModal
- Customer selected via button text "Select customer…" → modal search → select/create
- E2E flow: store select → service select → item → "Select customer…" button → lookup/create → Place Order

## Automation Gotchas

- DataTable `<tr>` React onClick NOT triggered by Playwright `.click()` or `.dispatchEvent()` — use row-level buttons or manual testing for drawers
- Catalog tab buttons: regular click triggers row edit drawer — use `.dispatchEvent('click')` on ActionMenu kebab buttons to bypass overlay
- Admin login field: `id="identifier"` (not type="email")
- Settings API loads async — wait ≥4s after navigation before checking panel content
- POS requires: store selection (topbar) → service combobox → items appear → customer lookup → Place Order enables
- POS Cash Book requires store selected AND an open book before Close button enables
