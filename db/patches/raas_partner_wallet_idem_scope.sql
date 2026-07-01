-- =============================================================================
-- db/patches/raas_partner_wallet_idem_scope.sql
--
-- PURPOSE: scope the partner wallet ledger's idempotency uniqueness to the PARTNER.
--
-- The original schema (raas_partner_wallet_schema.sql) declared
--   CONSTRAINT partner_wallet_transactions_idempotency_key_key UNIQUE (idempotency_key)
-- i.e. GLOBALLY unique across every partner. But idempotency_key is a caller-supplied,
-- free-form string and the ledger's dedup/recovery lookup is PARTNER-scoped
-- (WHERE idempotency_key = @k AND partner_id = @p). So if partner B reuses a key that
-- partner A already used:
--   • the INSERT trips the GLOBAL unique (23505),
--   • the recovery re-read (partner-scoped) finds no row for B,
--   • AppendAsync re-throws → 500, and B can never top up with that key.
--
-- Fix: replace the global unique with a PER-PARTNER composite UNIQUE (partner_id, idempotency_key),
-- matching the house pattern UNIQUE(customer_id, idempotency_key)
-- (db/patches/pickup_idempotency_and_source.sql). A global unique is strictly stronger than the
-- composite, so no existing row can violate the new constraint — the migration is data-safe.
--
-- Idempotent. Safe to re-run. Safe to run before OR after a fresh raas_partner_wallet_schema.sql
-- (which now already declares the composite constraint).
--
-- RUN (manual, as postgres — AFTER raas_partner_wallet_schema.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/raas_partner_wallet_idem_scope.sql
-- =============================================================================

SET client_min_messages = WARNING;

DO $$
BEGIN
    IF to_regclass('commerce.partner_wallet_transactions') IS NULL THEN
        RAISE NOTICE 'commerce.partner_wallet_transactions not found — run raas_partner_wallet_schema.sql first; skipping.';
        RETURN;
    END IF;

    -- 1. Drop the legacy GLOBAL unique, whether it exists as a table constraint or a bare index.
    ALTER TABLE commerce.partner_wallet_transactions
        DROP CONSTRAINT IF EXISTS partner_wallet_transactions_idempotency_key_key;
    DROP INDEX IF EXISTS commerce.partner_wallet_transactions_idempotency_key_key;

    -- 2. Add the PER-PARTNER composite unique if it is not already present.
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'commerce.partner_wallet_transactions'::regclass
          AND conname  = 'partner_wallet_transactions_partner_idempotency_key'
    ) THEN
        ALTER TABLE commerce.partner_wallet_transactions
            ADD CONSTRAINT partner_wallet_transactions_partner_idempotency_key
            UNIQUE (partner_id, idempotency_key);
    END IF;
END $$;

SELECT 'raas_partner_wallet_idem_scope.sql applied successfully.' AS result;
