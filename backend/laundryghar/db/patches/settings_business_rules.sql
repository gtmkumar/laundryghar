-- =============================================================================
-- settings_business_rules.sql  (#26 — scope-aware business-rule settings foundation)
--   Seeds PLATFORM-default rows (scope_type='platform', brand_id NULL) into
--   kernel.system_settings for ONLY the five values that were previously hardcoded
--   in OrdersSettings / InvoiceTaxCalculator, so migrating those consumers onto the
--   SettingsResolver preserves current behaviour byte-for-byte.
--
--   DELIBERATELY NOT SEEDED (product decision): min_order_value and every other new
--   key (cancellation_fee, high_value_garment_threshold, logistics rates, …) must
--   have NO default at any scope — they resolve to null until an operator sets them.
--
--   No new permissions are needed: the existing settings.read / settings.manage
--   permissions (seeded by IdentitySeeder) gate the /api/v1/admin/business-settings
--   endpoints. This patch therefore only seeds data.
--
--   setting_value is jsonb, so scalars are stored as JSON literals: numbers bare
--   (18), strings quoted ("INR"). data_type mirrors the codec's scalar types.
--
--   Idempotent: each row is guarded by NOT EXISTS on its natural key. The unique
--   index (scope_type, brand_id, franchise_id, store_id, category, setting_key)
--   treats NULL brand_id as DISTINCT, so ON CONFLICT is unreliable for platform
--   rows — NOT EXISTS is the correct guard (mirrors the roles seed pattern).
--
--   Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/settings_business_rules.sql
-- =============================================================================

BEGIN;

INSERT INTO kernel.system_settings
    (id, brand_id, franchise_id, store_id, scope_type, category, setting_key,
     setting_value, data_type, is_encrypted, is_readonly, requires_restart,
     description, version, status, created_at, updated_at)
SELECT gen_random_uuid(), NULL, NULL, NULL, 'platform', 'orders', v.key,
       v.val::jsonb, v.dtype, false, false, false,
       v.descr, 1, 'active', now(), now()
FROM (VALUES
    ('tax_rate_percent',          '18',    'decimal', 'Default GST rate % applied to taxable order value'),
    ('express_surcharge_percent', '50',    'decimal', 'Express order surcharge as % of subtotal'),
    ('default_tat_hours',         '48',    'int',     'Fallback turnaround (hours) for standard orders'),
    ('express_tat_hours',         '24',    'int',     'Fallback turnaround (hours) for express orders'),
    ('currency_code',             '"INR"', 'string',  'Default ISO currency code for new orders')
) AS v(key, val, dtype, descr)
WHERE NOT EXISTS (
    SELECT 1 FROM kernel.system_settings s
    WHERE s.scope_type = 'platform'
      AND s.brand_id IS NULL
      AND s.franchise_id IS NULL
      AND s.store_id IS NULL
      AND s.category = 'orders'
      AND s.setting_key = v.key
);

COMMIT;

-- Verification.
SELECT setting_key, setting_value, data_type
FROM kernel.system_settings
WHERE scope_type = 'platform' AND brand_id IS NULL AND category = 'orders'
ORDER BY setting_key;
