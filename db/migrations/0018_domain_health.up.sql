-- 0018_domain_health — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- §12's risk list calls for automated SSL and domain health checks "from day one" (docs/TASKS.md
-- T-12). That task is BLOCKED on OQ-6 — §4.2 offers "Let's Encrypt / Cloudflare-for-SaaS" without
-- choosing, and the two differ in infrastructure, cost, and whether ssl_status is polled or
-- webhook-driven.
--
-- ─── What this migration deliberately does and does not do ───────────────────────────────────
-- It does NOT choose. Certificate ISSUANCE stays blocked, because picking a vendor on someone
-- else's behalf is picking their bill.
--
-- What it builds is the half that is the same whichever vendor wins: knowing whether each custom
-- domain is actually WORKING. That is the half §12 asks for by name, and it is worth having on its
-- own — today a custom domain's certificate can expire and the first anyone hears of it is a
-- customer's customer seeing a browser warning.
--
-- Every column here is a fact an observer records. None of it presumes how the certificate got
-- there: a cert issued by Let's Encrypt, by Cloudflare, or uploaded by hand all leave the same
-- observable expiry.

ALTER TABLE tenancy_org.brand_domains
    -- When the certificate stops being valid. The single most useful number here: it is what turns
    -- "something broke" into "this breaks on Thursday".
    ADD COLUMN IF NOT EXISTS ssl_expires_at     timestamptz,
    ADD COLUMN IF NOT EXISTS ssl_issuer         varchar(200),

    -- When we last looked, and what we saw. `last_checked_at` matters as much as the result: a
    -- healthy-looking row that has not been checked for a month is not evidence of health.
    ADD COLUMN IF NOT EXISTS last_checked_at    timestamptz,
    ADD COLUMN IF NOT EXISTS health_status      varchar(16) NOT NULL DEFAULT 'unknown',
    ADD COLUMN IF NOT EXISTS health_detail      text,

    -- Consecutive failures. One failed check is a network blip; five in a row is an outage, and the
    -- difference is the whole reason to count rather than store a boolean.
    ADD COLUMN IF NOT EXISTS consecutive_failures int NOT NULL DEFAULT 0;

ALTER TABLE tenancy_org.brand_domains DROP CONSTRAINT IF EXISTS brand_domains_health_status_check;
ALTER TABLE tenancy_org.brand_domains ADD CONSTRAINT brand_domains_health_status_check
    CHECK (health_status IN ('unknown', 'healthy', 'degraded', 'unreachable'));

COMMENT ON COLUMN tenancy_org.brand_domains.health_status IS
    'unknown = never checked; healthy = resolves and the certificate is valid; '
    'degraded = resolves but the certificate is expiring or mismatched; unreachable = does not resolve.';

-- Ordered by "check the stalest first", which is what the sweep wants and what makes a backlog
-- drain fairly rather than starving whichever domain sorts last.
CREATE INDEX IF NOT EXISTS idx_brand_domains_health_due
    ON tenancy_org.brand_domains (last_checked_at NULLS FIRST)
    WHERE verified_at IS NOT NULL;

-- ─── What the operator console needs to answer ───────────────────────────────────────────────
-- "Which domains are about to break." SECURITY DEFINER because it deliberately spans ALL tenants —
-- this is the platform's own health view, not a tenant's, and it returns nothing a tenant could use
-- to enumerate others: the caller is gated on saas.read at the endpoint.
CREATE OR REPLACE FUNCTION kernel.domains_needing_attention(p_within_days int DEFAULT 21)
RETURNS TABLE (
    domain text, brand_code text, health_status text, ssl_status text,
    ssl_expires_at timestamptz, days_left int, consecutive_failures int, last_checked_at timestamptz)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = tenancy_org, kernel, public, pg_catalog
AS $$
    SELECT d.domain::text, b.code::text, d.health_status::text, d.ssl_status::text,
           d.ssl_expires_at,
           CASE WHEN d.ssl_expires_at IS NULL THEN NULL
                ELSE floor(extract(epoch FROM d.ssl_expires_at - now()) / 86400)::int END,
           d.consecutive_failures, d.last_checked_at
    FROM   tenancy_org.brand_domains d
    JOIN   tenancy_org.brands b ON b.id = d.brand_id
    WHERE  d.verified_at IS NOT NULL
      AND  (
             d.health_status IN ('degraded', 'unreachable')
             OR d.consecutive_failures > 0
             -- Expiring soon. The default window is three weeks: long enough that a human has time
             -- to act, short enough that the list is not permanently full and therefore ignored.
             OR (d.ssl_expires_at IS NOT NULL AND d.ssl_expires_at < now() + make_interval(days => p_within_days))
             -- Never checked, or not checked in a week. Silence is not health.
             OR d.last_checked_at IS NULL
             OR d.last_checked_at < now() - interval '7 days'
           )
    ORDER BY d.consecutive_failures DESC, d.ssl_expires_at NULLS LAST;
$$;

REVOKE ALL ON FUNCTION kernel.domains_needing_attention(int) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.domains_needing_attention(int) TO app_user;

-- ─── Recording a check ───────────────────────────────────────────────────────────────────────
-- One call per observation, from a worker with no tenant context. Keeps the failure counter honest:
-- a success resets it to zero, a failure increments — the caller never computes it, so two workers
-- racing cannot both write "1".
CREATE OR REPLACE FUNCTION kernel.record_domain_check(
    p_domain      text,
    p_health      text,
    p_detail      text,
    p_ssl_expires timestamptz,
    p_issuer      text)
RETURNS void
LANGUAGE sql
SECURITY DEFINER
SET search_path = tenancy_org, kernel, public, pg_catalog
AS $$
    UPDATE tenancy_org.brand_domains d
       SET health_status = p_health,
           health_detail = p_detail,
           ssl_expires_at = COALESCE(p_ssl_expires, d.ssl_expires_at),
           ssl_issuer     = COALESCE(p_issuer, d.ssl_issuer),
           last_checked_at = now(),
           consecutive_failures =
               CASE WHEN p_health = 'healthy' THEN 0 ELSE d.consecutive_failures + 1 END,
           -- ssl_status follows the observation, so the column that already existed stops being a
           -- value nobody updates. An expired certificate is 'expired' whoever issued it.
           ssl_status =
               CASE
                   WHEN p_ssl_expires IS NOT NULL AND p_ssl_expires <= now() THEN 'expired'
                   WHEN p_health = 'healthy' THEN 'active'
                   WHEN p_health = 'unreachable' THEN d.ssl_status
                   ELSE d.ssl_status
               END,
           updated_at = now()
     WHERE d.domain = p_domain::citext;
$$;

REVOKE ALL ON FUNCTION kernel.record_domain_check(text, text, text, timestamptz, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.record_domain_check(text, text, text, timestamptz, text) TO app_user;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'tenancy_org' AND table_name = 'brand_domains'
                     AND column_name = 'ssl_expires_at')
    THEN
        RAISE EXCEPTION 'domain health columns were not added';
    END IF;
    RAISE NOTICE 'domain health tracking ready (issuance still blocked on OQ-6)';
END $$;
