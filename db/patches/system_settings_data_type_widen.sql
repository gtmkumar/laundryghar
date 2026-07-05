-- =============================================================================
-- system_settings_data_type_widen.sql  (GH #26 fix — data_type vocabulary mismatch)
--   kernel.system_settings.data_type was constrained to the JSON-Schema-ish
--   vocabulary {string,number,boolean,object,array} in the base kernel DDL, but
--   the settings-foundation code (SettingValueCodec.ScalarDataTypes, the
--   business-settings admin API, and the already-shipped admin-web Business
--   Rules panel) uses {decimal,int,bool,string} for scalar values. Every write
--   through PUT /api/v1/admin/business-settings for a non-string key fails this
--   CHECK on a real Postgres database (EF InMemory tests don't enforce it, which
--   is how this shipped unnoticed).
--
--   Fix: widen the CHECK to admit both vocabularies rather than rewrite the
--   already-merged application + frontend. 'string' is shared by both sets.
--
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/system_settings_data_type_widen.sql
-- =============================================================================

BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'system_settings_data_type_check'
          AND conrelid = 'kernel.system_settings'::regclass
    ) THEN
        ALTER TABLE kernel.system_settings
            DROP CONSTRAINT system_settings_data_type_check;
    END IF;
    ALTER TABLE kernel.system_settings
        ADD CONSTRAINT system_settings_data_type_check
        CHECK (data_type IN ('string','number','boolean','object','array','decimal','int','bool'));
END $$;

COMMIT;

-- Verification.
SELECT 'system_settings data_type admits decimal/int/bool' AS check,
       pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conname = 'system_settings_data_type_check'
  AND conrelid = 'kernel.system_settings'::regclass;
