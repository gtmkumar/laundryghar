-- =============================================================================
-- notification_delivery.sql  (Task #6 — Real Notification Delivery)
-- Adds:
--   engagement_cms.push_tokens      — Expo push token registry (Task #7 adds the
--                                      registration endpoints; the sender reads it now)
--   engagement_cms.notification_event_cursors
--                                   — per-consumer dedup cursor so
--                                      NotificationMappingService does not fight
--                                      OutboxEventRelayService for the same rows
--
-- Idempotent. Additive only. Run:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@18/bin/psql \
--     -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/notification_delivery.sql
-- =============================================================================

BEGIN;

-- ── 1. push_tokens ────────────────────────────────────────────────────────────
-- Stores the Expo push token(s) for a customer/user device.
-- Task #7 will add the HTTP registration endpoint; the ExpoPushChannelSender
-- reads this table to resolve tokens for a recipient.
-- Unique on token itself — a token belongs to exactly one device.
CREATE TABLE IF NOT EXISTS engagement_cms.push_tokens (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id     uuid        NOT NULL,
    -- recipient identity (at least one must be non-null)
    user_type    varchar(20) NOT NULL     -- CHECK below: customer, rider, staff
        CHECK (user_type IN ('customer','rider','staff')),
    customer_id  uuid        NULL,        -- FK customer_catalog.customers.id (cross-BC, no FK)
    user_id      uuid        NULL,        -- FK identity_access.users.id (cross-BC, no FK)
    -- token fields
    platform     varchar(10) NOT NULL
        CHECK (platform IN ('ios','android','web')),
    token        text        NOT NULL,
    is_active    boolean     NOT NULL DEFAULT true,
    -- timestamps
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

-- Uniqueness: one row per token string
CREATE UNIQUE INDEX IF NOT EXISTS push_tokens_token_key
    ON engagement_cms.push_tokens (token);

-- Lookup: find all tokens for a customer/user
CREATE INDEX IF NOT EXISTS idx_push_tokens_customer
    ON engagement_cms.push_tokens (brand_id, customer_id)
    WHERE customer_id IS NOT NULL AND is_active = true;

CREATE INDEX IF NOT EXISTS idx_push_tokens_user
    ON engagement_cms.push_tokens (brand_id, user_id)
    WHERE user_id IS NOT NULL AND is_active = true;

-- RLS — brand-scoped, same pattern as rider_settlements.
ALTER TABLE engagement_cms.push_tokens ENABLE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
        WHERE schemaname = 'engagement_cms'
          AND tablename  = 'push_tokens'
          AND policyname = 'rls_brand'
    ) THEN
        CREATE POLICY rls_brand ON engagement_cms.push_tokens
            USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
    END IF;
END $$;

GRANT SELECT, INSERT, UPDATE, DELETE ON engagement_cms.push_tokens TO app_user;

-- ── 2. notification_event_cursors ─────────────────────────────────────────────
-- Tracks the last kernel.outbox_event.id processed by each named consumer.
-- NotificationMappingService uses consumer_name = 'notification_mapper' to advance
-- its own watermark independently of OutboxEventRelayService.
CREATE TABLE IF NOT EXISTS engagement_cms.notification_event_cursors (
    consumer_name   varchar(100) PRIMARY KEY,
    last_event_id   uuid         NULL,        -- NULL = start from beginning
    processed_count bigint       NOT NULL DEFAULT 0,
    updated_at      timestamptz  NOT NULL DEFAULT now()
);

-- Seed the row for the mapping consumer (idempotent).
INSERT INTO engagement_cms.notification_event_cursors (consumer_name, last_event_id, processed_count, updated_at)
VALUES ('notification_mapper', NULL, 0, now())
ON CONFLICT (consumer_name) DO NOTHING;

GRANT SELECT, INSERT, UPDATE ON engagement_cms.notification_event_cursors TO app_user;

COMMIT;

-- Quick verification
SELECT 'push_tokens' AS what, to_regclass('engagement_cms.push_tokens')::text AS detail
UNION ALL
SELECT 'notification_event_cursors', to_regclass('engagement_cms.notification_event_cursors')::text
UNION ALL
SELECT 'rls push_tokens', policyname FROM pg_policies
    WHERE schemaname = 'engagement_cms' AND tablename = 'push_tokens';
