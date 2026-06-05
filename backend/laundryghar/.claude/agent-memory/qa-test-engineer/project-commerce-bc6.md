---
name: project-commerce-bc6
description: BC-6 Commerce service QA findings — defects found and fixed, risk areas, test patterns
metadata:
  type: project
---

BC-6 laundryghar.Commerce service was QA'd on 2026-06-05. Three defects were found and fixed.

**Why:** BC-6 introduces payment processing, wallet, package, coupon, and loyalty flows. All money-flow paths were audited for atomicity, idempotency, and constraint correctness.

**How to apply:** Re-run the five checks on every PR that touches Commerce handlers.

## Defects Found and Fixed (2026-06-05)

### DEF-1: BeginTransactionAsync incompatible with NpgsqlRetryingExecutionStrategy (CRITICAL)
- **Files affected:** All 4 handlers that opened DB transactions:
  - `Application/Customer/Wallet/CustomerWalletHandlers.cs` (WalletTopUpVerifyHandler)
  - `Application/Customer/Packages/CustomerPackageHandlers.cs` (PurchasePackageVerifyHandler)
  - `Application/Customer/Coupons/CustomerCouponHandlers.cs` (ValidateApplyCouponHandler)
  - `Application/Admin/Wallet/AdminWalletHandlers.cs` (AdminWalletAdjustHandler)
- **Symptom:** HTTP 500 `InvalidOperationException: The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions`
- **Fix:** Wrapped `BeginTransactionAsync` block inside `_db.Database.CreateExecutionStrategy().ExecuteAsync(async () => { ... })` in all four handlers.

### DEF-2: Wrong `transaction_type` value in WalletTopUpVerifyHandler (CRITICAL)
- **File:** `Application/Customer/Wallet/CustomerWalletHandlers.cs`
- **Symptom:** DB check constraint violation — `wallet_transactions_transaction_type_check` rejects `'top_up'`; constraint only allows `'topup'`.
- **Fix:** Changed `TransactionType = "top_up"` to `TransactionType = "topup"`.

### DEF-3: Wrong `payment_purpose` value in PurchasePackageInitiateHandler (CRITICAL)
- **File:** `Application/Customer/Packages/CustomerPackageHandlers.cs`
- **Symptom:** DB check constraint violation — `payments_payment_purpose_check` rejects `'package_purchase'`; constraint only allows `'package'`.
- **Fix:** Changed `"package_purchase"` to `"package"` in `InitiatePaymentRequest`.

### DEF-4: Wrong coupon type string in ValidateApplyCouponHandler discount calculation (HIGH)
- **File:** `Application/Customer/Coupons/CustomerCouponHandlers.cs`
- **Symptom:** Percentage coupons always applied as flat rate. 10% of 500 returned 10.0 instead of 50.0.
- **Root cause:** Handler compared `coupon.CouponType == "percentage"` but DB stores `coupon_type='percent'` (no 'age' suffix).
- **Fix:** Changed comparison string from `"percentage"` to `"percent"`.

## Known DB Constraints (for future handler authors)
- `payments.payment_purpose` allowed values: `order, package, wallet_topup, tip, adjustment, refund, royalty`
- `wallet_transactions.transaction_type` allowed values: `topup, debit, refund, cashback, bonus, adjustment, reversal, lock, unlock`
- `coupon_redemptions.order_id` FK → `order_lifecycle.orders(id, created_at)` — requires a real order to exist before coupon apply

## Test Environment Note
- Coupon apply (`/customer/coupons/validate-apply`) requires a valid `order_id` from `order_lifecycle.orders`. No synthetic order IDs work. A seeded test order `ORD-TEST-001` (`98c4a12b-8c4d-434f-9540-cb2f53b84e19`) exists for this purpose.

## Architecture Notes
- Admin handlers use `_user.RequireBrandId()` from JWT claim — X-Brand-Id header is NOT used for admin brand scoping.
- All customer handlers self-filter via `sub` JWT claim — IDOR is prevented at the query level.
- DevPaymentGateway (Development env only) always returns `true` for signature verification.
