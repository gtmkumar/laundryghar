-- =============================================================================
-- db/patches/phase4_role_vertical_key.sql
--
-- Multi-vertical · make SYSTEM ROLES vertical-aware (the role analogue of the 2B/2C
-- module/bundle vertical gating and the 2D user_type neutralization).
--
--   * Adds identity_access.roles.vertical_key (nullable = vertical-neutral / every brand).
--   * Tags the laundry-specific on-site roles (warehouse_supervisor / warehouse_staff)
--     vertical_key='laundry' so salon/logistics brands no longer see "Warehouse …" roles
--     (GetAccessRoles / GetRoles now filter by VerticalKey.IsAvailableTo, like the navigator).
--   * Seeds the salon (salon_manager / salon_staff) and logistics (hub_supervisor /
--     hub_operator) on-site roles, each tagged to its vertical, sharing the laundry on-site
--     permission set (operational codes; per-vertical entitlement gating hides unlicensed modules).
--
-- Mirrors core.Infrastructure/Seeders/IdentitySeeder (RoleDefs + on-site perm sets) so a
-- fresh seed and this patch converge on the same rows. Non-destructive + idempotent.
-- RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_role_vertical_key.sql
-- =============================================================================

BEGIN;

-- 1. vertical_key column on the role registry (null = neutral) ----------------
ALTER TABLE identity_access.roles
    ADD COLUMN IF NOT EXISTS vertical_key VARCHAR(20);

DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'roles_vertical_key_check') THEN
        ALTER TABLE identity_access.roles
            ADD CONSTRAINT roles_vertical_key_check
            CHECK (vertical_key IS NULL OR vertical_key IN ('laundry','salon','logistics'));
    END IF;
END
$chk$;

-- 2. Tag the existing laundry on-site system roles ---------------------------
UPDATE identity_access.roles
   SET vertical_key = 'laundry', updated_at = now()
 WHERE brand_id IS NULL
   AND code IN ('warehouse_supervisor','warehouse_staff')
   AND vertical_key IS DISTINCT FROM 'laundry';

-- 3. Seed the salon + logistics on-site system roles -------------------------
INSERT INTO identity_access.roles
    (id, brand_id, code, name, scope_type, vertical_key, is_system, is_assignable, priority, status, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.name, 'warehouse', v.vertical_key, true, true, v.priority, 'active', now(), now()
FROM (VALUES
    ('salon_manager',  'Salon Manager',  'salon',     70::smallint),
    ('salon_staff',    'Stylist',        'salon',     80::smallint),
    ('hub_supervisor', 'Hub Supervisor', 'logistics', 70::smallint),
    ('hub_operator',   'Hub Operator',   'logistics', 80::smallint)
) AS v(code, name, vertical_key, priority)
WHERE NOT EXISTS (
    SELECT 1 FROM identity_access.roles r WHERE r.code = v.code AND r.brand_id IS NULL
);

-- 4a. Supervisor-tier permissions (salon_manager / hub_supervisor) -----------
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'warehouses.list','orders.list','orders.update','orders.read',
    'fulfillment.read','fulfillment.tag','fulfillment.inspect',
    'warehouse.batch.manage','warehouse.process.scan','qc.perform','stockrecon.manage',
    'rider.read','rider.assignment.manage','rider.capacity.manage'])
WHERE r.brand_id IS NULL AND r.code IN ('salon_manager','hub_supervisor')
  AND NOT EXISTS (
      SELECT 1 FROM identity_access.role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id);

-- 4b. Staff-tier permissions (salon_staff / hub_operator) --------------------
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = ANY (ARRAY[
    'orders.list','orders.read','fulfillment.read','fulfillment.tag','fulfillment.inspect',
    'warehouse.process.scan'])
WHERE r.brand_id IS NULL AND r.code IN ('salon_staff','hub_operator')
  AND NOT EXISTS (
      SELECT 1 FROM identity_access.role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id);

-- 5. Verification gate -------------------------------------------------------
DO $verify$
DECLARE
    has_col       int;
    laundry_wh    int;
    salon_mgr     text;
    salon_mgr_pc  int;
BEGIN
    SELECT count(*) INTO has_col FROM information_schema.columns
     WHERE table_schema='identity_access' AND table_name='roles' AND column_name='vertical_key';
    IF has_col <> 1 THEN RAISE EXCEPTION 'role_vertical_key: roles.vertical_key column missing'; END IF;

    SELECT count(*) INTO laundry_wh FROM identity_access.roles
     WHERE brand_id IS NULL AND code IN ('warehouse_supervisor','warehouse_staff') AND vertical_key = 'laundry';
    IF laundry_wh <> 2 THEN RAISE EXCEPTION 'role_vertical_key: warehouse_* not both tagged laundry (got %)', laundry_wh; END IF;

    SELECT vertical_key INTO salon_mgr FROM identity_access.roles WHERE code='salon_manager' AND brand_id IS NULL;
    IF salon_mgr IS DISTINCT FROM 'salon' THEN
        RAISE EXCEPTION 'role_vertical_key: salon_manager missing/untagged (got %)', salon_mgr; END IF;

    SELECT count(*) INTO salon_mgr_pc
      FROM identity_access.role_permissions rp
      JOIN identity_access.roles r ON r.id = rp.role_id
     WHERE r.code='salon_manager' AND r.brand_id IS NULL;
    IF salon_mgr_pc < 14 THEN
        RAISE EXCEPTION 'role_vertical_key: salon_manager has too few permissions (got %)', salon_mgr_pc; END IF;

    RAISE NOTICE 'role_vertical_key verification passed: roles.vertical_key added; laundry tagged; salon/logistics on-site roles seeded with permissions.';
END
$verify$;

COMMIT;

SELECT 'phase4_role_vertical_key.sql applied successfully.' AS result;
