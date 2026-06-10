---
name: project-commerce-payments
description: Security posture of laundryghar.Commerce payments/refunds + the IPaymentGateway seam. Critical dev-stub registration gap + refund aggregate gap.
metadata:
  type: project
---

laundryghar.Commerce handles payments, refunds, wallet, packages. Connects as `app_user` in dev (RLS enforced — better than Catalog's postgres superuser posture).

**CRITICAL — DevPaymentGateway registered unconditionally.** `laundryghar.Commerce/Program.cs:49` does `AddSingleton<IPaymentGateway, DevPaymentGateway>()` with NO `IsDevelopment()` gate, despite the class XML doc saying "NEVER register this in non-Development environments." DevPaymentGateway.VerifySignatureAsync ALWAYS returns true (Gateway/DevPaymentGateway.cs:42). In prod this means any forged payment passes signature verification → free orders / fraudulent package & wallet credit. Must env-gate before any real money flow. This is the dominant payments risk.

**Payment seam is otherwise well-designed** (good for real Razorpay swap): IPaymentGateway abstracts CreateOrder/VerifySignature/InitiateRefund; VerifyPaymentHandler marks payment failed on signature mismatch; idempotency via Idempotency-Key on initiate. GAP: VerifyPaymentHandler passes client-supplied req.GatewayOrderId straight to the gateway and does NOT assert req.GatewayOrderId == payment.GatewayOrderId, nor re-checks captured amount == payment.Amount. Add those binds when wiring real gateway.

**Refund aggregate gap (High, money leak).** AdminPaymentHandlers.IssueRefundHandler (line 84) validates only `req.Amount <= 0 || req.Amount > payment.Amount` — a SINGLE-refund cap. It does NOT sum prior PaymentRefunds for the payment, so N partial refunds each ≤ original can cumulatively exceed the captured amount (e.g. refund a ₹100 payment 5× as wallet credit). Fix: SUM existing completed/processing refunds + req.Amount ≤ payment.Amount.

**Money validators present:** InitiatePaymentValidator Amount>0; Finance CashBook entry Amount>0, OpeningBalance>=0. Generally good on negative-amount guarding.
