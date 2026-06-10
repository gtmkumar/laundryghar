-- =============================================================================
-- seed_garment_lost_template.sql  (Task #20 — Warehouse board actions + recon)
-- Seeds GARMENT_LOST notification templates in engagement_cms.notification_templates
-- for the LG-MAIN brand.
--
-- Templates: GARMENT_LOST × 3 channels (whatsapp, sms, push) × 1 locale (en) = 3 rows.
-- Variables used: {{customer_name}}, {{order_number}}
--
-- The garment.lost outbox event is emitted by LostGarmentProcessor (CloseStockReconHandler)
-- when a stock reconciliation is closed with unresolved missing items.
-- NotificationMappingService maps garment.lost → GARMENT_LOST_* templates.
--
-- Wallet compensation for confirmed-lost garments is OUT OF SCOPE this round
-- (needs compensation policy — credit amount, caps, approval workflow).
-- The garment.lost outbox event carries enough context for a future compensation handler.
--
-- Idempotent: INSERT ... ON CONFLICT DO NOTHING on the unique key
-- (brand_id, code, channel, locale, version_number).
--
-- Run:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@18/bin/psql \
--     -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/seed_garment_lost_template.sql
-- =============================================================================

BEGIN;

DO $$
DECLARE
    v_brand_id uuid;
    v_now      timestamptz := now();
BEGIN
    SELECT id INTO v_brand_id FROM tenancy_org.brands WHERE code = 'LG-MAIN' LIMIT 1;
    IF v_brand_id IS NULL THEN
        RAISE NOTICE 'Brand LG-MAIN not found — skipping GARMENT_LOST template seed.';
        RETURN;
    END IF;

    INSERT INTO engagement_cms.notification_templates
        (id, brand_id, code, name, channel, category, locale, body_template,
         variables, version_number, is_transactional, is_active, status, created_at, updated_at)
    VALUES

      -- WhatsApp
      (gen_random_uuid(), v_brand_id,
       'GARMENT_LOST_WHATSAPP', 'Garment Lost - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, we are sorry to inform you that a garment from your order #{{order_number}} could not be located during our warehouse reconciliation. Our team will contact you within 24 hours to resolve this. We sincerely apologise for this inconvenience.',
       '[]', 1, true, true, 'active', v_now, v_now),

      -- SMS
      (gen_random_uuid(), v_brand_id,
       'GARMENT_LOST_SMS', 'Garment Lost - SMS',
       'sms', 'transactional', 'en',
       'Hi {{customer_name}}, a garment from your Laundry Ghar order #{{order_number}} could not be located in our warehouse. Our team will contact you within 24 hrs. Apologies for the inconvenience.',
       '[]', 1, true, true, 'active', v_now, v_now),

      -- Push
      (gen_random_uuid(), v_brand_id,
       'GARMENT_LOST_PUSH', 'Garment Lost - Push',
       'push', 'transactional', 'en',
       'We could not locate a garment from order #{{order_number}}. Our team will reach out shortly.',
       '[]', 1, true, true, 'active', v_now, v_now)

    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'GARMENT_LOST templates seeded (or already existed) for brand %', v_brand_id;
END $$;

COMMIT;
