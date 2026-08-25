-- 0021_brand_dunning — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- §9's missing edge: `Active → PastDue → (dunning retries) → Suspended`, and back.
--
-- ─── The gap, and why it was invisible ───────────────────────────────────────────────────────
-- §5 says "suspend-on-nonpay" is already built, and §9 draws the whole state machine. Both are true
-- of the CUSTOMER subscription engine (`SubscriptionBillingService` — retries, backoff, suspend, all
-- shipped). Neither was true of the COMPANY's subscription to us.
--
-- The suspension GATE works and is tested (T-18): a suspended brand drops to login-only mode with
-- billing and export still reachable. What was missing is the thing that pulls the trigger. Nothing
-- anywhere set `tenancy_org.brands.status = 'suspended'` — grep found exactly zero writers. So a
-- provider could stop paying indefinitely and keep trading, and the only way to suspend anyone was
-- by hand, in SQL.
--
-- Found by auditing the live database against PLATFORM_STRATEGY.md §9 state by state, rather than
-- against the task list — the task list had T-18 "suspend / login-only mode" marked Done, and it
-- was: the half that existed.

-- ─── 1. Why a brand is suspended ─────────────────────────────────────────────────────────────
-- Without this, automatic reactivation on payment would silently undo a MANUAL suspension — §7 lists
-- ToS enforcement as a separate reason to suspend, and a fraudster paying an invoice must not
-- reinstate themselves. The dunning worker only ever touches brands it suspended itself.
ALTER TABLE tenancy_org.brands
    ADD COLUMN IF NOT EXISTS suspension_reason varchar(32);

ALTER TABLE tenancy_org.brands DROP CONSTRAINT IF EXISTS brands_suspension_reason_check;
ALTER TABLE tenancy_org.brands ADD CONSTRAINT brands_suspension_reason_check
    CHECK (suspension_reason IS NULL
           OR suspension_reason IN ('nonpayment', 'tos', 'manual'));

COMMENT ON COLUMN tenancy_org.brands.suspension_reason IS
    'Why this brand is suspended. Only `nonpayment` is auto-reversible — the dunning worker will '
    'not reactivate a brand suspended for ToS or by hand.';

-- ─── 2. Dunning state on the invoice ─────────────────────────────────────────────────────────
ALTER TABLE identity_access.brand_platform_invoice
    ADD COLUMN IF NOT EXISTS attempt_count   int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz,
    -- When to try again. NULL means "not in dunning" — either not yet due, or finished with.
    ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz;

CREATE INDEX IF NOT EXISTS idx_brand_platform_invoice_dunning
    ON identity_access.brand_platform_invoice (next_attempt_at)
    WHERE status IN ('issued', 'past_due');

-- ─── 3. Suspending and reinstating ───────────────────────────────────────────────────────────
-- THE BRANDS-RLS TRAP, SIXTH TIME. `tenancy_org.brands` is `rls_admin_only`. The worker happens to
-- run RLS-bypassed, so this would have worked there — but a function keeps the RULES in one place
-- rather than spread across whatever code path happens to have bypass, and it is the rules that
-- matter here:
--
--   * suspending records WHY, so reinstatement can tell nonpayment from ToS;
--   * reinstating refuses unless the reason was nonpayment;
--   * neither ever touches a brand that is `cancelled` or `archived` — a wind-down is not a billing
--     problem, and suspending a brand mid-export would break the §8.2 promise that they can take
--     their data with them.
CREATE OR REPLACE FUNCTION kernel.set_brand_suspension(
    p_brand_id uuid, p_suspend boolean, p_reason text DEFAULT 'nonpayment')
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = tenancy_org, kernel, pg_catalog
AS $$
DECLARE
    v_status text;
    v_reason text;
BEGIN
    SELECT b.status, b.suspension_reason INTO v_status, v_reason
    FROM   tenancy_org.brands b WHERE b.id = p_brand_id;

    IF v_status IS NULL THEN
        RETURN NULL;
    END IF;

    -- A wind-down outranks a billing problem, in both directions.
    IF v_status IN ('cancelled', 'archived') THEN
        RETURN v_status;
    END IF;

    IF p_suspend THEN
        IF v_status = 'suspended' THEN
            RETURN v_status;                      -- already there; do not overwrite the reason
        END IF;
        UPDATE tenancy_org.brands
           SET status = 'suspended', suspension_reason = p_reason, updated_at = now()
         WHERE id = p_brand_id;
        RETURN 'suspended';
    END IF;

    -- Reinstating. Only ever undoes what dunning did.
    IF v_status <> 'suspended' THEN
        RETURN v_status;
    END IF;
    IF v_reason IS DISTINCT FROM 'nonpayment' THEN
        RETURN v_status;                          -- ToS or manual: a payment does not clear it
    END IF;

    UPDATE tenancy_org.brands
       SET status = 'active', suspension_reason = NULL, updated_at = now()
     WHERE id = p_brand_id;
    RETURN 'active';
END $$;

REVOKE ALL ON FUNCTION kernel.set_brand_suspension(uuid, boolean, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.set_brand_suspension(uuid, boolean, text) TO app_user;

-- ─── 4. Assertions ───────────────────────────────────────────────────────────────────────────
DO $$
DECLARE r text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'brand_platform_invoice' AND column_name = 'next_attempt_at')
    THEN
        RAISE EXCEPTION 'dunning columns were not added';
    END IF;

    -- Any brand already suspended predates this column and cannot be attributed. Left NULL, which
    -- reads as "not nonpayment" — so the worker will never auto-reinstate one it did not suspend.
    SELECT count(*)::text INTO r FROM tenancy_org.brands
     WHERE status = 'suspended' AND suspension_reason IS NULL;
    IF r <> '0' THEN
        RAISE NOTICE '% pre-existing suspended brand(s) have no recorded reason — they will not be '
                     'auto-reinstated, which is the safe default', r;
    END IF;

    RAISE NOTICE 'brand dunning ready: PastDue -> retries -> Suspended -> (payment) -> Active';
END $$;
