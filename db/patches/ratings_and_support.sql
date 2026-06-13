-- =============================================================================
-- ratings_and_support.sql  (Laundry + Logistics marketplace — Wave 5)
--   1. logistics.rider_ratings — customer → rider rating with attribution + audit,
--      separate from the order-level rating; maintains riders.rating_average/count.
--   2. engagement_cms.support_tickets + engagement_cms.ticket_messages — customer/rider
--      support tickets with a threaded conversation and admin agent replies.
-- Additive + idempotent. Run as superuser (postgres).
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/ratings_and_support.sql
-- =============================================================================

BEGIN;

-- ── 1. Rider ratings ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS logistics.rider_ratings (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rider_id      UUID NOT NULL REFERENCES logistics.riders(id) ON DELETE CASCADE,
    brand_id      UUID NOT NULL,
    order_id      UUID NULL,
    customer_id   UUID NOT NULL,
    leg_type      VARCHAR(20) NULL,
    rating        SMALLINT NOT NULL CHECK (rating BETWEEN 1 AND 5),
    comment       TEXT NULL,
    is_flagged    BOOLEAN NOT NULL DEFAULT false,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by    UUID NULL
);
-- One rating per (customer, order, rider) — re-rating updates in app code.
CREATE UNIQUE INDEX IF NOT EXISTS uq_rider_rating_order
    ON logistics.rider_ratings (rider_id, order_id, customer_id) WHERE order_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_rider_ratings_rider ON logistics.rider_ratings (rider_id, created_at);

-- ── 2. Support tickets + messages ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS engagement_cms.support_tickets (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        UUID NOT NULL,
    ticket_number   VARCHAR(40) NOT NULL,
    requester_type  VARCHAR(20) NOT NULL CHECK (requester_type IN ('customer','rider')),
    requester_id    UUID NOT NULL,           -- user id of the customer/rider
    customer_id     UUID NULL,
    rider_id        UUID NULL,
    order_id        UUID NULL,
    subject         VARCHAR(200) NOT NULL,
    category        VARCHAR(40) NOT NULL DEFAULT 'general',
    priority        VARCHAR(20) NOT NULL DEFAULT 'normal' CHECK (priority IN ('low','normal','high')),
    status          VARCHAR(20) NOT NULL DEFAULT 'open'
                    CHECK (status IN ('open','in_progress','resolved','closed')),
    assigned_to     UUID NULL,
    last_message_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    metadata        JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by      UUID NULL,
    updated_by      UUID NULL
);
CREATE INDEX IF NOT EXISTS idx_support_tickets_inbox  ON engagement_cms.support_tickets (brand_id, status, last_message_at DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_requester ON engagement_cms.support_tickets (requester_id, created_at DESC);

CREATE TABLE IF NOT EXISTS engagement_cms.ticket_messages (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ticket_id    UUID NOT NULL REFERENCES engagement_cms.support_tickets(id) ON DELETE CASCADE,
    brand_id     UUID NOT NULL,
    sender_type  VARCHAR(20) NOT NULL CHECK (sender_type IN ('customer','rider','agent','system')),
    sender_id    UUID NULL,
    body         TEXT NOT NULL,
    metadata     JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by   UUID NULL
);
CREATE INDEX IF NOT EXISTS idx_ticket_messages_ticket ON engagement_cms.ticket_messages (ticket_id, created_at);

-- ── RLS (brand-scoped) + grants ──────────────────────────────────────────────
DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT * FROM (VALUES
        ('logistics','rider_ratings'),
        ('engagement_cms','support_tickets'),
        ('engagement_cms','ticket_messages')) AS t(sch, tbl)
    LOOP
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY;', r.sch, r.tbl);
        EXECUTE format('DROP POLICY IF EXISTS rls_brand ON %I.%I;', r.sch, r.tbl);
        EXECUTE format('CREATE POLICY rls_brand ON %I.%I USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));', r.sch, r.tbl);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON %I.%I TO app_user, app_admin;', r.sch, r.tbl);
    END LOOP;
END $$;

COMMIT;

-- Quick check
SELECT table_schema, table_name FROM information_schema.tables
WHERE (table_schema='logistics' AND table_name='rider_ratings')
   OR (table_schema='engagement_cms' AND table_name IN ('support_tickets','ticket_messages'))
ORDER BY table_name;
