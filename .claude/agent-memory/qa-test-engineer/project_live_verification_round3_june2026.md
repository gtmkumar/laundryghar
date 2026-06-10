---
name: project-live-verification-round3-june2026
description: Live verification run on 2026-06-10 for tasks #7, #9, #16, #18, #21 — verdicts and defects found in Round 3
metadata:
  type: project
---

Verification run date: 2026-06-10. Stack restarted 4× during run (auto-dispatch dry-run + royalty dry-run + revert). Final state: 9×200 + Worker alive, AutoDispatchEnabled=false, RoyaltyGenerationEnabled=false.

**Task #7 (Push tokens):** PASS.
- Customer: POST :5001/api/v1/customer/push-token 200; DB row user_type=customer, is_active=true. Re-POST same token → 200, no dup (unique index). DELETE with body → 200, is_active=false (hotfix regression confirmed).
- Rider: POST :5004/api/v1/rider/push-token 200; DB row user_type=rider, is_active=true. Re-POST 200. DELETE 200, is_active=false.
- Validation: empty token → 422; platform='web' → 422 ("platform must be 'ios' or 'android'").
- Note: DB CHECK constraint allows 'web' but API validator explicitly rejects it — validation layer is correct.

**Task #9 (Duty + load + auto-dispatch):** PASS-WITH-DEFECT.
- Duty toggle: PATCH :5004/api/v1/rider/duty {onDuty:true} → 200 {onDuty:true, openTaskCount:0}; DB is_on_duty=true, on_duty_since set. Off → false, on_duty_since null. ✓
- CurrentLoad: POST :5002/api/v1/admin/pickup-requests/{id}/assign → 201, current_load +1; PUT assignment to cancelled → current_load 0. ✓
  - NOTE: delivery_assignments.store_id is NOT NULL FK. Platform admin has no StoreId, handler uses `_user.StoreId ?? Guid.Empty` → FK violation (DEFECT-R3-A). Worked around by setting store_id on the pickup request directly before assignment.
- Auto-dispatch dry-run: FAIL with crash on every poll cycle.
  - DEFECT-R3-B (Critical): AutoDispatchService.cs lines 111-112 and 140-141 call `.GeoLocation!.Y` / `.X` on geography(Point,4326) columns. EF Core + NetTopologySuite translates .Y/.X to `ST_Y(geography)` / `ST_X(geography)`. PostGIS `st_y()/st_x()` only accept geometry type, not geography → Npgsql.PostgresException 42883 "function st_y(geography) does not exist" on every poll cycle. Auto-dispatch is completely non-functional even with AutoDispatchEnabled=true.
  - Assignment logic and outbox event code are correct by inspection; crash is in the data-fetch LINQ projection before any rider-matching or insert.
  - Fix: cast to geometry in LINQ: `(double?)EF.Functions.Something(p.Address.GeoLocation)` or change entity mapping from geography to geometry, or project address fields without .Y/.X (null-safe fallback).

**Task #16 (Rider screens backend):** PASS.
- GET :5004/api/v1/rider/payouts?days=30 → 200 {totalPayout:0, avgPerTask:0, days:30, breakdown:[]}. DB-consistent: 8 completed assignments but all payout_amount=null → 0 aggregate is correct.
- GET :5004/api/v1/rider/cash/summary → 200 {cashInHand:0.0, lastSettlementAt:null, recentSettlements:[]}. DB-consistent: 0 rider_settlements rows.
- Failure flow: PATCH :5004/api/v1/rider/tasks/{id}/status {status:failed, reason:customer_unavailable, note:qa probe} → 200; DB delivery_assignments.cancellation_reason=customer_unavailable, notes=qa probe; current_load decremented from 1→0. ✓
- Route correction: the status update is PATCH /api/v1/rider/tasks/{id}/status (not /tasks/{id}).

**Task #18 (Royalty):** PASS-WITH-NOTES.
- GET :5006/api/v1/admin/royalty-invoices → 200 paginated list. ✓
- POST .../generate for May 2026 (franchise LGF-DLF4) → 201 draft, invoice ROY-20260610-0001. ✓
- Duplicate generate same period → 422. ✓
- POST .../issue → 200 status=issued. ✓
- POST .../record-payment: WRONG field name — body must use {amountPaid:N} not {amount:N}; using {amount:N} → 422 "AmountPaid must be greater than 0". Once correct, full payment → 200 status=paid, amountDue=0. ✓
- Worker royalty dry-run: triggered for May 2026 period; generated=8 (all remaining franchises), skipped=1 (LGF-DLF4 already had invoice), failed=0; 8 royalty.invoice_generated outbox events confirmed in DB, status=published. ✓
- Minor serialization DEFECT-R3-C: generate response JSON shows 2 identical calculation objects. DB has 1 row. Root cause: GenerateRoyaltyHandler.cs line 186 — `invoice.Calculations.Add(calcLine)` called after SaveChanges; EF tracking already loaded calcLine into invoice.Calculations during SaveChanges, so Add() creates a second reference. Cosmetic only, data is correct.
- Admin UI: /royalty route in App.tsx line 71, identity_access.modules row (key=royalty, route=/royalty, status=active). ✓

**Task #21 (Pickup queue):** PASS.
- GET :5002/api/v1/admin/pickup-requests → 200, includes PKP-2026-5B37-000001 with cartItems=[Shirt×2, Trousers×1], paymentPreference=wallet. ✓
- POST .../pickup-requests/{id}/assign {riderId} → 200, delivery_assignments row created (id=02c08332), pickup_request status=assigned. ✓ (required store_id to be set on pickup request first — see DEFECT-R3-A)
- GET :5002/api/v1/admin/delivery-slots → 200, 73 slots. ✓
- POST .../delivery-slots → 201 new slot (id=6250fcc9, 2026-06-20 pickup, capacity=15). ✓
- PUT .../delivery-slots/{id} → 200 capacity updated to 20. ✓

**Defects summary:**
- DEFECT-R3-A (Major): `_user.StoreId ?? Guid.Empty` in CreateDeliveryAssignmentHandler.cs:26 and AssignPickupHandler.cs:161; same pattern in AutoDispatchService.cs:243. Platform admins (no store scope) cannot assign deliveries through normal flow — FK violation on store_id. Pickup requests without store_id also affected. Fix: make store_id nullable in delivery_assignments (schema change) OR resolve store from franchise/pickup context before insert.
- DEFECT-R3-B (Critical): AutoDispatchService.cs:111-112, 140-141 — `.GeoLocation!.Y` / `.X` on geography-typed columns fails with PostGIS error "function st_y(geography) does not exist". Auto-dispatch is entirely non-functional when enabled.
- DEFECT-R3-C (Minor): GenerateRoyaltyHandler.cs:186 — duplicate calculation object in POST /generate response JSON. DB is correct.

**Config flags confirmed reverted (final state):**
- AutoDispatchEnabled=false
- RoyaltyGenerationEnabled=false (not present in file = default false)

**Artifacts left in DB (2026-06-10 Round 3):**
- engagement_cms.push_tokens: ExponentPushToken[test-cust-1], ExponentPushToken[test-rider-1] (both is_active=false)
- finance_royalty.royalty_invoices: 9 invoices ROY-20260610-0001 through 0009
- order_lifecycle.delivery_assignments: 3 QA test assignments (216a786c cancelled, 02c08332 assigned, 1ae28bbc failed/qa probe)
- order_lifecycle.delivery_slots: 6250fcc9 (2026-06-20 test slot, capacity=20)
- order_lifecycle.pickup_requests: 08e137ea status=assigned (was pending; progressed via #21 assign test)

**Why:** Round 3 final QA sweep before sprint close.
**How to apply:** #7/#16/#18/#21 can be marked completed. #9 PASS for duty/load; auto-dispatch sub-task BLOCKED by DEFECT-R3-B — mark #9 as pass-with-open-defect (core duty/load pass; Worker auto-assign broken). DEFECT-R3-A affects manual assignment flow (workaround: set store_id on pickup request or pass store-scoped admin token). DEFECT-R3-B is the most critical — file against Worker/AutoDispatchService and SharedDataModel geography column mapping.
