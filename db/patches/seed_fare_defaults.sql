-- ─────────────────────────────────────────────────────────────────────────────
-- seed_fare_defaults.sql  (Laundry + Logistics marketplace — Wave 1.3)
-- Seeds default distance + time + surge delivery fare config into
-- kernel.system_settings (category 'fare', key 'quote'), brand-scoped. The fare engine
-- (FareSettings.Compute) and the Settings UI read/write this same row.
--
-- Shape (camelCase to match the web JSON serializer):
--   minFare, roundToNearest, quoteTtlSeconds,
--   tierRates: { <tier>: { baseFare, perKm, pickupFlat } },
--   surge:     [ { days:[0..6], startHour, endHour, multiplier } ]  (empty by default)
--
-- Idempotent: ON CONFLICT leaves any operator-entered values untouched.
-- Run as superuser (postgres) so RLS does not block the seed.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/seed_fare_defaults.sql
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_brand uuid;
BEGIN
    SELECT id INTO v_brand FROM tenancy_org.brands ORDER BY created_at LIMIT 1;
    IF v_brand IS NULL THEN
        RAISE NOTICE 'No brand found — skipping fare defaults seed.';
        RETURN;
    END IF;

    INSERT INTO kernel.system_settings
        (scope_type, brand_id, category, setting_key, setting_value, data_type, description, is_encrypted, status, created_at, updated_at)
    VALUES
        ('brand', v_brand, 'fare', 'quote',
         '{
            "minFare": 30,
            "roundToNearest": 5,
            "quoteTtlSeconds": 600,
            "tierRates": {
              "foot":          { "baseFare": 20, "perKm": 6,  "pickupFlat": 10 },
              "cycle":         { "baseFare": 20, "perKm": 6,  "pickupFlat": 10 },
              "two_wheeler":   { "baseFare": 25, "perKm": 8,  "pickupFlat": 15 },
              "three_wheeler": { "baseFare": 35, "perKm": 12, "pickupFlat": 20 },
              "four_wheeler":  { "baseFare": 60, "perKm": 18, "pickupFlat": 30 }
            },
            "surge": []
          }'::jsonb,
         'object', 'Distance + time + surge delivery fare rates per vehicle tier.', false, 'active', now(), now())
    ON CONFLICT (scope_type, brand_id, franchise_id, store_id, category, setting_key) DO NOTHING;

    RAISE NOTICE 'Fare defaults ensured for brand %.', v_brand;
END $$;
