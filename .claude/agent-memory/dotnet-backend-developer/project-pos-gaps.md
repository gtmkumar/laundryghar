---
name: project-pos-gaps
description: Task #31 POS backend gaps — admin create-customer, offline payment record, coupon on order create; permission codes seeded, cash-book pattern, coupon math source
metadata:
  type: project
---

Task #31 closed three POS backend gaps.

**Why:** POS UI (Task #12) shipped with these features disabled, waiting on backend.

**Permissions added (db/patches/pos_permissions.sql):**
- `customer.create` — granted to platform_admin, brand_admin, franchise_owner, store_admin
- `payment.record` — same four roles
- risk_level CHECK accepts: low, normal, high, critical (not 'medium')

**Admin create customer (Catalog):**
- `POST /api/v1/admin/customers` with `permission:customer.create`
- Customer code generation cloned from `CustomerOtpVerifyHandler.GenerateUniqueCodeAsync` (10-char ABCDEFGHJKLMNPQRSTUVWXYZ23456789)
- Phone uniqueness enforced in-handler (friendly 422) before PG unique violation
- Admin-created customers have `PhoneVerifiedAt = null` (can verify via OTP later)
- DPDP: all marketing opt-ins default false

**Offline payment (Commerce):**
- `POST /api/v1/admin/payments` — new file `RecordOfflinePaymentCommand.cs`
- Idempotency key = `offline:{orderId}:{amount:F2}:{ref|noref}` (deterministic)
- Cumulative guard: `alreadyPaid + newAmount > grandTotal` → 422
- Updates `order.AmountPaid` and `order.PaymentStatus` (paid/partial) via shared DbContext
- Cash-book posting mirrors `SettleRiderCodHandler.PostCashBookEntryAsync` exactly — opens day's full_day book if absent, skips if closed, best-effort no-op
- Commerce port = 5005; added `VITE_COMMERCE_URL` + `commerceClient` to pos-web

**Coupon on order create (Orders):**
- `CreateOrderRequest` gains optional `couponCode` (default null) — additive
- Coupon math source: mirrors `ValidateApplyCouponHandler` in Commerce exactly
- Coupon validation + discount applied BEFORE tax base computation (reduces GST base)
- Redemption row + `coupon.CurrentUsageCount++` inside the same Npgsql execution strategy transaction as the order insert
- `OrderDto` gains `discountTotal` field (breaks API contract — consumers updated)
- `order.DiscountTotal = couponDiscount`, `order.CouponDiscount = couponDiscount`, `order.CouponId`, `order.CouponCode` set on entity

**How to apply:** When touching order totals or payment flows, note: (1) coupon discount reduces taxable amount, (2) cash-book entries for counter payments are now server-side not client-side, (3) `OrderDto.discountTotal` is now always present.
