# ADR-006 — NUMERIC(14,2) money, append-only ledgers, idempotency keys

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture, Finance

## Context

The platform moves real money in INR: customer payments, wallets, prepaid packages, COD cash, refunds, royalty invoices. Floating point is unacceptable for money; mutable balance columns invite race conditions and unauditable history; payment retries (mobile networks) invite double charges.

## Decision

Three rules, applied together across the schema:

1. **`NUMERIC(14,2)` for every money column** (~117 occurrences across `database_scripts/`) — paise-exact, headroom to ₹999 crore, never FLOAT/REAL. Tax splits (CGST/SGST/IGST) are stored as separate NUMERIC columns, often with generated totals.
2. **Append-only ledgers for anything with a balance.** Balances are derived from immutable entry rows, not updated in place: `commerce.package_usage_ledger`, `commerce.loyalty_points_ledger`, `commerce.wallet_transactions` (against `wallet_accounts`), `finance_royalty.cash_book_entries`, and the rider COD ledger (`db/patches/rider_cod_settlement.sql`). Corrections are reversal entries.
3. **Idempotency keys on charge-creating writes** — `idempotency_key VARCHAR(100) UNIQUE` on `commerce.payments`, `wallet_transactions`, `kernel.outbox_events`, and notification sends (`db/patches/payment_idempotency.sql` hardened this); a retried request lands on the unique constraint instead of double-debiting.

**Where it lives:** `database_scripts/06_bc6_commerce.sql`, `07_bc7_finance_royalty.sql`, `00_kernel.sql`; .NET maps to `decimal`.

## Consequences

- **+** Paise-exact arithmetic; full audit trail; safe retries by construction.
- **−** Balance reads aggregate ledger rows (mitigated with running-balance columns/snapshot rows where hot).
- **−** Developers must remember the idempotency-key contract on every new money endpoint — enforced in review.
