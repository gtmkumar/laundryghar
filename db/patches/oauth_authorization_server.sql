-- =============================================================================
-- db/patches/oauth_authorization_server.sql
-- OAuth 2.1 authorization-server facade tables in the identity_access schema.
-- RFC 7591 dynamic client registration + RFC 7636 PKCE authorization codes.
--
-- Tables added: oauth_clients, oauth_authorization_codes
-- Idempotent: uses CREATE TABLE IF NOT EXISTS + CREATE INDEX IF NOT EXISTS.
-- Apply after: 02_bc2_identity_access.sql
-- =============================================================================

-- ── oauth_clients — RFC 7591 dynamically-registered public clients ───────────
CREATE TABLE IF NOT EXISTS identity_access.oauth_clients (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    client_id           VARCHAR(64) NOT NULL UNIQUE,
    client_name         VARCHAR(200) NOT NULL,
    -- JSON array of allowed redirect URIs stored as text[].
    -- Validated on registration: https:// exact-match OR http://localhost/* OR
    -- http://127.0.0.1/* (port-agnostic loopback, scheme+host match only).
    redirect_uris       TEXT[] NOT NULL,
    -- Public clients only (no client_secret). Extension: token_endpoint_auth_method='none'.
    is_active           BOOLEAN NOT NULL DEFAULT true,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_oauth_clients_client_id
    ON identity_access.oauth_clients (client_id);

-- ── oauth_authorization_codes — single-use PKCE codes (5-min TTL) ───────────
-- code_hash:      SHA-256 of the raw opaque code returned to the client.
--                 The raw code is ≥ 256 bits of cryptographic randomness.
-- code_challenge: BASE64URL(SHA-256(code_verifier)) sent by the client (S256).
-- consumed_at:    set atomically on first use; any subsequent use = replay attack.
CREATE TABLE IF NOT EXISTS identity_access.oauth_authorization_codes (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code_hash           TEXT NOT NULL UNIQUE,   -- SHA-256(raw_code), hex-encoded
    client_id           VARCHAR(64) NOT NULL
                            REFERENCES identity_access.oauth_clients(client_id)
                            ON DELETE CASCADE,
    redirect_uri        TEXT NOT NULL,          -- exact redirect_uri used at /authorize
    code_challenge      TEXT NOT NULL,          -- BASE64URL(SHA-256(verifier)) — S256 only
    customer_id         UUID NOT NULL,
    brand_id            UUID NOT NULL,
    scope               TEXT NOT NULL DEFAULT 'mcp:booking',
    expires_at          TIMESTAMPTZ NOT NULL,   -- now() + 5 minutes
    consumed_at         TIMESTAMPTZ,            -- NULL = unused; non-NULL = redeemed or replayed
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_oauth_codes_hash
    ON identity_access.oauth_authorization_codes (code_hash)
    WHERE consumed_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_oauth_codes_cleanup
    ON identity_access.oauth_authorization_codes (expires_at);

-- ── Addendum: last_used_at + stale-client cleanup support ──────────────────
-- last_used_at: updated on each successful /oauth/token exchange.
-- Used by OAuthCleanupService to retain active clients and purge never-used ones
-- that were registered > 7 days ago (likely abandoned / probing attempts).

ALTER TABLE identity_access.oauth_clients
    ADD COLUMN IF NOT EXISTS last_used_at TIMESTAMPTZ NULL;

-- Index for the cleanup query: WHERE last_used_at IS NULL AND created_at < now() - interval '7 days'
CREATE INDEX IF NOT EXISTS idx_oauth_clients_stale
    ON identity_access.oauth_clients (created_at)
    WHERE last_used_at IS NULL;

-- RLS: no RLS on these tables — they are accessed only by the Identity service
-- which connects as the runtime app_user and performs its own logic-level checks.
-- No cross-tenant data here (customer_id + brand_id embedded in the code row).
