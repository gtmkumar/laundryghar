-- =============================================================================
-- db/patches/phase4_brand_platform_invoice_paylink.sql
--
-- Razorpay collection for brand platform-tier invoices: store the generated Payment Link on the
-- invoice so it can be (re)shown and reconciled (CreateBrandPlatformInvoicePaymentLinkCommand /
-- SyncBrandPlatformInvoicePaymentCommand). Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_brand_platform_invoice_paylink.sql
-- =============================================================================

BEGIN;

ALTER TABLE identity_access.brand_platform_invoice
    ADD COLUMN IF NOT EXISTS razorpay_payment_link_id VARCHAR(64),
    ADD COLUMN IF NOT EXISTS payment_link_url         VARCHAR(512);

DO $verify$
DECLARE n int;
BEGIN
    SELECT count(*) INTO n FROM information_schema.columns
     WHERE table_schema='identity_access' AND table_name='brand_platform_invoice'
       AND column_name IN ('razorpay_payment_link_id','payment_link_url');
    IF n <> 2 THEN RAISE EXCEPTION 'invoice_paylink: expected 2 columns, found %', n; END IF;
    RAISE NOTICE 'invoice_paylink verification passed: payment-link columns ready.';
END
$verify$;

COMMIT;

SELECT 'phase4_brand_platform_invoice_paylink.sql applied successfully.' AS result;
