-- =============================================================================
-- db/patches/raas_partner_wallet_schema.sql
--
-- PURPOSE (RaaS full build, FULL-9 / issue #14): create the partner prepaid wallet
-- data layer in the COMMERCE schema —
--   • commerce.partner_wallet_accounts     — one prepaid balance per partner (partner_id UNIQUE)
--   • commerce.partner_wallet_transactions — append-only credit/debit ledger
--
-- Cloned from commerce.wallet_accounts / commerce.wallet_transactions, swapping
-- customer_id → partner_id. partner_id is a SCALAR cross-BC reference to
-- logistics.partners(id): NO foreign key is declared (commerce and logistics are separate
-- bounded contexts — mirrors partner_bookings.brand_id "no FK / cross-schema"); isolation is
-- enforced by the rls_partner policy, not referential integrity.
--
-- available_balance is a GENERATED ALWAYS column (balance - locked_balance) — read-only.
--
-- RLS (rls_partner policies + ENABLE RLS) lives in the companion patch
-- db/patches/rls_partner_wallet.sql — run it AFTER this one.
--
-- Idempotent. Safe to re-run.
--
-- RUN (manual, as postgres — AFTER harden_app_user_and_rls_bypass.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/raas_partner_wallet_schema.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- Harmless: schema already exists in every deployed environment.
CREATE SCHEMA IF NOT EXISTS commerce;

-- ---------------------------------------------------------------------------
-- 1. commerce.partner_wallet_accounts — one prepaid balance per partner
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS commerce.partner_wallet_accounts (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    partner_id          UUID NOT NULL,                 -- rls_partner key; scalar cross-BC ref → logistics.partners (no FK)
    currency_code       CHARACTER(3) NOT NULL DEFAULT 'INR',
    balance             NUMERIC(14,2) NOT NULL DEFAULT 0,
    locked_balance      NUMERIC(14,2) NOT NULL DEFAULT 0,
    available_balance   NUMERIC(14,2) GENERATED ALWAYS AS (balance - locked_balance) STORED,
    lifetime_credit     NUMERIC(14,2) NOT NULL DEFAULT 0,
    lifetime_debit      NUMERIC(14,2) NOT NULL DEFAULT 0,
    last_transaction_at TIMESTAMPTZ,
    version             INTEGER NOT NULL DEFAULT 1,
    status              VARCHAR(20) NOT NULL DEFAULT 'active'
                        CHECK (status IN ('active','frozen','closed')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by          UUID,
    updated_by          UUID,
    CONSTRAINT partner_wallet_accounts_partner_id_key UNIQUE (partner_id)
);

-- ---------------------------------------------------------------------------
-- 2. commerce.partner_wallet_transactions — append-only credit/debit ledger
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS commerce.partner_wallet_transactions (
    id                        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    partner_wallet_account_id UUID NOT NULL,           -- in-BC FK → partner_wallet_accounts.id
    partner_id                UUID NOT NULL,            -- rls_partner key (denormalised for isolation)
    direction                 SMALLINT NOT NULL CHECK (direction IN (1, -1)),  -- 1 = credit, -1 = debit
    amount                    NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    balance_before            NUMERIC(14,2) NOT NULL,
    balance_after             NUMERIC(14,2) NOT NULL,
    reference_type            VARCHAR(30),              -- 'topup' | 'partner_booking'
    reference_id              UUID,
    idempotency_key           VARCHAR(100),
    notes                     TEXT,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by                UUID,
    CONSTRAINT partner_wallet_transactions_partner_wallet_account_id_fkey
        FOREIGN KEY (partner_wallet_account_id)
        REFERENCES commerce.partner_wallet_accounts(id) ON DELETE RESTRICT,
    CONSTRAINT partner_wallet_transactions_idempotency_key_key UNIQUE (idempotency_key)
);
CREATE INDEX IF NOT EXISTS idx_partner_wallet_transactions_partner
    ON commerce.partner_wallet_transactions(partner_id);

-- ---------------------------------------------------------------------------
-- 3. GRANTs — explicit CRUD for the runtime roles. ALTER DEFAULT PRIVILEGES in
-- harden_app_user_and_rls_bypass.sql already covers future commerce tables, but grant
-- explicitly so this patch is self-contained regardless of apply order.
-- ---------------------------------------------------------------------------
DO $grants$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['app_user','app_admin'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA commerce TO %I', r);
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE, DELETE ON '
                'commerce.partner_wallet_accounts, commerce.partner_wallet_transactions TO %I', r);
        END IF;
    END LOOP;
END
$grants$;

SELECT 'raas_partner_wallet_schema.sql applied successfully.' AS result;
