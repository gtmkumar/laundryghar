# ADR-010 — Recurring subscriptions: separate from prepaid packages & royalty; mandate-based billing with dunning

**Status:** Accepted · **Date:** 2026-05 · **Deciders:** Architecture, Finance

## Context
Two distinct recurring-revenue needs surfaced:
- **(A) Customers** want auto-renewing plans ("₹999/mo, 15 kg wash"), not just one-shot prepaid packs.
- **(B) The platform** wants to charge franchises a real tiered SaaS fee (Basic/Pro/Enterprise) with auto-renew and suspend-on-nonpayment — not a flat `technology_fee_monthly` buried in the franchise agreement.

We already have `packages` (prepaid, one-shot credit) and `royalty_invoices` (revenue share). Neither is a subscription engine. The risk is conflating three different money concepts.

## Decision
Model three things as **separate** concerns, never merged:
1. **Prepaid packages** (existing) — buy credit once, draw down, expire. No renewal.
2. **Customer subscriptions** (module A) — `subscription_plans`, `customer_subscriptions`, `subscription_invoices`, `subscription_billing_attempts`, `subscription_usage_ledger`, plus `payment_mandates` for UPI AutoPay / e-mandate authorization. Quota **resets each billing cycle** (unlike prepaid).
3. **Franchise SaaS subscriptions** (module B) — `platform_plans`, `franchise_subscriptions`, `franchise_subscription_invoices`, `franchise_subscription_events`. This is the **SaaS access fee**; royalty stays a **separate revenue share**.

Billing mechanics common to A and B:
- A **mandate / gateway subscription** authorizes recurring debit (Razorpay subscriptions / UPI AutoPay).
- A dunning state machine: `active → past_due → (retry attempts) → suspended/cancelled`, with `dunning_attempts`, `next_retry_at`, and a grace window.
- Every charge attempt is an append-only row (`*_billing_attempts` / events) with an `idempotency_key` to prevent double debits.
- Invoices carry their own tax breakdown (CGST/SGST/IGST) and a generated `amount_due`.

## Consequences
- **+** Clean separation: prepaid vs subscription vs royalty never cross-contaminate reporting.
- **+** Quota-reset-per-cycle is explicit in `subscription_usage_ledger`; prepaid draw-down stays in `package_usage_ledger`.
- **+** Suspend-on-nonpayment for franchises is a defined lifecycle (module B), enforced by a dunning job, not ad-hoc.
- **+** MRR/ARR are first-class via `mv_subscription_mrr` and `mv_franchise_saas_mrr`.
- **−** `franchise_agreements.technology_fee_monthly` is now superseded by module B for franchises that subscribe. Keep the column for legacy/flat-fee franchises; the subscription wins when a `franchise_subscriptions` row exists. Documented, not silently dropped.
- **−** More tables (10) and a dunning job to operate (see DEPLOYMENT.md). Worth it for correct recurring revenue.
- **−** Mandates depend on gateway support (Razorpay UPI AutoPay limits per-debit amount); `payment_mandates.max_amount` caps each debit accordingly.
