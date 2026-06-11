---
name: live-e2e-june2026
description: Full platform live E2E test 2026-06-11 after gap-fix initiative — A1-A5 backend + rider/customer mobile + iOS smoke results and open defects
metadata:
  type: project
---

Full live E2E test run on 2026-06-11. DB seeded via psql (1 franchise, 1 store, 1 customer QA-CUST-001 +919876543210, 1 rider QA-RIDER-001 +919876543211, 1 order out_for_delivery with COD 550.00).

## Open Defects

### DEF-A1 (Critical / P1) — Rider delivery completion API crashes with DB constraint violation
- File: `laundryghar.Logistics/Application/RiderSelf/RiderTaskQueries.cs`
- Line 424: `PaymentPurpose = "order_payment"` — invalid; valid values: 'order', 'package', 'wallet_topup', 'tip', 'adjustment', 'refund', 'royalty'
- Line 433: `Status = "completed"` — invalid; valid values include 'succeeded' (not 'completed')
- Fix: Change to `PaymentPurpose = "order"` and `Status = "succeeded"`

### DEF-A3 (Major / P2) — Admin order creation crashes with jsonb ~~ jsonb operator error
- File: `laundryghar.Orders/Application/Orders/Commands/CreateOrderCommand.cs`
- Line 53: `.Where(o => o.BrandId == brandId && o.Metadata.Contains(idemSearch))`
- EF Core translates `.Contains()` on jsonb Metadata column to `jsonb ~~ jsonb` which PostgreSQL rejects (error 42883)
- Fix: Use `EF.Functions.JsonContains()` or raw `@>` SQL operator

## Test Results

| Item | Result | Notes |
|------|--------|-------|
| A1 DB side effects (delivery complete) | PASS | Verified via psql: status=delivered, delivered_at set, status_history row, COD payment row (succeeded), outbox event |
| A1 API path | FAIL (DEF-A1) | Cannot call API directly; constraint violation on payment insert |
| A2 H1 idempotency | PASS | Second API call returned 200, no duplicate rows |
| A3 order dedup | FAIL (DEF-A3) | Every order creation attempt returns 500 |
| A4 loyalty ledger | PARTIAL | Worker created 550 points entry; discount columns correct at 0; package path untestable |
| A5 SEC-1 permission gate | PASS | 403 without cms.notification.manage, 200 with it |
| B rider mobile Android | PASS | Full login→OTP→home→tasks flow; QA-RIDER-001 loaded; Done(1)/Tasks(0) correct |
| B customer API | PASS | OTP send+verify+/me all correct; token_use=customer, brand_id, isNewCustomer=false |
| B customer mobile UI | PARTIAL | Login screen rendered (cream/olive theme); SnapAccount on same emulator blocked full flow |
| C iOS smoke | PASS | customer-mobile bundle loaded in Expo Go on iPhone 17 Pro sim; SDK 52, runtime 2.0.0; 60fps |

## DB Constraint Lessons (for future seeding)
- `users.user_type_check`: only 'staff', 'rider', 'admin' (not 'customer')
- `riders.employment_type_check`: 'employee' not 'permanent'
- `riders.vehicle_type_check`: 'two_wheeler' not 'bike'
- `riders.kyc_status_check`: 'verified' not 'approved'
- `orders.order_type_check`: 'standard' not 'laundry'
- `commerce.payments.amount_due`: generated column, cannot be inserted
- `order_status_history` FK: composite (order_id, order_created_at) must match exact stored timestamp
- Partitioned orders table: orders_p20260601 is June 2026 partition; ON CONFLICT not usable without partition index

## Rider JWT requirement
Rider needs a `user_scope_memberships` entry with `scope_type = 'franchise'` pointing to a franchise with a valid `brand_id`. Without this, JWT has no `brand_id` claim and Logistics endpoints return 401.

## Mobile test env notes
- Rider Metro: port 8083, customer Metro: port 8082
- ADB reverse needed for each service port (5050, 5004, 5007 for rider; 5050, 5001, 5002, 5005, 5007 for customer)
- iOS simulator: `exp://localhost:8082` works directly (no ADB needed)
- Multiple Expo Go projects on same Android emulator conflict — SnapAccount on 8081 grabs focus when Expo Go transitions
- Force-stop SnapAccount (`am force-stop com.snapaccount.app`) before loading LaundryGhar apps

**Why:** Coordinator-level post-initiative E2E validation.
**How to apply:** When retesting after DEF-A1/DEF-A3 fixes, re-run PATCH /rider/tasks/{id}/status and POST /admin/orders with real HTTP calls — no psql workaround needed.
