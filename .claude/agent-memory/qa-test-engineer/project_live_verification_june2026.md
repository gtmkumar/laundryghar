---
name: project-live-verification-june2026
description: Live verification run on 2026-06-10 for tasks #2, #6, #8, #10, #14 — verdicts and defects found
metadata:
  type: project
---

Verification run date: 2026-06-10. All 9 services healthy, Worker alive.

**Task #2 (Refund constraint):** PASS at DB level. No captured payments existed in payments table (all 0 rows). DB-level proof: commerce.check_refund_cap trigger fires ERRCODE=check_violation when cumulative refunds exceed original payment amount. Valid partial refund and first-of-two partial refunds both pass. Admin refund route: POST :5005/api/v1/admin/payments/refunds, permission=payment.refund.

**Task #6 (Notification end-to-end):** PASS-WITH-NOTES. Pipeline works: order.status_changed events are picked up by NotificationMappingService within one poll cycle (5s), notifications_outbox gains ORDER_PICKED_UP_WHATSAPP row, NotificationDispatcherService logs [NOTIFY] and marks sent. Two template-quality defects confirmed LIVE:
- Defect (a) CONFIRMED: order_number variable = "54d89395" (UUID prefix, first 8 chars of aggregate_id.ToString()) instead of "LG-2026-LGS-MUM-001-000002". Root cause: NotificationMappingService.cs BuildVariables() line 372 — payload.OrderNumber is null because the order.status_changed event payload does NOT include an orderNumber field (only orderId/toStatus/fromStatus). Fallback: `evt.AggregateId.ToString()[..8]`.
- Defect (b) CONFIRMED: pickup_date is "" so body renders "scheduled for ." Rendered live: "Hi K. Iyer, your Laundry Ghar order #54d89395 pickup is scheduled for .". Root cause: same BuildVariables() line 376 — payload.PickupDate is null (not in event payload). Fix: Orders service must include orderNumber and pickupDate in status_changed event payload.
- pickup_assigned and pickup_in_progress correctly produce no outbox rows (not in template map).
- Customer opt-ins all true → whatsapp channel selected correctly.

**Task #8 (Booking persistence):** PASS. POST /api/v1/customer/pickup-requests returns 201 with PKP-2026-5B37-000001, cartItems echoed (2 lines), paymentPreference=wallet. DB row confirmed: requested_items jsonb populated. GET list and GET by id both return self-filtered results. Validation: 51 items → 422 ("at most N"); qty=0 → 422 ("at least 1"). Note: ServicesRequested is Guid[] required field — must send empty array [] not omit.

**Task #10 (File uploads):** PASS.
- (a) Warehouse: POST .../garment-inspections/{id}/photos multipart → 201, s3Key="{brand}/{area}/{uuid}.jpg", GET list returns 1 photo, GET stream → 148 bytes, content-type=image/jpeg, bytes match.
- (b) Rider PoD: POST :5004/api/v1/rider/tasks/{id}/proof-photo → 200, delivery_assignments.proof_photo_s3_key and proof_photo_taken_at set in DB; admin GET :5004/api/v1/admin/rider-tasks/{id}/proof-photo → 200 image/jpeg 148 bytes.
- (c) PDF → 422 "Photo must be image/jpeg, image/png, or image/webp."; >10MB → 422 "Photo must be ≤ 10 MB."
- (d) Files land at /tmp/laundryghar-uploads/{brand_nodash}/{area}/{uuid_nodash}.ext — confirmed by ls.

**Task #14 (DPDP erasure):** FAIL. Worker finds the eligible request and initiates erasure but crashes with Npgsql.PostgresException 22001 "value too long for type character varying(20)". Root cause: CustomerAnonymizer.cs line 51 — `customer.PhoneE164 = $"+00deleted{tombstoneId}"` generates 22-char string ("{+00deleted}=10 + 12 hex chars=22) but customers.phone_e164 is varchar(20). Fix: shorten prefix to "+0del" (5) + tombstoneId[..12] = 17, or increase column to varchar(25), or truncate tombstoneId to 8 chars. The API flow (POST→pending, idempotent POST→same id, DELETE→cancelled+active, POST→new pending) all work correctly. RetentionSweepService cycles confirmed in Worker log (every 300s dev). grievance_officer rows exist in engagement_cms.mobile_app_config (4 rows active).

**Artifacts left in DB (2026-06-10):**
- order_lifecycle.pickup_requests: 08e137ea (PKP-2026-5B37-000001, pending) — harmless
- order_lifecycle.garment_inspections: 68a7862d — harmless  
- customer_catalog.customers: c622fff9 (+919800000777, deletion_requested) — stuck due to erasure bug; needs manual cleanup or bug fix
- customer_catalog.account_deletion_requests: b4c820db (cancelled), be619293 (pending) — harmless
- engagement_cms.notifications_outbox: 2 rows (ORDER_PICKUP_SCHEDULED_WHATSAPP, ORDER_PICKED_UP_WHATSAPP) — harmless
- Local files: /tmp/laundryghar-uploads/{brand}/proof/ and /inspections/ — ephemeral
- Test orders advanced: LG-2026-LGS-MUM-001-000002 is now at picked_up status (was pickup_scheduled)

**Why:** This was the final verification sweep before marking tasks complete.

**How to apply:** #2, #6, #8, #10 can be marked completed. #14 FAIL — erasure bug blocks completion. Link defects: (a) orderNumber in event payload, (b) pickupDate in event payload, (c) phone_e164 tombstone overflow.
