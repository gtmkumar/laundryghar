-- =============================================================================
-- db/patches/phase2_slice_d_user_type_neutral.sql
--
-- Multi-vertical Phase 2 · Slice 2D — neutralize the user_type vocabulary
-- (blueprint §5 Identity "Neutralize UserType"). Adds the vertical-neutral `ops_staff`
-- operational-staff type alongside the laundry-specific `warehouse_staff` (which is RETAINED
-- for data compatibility — seeded users/roles reference it). New verticals (salon stylist,
-- logistics hub operator) use `ops_staff` instead of a laundry-flavoured label.
--
-- Non-destructive (widens the CHECK only) + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_d_user_type_neutral.sql
-- =============================================================================

BEGIN;

-- Drop the existing inline CHECK (auto-named users_user_type_check) and re-add it widened.
DO $relax$
DECLARE c record;
BEGIN
    FOR c IN
        SELECT con.conname FROM pg_constraint con
        JOIN pg_class rel     ON rel.oid = con.conrelid
        JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
        WHERE nsp.nspname='identity_access' AND rel.relname='users' AND con.contype='c'
          AND pg_get_constraintdef(con.oid) ILIKE '%user_type%'
    LOOP
        EXECUTE format('ALTER TABLE identity_access.users DROP CONSTRAINT %I;', c.conname);
    END LOOP;

    ALTER TABLE identity_access.users
        ADD CONSTRAINT users_user_type_check
        CHECK (user_type IN ('platform_admin','brand_admin','franchise_owner','store_admin',
                             'staff','warehouse_staff','ops_staff','rider','auditor','support'));
END
$relax$;

-- Verification gate -----------------------------------------------------------
DO $verify$
DECLARE def text;
BEGIN
    SELECT pg_get_constraintdef(con.oid) INTO def FROM pg_constraint con
    JOIN pg_class rel     ON rel.oid = con.conrelid
    JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
    WHERE nsp.nspname='identity_access' AND rel.relname='users' AND con.conname='users_user_type_check';
    IF def IS NULL OR def NOT ILIKE '%ops_staff%' THEN
        RAISE EXCEPTION 'Slice 2D: user_type CHECK did not gain ops_staff';
    END IF;
    RAISE NOTICE 'Slice 2D verification passed: ops_staff added; warehouse_staff retained.';
END
$verify$;

COMMIT;

SELECT 'phase2_slice_d_user_type_neutral.sql applied successfully.' AS result;
