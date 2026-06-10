-- =============================================================================
-- seed_pickup_rejected_notification_templates.sql  (Task #32 — Pickup Reject Flow)
-- Seeds PICKUP_REJECTED notification templates in engagement_cms.notification_templates
-- for the LG-MAIN brand.
--
-- Three channels × 1 base code = 3 templates:
--   PICKUP_REJECTED_WHATSAPP, PICKUP_REJECTED_SMS, PICKUP_REJECTED_PUSH
--
-- Variables used in body templates (matching Worker render engine):
--   {{customer_name}}, {{request_number}}, {{rejection_reason}}
--
-- Idempotent: INSERT ... ON CONFLICT DO NOTHING on
--   (brand_id, code, channel, locale, version_number).
--
-- Run:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@18/bin/psql \
--     -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/seed_pickup_rejected_notification_templates.sql
-- =============================================================================

BEGIN;

DO $$
DECLARE
    v_brand_id uuid;
    v_now      timestamptz := now();
BEGIN
    SELECT id INTO v_brand_id FROM tenancy_org.brands WHERE code = 'LG-MAIN' LIMIT 1;
    IF v_brand_id IS NULL THEN
        RAISE NOTICE 'Brand LG-MAIN not found — skipping PICKUP_REJECTED template seed.';
        RETURN;
    END IF;

    INSERT INTO engagement_cms.notification_templates
        (id, brand_id, code, name, channel, category, locale, body_template,
         variables, version_number, is_transactional, is_active, status, created_at, updated_at)
    VALUES

      -- WhatsApp
      (gen_random_uuid(), v_brand_id, 'PICKUP_REJECTED_WHATSAPP', 'Pickup Rejected - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, unfortunately we could not fulfill your Laundry Ghar pickup request #{{request_number}}. Reason: {{rejection_reason}}. Please book a new slot — we''re sorry for the inconvenience.',
       '[]', 1, true, true, 'active', v_now, v_now),

      -- SMS
      (gen_random_uuid(), v_brand_id, 'PICKUP_REJECTED_SMS', 'Pickup Rejected - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Pickup #{{request_number}} could not be completed. Reason: {{rejection_reason}}. Please rebook. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      -- Push
      (gen_random_uuid(), v_brand_id, 'PICKUP_REJECTED_PUSH', 'Pickup Rejected - Push',
       'push', 'transactional', 'en',
       'Pickup #{{request_number}} was not fulfilled. {{rejection_reason}} — please book a new slot.',
       '[]', 1, true, true, 'active', v_now, v_now)

    ON CONFLICT ON CONSTRAINT notification_templates_brand_id_code_channel_locale_version_key DO NOTHING;

    RAISE NOTICE 'PICKUP_REJECTED templates seeded for brand %', v_brand_id;
END $$;

COMMIT;

-- Verification
SELECT t.code, t.channel, t.status
FROM engagement_cms.notification_templates t
JOIN tenancy_org.brands b ON b.id = t.brand_id
WHERE b.code = 'LG-MAIN'
  AND t.code LIKE 'PICKUP_REJECTED%'
ORDER BY t.channel;
