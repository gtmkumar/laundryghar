---
name: security-tasks-review-june2026
description: QA review findings for Tasks #1-#6 and #13 (Razorpay, Refunds, PII, Cross-brand FK, OTP hardening, Notifications, GST Invoice) — June 2026
metadata:
  type: project
---

Wave-0 security/feature batch reviewed 2026-06-10. Three tasks PASS, four FAIL.

**FAIL — Task #2 (Refunds): Three DB CHECK constraint violations**
- IssueRefundHandler (AdminPaymentHandlers.cs line 213): `refund.Status = "completed"` — NOT in constraint; allowed values are pending/approved/processing/succeeded/failed/rejected. Should be "succeeded".
- CancelOrderCommand.cs line 143: `RefundType = "gateway"` — NOT in refund_type CHECK; allowed: full/partial/goodwill/dispute_loss. Should be "full".
- IssueRefundHandler (AdminPaymentHandlers.cs line 210): `RefundMethod = "gateway"` — NOT in refund_method CHECK; allowed: original/wallet/bank_transfer/store_credit. Should be "original".
All three will throw DB exceptions at runtime. No test covers these.

**FAIL — Task #6 (Notifications): Two bugs in NotificationMappingService.cs**
- Bug 1 (Critical): `EventPayload.NewStatus` field doesn't match JSON key `toStatus` emitted by all Orders status-change commands. With case-insensitive deserialization, `newStatus` != `toStatus`. Result: `newStatus` is always null for `order.status_changed` events → `ResolveTemplate` returns null → all order status notifications silently skipped.
- Bug 2 (Critical): `string.Compare(..., StringComparison.Ordinal)` in LINQ query at line ~130 cannot be translated by EF Core to SQL. After the first poll (when cursor was null), every subsequent poll throws `System.InvalidOperationException` and retries every 5 seconds, permanently failing. Zero notifications_outbox rows ever inserted.
- Confirmed live: Worker log shows the exception on every 5s tick. Cursor advanced once (count=1) but only because the null-cursor code path doesn't hit string.Compare. No notifications_outbox rows in DB.

**PASS — Task #1 (Razorpay)**: env-gating correct, HMAC constant-time correct, raw-body webhook correct, idempotency column applied, VerifyPaymentHandler gateway-order binding present.

**PASS — Task #3 (PII)**: AesGcmFieldCipher correct, PiiValueConverter legacy-passthrough correct, column widening applied, users.read_financial permission granted. PersonDetailDrawer.tsx save() fix correctly uses ifEdited() to omit unchanged masked values.

**PASS-with-notes — Task #4 (Cross-brand FK sweep)**: All 14 handlers sample-verified with brand-scoped guards. No violations found.

**PASS — Task #5 (OTP hardening)**: 15-min/10-attempt lockout on both user+customer flows, salted HMAC+legacy fallback, ForwardedHeaders extension present, code_salt column + lockout index applied.

**PASS-with-notes — Task #13 (GST Invoice)**: Tax math code correct (InvoiceTaxCalculator uses DefaultHalfRate=9%). Invoice faithfully reads order.cgst/sgst as designed. The specific invoice INV-2627-LGG-DLF-003-000001 shows grand_total=424.01 with taxable+taxes=374.02 (49.99 gap) — this is a data corruption on the source order (LG-2026-LGG-DLF-003-002300), NOT a code bug. All other orders in DB are internally consistent. The 2.5% rate is from seed data created before 18% was configured; the code correctly uses whatever rate is in the order.

**Why:** Pre-release code review for this batch of tasks.
**How to apply:** Tasks #2 and #6 must NOT be marked complete until the three constraint bugs and two NotificationMappingService bugs are fixed.
