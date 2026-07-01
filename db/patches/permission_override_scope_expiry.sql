-- =============================================================================
-- permission_override_scope_expiry.sql  (RBAC Phase 1 / issue #10 — deny-wins completeness)
--
-- Brings identity_access.user_permission_override up to the docs/rbac.md §7 spec by adding the
-- four missing columns so an override can be SCOPED and TIME-BOXED (not just brand-wide+forever):
--     scope_type, scope_id   — confine the override to one node's subtree (null = global)
--     reason                 — audit trail for why the capability was granted/suspended
--     expires_at             — "suspend a capability until X"; expired rows are ignored at resolution
--
-- Also migrates the primary key from the natural (user_id, permission_id) — which allowed only ONE
-- override per user+permission, forcing every override brand-wide — to a surrogate id, with a
-- unique index on the full natural key (user, permission, scope) so a user can hold one global
-- plus several scoped overrides for the same permission.
--
-- Additive + idempotent (safe to re-run). Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/permission_override_scope_expiry.sql
-- Depends on: db/patches/permission_overrides.sql (creates the base table).
-- =============================================================================

BEGIN;

ALTER TABLE identity_access.user_permission_override
    ADD COLUMN IF NOT EXISTS id         uuid,
    ADD COLUMN IF NOT EXISTS scope_type varchar(20)
        CHECK (scope_type IN ('platform','brand','territory','franchise','store','warehouse')),
    ADD COLUMN IF NOT EXISTS scope_id   uuid,
    ADD COLUMN IF NOT EXISTS reason     text,
    ADD COLUMN IF NOT EXISTS expires_at timestamptz;

-- Backfill the surrogate id for any pre-existing rows, then pin it NOT NULL + default.
UPDATE identity_access.user_permission_override SET id = gen_random_uuid() WHERE id IS NULL;
ALTER TABLE identity_access.user_permission_override ALTER COLUMN id SET NOT NULL;
ALTER TABLE identity_access.user_permission_override ALTER COLUMN id SET DEFAULT gen_random_uuid();

-- Swap the primary key: drop the old (user_id, permission_id) PK, add PK (id).
DO $$
DECLARE pk_name text;
BEGIN
    SELECT conname INTO pk_name
      FROM pg_constraint
     WHERE conrelid = 'identity_access.user_permission_override'::regclass
       AND contype  = 'p';
    IF pk_name IS NOT NULL AND pk_name <> 'user_permission_override_pkey_id' THEN
        EXECUTE format('ALTER TABLE identity_access.user_permission_override DROP CONSTRAINT %I', pk_name);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conrelid = 'identity_access.user_permission_override'::regclass
           AND contype  = 'p'
    ) THEN
        ALTER TABLE identity_access.user_permission_override
            ADD CONSTRAINT user_permission_override_pkey_id PRIMARY KEY (id);
    END IF;
END $$;

-- Natural-key uniqueness across the nullable scope columns: one global (null scope) plus N scoped
-- overrides per (user, permission). COALESCE maps NULLs to sentinels so they participate in the index.
CREATE UNIQUE INDEX IF NOT EXISTS ux_upo_natural
    ON identity_access.user_permission_override (
        user_id,
        permission_id,
        COALESCE(scope_type, ''),
        COALESCE(scope_id, '00000000-0000-0000-0000-000000000000'::uuid)
    );

COMMIT;

-- Verification
SELECT column_name
  FROM information_schema.columns
 WHERE table_schema = 'identity_access'
   AND table_name   = 'user_permission_override'
   AND column_name IN ('id','scope_type','scope_id','reason','expires_at')
 ORDER BY column_name;
