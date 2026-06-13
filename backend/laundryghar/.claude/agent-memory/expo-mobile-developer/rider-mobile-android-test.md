---
name: rider-mobile-android-test
description: Rider-mobile Android RE-TEST (2026-06-13 PM) — R1-R6 PASS (real task payload, start/arrived PATCH, lifecycle completes, GPS ping 200, COD label, off-duty UX); NEW gap COD collection not persisted (assign doesn't set cod_amount)
metadata:
  type: project
---

Rider-mobile Android targeted RE-TEST passed 2026-06-13 (evening). R1-R6 all PASS live:
task card shows real customer name/address/COD (QA Customer Android, 1 QA Street, Collect ₹100); Start → delivery_assignments.status=started, I've arrived → arrived+arrived_at; skip-inspection → Mark COD collected → drop → complete advances pickup_requests.status to "completed" (no longer stuck at assigned); POST /rider/location/ping → 200 {accepted:1} and row lands in logistics.rider_location_pings; CTA is payment-aware (live: "Mark COD collected" at ₹100 due; no-due branch at app/(app)/tasks/[id].tsx ~line 736); off-duty home keeps "View today's tasks" enabled with hint and tasks empty-state copy is duty-aware (on: "You're on duty — new tasks will appear", off: "Go on duty to start receiving").

NEW defect (MED, backend): COD collection is never persisted. Admin POST /admin/pickup-requests/{id}/assign creates the assignment with cod_amount NULL (doesn't copy the pickup's COD estimate), the app's collect PATCH sends only {status:'collected'}, so cod_collected_at stays NULL and no finance cash entry is written — rider cash-in-hand is untracked. UI ₹100 comes from the tasks-today projection, not the assignment row.

Minor: Metro still logs the "New Architecture not explicitly enabled" warning in Expo Go even though newArchEnabled:true is set in app.config.ts:18 (Expo Go always-on quirk; verify on a dev build).

DB landmarks (consolidated): db name laundry_ghar_db; assignments live in order_lifecycle.delivery_assignments (NOT logistics.rider_assignments — that's shift records); pings in logistics.rider_location_pings (pinged_at, location); cash in finance_royalty.cash_book_entries. Rider API auth: POST /auth/otp/send + /auth/otp/verify with {identifier,identifierType:'phone',purpose:'login',code}.

QA fixtures after retest: rider 9829cf7b (R-20260612-0003) left ON duty, 2 tasks done today ₹80 earned; PKP-2026-5B37-000006 completed; -000004/-000005 cancelled.

**Why:** Rider defect-fix wave verified closed on Android except the new COD-persistence gap; iOS pass pending.
**How to apply:** File/fix the COD cod_amount gap next; use the DB landmarks for verification queries. See [[android-emulator-testing]] and [[customer-mobile-android-test]].
