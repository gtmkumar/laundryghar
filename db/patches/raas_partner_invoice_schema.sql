-- =============================================================================
-- db/patches/raas_partner_invoice_schema.sql
--
-- PURPOSE (RaaS full build, FULL-10 / issue #14): create the partner invoice data layer in the
-- COMMERCE schema —
--   • commerce.partner_invoices — periodic invoices for a partner's billed bookings.
--
-- Cloned in shape from finance_royalty.franchise_subscription_invoices (the platform→tenant SaaS
-- invoice), swapping the franchise/subscription key for partner_id. partner_id is a SCALAR cross-BC
-- reference to logistics.partners(id): NO foreign key is declared (commerce and logistics are separate
-- bounded contexts — mirrors partner_wallet_accounts); isolation is enforced by the rls_partner policy,
-- not referential integrity.
--
--   • tax_breakdown is a jsonb owned type (the shared GST shape used by every invoice table).
--   • amount_due is GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only.
--   • razorpay_payment_link_id / payment_link_url carry the generated Razorpay Payment Link so the
--     invoice can be (re)collected and reconciled (PayPartnerInvoiceCommand + the partner paylink
--     webhook / SyncPartnerInvoicePaymentCommand), mirroring identity_access.brand_platform_invoice.
--
-- RLS (rls_partner policy + ENABLE RLS) lives in the companion patch
-- db/patches/rls_partner_invoice.sql — run it AFTER this one.
--
-- Idempotent. Safe to re-run.
--
-- RUN (manual, as postgres — AFTER harden_app_user_and_rls_bypass.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/raas_partner_invoice_schema.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- Harmless: schema already exists in every deployed environment.
CREATE SCHEMA IF NOT EXISTS commerce;

-- ---------------------------------------------------------------------------
-- commerce.partner_invoices — a partner's periodic invoice
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS commerce.partner_invoices (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    partner_id               UUID NOT NULL,                 -- rls_partner key; scalar cross-BC ref → logistics.partners (no FK)
    invoice_number           VARCHAR(40) NOT NULL,
    billing_period_start     TIMESTAMPTZ NOT NULL,
    billing_period_end       TIMESTAMPTZ NOT NULL,
    line_items               JSONB NOT NULL DEFAULT '[]',   -- the bookings billed
    subtotal                 NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_breakdown            JSONB NOT NULL DEFAULT '{}',    -- shared GST owned type
    tax_total                NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total              NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid              NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due               NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code            CHARACTER(3) NOT NULL DEFAULT 'INR',
    status                   VARCHAR(20) NOT NULL DEFAULT 'issued'
                             CHECK (status IN ('draft','issued','paid','void')),
    invoice_pdf_url          VARCHAR(512),
    razorpay_payment_link_id VARCHAR(64),
    payment_link_url         VARCHAR(512),
    issued_at                TIMESTAMPTZ,
    due_at                   TIMESTAMPTZ,
    paid_at                  TIMESTAMPTZ,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by               UUID,
    updated_by               UUID,
    CONSTRAINT partner_invoices_invoice_number_key UNIQUE (invoice_number)
);
CREATE INDEX IF NOT EXISTS idx_partner_invoices_partner
    ON commerce.partner_invoices(partner_id);

-- ---------------------------------------------------------------------------
-- GRANTs — explicit CRUD for the runtime roles (self-contained regardless of apply order).
-- ---------------------------------------------------------------------------
DO $grants$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['app_user','app_admin'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA commerce TO %I', r);
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.partner_invoices TO %I', r);
        END IF;
    END LOOP;
END
$grants$;

SELECT 'raas_partner_invoice_schema.sql applied successfully.' AS result;
