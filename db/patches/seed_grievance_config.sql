-- ─── Grievance Officer app-config seed — idempotent ──────────────────────────
-- Task #14 Scope 4: seeds a mobile_app_config row with grievance officer contact
-- details so the customer app can render a "Contact Grievance Officer" screen
-- as required by the DPDP Act (Clause 13).
--
-- Config key: grievance_officer
-- App type:   customer  (no CHECK constraint on app_type — validated by app)
-- Platform:   android   (one row; iOS row added below — UNIQUE key is per-platform)
--
-- The config_value JSON structure:
--   {
--     "name":    "<Grievance Officer full name>",
--     "email":   "<grievance@example.com>",
--     "phone":   "<+91XXXXXXXXXX>",
--     "address": "<postal address>",
--     "hours":   "Mon-Fri 10:00-18:00 IST"
--   }
--
-- IMPORTANT: Replace the placeholder values with real officer details before
-- going to production.  See PRODUCTION_ENV.md for the required env / config keys.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

DO $$
DECLARE
    v_brand_id  uuid;
    v_now       timestamptz := now();
    v_value     jsonb       := '{
        "name":    "Grievance Officer",
        "email":   "grievance@laundryghar.in",
        "phone":   "+910000000000",
        "address": "Laundry Ghar Pvt Ltd, [Full Address], India",
        "hours":   "Mon-Fri 10:00-18:00 IST"
    }'::jsonb;
BEGIN
    -- Resolve the canonical brand.  If more than one brand exists, insert for each.
    FOR v_brand_id IN SELECT id FROM tenancy_org.brands LOOP

        -- Android row
        INSERT INTO engagement_cms.mobile_app_config
            (id, brand_id, app_type, platform, config_key, config_value,
             description, is_force_update, rollout_percent, is_active, status,
             created_at, updated_at)
        VALUES
            (gen_random_uuid(), v_brand_id, 'customer', 'android', 'grievance_officer', v_value,
             'DPDP Act Clause 13 — Grievance Officer contact details displayed in app settings.',
             false, 100, true, 'active',
             v_now, v_now)
        ON CONFLICT (brand_id, app_type, platform, config_key)
        DO UPDATE SET
            config_value = EXCLUDED.config_value,
            description  = EXCLUDED.description,
            updated_at   = v_now;

        -- iOS row
        INSERT INTO engagement_cms.mobile_app_config
            (id, brand_id, app_type, platform, config_key, config_value,
             description, is_force_update, rollout_percent, is_active, status,
             created_at, updated_at)
        VALUES
            (gen_random_uuid(), v_brand_id, 'customer', 'ios', 'grievance_officer', v_value,
             'DPDP Act Clause 13 — Grievance Officer contact details displayed in app settings.',
             false, 100, true, 'active',
             v_now, v_now)
        ON CONFLICT (brand_id, app_type, platform, config_key)
        DO UPDATE SET
            config_value = EXCLUDED.config_value,
            description  = EXCLUDED.description,
            updated_at   = v_now;

        RAISE NOTICE 'Upserted grievance_officer config for brand %', v_brand_id;
    END LOOP;
END;
$$;

COMMIT;
