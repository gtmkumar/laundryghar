-- 0003_resolve_brand_domain — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- PLATFORM_STRATEGY.md §4.2 item 4 — the lookup half of Host-header brand resolution.
-- Migration 0002 added tenancy_org.brand_domains; this adds the ONE function the resolution
-- middleware calls.
--
-- ─── Why a SECURITY DEFINER function and not a plain SELECT ────────────────────────────────
-- The middleware runs on ANONYMOUS requests, before any token exists, as the non-superuser
-- `app_user`. brand_domains carries the house brand-scoped policy
--     USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
-- and on an anonymous request neither disjunct is true: bypass is off and current_brand_id() is
-- NULL, so `brand_id = NULL` is NULL and the table reads as EMPTY. Verified against the live DB:
--     $ psql -U app_user -c 'select count(*) from tenancy_org.brand_domains'   ->  0
-- This is the same trap that bites every new anonymous endpoint here.
--
-- The alternative — adding the resolution path to core.WebApi's pre-auth `bypass_rls` allow-list —
-- would hand those requests a BLANKET bypass over every tenant table for the rest of the request.
-- This function instead grants exactly one capability, "given a hostname, tell me its brand id",
-- and returns nothing else. Same pattern, and same rationale, as kernel.user_perm_version(uuid).
--
-- ─── Two behaviours worth stating, because they are load-bearing ───────────────────────────
--   * verified_at IS NOT NULL is REQUIRED. A row exists from the moment a provider *adds* a
--     domain, long before they prove they own it. Resolving unverified rows would let anyone
--     claim any hostname and capture its traffic.
--   * A SUSPENDED brand still resolves. Per §9 suspension is "login-only mode" — the owner must
--     still be able to reach their own domain to see and pay the invoice that reinstates them.
--     Only soft-deleted brands stop resolving.

CREATE OR REPLACE FUNCTION kernel.resolve_brand_domain(p_host text)
    RETURNS uuid
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    -- Pinned so a caller cannot shadow `tenancy_org` or the citext operators with a temp schema
    -- and steer a SECURITY DEFINER body. Mandatory for any SECURITY DEFINER function.
    SET search_path = pg_catalog, tenancy_org, public
AS $$
    SELECT d.brand_id
    FROM   tenancy_org.brand_domains d
    JOIN   tenancy_org.brands b ON b.id = d.brand_id
    -- The ::citext cast is REQUIRED, not decorative. `citext = text` resolves to the TEXT
    -- operator and compares CASE-SENSITIVELY:
    --     SELECT 'ABC'::citext = 'abc'::text;   ->  f
    --     SELECT 'ABC'::citext = 'abc'::citext; ->  t
    -- p_host arrives as text. What this protects is the casing of the STORED value: the column is
    -- citext, so uniqueness ignores case, but the row keeps whatever spelling it was inserted with.
    -- A provider who registers "TheirBrand.example" must still be reachable at the lowercased host
    -- the caller looks up — without the cast that row would never match.
    WHERE  d.domain = p_host::citext
      AND  d.verified_at IS NOT NULL
      AND  b.deleted_at IS NULL
    LIMIT  1
$$;

-- Least privilege: not callable by PUBLIC, only by the two application roles.
REVOKE ALL ON FUNCTION kernel.resolve_brand_domain(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.resolve_brand_domain(text) TO app_user, app_admin;

COMMENT ON FUNCTION kernel.resolve_brand_domain(text) IS
    'Host header -> brand_id for white-label tier T2 (PLATFORM_STRATEGY.md §4.2). SECURITY DEFINER '
    'because the caller is anonymous and brand_domains is RLS-protected; grants exactly this one '
    'lookup rather than a blanket bypass. Only VERIFIED domains of non-deleted brands resolve; '
    'a suspended brand still resolves so its owner can reach the billing page (§9 login-only mode).';
