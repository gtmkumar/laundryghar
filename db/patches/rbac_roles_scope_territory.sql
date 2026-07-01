-- =============================================================================
-- rbac_roles_scope_territory.sql  (RBAC #12 — relax roles.scope_type CHECK)
--   database_scripts/02_bc2_identity_access.sql seeds roles.scope_type with a CHECK
--   that omits 'territory', even though user_scope_memberships.scope_type already
--   allows it. This blocks any territory-scoped system/custom role. Relax the CHECK
--   to include 'territory' so the scope hierarchy (§3) is representable end-to-end.
--
--   NOTE: this does NOT re-scope regional_manager — it stays 'brand' for now. This
--   patch only widens the allowed set; no existing row is touched.
--
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/rbac_roles_scope_territory.sql
-- =============================================================================

BEGIN;

DO $$
BEGIN
    -- Drop the existing CHECK (PostgreSQL auto-names inline column checks
    -- <table>_<column>_check) if present, then re-add including 'territory'.
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'roles_scope_type_check'
          AND conrelid = 'identity_access.roles'::regclass
    ) THEN
        ALTER TABLE identity_access.roles DROP CONSTRAINT roles_scope_type_check;
    END IF;

    ALTER TABLE identity_access.roles
        ADD CONSTRAINT roles_scope_type_check
        CHECK (scope_type IN ('platform','brand','territory','franchise','store','warehouse'));
END $$;

COMMIT;

-- Verification: the constraint definition should now list 'territory'.
SELECT conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conname = 'roles_scope_type_check'
  AND conrelid = 'identity_access.roles'::regclass;
