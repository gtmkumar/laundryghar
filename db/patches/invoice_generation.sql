-- =============================================================================
-- db/patches/invoice_generation.sql
--
-- GST-compliant invoice generation for LaundryGhar.
--
-- Creates:
--   order_lifecycle.invoice_number_sequences  — per-(brand,store,fiscal-year) counter
--   order_lifecycle.invoices                  — immutable invoice record per order
--   order_lifecycle.next_invoice_number(...)  — atomic counter allocator
--
-- Indian fiscal year: April 1 – March 31 (e.g. FY 2026-27 starts 2026-04-01).
-- Invoice number format: INV-<FY>-<storeCode>-<NNNNNN>
--   e.g. INV-2627-LGG-S45-001-000001
--
-- Tax treatment:
--   CGST 9% + SGST 9% for intra-state supply (default).
--   IGST 18% for inter-state supply — field present, default 0.
--   SAC 999712 — Laundry / dry-cleaning services (GST Council classification).
--
-- RLS: brand-scoped, mirrors rls_proposal.sql pattern used across all BCs.
--
-- Idempotent. Run as postgres user.
-- =============================================================================

SET client_min_messages = WARNING;

BEGIN;

-- ── 1. Per-(brand, store, fiscal-year) invoice counter ───────────────────────
CREATE TABLE IF NOT EXISTS order_lifecycle.invoice_number_sequences (
    brand_id   uuid   NOT NULL,
    store_id   uuid   NOT NULL,
    fy         int    NOT NULL,   -- Indian fiscal year start year, e.g. 2026 for FY 2026-27
    last_value bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (brand_id, store_id, fy)
);

COMMENT ON TABLE order_lifecycle.invoice_number_sequences IS
    'Atomic per-(brand,store,fiscal-year) counter for invoice number generation.
     fy = starting year of Indian fiscal year (Apr–Mar), e.g. 2026 for FY 2026-27.
     Uses INSERT ... ON CONFLICT DO UPDATE ... RETURNING to serialise concurrent callers.';

ALTER TABLE order_lifecycle.invoice_number_sequences ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON order_lifecycle.invoice_number_sequences;
CREATE POLICY rls_brand ON order_lifecycle.invoice_number_sequences
    USING      (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()))
    WITH CHECK (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));

GRANT SELECT, INSERT, UPDATE, DELETE ON order_lifecycle.invoice_number_sequences TO app_user;

-- ── 2. Atomic invoice number allocator ────────────────────────────────────────
CREATE OR REPLACE FUNCTION order_lifecycle.next_invoice_number(
    p_brand      uuid,
    p_store      uuid,
    p_store_code text,
    p_fy         int    -- Indian fiscal year start year (Apr = new FY)
)
RETURNS text
LANGUAGE plpgsql
AS $function$
DECLARE
    v_seq  bigint;
    v_fy_s text;
BEGIN
    INSERT INTO order_lifecycle.invoice_number_sequences AS s (brand_id, store_id, fy, last_value)
    VALUES (p_brand, p_store, p_fy, 1)
    ON CONFLICT (brand_id, store_id, fy)
    DO UPDATE SET last_value = s.last_value + 1
    RETURNING s.last_value INTO v_seq;

    -- FY label: 2026-27 → "2627"
    v_fy_s := lpad((p_fy % 100)::text, 2, '0') || lpad(((p_fy + 1) % 100)::text, 2, '0');

    RETURN format('INV-%s-%s-%s', v_fy_s, p_store_code, lpad(v_seq::text, 6, '0'));
END
$function$;

GRANT EXECUTE ON FUNCTION order_lifecycle.next_invoice_number(uuid, uuid, text, int) TO app_user;

-- ── 3. Invoices table ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS order_lifecycle.invoices (
    id                    uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id              uuid          NOT NULL,
    order_id              uuid          NOT NULL,
    invoice_number        text          NOT NULL,
    invoice_date          date          NOT NULL,

    -- Supplier snapshot (store / franchise at time of generation — immutable)
    supplier_name         text          NOT NULL,
    supplier_address      text          NOT NULL,
    supplier_gstin        text          NULL,       -- franchise GSTIN (may be null for unregistered)

    -- Customer snapshot (immutable)
    customer_name         text          NOT NULL,
    customer_phone        text          NOT NULL,
    customer_gstin        text          NULL,       -- B2B: optional

    -- GST fields
    place_of_supply       text          NOT NULL,   -- state name or code (e.g. "Haryana", "29")
    sac_code              varchar(10)   NOT NULL DEFAULT '999712',

    -- Line items: JSONB snapshot [{description, qty, unit_price, taxable_value}, ...]
    line_items            jsonb         NOT NULL DEFAULT '[]',

    -- Totals
    subtotal              numeric(14,2) NOT NULL,
    discount_total        numeric(14,2) NOT NULL DEFAULT 0,
    taxable_total         numeric(14,2) NOT NULL,
    cgst_rate             numeric(5,2)  NOT NULL DEFAULT 9,
    cgst_amount           numeric(14,2) NOT NULL DEFAULT 0,
    sgst_rate             numeric(5,2)  NOT NULL DEFAULT 9,
    sgst_amount           numeric(14,2) NOT NULL DEFAULT 0,
    igst_rate             numeric(5,2)  NOT NULL DEFAULT 0,
    igst_amount           numeric(14,2) NOT NULL DEFAULT 0,
    round_off             numeric(14,2) NOT NULL DEFAULT 0,
    grand_total           numeric(14,2) NOT NULL,

    status                varchar(20)   NOT NULL DEFAULT 'issued'
                              CHECK (status IN ('issued','cancelled')),

    created_at            timestamptz   NOT NULL DEFAULT now(),
    created_by            uuid          NULL,

    -- Constraints
    CONSTRAINT invoices_order_id_key      UNIQUE (order_id),
    CONSTRAINT invoices_invoice_number_brand_key UNIQUE (brand_id, invoice_number)
);

COMMENT ON TABLE order_lifecycle.invoices IS
    'Immutable GST tax invoice record per order (one-to-one).
     All supplier/customer/line-item data is snapshotted at generation time.
     SAC 999712 = laundry / dry-cleaning services.
     CGST+SGST for intra-state, IGST for inter-state (field present, default 0).';

COMMENT ON COLUMN order_lifecycle.invoices.sac_code      IS '999712 — Laundry / dry-cleaning services (SAC under GST Council classification).';
COMMENT ON COLUMN order_lifecycle.invoices.supplier_gstin IS 'Franchise GSTIN at time of invoice generation. NULL for composition or unregistered suppliers.';
COMMENT ON COLUMN order_lifecycle.invoices.customer_gstin IS 'Customer GSTIN for B2B supplies. NULL for B2C.';
COMMENT ON COLUMN order_lifecycle.invoices.place_of_supply IS 'State of customer address (for CGST/SGST vs IGST determination).';
COMMENT ON COLUMN order_lifecycle.invoices.line_items     IS 'Immutable snapshot: [{description, qty, unit_price, taxable_value}]. Never mutated post-generation.';

CREATE INDEX IF NOT EXISTS idx_invoices_brand_date
    ON order_lifecycle.invoices (brand_id, invoice_date DESC);

CREATE INDEX IF NOT EXISTS idx_invoices_order
    ON order_lifecycle.invoices (order_id);

-- RLS — brand-scoped.
ALTER TABLE order_lifecycle.invoices ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON order_lifecycle.invoices;
CREATE POLICY rls_brand ON order_lifecycle.invoices
    FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT, INSERT, UPDATE, DELETE ON order_lifecycle.invoices TO app_user;

COMMIT;

SELECT 'invoice_generation.sql applied successfully.' AS result;
