---
name: project-adminweb-r4
description: admin-web/pos-web live browser QA R4 (2026-06-13) + R5 re-verify — all 6 R4 defects FIXED; port swap; pos-web brand/store-select gotcha
metadata:
  type: project
---

# admin-web Live Browser QA — Round 4 (2026-06-13) + Round 5 re-verify

Live Playwright QA of admin-web/pos-web (consolidated backend: Core 5050, Operations 5002, Commerce 5005).
**Why:** End-to-end UI verification after consolidation + recent backend envelope changes, then targeted re-verify of fixes.
**How to apply:** Start the next admin-web UI cycle from these corrected facts.

## CRITICAL ENV FACT — ports
- **admin-web on :5174**, **pos-web on :5173** (title "LG POS", redirects to /new-order). Login: role-select screen FIRST (click "Super Admin"), THEN `#identifier`+`#password` (admin@laundryghar.local / Admin@123) + "Sign in".
- **pos-web GOTCHA:** /orders fires `/v1/admin/orders` with NO storeId on first load → 401 "Failed to load orders." until a **brand AND store are selected** via topbar dropdown buttons ("Select brand…", "Select store…" — custom buttons, NOT <select>). After selecting brand "Laundry Ghar" + store "Laundry Ghar Mumbai Central", query gains storeId and returns 200. The pre-context 401s are benign-but-ugly; pre-existing, not a fix-target. "Today" preset = today IST.

## R5 RE-VERIFICATION 2026-06-13 — ALL 6 R4 DEFECTS FIXED (PASS)
1. **Package tier** (was DEF-AW-R4-2): drawer Tier select = exactly Silver/Gold/Diamond/Platinum/Custom, default "silver", NO bronze. Create with default tier → POST 201. Archive via confirm-dialog "Archive package" → DELETE 200, row gone.
2. **Error surfacing** (was DEF-AW-R4-3): duplicate-coupon create (code FLAT50) → server 409, surfaced BOTH as role=alert toast AND in-drawer red banner "A record with the same value already exists." No more silent failure.
3. **FormDrawer focus trap** (was DEF-AW-R4-4): on open focus moves into dialog; Tab cycles stay inside (30 tabs, never escaped); Escape closes; focus returns to trigger button. FIXED.
4. **Customer delete** (was DEF-AW-R4-5 GAP): /customers ActionMenu now View/Edit/**Delete**; Delete shows confirm dialog ("permanently removes customer ... order history remains"), DELETE 200, row gone. Used it to clean leftover "QA-UI Walkin" (+919869350764) — now deleted.
5. **Analytics Customer-LTV** (was UX note): first column shows NAMES ("QA Customer", "QA-UI Walkin"), no raw UUID; full UUID available as `title` tooltip.
6. **pos-web order #LG-2026-LGS-MUM-001-000005**: Advance Status offers exactly pickup scheduled / cancelled / disputed, NO "Received". Click "Pickup Scheduled" → PATCH /v1/admin/orders/{id}/status 200; status advanced placed→pickup_scheduled (next buttons became pickup assigned/cancelled/disputed).

## Playwright gotchas (carry forward)
- **Controlled React inputs:** `.fill()` sets DOM value but may NOT fire React onChange → form still thinks field empty. Use `.click()` then `.pressSequentially(val,{delay})` for fields the form validates (esp. the coupon Code field, placeholder "WELCOME50"). Locate drawer fields by **placeholder**, not by label-xpath (label `following::input` can grab a checkbox or the page search box).
- Coupon drawer code field = input with placeholder "WELCOME50"; Name="Welcome offer"; Discount="50". Package drawer: Code ph "PKG-SILVER", Name/Localized ph "Silver Package", Price "999", Credit value "1100", Credit multiplier required (no ph, label-xpath).
- Archive/Delete in drawers needs the CONFIRM-dialog button (e.g. "Archive package", "Delete customer"), not just the footer "Archive"/"Delete".
- Session token in-memory; re-login per script run.

## R5 leftover QA data state
- All clean. QA-RV-Pkg-* package created+archived (soft-deleted). Leftover "QA-UI Walkin" customer from R4 now hard-deleted via new UI. Duplicate-coupon negative test created nothing (FLAT50 already existed). One real data mutation: order #...000005 advanced placed→pickup_scheduled (required by test).
