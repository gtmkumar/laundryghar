-- =============================================================================
-- db/patches/phase2_slice_f_tax_breakdown.sql
--
-- Multi-vertical Phase 2 · Slice 2F — THE THREE-WAY TAX COORDINATION (blueprint §8 Risk #4).
-- Orders, Commerce, and Finance each carry their own GST columns on their invoice tables. This
-- ONE atomic migration moves all four onto the shared `tax_breakdown` jsonb contract so the three
-- tax schemas cannot diverge. Landing any single table alone is the trap this slice avoids — all
-- four ALTERs run in the SAME transaction.
--
--   order_lifecycle.invoices                  cgst_rate/cgst_amount/… (rate+amount) → tax_breakdown
--   commerce.subscription_invoices            cgst/sgst/igst (amount-only)          → tax_breakdown
--   finance_royalty.royalty_invoices          cgst/sgst/igst (amount-only)          → tax_breakdown
--   finance_royalty.franchise_subscription_invoices  cgst/sgst/igst                 → tax_breakdown
--
-- jsonb keys match the EF owned-type (TaxBreakdownMapping): cgst_rate/cgst_amount/sgst_rate/
-- sgst_amount/igst_rate/igst_amount. Amount-only tables backfill rates as 0.
--
-- DESTRUCTIVE (drops the old GST columns after backfill) + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_f_tax_breakdown.sql
-- =============================================================================

BEGIN;

-- 1. Add the shared column to all four tables --------------------------------
ALTER TABLE order_lifecycle.invoices                        ADD COLUMN IF NOT EXISTS tax_breakdown jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE commerce.subscription_invoices                  ADD COLUMN IF NOT EXISTS tax_breakdown jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE finance_royalty.royalty_invoices                ADD COLUMN IF NOT EXISTS tax_breakdown jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE finance_royalty.franchise_subscription_invoices ADD COLUMN IF NOT EXISTS tax_breakdown jsonb NOT NULL DEFAULT '{}'::jsonb;

-- 2. Backfill (orders carries rate+amount; the other three are amount-only) ---
DO $backfill$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='order_lifecycle' AND table_name='invoices' AND column_name='cgst_amount') THEN
        UPDATE order_lifecycle.invoices SET tax_breakdown = jsonb_build_object(
            'cgst_rate', cgst_rate, 'cgst_amount', cgst_amount,
            'sgst_rate', sgst_rate, 'sgst_amount', sgst_amount,
            'igst_rate', igst_rate, 'igst_amount', igst_amount);
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='commerce' AND table_name='subscription_invoices' AND column_name='cgst') THEN
        UPDATE commerce.subscription_invoices SET tax_breakdown = jsonb_build_object(
            'cgst_rate', 0, 'cgst_amount', cgst, 'sgst_rate', 0, 'sgst_amount', sgst, 'igst_rate', 0, 'igst_amount', igst);
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='finance_royalty' AND table_name='royalty_invoices' AND column_name='cgst') THEN
        UPDATE finance_royalty.royalty_invoices SET tax_breakdown = jsonb_build_object(
            'cgst_rate', 0, 'cgst_amount', cgst, 'sgst_rate', 0, 'sgst_amount', sgst, 'igst_rate', 0, 'igst_amount', igst);
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema='finance_royalty' AND table_name='franchise_subscription_invoices' AND column_name='cgst') THEN
        UPDATE finance_royalty.franchise_subscription_invoices SET tax_breakdown = jsonb_build_object(
            'cgst_rate', 0, 'cgst_amount', cgst, 'sgst_rate', 0, 'sgst_amount', sgst, 'igst_rate', 0, 'igst_amount', igst);
    END IF;
END
$backfill$;

-- 3. Drop the old GST columns (all four, same transaction) --------------------
ALTER TABLE order_lifecycle.invoices
    DROP COLUMN IF EXISTS cgst_rate, DROP COLUMN IF EXISTS cgst_amount,
    DROP COLUMN IF EXISTS sgst_rate, DROP COLUMN IF EXISTS sgst_amount,
    DROP COLUMN IF EXISTS igst_rate, DROP COLUMN IF EXISTS igst_amount;
ALTER TABLE commerce.subscription_invoices
    DROP COLUMN IF EXISTS cgst, DROP COLUMN IF EXISTS sgst, DROP COLUMN IF EXISTS igst;
ALTER TABLE finance_royalty.royalty_invoices
    DROP COLUMN IF EXISTS cgst, DROP COLUMN IF EXISTS sgst, DROP COLUMN IF EXISTS igst;
ALTER TABLE finance_royalty.franchise_subscription_invoices
    DROP COLUMN IF EXISTS cgst, DROP COLUMN IF EXISTS sgst, DROP COLUMN IF EXISTS igst;

-- 4. Verification gate: tax_breakdown present on all 4; no old GST columns left
DO $verify$
DECLARE has_new int; has_old int;
BEGIN
    SELECT count(*) INTO has_new FROM information_schema.columns
    WHERE column_name='tax_breakdown' AND (
        (table_schema='order_lifecycle' AND table_name='invoices') OR
        (table_schema='commerce' AND table_name='subscription_invoices') OR
        (table_schema='finance_royalty' AND table_name IN ('royalty_invoices','franchise_subscription_invoices')));
    IF has_new <> 4 THEN RAISE EXCEPTION 'Slice 2F: expected tax_breakdown on 4 tables, found %', has_new; END IF;

    SELECT count(*) INTO has_old FROM information_schema.columns
    WHERE column_name IN ('cgst','sgst','igst','cgst_amount','sgst_amount','igst_amount','cgst_rate','sgst_rate','igst_rate')
      AND ((table_schema='order_lifecycle' AND table_name='invoices') OR
           (table_schema='commerce' AND table_name='subscription_invoices') OR
           (table_schema='finance_royalty' AND table_name IN ('royalty_invoices','franchise_subscription_invoices')));
    IF has_old <> 0 THEN RAISE EXCEPTION 'Slice 2F: % old GST column(s) remain across the invoice tables', has_old; END IF;

    RAISE NOTICE 'Slice 2F verification passed: all 4 invoice tables share tax_breakdown jsonb.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_f_tax_breakdown.sql applied successfully.' AS result;
