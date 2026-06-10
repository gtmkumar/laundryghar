# ADR-009 — Cash book per store/shift

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture, Finance, Ops

## Context

Indian laundry retail is heavily cash/COD: store counters and riders handle physical cash daily, and franchisors need to detect shrinkage per store, per shift, per person. A single running cash balance per store cannot answer "who was holding the drawer when ₹500 went missing?" — reconciliation must be scoped to an accountable session.

## Decision

Cash is accounted in **daily cash-book sessions per store and shift** (the Dhobi Cart pattern). `finance_royalty.cash_books` (`database_scripts/07_bc7_finance_royalty.sql`) has `UNIQUE (store_id, book_date, shift_label)` with `shift_label IN ('morning','afternoon','evening','night','full_day')`, an open/closed status, opening/closing floats, and a computed `variance` (indexed where non-zero for audit queries). Every movement is an append-only row in `finance_royalty.cash_book_entries`; handing the drawer over is a counted, signed `finance_royalty.shift_handovers` row. Rider COD closes into the same spine: COD collections accumulate in the rider cash ledger and settle via `db/patches/rider_cod_settlement.sql`, with the Finance service posting settled COD into the store's open cash book (cash-book posting wired 2026-06-10; admin UI under Finance → Cash Book).

## Consequences

- **+** Variance is attributable to a specific store + date + shift + handover chain; shrinkage is visible the day it happens.
- **+** Rider COD and counter cash reconcile in one ledger; `DailyReconService` in the Worker sweeps mismatches.
- **−** Staff must actually open/close books per shift — a closed-book gap blocks posting and needs ops discipline (default `full_day` label keeps single-shift stores simple).
- **−** One open book per (store, date, shift) is enforced by the unique key; backdated corrections are reversal entries, not edits.
