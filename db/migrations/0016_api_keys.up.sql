-- 0016_api_keys — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- §11 P4 + the `api_access` entitlement in §5: a machine API providers can build against.
--
-- What existed before: `PartnerAuth.cs` (RaaS partner-USER OTP login — a human) and
-- `oauth_authorization_server.sql` + `OAuth.cs` (the MCP/assistant flow — a delegated human).
-- Neither is a machine credential: both assume someone is present to prove who they are.
--
-- ─── The credential ──────────────────────────────────────────────────────────────────────────
-- An API key is `lg_<env>_<prefix>_<secret>`:
--
--   lg_live_a1b2c3d4_9f8e7d6c5b4a...
--   ▲   ▲     ▲         ▲
--   │   │     │         └─ 32 random bytes, base64url. Shown ONCE, stored only as a hash.
--   │   │     └─────────── public lookup handle, indexed and unique. NOT a secret.
--   │   └───────────────── environment, so a test key pasted into production fails loudly
--   └───────────────────── vendor tag, so leak scanners can recognise ours
--
-- The prefix exists so verification is one indexed lookup plus one hash comparison. Without it the
-- only way to identify a key is to hash the candidate against every row, which is O(keys) per
-- request and gets slower the more customers succeed.
--
-- ─── Why the secret is hashed ────────────────────────────────────────────────────────────────
-- Same reason passwords are. A database dump must not be a set of working credentials to every
-- provider's data. Argon2id via the existing IPasswordHasher — a new key format here would mean two
-- password-hashing implementations to keep correct, and the second one is always the weaker.

CREATE TABLE IF NOT EXISTS identity_access.api_keys (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        uuid NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,

    name            varchar(120) NOT NULL,
    key_prefix      varchar(32)  NOT NULL UNIQUE,
    secret_hash     text         NOT NULL,
    environment     varchar(8)   NOT NULL DEFAULT 'live'
                        CHECK (environment IN ('live', 'test')),

    -- Least privilege by default: a key with no scopes can authenticate and do nothing. Issuing a
    -- credential that can read a whole business because nobody chose scopes is how integrations
    -- become breaches.
    scopes          text[] NOT NULL DEFAULT ARRAY[]::text[],

    status          varchar(16) NOT NULL DEFAULT 'active'
                        CHECK (status IN ('active', 'revoked')),

    -- Per-key ceiling. NULL means "use the host default" rather than "unlimited" — an unlimited
    -- default is one misconfigured integration away from taking the platform down.
    rate_limit_per_minute int,

    expires_at      timestamptz,
    last_used_at    timestamptz,
    revoked_at      timestamptz,
    revoked_by_user_id uuid REFERENCES identity_access.users(id) ON DELETE SET NULL,

    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_by      uuid,

    CONSTRAINT api_keys_revoked_is_complete CHECK (status <> 'revoked' OR revoked_at IS NOT NULL),
    CONSTRAINT api_keys_rate_limit_sane
        CHECK (rate_limit_per_minute IS NULL OR rate_limit_per_minute BETWEEN 1 AND 100000)
);

CREATE INDEX IF NOT EXISTS idx_api_keys_brand ON identity_access.api_keys (brand_id, status);

ALTER TABLE identity_access.api_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.api_keys FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON identity_access.api_keys;
CREATE POLICY rls_brand ON identity_access.api_keys
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT, INSERT, UPDATE ON identity_access.api_keys TO app_user;

DROP TRIGGER IF EXISTS trg_api_keys_updated_at ON identity_access.api_keys;
CREATE TRIGGER trg_api_keys_updated_at
    BEFORE UPDATE ON identity_access.api_keys
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ─── Usage metering ──────────────────────────────────────────────────────────────────────────
-- A daily rollup per key, not a row per request. A row per request would make the busiest customer
-- the one whose metering table is unqueryable, which is backwards — and a provider asking "how much
-- am I using" wants a chart, not an event log.
CREATE TABLE IF NOT EXISTS identity_access.api_key_usage (
    api_key_id    uuid NOT NULL REFERENCES identity_access.api_keys(id) ON DELETE CASCADE,
    brand_id      uuid NOT NULL,
    usage_date    date NOT NULL,
    request_count bigint NOT NULL DEFAULT 0,
    error_count   bigint NOT NULL DEFAULT 0,
    updated_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (api_key_id, usage_date)
);

CREATE INDEX IF NOT EXISTS idx_api_key_usage_brand
    ON identity_access.api_key_usage (brand_id, usage_date DESC);

ALTER TABLE identity_access.api_key_usage ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.api_key_usage FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON identity_access.api_key_usage;
CREATE POLICY rls_brand ON identity_access.api_key_usage
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT, INSERT, UPDATE ON identity_access.api_key_usage TO app_user;

-- ─── The authentication lookup ───────────────────────────────────────────────────────────────
-- SECURITY DEFINER for the reason every other one in this schema is: the caller presenting an API
-- key has no session, no brand and no RLS reach — resolving the key is what ESTABLISHES those. It
-- returns the hash for the caller to verify, never a decision: the comparison happens in C# against
-- Argon2id, so this function cannot be turned into an oracle by feeding it guesses.
--
-- It also answers the entitlement question in the same round trip. `api_access` is a §5 sellable
-- feature, and a key belonging to a brand that has stopped paying for the API must stop working —
-- checking that here means it cannot be forgotten at a call site.
CREATE OR REPLACE FUNCTION kernel.resolve_api_key(p_prefix text)
RETURNS TABLE (
    id uuid, brand_id uuid, secret_hash text, scopes text[], status text,
    environment text, rate_limit_per_minute int, expires_at timestamptz, entitled boolean)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = identity_access, tenancy_org, kernel, pg_catalog
AS $$
    SELECT k.id, k.brand_id, k.secret_hash, k.scopes, k.status::text,
           k.environment::text, k.rate_limit_per_minute, k.expires_at,
           EXISTS (SELECT 1 FROM identity_access.brand_feature bf
                   WHERE bf.brand_id = k.brand_id
                     AND bf.feature_key = 'api_access'
                     AND bf.enabled
                     AND (bf.valid_until IS NULL OR bf.valid_until >= current_date))
    FROM   identity_access.api_keys k
    WHERE  k.key_prefix = p_prefix;
$$;

REVOKE ALL ON FUNCTION kernel.resolve_api_key(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.resolve_api_key(text) TO app_user;

-- ─── Metering write ──────────────────────────────────────────────────────────────────────────
-- One upsert per request, on the hot path, so it is deliberately the cheapest thing that is still
-- true: a single INSERT … ON CONFLICT DO UPDATE against a primary key. SECURITY DEFINER for the
-- same reason as the lookup — the key's brand is known, but the request has no RLS session.
CREATE OR REPLACE FUNCTION kernel.record_api_key_use(
    p_key_id uuid, p_brand_id uuid, p_is_error boolean)
RETURNS void
LANGUAGE sql
SECURITY DEFINER
SET search_path = identity_access, kernel, pg_catalog
AS $$
    INSERT INTO identity_access.api_key_usage (api_key_id, brand_id, usage_date, request_count, error_count)
    VALUES (p_key_id, p_brand_id, current_date, 1, CASE WHEN p_is_error THEN 1 ELSE 0 END)
    ON CONFLICT (api_key_id, usage_date) DO UPDATE
        SET request_count = identity_access.api_key_usage.request_count + 1,
            error_count   = identity_access.api_key_usage.error_count
                            + CASE WHEN p_is_error THEN 1 ELSE 0 END,
            updated_at    = now();
$$;

REVOKE ALL ON FUNCTION kernel.record_api_key_use(uuid, uuid, boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.record_api_key_use(uuid, uuid, boolean) TO app_user;

-- ─── Permission ──────────────────────────────────────────────────────────────────────────────
-- Issuing a key mints a credential to a company's data, so it is CRITICAL (step-up required) and
-- owner-side only. Hung off `settings`, a CORE module, so an entitlement lapse can never strip the
-- ability to REVOKE a key — the same reasoning as impersonation.approve in 0014.
INSERT INTO identity_access.permissions (id, code, module, action, name, description, is_system, requires_scope, risk_level, module_key, status, created_at, updated_at)
VALUES (gen_random_uuid(), 'api_keys.manage', 'api_keys', 'manage',
        'Manage API keys',
        'Issue, scope and revoke machine credentials for this account.',
        true, true, 'critical', 'settings', 'active', now(), now())
ON CONFLICT (code) DO UPDATE
    SET name = EXCLUDED.name, description = EXCLUDED.description,
        risk_level = EXCLUDED.risk_level, module_key = EXCLUDED.module_key, updated_at = now();

INSERT INTO identity_access.role_permissions (role_id, permission_id, created_at)
SELECT r.id, p.id, now()
FROM   identity_access.roles r
CROSS JOIN identity_access.permissions p
WHERE  p.code = 'api_keys.manage'
  AND  r.code IN ('brand_admin', 'franchise_owner', 'platform_admin')
ON CONFLICT DO NOTHING;

-- ─── Assertion ───────────────────────────────────────────────────────────────────────────────
-- The prefix must be unique or authentication resolves the wrong key. Cheap to assert, catastrophic
-- to get wrong.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'identity_access' AND tablename = 'api_keys'
          AND indexdef ILIKE '%UNIQUE%key_prefix%')
    THEN
        RAISE EXCEPTION 'api_keys.key_prefix is not uniquely indexed — key resolution would be ambiguous';
    END IF;
END $$;
