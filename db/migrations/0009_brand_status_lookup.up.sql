-- 0009_brand_status_lookup — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- The lookup behind §9's login-only suspension gate (task T-18).
--
-- ─── The brands-RLS trap, for the third time this initiative ─────────────────────────────────
-- `tenancy_org.brands` carries `rls_admin_only USING (kernel.rls_bypass())` — it is readable ONLY
-- under a bypass. Ordinary tenant traffic runs as `app_user` with bypass off, so:
--     $ psql -U app_user  -c 'select count(*) from tenancy_org.brands'   ->  0
--     $ psql -U postgres  -c 'select count(*) from tenancy_org.brands'   ->  1
--
-- The suspension middleware read `brands.status` through EF and therefore got NULL for every
-- tenant request. Because that store fails OPEN by design (a status lookup must never take a
-- healthy tenant offline), the gate did not error — it silently never fired. A suspended brand
-- kept trading, and every unit test still passed, because the tests inject a fake store.
--
-- Caught only by driving a real suspended brand against a running host. Worth recording: a
-- fail-open default plus an RLS-invisible table is a combination that hides itself.
--
-- Same remedy as kernel.resolve_brand_domain (0003): grant exactly ONE capability — "given a brand
-- id, tell me its status" — rather than opening a blanket bypass for the request.

CREATE OR REPLACE FUNCTION kernel.brand_status(p_brand_id uuid)
    RETURNS text
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    -- Pinned so a caller cannot shadow tenancy_org with a temp schema and steer a DEFINER body.
    SET search_path = pg_catalog, tenancy_org, public
AS $$
    SELECT b.status::text
    FROM   tenancy_org.brands b
    WHERE  b.id = p_brand_id
$$;

-- Least privilege: not callable by PUBLIC.
REVOKE ALL ON FUNCTION kernel.brand_status(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.brand_status(uuid) TO app_user, app_admin;

COMMENT ON FUNCTION kernel.brand_status(uuid) IS
    'Brand lifecycle status for the §9 login-only suspension gate. SECURITY DEFINER because '
    'tenancy_org.brands is readable only under an RLS bypass, and the gate runs on ordinary tenant '
    'traffic; grants exactly this one lookup rather than a request-wide bypass.';
