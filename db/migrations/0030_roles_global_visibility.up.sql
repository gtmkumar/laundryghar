-- 0030 — global system roles must be readable by every tenant session.
--
-- Found while fixing audit finding A-1 (guard asymmetry). The fix adds a rank guard to
-- SetRoleCells: an actor may not edit a role that outranks their own. Resolving "their own rank"
-- means joining the caller's live memberships to identity_access.roles — and that join returned
-- ZERO rows for every brand-scoped caller, so the guard refused everybody, including the brand
-- admin doing ordinary work.
--
-- ── The cause ────────────────────────────────────────────────────────────────────────────────
--
-- identity_access.roles was given the generic `rls_brand` policy from db/patches/rls_proposal.sql:
--
--     USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
--
-- That template is written for tables whose brand_id is NOT NULL, where it is exactly right. On
-- this table brand_id is NULLABLE and a NULL means "global, shipped with the platform" — the 17
-- system roles (platform_admin, brand_admin, store_staff, rider, auditor …). For those rows the
-- predicate evaluates `NULL = <uuid>` → NULL → not satisfied, so a brand session cannot see any
-- system role at all. Verified on the live database: a session scoped to the Laundry Ghar brand
-- saw 2 of the 19 roles it should see, and `GET /admin/access-control/roles` returned only that
-- brand's four custom roles with the Franchise group empty.
--
-- ── Why this is the layer to fix it at ───────────────────────────────────────────────────────
--
-- The application layer already states the intended rule outright, in GetAccessRoles:
--
--     "System roles (BrandId == null) are global; custom roles are brand-scoped and must not
--      leak across tenants."
--     .Where(r => r.BrandId == null || brandId == null || r.BrandId == brandId)
--
-- so the handler and the policy disagreed, and the policy silently won. A global role carries no
-- tenant data — same 17 rows for every brand, seeded by the platform — so reading one discloses
-- nothing about another tenant. Two visible consequences of the disagreement, both closed here:
-- the rank guard above, and a brand admin being unable to assign a rider, store or franchise role
-- to their own staff because those roles never reached the picker.
--
-- ── What stays strict ────────────────────────────────────────────────────────────────────────
--
-- The write path is NOT relaxed. Reading a global role is harmless; writing one is not.
--
-- A single `FOR ALL` policy cannot express that, and this is the trap: `FOR ALL` uses one USING
-- clause for reads, updates AND deletes, so relaxing USING to admit NULL-brand rows would also
-- have made every global role DELETE-able by any brand session. WITH CHECK does not cover DELETE —
-- there is no new row to check — so the strict WITH CHECK below would not have caught it. Measured:
-- against a `FOR ALL` version of this policy, a brand session deleted the global brand_admin row.
--
-- So the policy is split per command, the same shape migration 0029 used on identity_access.users:
-- SELECT gets the relaxed predicate, INSERT / UPDATE / DELETE keep the original strict one. A
-- brand session can read the platform catalogue and cannot touch it, failing at the database
-- underneath RoleEditGuard's "only a platform administrator may modify a system role" — the
-- two-independent-defences shape finding F-3 asked for.
--
-- Brand-scoped custom roles are untouched: a brand still sees, and still writes, only its own.

SET client_min_messages = WARNING;

DROP POLICY IF EXISTS rls_brand ON identity_access.roles;

-- READ: own brand's custom roles, plus the global system catalogue.
DROP POLICY IF EXISTS rls_roles_select ON identity_access.roles;
CREATE POLICY rls_roles_select ON identity_access.roles FOR SELECT TO app_user
    USING (kernel.rls_bypass()
           OR brand_id = kernel.current_brand_id()
           OR brand_id IS NULL);

-- WRITE: unchanged from the original rls_brand template — a global role is platform-owned.
DROP POLICY IF EXISTS rls_roles_insert ON identity_access.roles;
CREATE POLICY rls_roles_insert ON identity_access.roles FOR INSERT TO app_user
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

DROP POLICY IF EXISTS rls_roles_update ON identity_access.roles;
CREATE POLICY rls_roles_update ON identity_access.roles FOR UPDATE TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

DROP POLICY IF EXISTS rls_roles_delete ON identity_access.roles;
CREATE POLICY rls_roles_delete ON identity_access.roles FOR DELETE TO app_user
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
