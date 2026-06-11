---
name: project-pos-web-gap-r2
description: pos-web Gap Analysis R2 fixes (POS-1..7 + permission gating) — cart persistence, idempotency, partial pay, brand-keyed catalog cache
metadata:
  type: project
---

GAP_ANALYSIS_R2 pos-web remediation (POS-1 through POS-7 + WEB-3 POS side). Build + lint clean. Not committed.

**Why:** R2 review found POS reliability/correctness gaps; this batch closed all pos-web items. See [[project-pos-web-scaffold]] for base app.

**How to apply:** Reuse this infra for any new POS flow.

## New shared infra (reusable)
- `src/stores/cartStore.ts` — persisted zustand cart (`lg-pos-cart`), scoped per active store via `storeId` field + `isCartForeign()` guard. Holds customer/isExpress/coupon/lines. `clearCart()` on submit.
- `src/hooks/usePermissions.ts` — `usePermissions().can(perm)` reads `permissions[]` off the JWT (authStore.user). `*` = superuser. `PERMISSIONS.customerCreate`/`.paymentRecord`.
- `src/hooks/useDebounce.ts` — generic debounce (customer search 300ms).
- `src/components/shared/EmptyState.tsx` — neutral empty placeholder (compact variant for inline lists).
- `lib/utils.ts`: `newIdempotencyKey()` (crypto.randomUUID + fallback), `normalizePhoneE164()` (accepts bare 10-digit Indian → +91).

## Key decisions
- **Idempotency (POS-2):** key sent BOTH in request body (`idempotencyKey` on CreateOrderRequest/RecordOfflinePaymentRequest) AND as `Idempotency-Key` header. Order key held in a ref, minted on submit start, reused on retry, cleared on success. Payment key fresh per attempt. Backend dedupes (parallel backend agent adding support).
- **Brand-keyed catalog (POS-5):** `useEffectiveBrandId()` now EXPORTED from useCatalog.ts; brandId is the 1st arg of every `catalogKeys.*` entry so a brand switch busts cache. Was only gating `enabled`.
- **Payment cache (POS-6):** usePayments onSuccess invalidates `['orders','list']`, `orderKeys.detail(orderId)`, `['cash-books']`. useCreateOrder seeds detail cache via setQueryData.
- **Receipt change (POS-3):** PaymentModal passes RAW `tendered` separately from booked `amount`; Receipt computes `change = tendered - grandTotal`. `tendered` optional, defaults to amountPaid (UPI/card / OrderDetailPage where raw tender unknown).
- **Partial/credit (POS-4):** RecordedPayment carries `tendered`, `paymentStatus`, `balanceDue`, `credit`. PaymentModal has explicit "Pay later (credit)" button (distinct from Skip). Confirmation shows partial/credit + balance-due; "Collect balance" path re-opens modal when balanceDue>0.
- **PaymentModal reset (POS-7):** split into wrapper + inner `PaymentForm` keyed by `order.id`, mounted only when `open` — fresh useState per open. Avoids react-hooks/set-state-in-effect lint (that rule is ON in this repo — don't reset state in effects).
- **Permission gating (WEB-3):** create-customer fieldset + record-payment gated on `can(...)`, disabled with tooltip/note. UX guard only; backend still enforces.

## Gotchas
- eslint rule `react-hooks/set-state-in-effect` is enforced — never call setState synchronously in an effect body; key+remount or derive instead.
- `orderKeys.list()` with no params = `['orders','list',undefined]` which does NOT match params-keyed list queries; invalidate by `['orders','list']` prefix instead.
- New i18n keys added to en.json + hi.json (pos.* and payment.*); all call sites also pass `defaultValue` so missing translations never break.
