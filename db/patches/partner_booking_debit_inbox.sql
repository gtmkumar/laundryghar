-- =============================================================================
-- partner_booking_debit_inbox.sql  (PR #16 fix — NO-SKIP partner debit delivery)
-- Adds:
--   kernel.outbox_consumed_events
--       — per-consumer "inbox" marker table. Records EACH kernel.outbox_events row a
--         named worker consumer has durably processed, so the consumer can query the
--         set of UNPROCESSED events with an anti-join (order-independent) instead of a
--         moving occurred_at watermark.
--   idx_outbox_partner_debit
--       — partial index over the 'partner_booking.debit_wallet' events the debit
--         consumer scans, so the anti-join stays cheap.
--
-- WHY: PartnerBookingDebitService (a MONEY path) advanced a time-watermark with a
-- strict `occurred_at > last` filter. occurred_at is app-assigned at booking START
-- (not commit), so two failure modes PERMANENTLY SKIPPED debit events — a booking is
-- created but the partner wallet is never charged (free booking / revenue loss):
--   1. Out-of-order commit: an earlier-occurred_at row commits AFTER a later one the
--      watermark already passed → the earlier row is < watermark and never re-read.
--   2. Timestamp ties / bursts: >BatchSize rows share one occurred_at; the batch stops
--      mid-tie and the next `> T` skips the tied remainder.
-- The per-event marker removes the watermark entirely for this consumer, so an event is
-- eligible until it is provably processed — structurally no-skip. DebitPartnerWallet is
-- idempotent (idempotency_key = booking id), so re-delivery is a harmless no-op.
--
-- TRANSITION: on first run after deploy the inbox is empty, so the consumer re-queries
-- historical 'partner_booking.debit_wallet' events. This is SAFE and self-healing:
-- already-debited bookings idempotently no-op; already-cancelled unfunded bookings stay
-- cancelled; and any event previously SKIPPED by the watermark bug is now correctly
-- debited (or its unfunded booking cancelled). We intentionally do NOT back-fill the
-- inbox from the old cursor, because the old watermark is exactly what could not be
-- trusted at the tie/out-of-order boundary.
--
-- Idempotent. Additive only. Run:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@18/bin/psql \
--     -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/partner_booking_debit_inbox.sql
-- =============================================================================

BEGIN;

-- ── 1. outbox_consumed_events ─────────────────────────────────────────────────
-- Composite PK (consumer_name, event_id): one marker per (consumer, event). Lets many
-- consumers track the same event id independently and lets each anti-join on its own
-- consumer_name. FK → outbox_events with ON DELETE CASCADE so retention/cleanup of an
-- outbox row also drops its markers (no orphans; the event leaves the scan set too).
-- No RLS — this is an internal, cross-brand processing marker (same posture as
-- engagement_cms.notification_event_cursors); the worker runs RLS-bypassed anyway.
CREATE TABLE IF NOT EXISTS kernel.outbox_consumed_events (
    consumer_name varchar(100) NOT NULL,
    event_id      uuid         NOT NULL,
    processed_at  timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT outbox_consumed_events_pkey
        PRIMARY KEY (consumer_name, event_id),
    CONSTRAINT outbox_consumed_events_event_fk
        FOREIGN KEY (event_id) REFERENCES kernel.outbox_events (id) ON DELETE CASCADE
);

-- ── 2. partial index for the partner-debit consumer's unprocessed scan ─────────
-- The consumer filters event_type = 'partner_booking.debit_wallet' and orders by
-- occurred_at; a partial index keeps that scan cheap without bloating on other types.
CREATE INDEX IF NOT EXISTS idx_outbox_partner_debit
    ON kernel.outbox_events (occurred_at)
    WHERE event_type = 'partner_booking.debit_wallet';

GRANT SELECT, INSERT, DELETE ON kernel.outbox_consumed_events TO app_user;

COMMIT;

-- Quick verification
SELECT 'outbox_consumed_events' AS what,
       to_regclass('kernel.outbox_consumed_events')::text AS detail
UNION ALL
SELECT 'idx_outbox_partner_debit',
       to_regclass('kernel.idx_outbox_partner_debit')::text;
