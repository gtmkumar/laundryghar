-- ─────────────────────────────────────────────────────────────────────────────
-- Default platform settings (kernel.system_settings), brand-scoped.
--
-- Commit-safe: this seeds DISABLED / EMPTY defaults only. Real SMTP credentials
-- are entered at runtime through the Settings UI (PUT /admin/settings/email),
-- which writes them back into this same table — they are never committed here.
--
-- Categories seeded:
--   email/smtp        → outbound mail transport (disabled, empty until configured)
--   provisioning/invite → how invited users activate (admin_activate | self_service)
--   app/urls          → base URL used to build invite / reset links
--
-- Idempotent: ON CONFLICT on the natural key leaves any operator-entered values
-- untouched (we never overwrite an existing row's setting_value).
-- Run as a superuser (postgres) so RLS does not block the seed.
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_brand uuid;
BEGIN
    SELECT id INTO v_brand FROM tenancy_org.brands ORDER BY created_at LIMIT 1;
    IF v_brand IS NULL THEN
        RAISE NOTICE 'No brand found — skipping settings seed.';
        RETURN;
    END IF;

    INSERT INTO kernel.system_settings
        (scope_type, brand_id, category, setting_key, setting_value, data_type, description, is_encrypted, status, created_at, updated_at)
    VALUES
        ('brand', v_brand, 'email', 'smtp',
         '{"enabled":false,"host":"","port":465,"secure":true,"username":"","password":"","fromEmail":"","fromName":"Laundry Ghar"}'::jsonb,
         'object', 'Outbound SMTP transport for transactional email.', true, 'active', now(), now()),

        ('brand', v_brand, 'provisioning', 'invite',
         '{"mode":"admin_activate"}'::jsonb,
         'object', 'How invited users are activated: admin_activate or self_service.', false, 'active', now(), now()),

        ('brand', v_brand, 'app', 'urls',
         '{"adminBaseUrl":"http://localhost:5173"}'::jsonb,
         'object', 'Base URLs used when building links inside outbound email.', false, 'active', now(), now())
    ON CONFLICT (scope_type, brand_id, franchise_id, store_id, category, setting_key) DO NOTHING;

    RAISE NOTICE 'Settings defaults ensured for brand %.', v_brand;
END $$;
