-- =============================================================================
-- seed_notification_lifecycle_templates.sql  (Task #6 — Real Notification Delivery)
-- Seeds lifecycle notification templates in engagement_cms.notification_templates
-- for the LG-MAIN brand.
--
-- Templates cover:
--   ORDER_PICKUP_SCHEDULED, ORDER_PICKED_UP, ORDER_READY, ORDER_OUT_FOR_DELIVERY,
--   ORDER_DELIVERED, ORDER_CANCELLED, PAYMENT_CAPTURED, REFUND_INITIATED
-- Each base code × 3 channels = 24 templates.
-- Also seeds the pre-existing ORDER_PLACED_SMS + ORDER_READY_WHATSAPP if missing.
--
-- Bilingual-ready {{variable}} placeholders matching the Worker render engine:
--   {{customer_name}}, {{order_number}}, {{pickup_date}}, {{delivery_date}},
--   {{tracking_url}}, {{amount}}
--
-- Idempotent: uses INSERT ... ON CONFLICT DO NOTHING on (brand_id, code, channel, locale, version_number).
-- Run:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@18/bin/psql \
--     -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/seed_notification_lifecycle_templates.sql
-- =============================================================================

BEGIN;

DO $$
DECLARE
    v_brand_id uuid;
    v_now      timestamptz := now();
BEGIN
    SELECT id INTO v_brand_id FROM tenancy_org.brands WHERE code = 'LG-MAIN' LIMIT 1;
    IF v_brand_id IS NULL THEN
        RAISE NOTICE 'Brand LG-MAIN not found — skipping template seed.';
        RETURN;
    END IF;

    -- Helper: insert one template if the (brand_id, code, channel, locale, version_number) key is new.
    -- The unique index: notification_templates_brand_id_code_channel_locale_version_key
    --
    -- We use INSERT ... ON CONFLICT DO NOTHING so re-runs are safe.

    -- ── WhatsApp templates (utility/transactional) ──────────────────────────
    INSERT INTO engagement_cms.notification_templates
        (id, brand_id, code, name, channel, category, locale, body_template,
         variables, version_number, is_transactional, is_active, status, created_at, updated_at)
    VALUES
      (gen_random_uuid(), v_brand_id, 'ORDER_PLACED_WHATSAPP', 'Order Placed - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been placed. We''ll pick it up on {{pickup_date}}. Track: {{tracking_url}}',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_PICKUP_SCHEDULED_WHATSAPP', 'Order Pickup Scheduled - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} pickup is scheduled for {{pickup_date}}. Our rider will be at your door soon!',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_PICKED_UP_WHATSAPP', 'Order Picked Up - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, we''ve picked up your order #{{order_number}}. Your clothes are on their way to our laundry facility. We''ll keep you updated!',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_READY_WHATSAPP', 'Order Ready for Delivery - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, your order #{{order_number}} is ready! We''ll deliver it on {{delivery_date}}. Thank you for choosing Laundry Ghar.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_OUT_FOR_DELIVERY_WHATSAPP', 'Order Out for Delivery - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, your order #{{order_number}} is out for delivery! Our rider is on the way. Estimated delivery: {{delivery_date}}.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_DELIVERED_WHATSAPP', 'Order Delivered - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been delivered! Thank you for choosing us. Rate your experience: {{tracking_url}}',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_CANCELLED_WHATSAPP', 'Order Cancelled - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been cancelled. If you have questions please contact our support. We''re sorry for the inconvenience.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'PAYMENT_CAPTURED_WHATSAPP', 'Payment Received - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, we''ve received your payment of ₹{{amount}} for order #{{order_number}}. Thank you!',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'REFUND_INITIATED_WHATSAPP', 'Refund Initiated - WhatsApp',
       'whatsapp', 'transactional', 'en',
       'Hi {{customer_name}}, a refund of ₹{{amount}} for order #{{order_number}} has been initiated. It will reflect in your account within 5-7 business days.',
       '[]', 1, true, true, 'active', v_now, v_now)

    ON CONFLICT ON CONSTRAINT notification_templates_brand_id_code_channel_locale_version_key DO NOTHING;

    -- ── SMS templates ─────────────────────────────────────────────────────────
    INSERT INTO engagement_cms.notification_templates
        (id, brand_id, code, name, channel, category, locale, body_template,
         variables, version_number, is_transactional, is_active, status, created_at, updated_at)
    VALUES
      (gen_random_uuid(), v_brand_id, 'ORDER_PLACED_SMS', 'Order Placed - SMS',
       'sms', 'transactional', 'en',
       'Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been placed. We''ll pick it up on {{pickup_date}}. Track: {{tracking_url}}',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_PICKUP_SCHEDULED_SMS', 'Order Pickup Scheduled - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Order #{{order_number}} pickup scheduled for {{pickup_date}}. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_PICKED_UP_SMS', 'Order Picked Up - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Order #{{order_number}} picked up. We''ll notify you when ready. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_READY_SMS', 'Order Ready - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Your order #{{order_number}} is ready for delivery on {{delivery_date}}. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_OUT_FOR_DELIVERY_SMS', 'Order Out for Delivery - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Order #{{order_number}} is out for delivery. ETA: {{delivery_date}}. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_DELIVERED_SMS', 'Order Delivered - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Order #{{order_number}} delivered! Thank you. Rate us: {{tracking_url}} -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_CANCELLED_SMS', 'Order Cancelled - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Order #{{order_number}} has been cancelled. Contact support for assistance. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'PAYMENT_CAPTURED_SMS', 'Payment Received - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Payment of Rs.{{amount}} received for order #{{order_number}}. Thank you! -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'REFUND_INITIATED_SMS', 'Refund Initiated - SMS',
       'sms', 'transactional', 'en',
       'Laundry Ghar: Refund of Rs.{{amount}} for order #{{order_number}} initiated. Allow 5-7 days. -LaunGhar',
       '[]', 1, true, true, 'active', v_now, v_now)

    ON CONFLICT ON CONSTRAINT notification_templates_brand_id_code_channel_locale_version_key DO NOTHING;

    -- ── Push templates ────────────────────────────────────────────────────────
    INSERT INTO engagement_cms.notification_templates
        (id, brand_id, code, name, channel, category, locale, body_template,
         variables, version_number, is_transactional, is_active, status, created_at, updated_at)
    VALUES
      (gen_random_uuid(), v_brand_id, 'ORDER_PLACED_PUSH', 'Order Placed - Push',
       'push', 'transactional', 'en',
       'Your order #{{order_number}} is placed. Pickup scheduled for {{pickup_date}}.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_PICKUP_SCHEDULED_PUSH', 'Order Pickup Scheduled - Push',
       'push', 'transactional', 'en',
       'Your order #{{order_number}} pickup is scheduled for {{pickup_date}}.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_PICKED_UP_PUSH', 'Order Picked Up - Push',
       'push', 'transactional', 'en',
       'We''ve picked up order #{{order_number}}. It''s in our facility now!',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_READY_PUSH', 'Order Ready - Push',
       'push', 'transactional', 'en',
       'Order #{{order_number}} is clean and ready for delivery on {{delivery_date}}.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_OUT_FOR_DELIVERY_PUSH', 'Order Out for Delivery - Push',
       'push', 'transactional', 'en',
       'Order #{{order_number}} is on its way! ETA: {{delivery_date}}.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_DELIVERED_PUSH', 'Order Delivered - Push',
       'push', 'transactional', 'en',
       'Order #{{order_number}} delivered! Fresh and clean. Thank you for choosing Laundry Ghar.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'ORDER_CANCELLED_PUSH', 'Order Cancelled - Push',
       'push', 'transactional', 'en',
       'Your order #{{order_number}} has been cancelled.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'PAYMENT_CAPTURED_PUSH', 'Payment Received - Push',
       'push', 'transactional', 'en',
       'Payment of ₹{{amount}} received for order #{{order_number}}.',
       '[]', 1, true, true, 'active', v_now, v_now),

      (gen_random_uuid(), v_brand_id, 'REFUND_INITIATED_PUSH', 'Refund Initiated - Push',
       'push', 'transactional', 'en',
       'Refund of ₹{{amount}} initiated for order #{{order_number}}. Allow 5-7 business days.',
       '[]', 1, true, true, 'active', v_now, v_now)

    ON CONFLICT ON CONSTRAINT notification_templates_brand_id_code_channel_locale_version_key DO NOTHING;

    RAISE NOTICE 'Notification lifecycle templates seeded for brand %', v_brand_id;
END $$;

COMMIT;

-- Verification
SELECT channel, count(*) AS template_count
FROM engagement_cms.notification_templates t
JOIN tenancy_org.brands b ON b.id = t.brand_id
WHERE b.code = 'LG-MAIN'
GROUP BY channel
ORDER BY channel;
