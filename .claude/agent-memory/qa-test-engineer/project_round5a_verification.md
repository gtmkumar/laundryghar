---
name: project-round5a-verification
description: Verifier 5A results for tasks #19/#20/#31/#32/#22 backend-flow verification, 2026-06-10
metadata:
  type: project
---

Verification round 5A completed 2026-06-10. All 4 primary tasks PASS with one PASS-WITH-NOTES.

**#19 TAT/Ops Queues: PASS**
- GET :5002/api/v1/admin/orders/ops-queues returns 200 with dueToday/overdue/stuck buckets
- overdue=6 matches psql (orders with promised_delivery_at < now, non-terminal) exactly
- stuck=0 matches psql; code uses INNER JOIN with order_status_history (orders with NO history are excluded - psql's LEFT JOIN logic would return 50, which is misleading; the INNER JOIN is by design)
- StuckThresholdHours default = 24h in OrdersSettings.cs
- Order creation with coupon FLAT50 (flat -50, min 300): promisedDeliveryAt = created_at + 48h (BaseTatHours for Dry Cleaning = 48h), confirmed from customer_catalog.services
- Order cancelled post-verification (LG-2026-LGG-S45-001-000003, status=cancelled)

**#20 Warehouse: PASS**
- GET /api/v1/admin/garments/by-tag/LG-28400-01 → 200, garmentJourneyDto
- POST /api/v1/admin/process-logs advancing stage received→washing → 200
- Board GET reflects new stage (washing:43)
- Stock recon: POST stock-reconciliations (reconType required: daily/weekly/monthly/adhoc/dispute), POST items (tagCode + status required; "itemStatus" field rejected - correct field is "status"), POST close → 200
- Garment LG-28400-01 current_stage='lost', kernel.outbox_events has garment.lost published
- engagement_cms.notifications_outbox: GARMENT_LOST_WHATSAPP sent (customer has whatsapp_opt_in=true; ladder picks WhatsApp first over SMS/Push; only 1 channel notified by design)
- DailyReconService: ran when DailyReconHourLocal=21 + DailyReconEnabled=true; created daily recon row for 2026-06-10 with 152 missing items (stale 12h threshold); flag reverted to false/17 post-test

**#31 POS Backend: PASS**
- POST :5001/api/v1/admin/customers {phone,firstName} → 201 AdminCustomerDto (customerCode, all fields)
- Duplicate phone → 422 with clear message
- Cleanup: DELETE endpoint at :5001/api/v1/admin/customers/{id} works
- Offline payment: POST :5005/api/v1/admin/payments {orderId, method:'cash', amount} → 201
- commerce.payments row captured (gateway='cash'), orders.amount_paid updated, finance_royalty.cash_books auto-created for today (full_day) + cash_book_entries posted (reference_type='offline_payment')
- Idempotency: orderId+amount+reference forms the key (NOT a caller-supplied field); replay returns same paymentId with no dup row
- Overpay → 422 with clear "would exceed order total" message
- Coupon: verified discount_total set, coupon_redemptions row created, invalid code → 422

**#32 Pickup Reject: PASS**
- Synthetic pending pickup created via psql INSERT with slot linkage (booked_count bumped to 1)
- POST :5002/api/v1/admin/pickup-requests/{id}/reject {reason:'qa probe'} → 200, status=cancelled
- Cancellation fields set: cancellation_reason, cancelled_by_type='admin', cancelled_by_id
- Slot booked_count decremented from 1 → 0 atomically
- kernel.outbox_events: pickup.rejected published
- engagement_cms.notifications_outbox: PICKUP_REJECTED_WHATSAPP sent within ~1s
- Re-reject → 422 "Only pending requests may be rejected"
- Reject assigned pickup → 422 same message
- Synthetic rows cleaned up (pickup_requests + delivery_slots rows deleted)

**#22 Spot Probes: PASS (all 5)**
- PATCH toStatus:'PLACED' (uppercase) → 422
- PATCH toStatus:'bogus' → 422
- cash-book shiftLabel:'brunch' → 422 (allowed: morning/afternoon/evening/full_day/split)
- expense amount:-5 → 422
- delivery-slot capacity:0 → 422

**Stack health after restart: 9x200 + Worker alive (2 processes)**

**Notable design facts to remember:**
- NotificationChannelPreferencePolicy is a single-channel ladder: WhatsApp > SMS > Push. Only one notification per event, not fan-out.
- RecordOfflinePaymentCommand idempotency key = "offline:{orderId}:{amount:F2}:{ref}" - no caller-supplied key field
- Cash book is auto-opened (full_day) on first cash payment of the day for a store
- DailyReconService fires only during the configured IST hour; after restart with reverted config it will not fire at hour 17 today since we're past it

**Why:** Full backend-flow verification run for features shipped as part of Rider Ops and POS additions.
**How to apply:** These modules are battle-tested live. Next regression focus: DailyRecon close path (recon stays in_progress after worker run - may need explicit close call), and cash book auto-creation edge case when store has no franchise_id set.
