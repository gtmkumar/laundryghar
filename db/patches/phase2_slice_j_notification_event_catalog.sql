-- =============================================================================
-- db/patches/phase2_slice_j_notification_event_catalog.sql
--
-- Multi-vertical Phase 2 · Slice 2J — make the notification event→template mapping data-driven
-- (blueprint §7.2 Engagement "NotificationMappingService event catalog ... template-only body").
-- Replaces the hardcoded (event_type, status) switch in NotificationChannelPreferencePolicy with a
-- seeded engagement_cms.notification_event_catalog so a new vertical adds rows, not code.
--
--   * Each row maps (event_type, status) → template_code + channel suffix, tagged with the vertical
--     it belongs to (null = neutral). The laundry fulfillment.lost → GARMENT_LOST mapping (deferred
--     in Phase 1) is now cataloged + tagged vertical_key='laundry'.
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_j_notification_event_catalog.sql
-- =============================================================================

BEGIN;

CREATE SCHEMA IF NOT EXISTS engagement_cms;

CREATE TABLE IF NOT EXISTS engagement_cms.notification_event_catalog (
    event_type     varchar(64)  NOT NULL,
    status_match    varchar(40)  NOT NULL DEFAULT '*',   -- '*' = any status
    template_code  varchar(64)  NOT NULL,
    channel_suffix varchar(20)  NOT NULL DEFAULT '_SMS',
    vertical_key   varchar(20)  CHECK (vertical_key IS NULL OR vertical_key IN ('laundry','salon','logistics')),
    status         varchar(20)  NOT NULL DEFAULT 'active',
    created_at     timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (event_type, status_match)
);

-- Seed from the existing NotificationChannelPreferencePolicy switch. fulfillment.lost is the
-- laundry-specific lost-garment mapping (Phase-1 deferred GARMENT_LOST template); the rest are
-- vertical-neutral order/payment events.
INSERT INTO engagement_cms.notification_event_catalog
    (event_type, status_match, template_code, channel_suffix, vertical_key)
VALUES
    ('order.status_changed', 'pickup_scheduled', 'ORDER_PICKUP_SCHEDULED', '_SMS',      NULL),
    ('order.status_changed', 'picked_up',        'ORDER_PICKED_UP',        '_SMS',      NULL),
    ('order.status_changed', 'ready',            'ORDER_READY',            '_WHATSAPP', NULL),
    ('order.status_changed', 'out_for_delivery', 'ORDER_OUT_FOR_DELIVERY', '_SMS',      NULL),
    ('order.status_changed', 'delivered',        'ORDER_DELIVERED',        '_SMS',      NULL),
    ('order.cancelled',      '*',                'ORDER_CANCELLED',        '_SMS',      NULL),
    ('payment.captured',     '*',                'PAYMENT_CAPTURED',       '_SMS',      NULL),
    ('refund.initiated',     '*',                'REFUND_INITIATED',       '_SMS',      NULL),
    ('pickup.rejected',      '*',                'PICKUP_REJECTED',        '_SMS',      NULL),
    ('fulfillment.lost',     '*',                'GARMENT_LOST',           '_SMS',      'laundry')
ON CONFLICT (event_type, status_match) DO UPDATE
    SET template_code  = EXCLUDED.template_code,
        channel_suffix = EXCLUDED.channel_suffix,
        vertical_key   = EXCLUDED.vertical_key;

DO $g$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='app_user') THEN
        GRANT SELECT ON engagement_cms.notification_event_catalog TO app_user;
    END IF;
END $g$;

-- Verification gate -----------------------------------------------------------
DO $verify$
DECLARE n int; lost_vertical text;
BEGIN
    SELECT count(*) INTO n FROM engagement_cms.notification_event_catalog WHERE status='active';
    IF n < 10 THEN RAISE EXCEPTION 'Slice 2J: expected >= 10 catalog rows, found %', n; END IF;

    SELECT vertical_key INTO lost_vertical FROM engagement_cms.notification_event_catalog
    WHERE event_type='fulfillment.lost';
    IF lost_vertical IS DISTINCT FROM 'laundry' THEN
        RAISE EXCEPTION 'Slice 2J: fulfillment.lost not cataloged as laundry (got %)', lost_vertical;
    END IF;

    RAISE NOTICE 'Slice 2J verification passed: notification event catalog seeded (% rows); GARMENT_LOST cataloged.', n;
END
$verify$;

COMMIT;

SELECT 'phase2_slice_j_notification_event_catalog.sql applied successfully.' AS result;
