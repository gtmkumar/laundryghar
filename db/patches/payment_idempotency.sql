-- =============================================================================
-- patch: payment_idempotency.sql
-- Purpose : Add idempotency_key column to commerce.payment_refunds, add a
--           partial unique index (WHERE idempotency_key IS NOT NULL), and
--           install a trigger guard that prevents cumulative refunds from
--           exceeding the original payment amount.
-- Idempotent: safe to run multiple times (IF NOT EXISTS / OR REPLACE).
-- DDL role  : postgres (superuser / DDL owner)
-- Runtime   : app_user (RLS-enforced, RW on commerce.*)
-- =============================================================================

-- ─── 1. idempotency_key column on payment_refunds ─────────────────────────────
-- payments already has idempotency_key; this adds the same protection to refunds.

ALTER TABLE commerce.payment_refunds
    ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(150);

-- Partial unique index: unique only when non-null (same pattern as
-- commerce.wallet_transactions.idempotency_key).
CREATE UNIQUE INDEX IF NOT EXISTS payment_refunds_idempotency_key_key
    ON commerce.payment_refunds (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

-- ─── 2. Cumulative refund cap trigger ─────────────────────────────────────────
-- Prevents the sum of non-failed refunds for a payment from exceeding the
-- original captured amount. This is a DB backstop; the application layer
-- (IssueRefundHandler) enforces the same rule before writing.

CREATE OR REPLACE FUNCTION commerce.check_refund_cap()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    captured_amount  NUMERIC(14,2);
    already_refunded NUMERIC(14,2);
BEGIN
    -- Only enforce on non-failed refunds being inserted or updated
    IF NEW.status = 'failed' THEN
        RETURN NEW;
    END IF;

    -- Fetch the original payment amount
    SELECT amount
      INTO captured_amount
      FROM commerce.payments
     WHERE id = NEW.original_payment_id;

    IF captured_amount IS NULL THEN
        RAISE EXCEPTION 'commerce.check_refund_cap: original payment % not found',
            NEW.original_payment_id;
    END IF;

    -- Sum all existing non-failed refunds for the same payment,
    -- excluding the current row (for UPDATE: exclude self by id)
    SELECT COALESCE(SUM(amount), 0)
      INTO already_refunded
      FROM commerce.payment_refunds
     WHERE original_payment_id = NEW.original_payment_id
       AND status <> 'failed'
       AND id <> NEW.id;        -- safe: NEW.id exists on both INSERT and UPDATE

    IF (already_refunded + NEW.amount) > captured_amount THEN
        RAISE EXCEPTION
            'commerce.check_refund_cap: cumulative refunds (% + %) exceed captured amount % for payment %',
            already_refunded, NEW.amount, captured_amount, NEW.original_payment_id
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END;
$$;

-- Install trigger (idempotent: drop-if-exists then recreate)
DROP TRIGGER IF EXISTS trg_check_refund_cap ON commerce.payment_refunds;

CREATE TRIGGER trg_check_refund_cap
    BEFORE INSERT OR UPDATE OF amount, status, original_payment_id
    ON commerce.payment_refunds
    FOR EACH ROW
    EXECUTE FUNCTION commerce.check_refund_cap();

-- Grant EXECUTE on the trigger function to app_user so it can fire on DML
GRANT EXECUTE ON FUNCTION commerce.check_refund_cap() TO app_user;
