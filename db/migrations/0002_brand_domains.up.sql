-- 0002_brand_domains — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- PLATFORM_STRATEGY.md §4.2 item 1 — the storage half of white-label tier T2 ("Own web (PWA)":
-- a provider runs on theirbrand.com under their own logo, colours and name).
--
--   1. Provider adds a CNAME → our edge, and proves ownership with a TXT record
--      (verification_txt). Until verified_at is stamped, the domain MUST NOT resolve.
--   2. SSL is automated; ssl_status is the feedback channel from that automation.
--   3. Resolution middleware maps a request's Host header → this table → brand_id →
--      app.current_brand_id, and RLS does the rest. One deployment, N branded domains,
--      no per-provider servers.
--
-- This migration is the schema only. Host resolution is 0003 (task T-09), TXT verification
-- is T-10, and the SSL automation that writes ssl_status is T-12 (gated on OQ-6: Let's Encrypt
-- via our own edge vs Cloudflare-for-SaaS).
--
-- Placed in tenancy_org because a domain is a property of the tenant, alongside brands.

CREATE TABLE IF NOT EXISTS tenancy_org.brand_domains (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id         UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,

    -- The hostname exactly as it arrives in the Host header, lowercased. CITEXT because DNS is
    -- case-insensitive and "TheirBrand.com" and "theirbrand.com" are the same host; storing it
    -- case-sensitively would let the same domain be claimed twice.
    domain           CITEXT NOT NULL,

    -- The value the provider publishes as a DNS TXT record to prove ownership. Generated when the
    -- domain is added; kept after verification so a re-check can be run without re-issuing it.
    verification_txt VARCHAR(128) NOT NULL,

    -- NULL = ownership not yet proven. Resolution MUST require this to be non-NULL, otherwise
    -- claiming someone else's hostname would hijack their traffic to your brand.
    verified_at      TIMESTAMPTZ,

    -- Certificate lifecycle, written by the SSL automation (T-12). Deliberately a small closed set
    -- with a CHECK rather than a PG enum, per ADR-005. 'pending' is the state on insert; T-12 may
    -- need to widen this once OQ-6 picks the issuer, which is a one-line CHECK change.
    ssl_status       VARCHAR(20) NOT NULL DEFAULT 'pending'
                     CHECK (ssl_status IN ('pending', 'active', 'failed', 'expired')),

    -- The canonical host for the brand: the one to redirect to, and the one to use when the
    -- platform has to *generate* an absolute URL (emails, payment-link returns, OAuth redirects).
    is_primary       BOOLEAN NOT NULL DEFAULT false,

    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by       UUID,
    updated_by       UUID,

    -- GLOBALLY unique, not per-brand. Host resolution is domain → brand, so a hostname that mapped
    -- to two brands would be unresolvable; this constraint is what makes the lookup total.
    CONSTRAINT brand_domains_domain_key UNIQUE (domain)
);

-- At most one primary host per brand. Partial, so any number of non-primary aliases can coexist.
CREATE UNIQUE INDEX IF NOT EXISTS idx_brand_domains_one_primary
    ON tenancy_org.brand_domains(brand_id)
    WHERE is_primary;

-- The resolution hot path: verified hosts only. The UNIQUE(domain) index alone would serve the
-- lookup, but this partial index keeps unverified rows out of it entirely.
CREATE INDEX IF NOT EXISTS idx_brand_domains_verified
    ON tenancy_org.brand_domains(domain)
    WHERE verified_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_brand_domains_brand
    ON tenancy_org.brand_domains(brand_id);

-- ─── RLS: the house brand-scoped pattern, identical to tenancy_org.stores/franchises ────────
ALTER TABLE tenancy_org.brand_domains ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON tenancy_org.brand_domains;
CREATE POLICY rls_brand ON tenancy_org.brand_domains
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));

GRANT SELECT, INSERT, UPDATE, DELETE ON tenancy_org.brand_domains TO app_user, app_admin;

-- ─── updated_at maintenance, same trigger every other table uses ────────────────────────────
DROP TRIGGER IF EXISTS trg_brand_domains_set_updated_at ON tenancy_org.brand_domains;
CREATE TRIGGER trg_brand_domains_set_updated_at
    BEFORE UPDATE ON tenancy_org.brand_domains
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

COMMENT ON TABLE tenancy_org.brand_domains IS
    'White-label tier T2 (PLATFORM_STRATEGY.md §4.2): custom hostnames a brand serves under. '
    'Host header -> brand_id resolution reads this table; only rows with verified_at set may resolve.';
COMMENT ON COLUMN tenancy_org.brand_domains.verification_txt IS
    'Value the provider publishes as a DNS TXT record to prove ownership of the domain.';
COMMENT ON COLUMN tenancy_org.brand_domains.verified_at IS
    'NULL until ownership is proven. Resolution must treat NULL as "does not resolve".';
COMMENT ON COLUMN tenancy_org.brand_domains.is_primary IS
    'The canonical host for the brand (redirect target, and the base for generated absolute URLs). '
    'At most one per brand, enforced by idx_brand_domains_one_primary.';
