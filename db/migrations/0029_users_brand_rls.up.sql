-- 0029 — F-3: give identity_access.users a database-level tenant boundary.
--
-- The live audit (docs/ABAC_AUDIT_2026-08-31.md) found two critical cross-tenant disclosures on
-- this table: GET /admin/users returned every user on the platform to any brand admin, and
-- GET /admin/users/{id} returned any user's PAN, masked Aadhaar, bank account, IFSC and UPI across
-- tenants. Both were fixed in application code (UserBrandScope.ScopedToCallerBrand).
--
-- F-3 is the CONDITION those two were symptoms of. Every other sensitive table in this database has
-- two independent defences: a tenancy predicate in the handler and an RLS policy underneath. This
-- one has exactly one, because `users` carries no brand_id column and has row security switched
-- off. So the rule lived in application code alone, written out by hand at each call site — and of
-- the three queries that needed it, two had simply omitted it and nothing detected that for as long
-- as they existed.
--
-- ── Why there is no brand_id column ───────────────────────────────────────────────────────────
--
-- A user is not owned by a brand; a user HOLDS MEMBERSHIPS, and a membership is what implies a
-- brand. The membership may be at the brand itself or at a franchise, store or warehouse beneath
-- it. Denormalising a primary_brand_id would need a trigger on every membership write to stay
-- true, and would still be wrong for anyone who legitimately holds memberships under two brands.
--
-- So the boundary is RESOLVED rather than stored, by the function below, which is the exact SQL
-- twin of UserBrandScope.ScopedToCallerBrand — same four scope types, same liveness test.
--
-- ── Why SECURITY DEFINER ──────────────────────────────────────────────────────────────────────
--
-- The resolver reads user_scope_memberships, franchises, stores and warehouses. The last three
-- already have RLS enabled, and memberships carries an rls_user_self policy that is currently
-- inert only because row security is off on that table. Evaluating the resolver as the caller
-- would therefore make this policy's meaning depend on three other tables' policies — and the day
-- someone enables RLS on memberships, a brand admin would silently stop seeing anyone but
-- themselves. SECURITY DEFINER with a pinned search_path makes the resolution mean one thing.
--
-- ── Why INSERT is deliberately unrestricted ───────────────────────────────────────────────────
--
-- A new user's row is written BEFORE their first membership — there is no other order available,
-- since the membership carries a foreign key to the user. A WITH CHECK requiring a resolvable
-- brand would therefore make it impossible to create a user at all: the predicate can only become
-- true after the row it is guarding already exists.
--
-- Leaving INSERT open costs nothing this policy was meant to buy. F-3 is a DISCLOSURE finding, and
-- a users row grants no authority on its own: it is inert until a membership row points at it, and
-- membership writes are guarded separately (AssignPermission, A0.4). What an attacker gains by
-- inserting an unreachable row is an unreachable row.

-- ── The resolver ──────────────────────────────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION identity_access.user_in_brand(p_user uuid, p_brand uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
PARALLEL SAFE
SET search_path = pg_catalog, identity_access, tenancy_org
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM identity_access.user_scope_memberships m
        WHERE m.user_id = p_user
          AND m.revoked_at IS NULL
          AND (m.expires_at IS NULL OR m.expires_at > now())
          AND (
                 (m.scope_type = 'brand' AND m.scope_id = p_brand)

              OR (m.scope_type = 'franchise' AND EXISTS (
                     SELECT 1 FROM tenancy_org.franchises f
                     WHERE f.id = m.scope_id AND f.brand_id = p_brand))

              OR (m.scope_type = 'store' AND EXISTS (
                     SELECT 1 FROM tenancy_org.stores s
                     WHERE s.id = m.scope_id AND s.brand_id = p_brand))

              OR (m.scope_type = 'warehouse' AND EXISTS (
                     SELECT 1 FROM tenancy_org.warehouses w
                     WHERE w.id = m.scope_id AND w.brand_id = p_brand))
          )
    )
$$;

-- A NULL p_brand resolves to false, not to true: `m.scope_id = NULL` is NULL, so EXISTS is empty.
-- That is the intended reading — a session that has not said which brand it is acting for has not
-- established a right to see anyone. Platform operators are not affected; they reach the table
-- through kernel.rls_bypass() below, which TenantResolutionMiddleware sets for every platform-admin
-- request. The 'platform' and 'territory' scope types are absent on purpose: neither resolves to a
-- single brand, and platform membership is already served by the bypass.

COMMENT ON FUNCTION identity_access.user_in_brand(uuid, uuid) IS
    'True when the user holds a live membership resolving to the given brand, directly or through '
    'one of its franchises, stores or warehouses. SQL twin of '
    'core.Application/Identity/Users/Common/UserBrandScope.ScopedToCallerBrand — change both or '
    'neither.';

REVOKE ALL ON FUNCTION identity_access.user_in_brand(uuid, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity_access.user_in_brand(uuid, uuid) TO app_user, app_admin;

-- ── The policies ──────────────────────────────────────────────────────────────────────────────
--
-- rls_admin_only is `USING (kernel.rls_bypass())` and nothing else. It has never run — row security
-- is off on this table — and enabling it as written would hide every user from every ordinary
-- session, which is why simply switching RLS on was never an option here.

DROP POLICY IF EXISTS rls_admin_only ON identity_access.users;
DROP POLICY IF EXISTS rls_users_select ON identity_access.users;
DROP POLICY IF EXISTS rls_users_insert ON identity_access.users;
DROP POLICY IF EXISTS rls_users_update ON identity_access.users;
DROP POLICY IF EXISTS rls_users_delete ON identity_access.users;

-- The self arm (`id = kernel.current_user_id()`) is not a convenience. Without it, anyone whose
-- own memberships do not resolve to the brand their session is carrying becomes invisible to
-- themselves, and every "read my own row" path — profile, password change, step-up — breaks in a
-- way that looks like the account has vanished. It is also exactly the shape user_scope_memberships
-- already uses for the same reason (rls_user_self).
CREATE POLICY rls_users_select ON identity_access.users FOR SELECT TO app_user
    USING (
        kernel.rls_bypass()
        OR id = kernel.current_user_id()
        OR identity_access.user_in_brand(id, kernel.current_brand_id())
    );

-- See the header: the row must exist before the membership that would make this predicate true.
CREATE POLICY rls_users_insert ON identity_access.users FOR INSERT TO app_user
    WITH CHECK (true);

-- USING and WITH CHECK are the same predicate because brand membership is not a column of this
-- table: an UPDATE cannot move a row between brands, so there is no post-image to test separately.
CREATE POLICY rls_users_update ON identity_access.users FOR UPDATE TO app_user
    USING (
        kernel.rls_bypass()
        OR id = kernel.current_user_id()
        OR identity_access.user_in_brand(id, kernel.current_brand_id())
    )
    WITH CHECK (
        kernel.rls_bypass()
        OR id = kernel.current_user_id()
        OR identity_access.user_in_brand(id, kernel.current_brand_id())
    );

CREATE POLICY rls_users_delete ON identity_access.users FOR DELETE TO app_user
    USING (
        kernel.rls_bypass()
        OR id = kernel.current_user_id()
        OR identity_access.user_in_brand(id, kernel.current_brand_id())
    );

ALTER TABLE identity_access.users ENABLE ROW LEVEL SECURITY;

-- ── What this deliberately does NOT do ────────────────────────────────────────────────────────
--
-- user_scope_memberships keeps row security OFF. It carries an rls_user_self policy which, if
-- enabled, would restrict every session to its OWN membership rows — and the access-control
-- screens (GetAccessPeople, the role matrix, AssignPermission's anti-escalation guard) all read
-- OTHER people's memberships by design. Enabling it needs its own policy and its own audit; doing
-- it here, as a side effect of a users fix, is how the inert rls_admin_only above came to exist.
--
-- Login and step-up are unaffected: core.WebApi/Program.cs runs the scope-resolving auth paths
-- with bypass_rls precisely because they must read identity rows before a brand is known.

COMMENT ON POLICY rls_users_select ON identity_access.users IS
    'F-3. Tenant boundary for a table with no brand_id: resolved through live memberships. Second '
    'line of defence behind UserBrandScope.ScopedToCallerBrand, not a replacement for it.';
