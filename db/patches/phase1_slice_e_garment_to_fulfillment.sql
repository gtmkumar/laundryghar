-- =============================================================================
-- db/patches/phase1_slice_e_garment_to_fulfillment.sql
--
-- Multi-vertical Phase 1 · Slice E — rename garment.* → fulfillment.* (blueprint
-- Risk #6). Renames the RBAC permission codes, the warehouse module's permission-tag
-- list, and the outbox/event discriminators in one atomic migration.
--
-- LOCKOUT SAFETY: role grants (identity_access.role_permissions) FK to permission.id,
-- so renaming the permission CODE in place preserves every grant — no role loses access.
-- JWTs minted before the rename still carry garment.* in their baked 'permissions' claim;
-- laundryghar.Utilities.Auth.PermissionAlias bridges those at the authorization layer
-- (canonicalizes garment.* → fulfillment.*) until tokens cycle and the Phase-2 seeder
-- split lands, at which point both the alias map and this bridge can be removed.
--
-- DEPLOY ORDERING: apply with the code deploy. Existing outbox rows are migrated below;
-- the 'permissions' claim is bridged in-app, so order between DB/app is not lockout-sensitive.
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase1_slice_e_garment_to_fulfillment.sql
-- =============================================================================

BEGIN;

-- Snapshot grant count for the 3 perms (by id) so we can prove nothing was orphaned.
CREATE TEMP TABLE _slice_e_pre ON COMMIT DROP AS
SELECT rp.permission_id, count(*) AS n_grants
FROM   identity_access.role_permissions rp
JOIN   identity_access.permissions p ON p.id = rp.permission_id
WHERE  p.code LIKE 'garment.%'
GROUP  BY rp.permission_id;

-- 1. Permission catalog — rename code + module IN PLACE (id unchanged → grants follow). ----
UPDATE identity_access.permissions
SET    code        = replace(code, 'garment.', 'fulfillment.'),
       module      = 'fulfillment',
       name        = CASE code
                        WHEN 'garment.read'    THEN 'Read fulfilment records'
                        WHEN 'garment.tag'     THEN 'Tag/label fulfilment units'
                        WHEN 'garment.inspect' THEN 'Inspect fulfilment units'
                        ELSE name END,
       updated_at  = now()
WHERE  code LIKE 'garment.%';

-- 2. Navigator module tag list + required permission (warehouse module). ------------------
UPDATE identity_access.modules
SET    permission_modules  = array_replace(permission_modules, 'garment', 'fulfillment'),
       required_permission = 'fulfillment.read'
WHERE  key = 'warehouse';

-- 3. Outbox events — aggregate discriminator + event types. -------------------------------
UPDATE kernel.outbox_events
SET    aggregate_type = 'fulfillment',
       event_type     = replace(event_type, 'garment.', 'fulfillment.')
WHERE  aggregate_type = 'garment' OR event_type LIKE 'garment.%';

-- 4. Verification gate --------------------------------------------------------------------
DO $verify$
DECLARE
    leftover_perms   int;
    renamed_perms    int;
    grant_drift      int;
    leftover_tag     int;
    leftover_outbox  int;
BEGIN
    SELECT count(*) INTO leftover_perms FROM identity_access.permissions WHERE code LIKE 'garment.%';
    IF leftover_perms <> 0 THEN RAISE EXCEPTION 'Slice E: % permission(s) still garment.*', leftover_perms; END IF;

    SELECT count(*) INTO renamed_perms FROM identity_access.permissions
    WHERE code IN ('fulfillment.read','fulfillment.tag','fulfillment.inspect');
    IF renamed_perms <> 3 THEN RAISE EXCEPTION 'Slice E: expected 3 fulfillment.* perms, found %', renamed_perms; END IF;

    -- grants preserved: same permission_id keeps the same grant count after the code rename.
    SELECT count(*) INTO grant_drift
    FROM _slice_e_pre pre
    JOIN (SELECT permission_id, count(*) n FROM identity_access.role_permissions GROUP BY permission_id) post
      ON post.permission_id = pre.permission_id
    WHERE post.n <> pre.n_grants;
    IF grant_drift <> 0 THEN RAISE EXCEPTION 'Slice E: grant count drifted on % permission(s)', grant_drift; END IF;

    SELECT count(*) INTO leftover_tag FROM identity_access.modules
    WHERE key='warehouse' AND 'garment' = ANY(permission_modules);
    IF leftover_tag <> 0 THEN RAISE EXCEPTION 'Slice E: warehouse module still tags garment'; END IF;

    SELECT count(*) INTO leftover_outbox FROM kernel.outbox_events
    WHERE aggregate_type='garment' OR event_type LIKE 'garment.%';
    IF leftover_outbox <> 0 THEN RAISE EXCEPTION 'Slice E: % outbox row(s) still garment', leftover_outbox; END IF;

    RAISE NOTICE 'Slice E verification passed: perms+module+outbox renamed garment→fulfillment; all grants preserved.';
END
$verify$;

COMMIT;

SELECT 'phase1_slice_e_garment_to_fulfillment.sql applied successfully.' AS result;
