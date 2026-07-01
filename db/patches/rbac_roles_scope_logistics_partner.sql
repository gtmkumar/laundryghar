-- =============================================================================
-- rbac_roles_scope_logistics_partner.sql  (RaaS FULL-12 — relax roles.scope_type CHECK)
--   database_scripts/02_bc2_identity_access.sql seeds roles.scope_type with a CHECK
--   whose live set (after rbac_roles_scope_territory.sql) is
--   platform/brand/territory/franchise/store/warehouse. The RaaS partner roles
--   (partner_admin/partner_operator) are scoped to 'logistics_partner', which the
--   CHECK omits — blocking them from being seeded. Relax the CHECK to include
--   'logistics_partner' so docs/rbac.md §4/§10 are representable end-to-end.
--
--   This clones rbac_roles_scope_territory.sql byte-for-byte, adding one value; the
--   resulting set is a superset, so it is safe whether or not the territory patch ran.
--
--   NOTE — user_scope_memberships.scope_type CHECK is deliberately NOT touched.
--   Partners do NOT get user_scope_memberships rows: a membership row FKs to
--   identity_access.users, and partners are external (isolated by partner_id RLS +
--   a partner_role JWT claim), so seeding memberships would require fake staff users.
--   Only the ROLES catalog needs 'logistics_partner'.
--
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/rbac_roles_scope_logistics_partner.sql
-- =============================================================================

BEGIN;

DO $$
BEGIN
    -- Drop the existing CHECK (PostgreSQL auto-names inline column checks
    -- <table>_<column>_check) if present, then re-add including 'logistics_partner'.
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'roles_scope_type_check'
          AND conrelid = 'identity_access.roles'::regclass
    ) THEN
        ALTER TABLE identity_access.roles DROP CONSTRAINT roles_scope_type_check;
    END IF;

    ALTER TABLE identity_access.roles
        ADD CONSTRAINT roles_scope_type_check
        CHECK (scope_type IN ('platform','brand','territory','franchise','store','warehouse','logistics_partner'));
END $$;

COMMIT;

-- Verification: the constraint definition should now list 'logistics_partner'.
SELECT conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conname = 'roles_scope_type_check'
  AND conrelid = 'identity_access.roles'::regclass;
