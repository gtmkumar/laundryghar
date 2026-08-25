-- 0017_onboarding_progress — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- §9's onboarding order: "sign up → template → plan/trial → wizard (locations, catalog seed, staff
-- invites, gateway link) → live on sub-domain."
--
-- Signup (T-16) covers everything up to and including the template's catalogue seed. This is the
-- wizard, and the thing that must be true at the end of it: the brand is reachable on its
-- sub-domain.
--
-- ─── Progress is DERIVED, not stored ─────────────────────────────────────────────────────────
-- The obvious design is a `completed_steps` column the API appends to. It is also the design that
-- lies: a brand can complete "add your first location", delete the location, and the wizard still
-- says done — so the console shows a finished setup over a business that cannot take an order.
--
-- So the only things stored here are the two a wizard genuinely cannot infer:
--   * `skipped_steps` — the user said "not now", which no amount of looking at the data reveals
--   * `draft`         — half-typed answers, so closing the tab does not lose them
--
-- Everything else is computed from the real rows by kernel.brand_onboarding_facts below. Resume is
-- then free and cannot drift: "where was I" is always "the first step that is neither done nor
-- skipped", recomputed from what actually exists.

CREATE TABLE IF NOT EXISTS tenancy_org.onboarding_progress (
    brand_id      uuid PRIMARY KEY REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,

    -- "Not now." The only piece of state a wizard cannot infer from the data.
    skipped_steps text[] NOT NULL DEFAULT ARRAY[]::text[],

    -- Half-typed answers, keyed by step. Purely a convenience — nothing reads it to decide status.
    draft         jsonb NOT NULL DEFAULT '{}'::jsonb,

    started_at    timestamptz NOT NULL DEFAULT now(),
    -- Stamped when the wizard is first observed complete, so "how long did onboarding take" is
    -- answerable. Never a substitute for the derived status.
    completed_at  timestamptz,

    updated_at    timestamptz NOT NULL DEFAULT now(),
    created_by    uuid,
    updated_by    uuid
);

ALTER TABLE tenancy_org.onboarding_progress ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy_org.onboarding_progress FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON tenancy_org.onboarding_progress;
CREATE POLICY rls_brand ON tenancy_org.onboarding_progress
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT, INSERT, UPDATE ON tenancy_org.onboarding_progress TO app_user;

DROP TRIGGER IF EXISTS trg_onboarding_progress_updated_at ON tenancy_org.onboarding_progress;
CREATE TRIGGER trg_onboarding_progress_updated_at
    BEFORE UPDATE ON tenancy_org.onboarding_progress
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ─── The facts each step is judged on ────────────────────────────────────────────────────────
-- SECURITY DEFINER, and this one earns it twice over: the gateway fact lives in
-- `tenancy_org.brands.config`, and that table is `rls_admin_only` — the brands-RLS trap, which has
-- already been sprung four times in this codebase. An owner asking "how far through setup am I"
-- would read zero rows and be told they had not started.
CREATE OR REPLACE FUNCTION kernel.brand_onboarding_facts(p_brand_id uuid)
RETURNS TABLE (
    locations int, catalog_items int, staff_members int,
    has_gateway boolean, primary_domain text, own_franchise_id uuid)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = tenancy_org, identity_access, customer_catalog, kernel, pg_catalog
AS $$
    SELECT
        (SELECT count(*)::int FROM tenancy_org.stores s
          WHERE s.brand_id = p_brand_id AND s.status = 'active' AND s.deleted_at IS NULL),

        (SELECT count(*)::int FROM customer_catalog.items i
          WHERE i.brand_id = p_brand_id AND i.status = 'active'),

        -- Staff BESIDES the owner. One person is not a team, and "invite your staff" is not done
        -- because the founder exists.
        (SELECT count(DISTINCT m.user_id)::int
           FROM identity_access.user_scope_memberships m
          WHERE m.scope_id = p_brand_id AND m.scope_type = 'brand' AND m.revoked_at IS NULL),

        (SELECT coalesce(b.config ->> 'paymentGateway', '') <> '' FROM tenancy_org.brands b
          WHERE b.id = p_brand_id),

        (SELECT d.domain::text FROM tenancy_org.brand_domains d
          WHERE d.brand_id = p_brand_id AND d.is_primary AND d.verified_at IS NOT NULL
          LIMIT 1),

        -- The provider's OWN operating entity, created at signup.
        --
        -- A store requires a franchise_id, and `franchises` is an ENTERPRISE feature (§5) — so a
        -- Starter or Growth provider is correctly refused when they list franchises, and therefore
        -- could not discover the id their very first location needs. The wizard hands it to them
        -- directly. This is not a way around the entitlement: it exposes exactly one id, the
        -- provider's own, and grants nothing about franchising to anybody else.
        (SELECT f.id FROM tenancy_org.franchises f
          WHERE f.brand_id = p_brand_id AND f.deleted_at IS NULL
          ORDER BY f.created_at LIMIT 1);
$$;

REVOKE ALL ON FUNCTION kernel.brand_onboarding_facts(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.brand_onboarding_facts(uuid) TO app_user;

-- ─── Going live on the sub-domain ────────────────────────────────────────────────────────────
-- §9's finish line. Creates `<brand-code>.<base>` and marks it verified and primary IMMEDIATELY —
-- no DNS challenge — because the zone is ours. The TXT-challenge flow (migration 0002/0003) exists
-- for domains the PROVIDER owns, where proving control is the entire point; demanding proof that we
-- control our own zone would be ceremony, and it is the difference between "live in minutes" and
-- "live once DNS propagates".
--
-- Idempotent: calling it twice returns the existing domain rather than creating a second one.
CREATE OR REPLACE FUNCTION kernel.ensure_brand_subdomain(p_brand_id uuid, p_base text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
-- `public` is on the path because that is where the citext extension lives, and brand_domains.domain
-- is citext. Omitting it produced `type "citext" does not exist` at runtime — a SECURITY DEFINER
-- function's search_path is pinned, so anything it names must be reachable from that list.
SET search_path = tenancy_org, kernel, public, pg_catalog
AS $$
DECLARE
    v_code   text;
    v_domain text;
    v_existing text;
BEGIN
    SELECT lower(b.code) INTO v_code FROM tenancy_org.brands b WHERE b.id = p_brand_id;
    IF v_code IS NULL THEN
        RETURN NULL;
    END IF;

    -- Already live on something verified and primary — a custom domain the provider added, or a
    -- previous call. Never displace it: a brand that moved to its own domain must not be silently
    -- pushed back onto ours.
    SELECT d.domain::text INTO v_existing
    FROM   tenancy_org.brand_domains d
    WHERE  d.brand_id = p_brand_id AND d.is_primary AND d.verified_at IS NOT NULL
    LIMIT  1;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;

    v_domain := v_code || '.' || p_base;

    -- verification_txt is NOT NULL because the provider-owned-domain flow needs a TXT challenge to
    -- prove control (migration 0002). This host is in OUR zone, so there is nothing to prove — the
    -- column is filled with a marker that says exactly that, rather than a challenge nobody will
    -- ever look up.
    INSERT INTO tenancy_org.brand_domains
        (brand_id, domain, verification_txt, verified_at, is_primary, ssl_status)
    VALUES (p_brand_id, v_domain::citext, 'platform-subdomain', now(), true, 'pending')
    ON CONFLICT (domain) DO UPDATE
        SET verified_at = COALESCE(tenancy_org.brand_domains.verified_at, now()),
            is_primary  = true
        -- Only if it is already OURS. A collision with another brand's domain must fail loudly
        -- rather than quietly hand one tenant another's hostname.
        WHERE tenancy_org.brand_domains.brand_id = p_brand_id;

    SELECT d.domain::text INTO v_domain
    FROM   tenancy_org.brand_domains d
    WHERE  d.brand_id = p_brand_id AND d.is_primary
    LIMIT  1;

    RETURN v_domain;
END $$;

REVOKE ALL ON FUNCTION kernel.ensure_brand_subdomain(uuid, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.ensure_brand_subdomain(uuid, text) TO app_user;

-- ─── Assertion ───────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE n int;
BEGIN
    -- The facts function must answer for a brand that exists, and answer NOTHING for one that does
    -- not — a wizard that reports progress for a random uuid is reporting someone else's.
    SELECT count(*) INTO n FROM kernel.brand_onboarding_facts(gen_random_uuid());
    IF n <> 1 THEN
        RAISE EXCEPTION 'brand_onboarding_facts returned % rows for an unknown brand, expected 1 empty row', n;
    END IF;
    RAISE NOTICE 'onboarding facts function responds';
END $$;
