---
name: project-pos-web-qa
description: pos-web live browser QA (2026-06-13) — port-swap gotcha, order-status map mismatch, cash-book POST dup-entry, TZ Today-filter bug; verified flows + selectors.
metadata:
  type: project
---

# pos-web Live Browser QA (2026-06-13)

Consolidated stack: Core :5050 (identity), Operations :5002 (catalog/orders), Commerce :5005 (commerce/finance). Login admin@laundryghar.local/Admin@123 = platform_admin (needs brand+store picked via 2 topbar comboboxes). Brand 5b375161-...; store db417624-0982-4ea2-9b89-3930c62c2232 "Laundry Ghar Mumbai Central".

## CRITICAL ENV GOTCHA
**pos-web runs on :5173, admin-web on :5174** — the OPPOSITE of what QA briefs often state. Verify by page <title>: "Laundry Ghar POS" vs "admin-web". Both vite. pos-web .env points IDENTITY=5050, CATALOG/ORDERS=5002, FINANCE/COMMERCE=5005 (correct, no dead-port refs in code; only a stale "5006"/"5001" code COMMENT in client.ts + CashBookPage.tsx header — cosmetic).

## Confirmed Defects

**POS-D1 (MAJOR, frontend/senior-react-architect):** `nextStatuses()` in `pos-web/src/lib/utils.ts` is OUT OF SYNC with backend `OrderStateMachine.cs`. FE map: `placed:['received','cancelled']`. Backend `[OrderStatus.Placed]=[PickupScheduled, Cancelled, Disputed]`. A walk-in `placed` order's Advance-Status card offers a "Received" button → backend 422 "Invalid status transition". Walk-in orders shouldn't route via pickup_scheduled at all — needs product decision on the walk-in lifecycle, then sync the FE map (or have BE return allowedTransitions and render those instead of a hardcoded map).

**POS-D2 (MINOR→MAJOR, backend/dotnet-backend-developer):** POST `/api/v1/admin/cash-books/{id}/entries` returns the just-added entry DUPLICATED in the response `entries[]` (e.g. 4 returned, 3 distinct). Subsequent GET `/cash-books/{id}` is correct (no dup). FE setQueryData(detail, postResponse) then renders 2 rows with same key → React "two children with same key" console error + a ghost duplicate entry row until refetch. Fix in the add-entry handler (entity re-added to an already-tracked collection / double-include from reloaded aggregate).

**POS-D3 (MAJOR, both layers — TZ boundary):** OrdersPage "Today" preset computes date in Asia/Kolkata; backend filters `placedAt` (UTC) treating dateFrom/dateTo as UTC day bounds. Server clock at test = 2026-06-12T21:34Z = 2026-06-13 03:04 IST. Order placed 21:32Z shows as "13 Jun 03:02am" in detail (IST) but is EXCLUDED from "Today" (=2026-06-13 UTC window) — appears only under 2026-06-12 / "Last 7 days". Cashier places order, taps Orders → empty "Today" list. Fix: backend should accept a tz or interpret dateFrom/dateTo in the store/brand local tz (IST), OR FE should send UTC-converted bounds.

## Verified PASS (live)
- Login → /new-order redirect; brand+store auto-pick via 2 comboboxes; clean console.
- Full sale: new customer (QA-UI) → category(Dry Clean)→service(Dry Cleaning)→item(Shirt x2)→Place Order → #LG-2026-LGS-MUM-001-000005, subtotal ₹300 +18% GST ₹54 = ₹354. PaymentModal cash → Record payment OK.
- Orders "Last 7 days" lists all 6; OrderDetail renders (totals, line items, status history, Advance Status, Documents).
- Invoice: order in `placed` → POST /invoice 422 + GET /invoice.pdf 404 → UI shows graceful "isn't available yet… needs ready/delivered/closed" amber message (CORRECT degradation, not a bug).
- CashBook: open book (opening 0) → add entry OK (persists, GET shows it).
- Wildcard /random-xyz → redirects /new-order. No dead-port (5001/3/4/6/7/8/9) requests anywhere.

## Selectors / automation notes
- pos-web login fields: `#identifier`, `#password`. Store pick: 2x `button[role=combobox]` in topbar, pick `[role=option]` first.
- Customer create: `#customerSearch`, `#newPhone`, placeholder "First name"/"Last name", button text "Create customer".
- Catalog: category chips = `.flex.flex-wrap.gap-2 > button`; service = last `button[role=combobox]`; items = `div.grid > button`.
- Payment: `#payAmount`, button "Record payment". CashBook: `#entryAmount`,`#entryDesc`, "Add Entry"; "Open Cash Book".
- Playwright: run from admin-web dir (`require('playwright')`), pos-web has NO playwright in node_modules.

## Leftover QA data
- Customer QA-UI Walkin +919869350764 (id cc0bdc29-b9bb-432b-9041-82807085ac31) — no UI delete path in pos-web (customers are admin-web concern).
- Order #LG-2026-LGS-MUM-001-000005 (id 4bb9d721-...) placed+cash-paid ₹354, status `placed`.
- Cash book 743e30fd-... (store db417624) open with QA-UI entries (123.45, 10, 7.77). pos-web has no entry-delete UI.
