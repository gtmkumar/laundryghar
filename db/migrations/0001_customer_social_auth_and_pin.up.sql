-- 0001_customer_social_auth_and_pin — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- Adds the storage needed for friction-free customer sign-up:
--   1. customer_catalog.customer_identities — federated (Google/Apple) identity links.
--      A customer may sign in with Google before they ever supply a phone number, so
--      the provider subject is the join key, NOT the email (a Google account's email
--      can change; `sub` never does).
--   2. PIN columns on customer_catalog.customers — returning users unlock with a PIN
--      (or device biometrics, which are enforced client-side over the stored refresh
--      token and therefore need no server state).
--   3. customers.phone_e164 becomes NULLABLE. Google-first sign-up creates the customer
--      row before a phone number exists, and supplying the phone is explicitly skippable.
--      The (brand_id, phone_e164) UNIQUE constraint keeps working: PostgreSQL treats
--      NULLs as distinct, so any number of phone-less customers can coexist.

-- ─── 1. Federated identity links ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS customer_catalog.customer_identities (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id    UUID NOT NULL REFERENCES customer_catalog.customers(id) ON DELETE CASCADE,
    brand_id       UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE RESTRICT,
    provider       VARCHAR(20) NOT NULL CHECK (provider IN ('google','apple','facebook')),
    -- The provider's immutable subject identifier (Google `sub`, Apple `sub`).
    provider_uid   VARCHAR(255) NOT NULL,
    email          CITEXT,
    email_verified BOOLEAN NOT NULL DEFAULT false,
    display_name   VARCHAR(200),
    avatar_url     TEXT,
    last_login_at  TIMESTAMPTZ,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    version        INTEGER NOT NULL DEFAULT 1,
    created_by     UUID,
    updated_by     UUID,
    -- One provider account maps to exactly one customer per brand.
    UNIQUE (brand_id, provider, provider_uid),
    -- A customer links at most one account per provider.
    UNIQUE (customer_id, provider)
);

CREATE INDEX IF NOT EXISTS idx_custident_customer
    ON customer_catalog.customer_identities(customer_id);
CREATE INDEX IF NOT EXISTS idx_custident_email
    ON customer_catalog.customer_identities(email)
    WHERE email IS NOT NULL;

ALTER TABLE customer_catalog.customer_identities ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS custident_tenant ON customer_catalog.customer_identities;
CREATE POLICY custident_tenant ON customer_catalog.customer_identities
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

GRANT SELECT, INSERT, UPDATE, DELETE
    ON customer_catalog.customer_identities TO app_user, app_admin;

COMMENT ON TABLE customer_catalog.customer_identities IS
    'Federated sign-in links (Google/Apple/Facebook) for customer_catalog.customers. '
    'Joined on the provider subject (provider_uid), never on email.';

-- ─── 2. PIN unlock for returning customers ──────────────────────────────────
ALTER TABLE customer_catalog.customers
    ADD COLUMN IF NOT EXISTS pin_hash            TEXT,
    ADD COLUMN IF NOT EXISTS pin_set_at          TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS pin_failed_attempts SMALLINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS pin_locked_until    TIMESTAMPTZ;

COMMENT ON COLUMN customer_catalog.customers.pin_hash IS
    'Argon2id hash of the customer''s unlock PIN (same IPasswordHasher as staff passwords). '
    'NULL until the customer sets one.';

-- ─── 3. Phone becomes optional (Google-first sign-up) ───────────────────────
ALTER TABLE customer_catalog.customers
    ALTER COLUMN phone_e164 DROP NOT NULL;

-- The partial index backing phone lookups should skip phone-less rows.
DROP INDEX IF EXISTS customer_catalog.idx_customers_brand_phone;
CREATE INDEX idx_customers_brand_phone
    ON customer_catalog.customers(brand_id, phone_e164)
    WHERE deleted_at IS NULL AND phone_e164 IS NOT NULL;
