-- =============================================================================
-- patch: pickup_idempotency_and_source.sql
-- Purpose : Add idempotency_key and source columns to
--           order_lifecycle.pickup_requests.
--           * idempotency_key: nullable VARCHAR(150), partial unique index on
--             (customer_id, idempotency_key) WHERE NOT NULL — mirrors the house
--             pattern in payment_idempotency.sql (commerce.payment_refunds).
--           * source: VARCHAR(20) NOT NULL DEFAULT 'app' with CHECK constraint —
--             tracks the channel that created the booking (app|web|mcp|whatsapp|pos|call).
--             Stored as a first-class column (not inside metadata JSONB) because
--             it is a filterable/reportable dimension and benefits from indexing.
-- Idempotent: safe to run multiple times (IF NOT EXISTS / OR REPLACE).
-- DDL role  : postgres (superuser / DDL owner)
-- Runtime   : app_user (RLS-enforced, RW on order_lifecycle.*)
-- Schema    : order_lifecycle (canonical LaundryGhar PostgreSQL cluster)
-- =============================================================================

DO $$
BEGIN
    -- ─── 1. idempotency_key ───────────────────────────────────────────────────
    -- Nullable. When supplied by the caller, uniqueness per (customer_id, key)
    -- prevents duplicate bookings from retries. NULL rows are excluded from the
    -- uniqueness check (partial index) — callers without a key are unaffected.
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'order_lifecycle'
          AND table_name   = 'pickup_requests'
          AND column_name  = 'idempotency_key'
    ) THEN
        ALTER TABLE order_lifecycle.pickup_requests
            ADD COLUMN idempotency_key VARCHAR(150);

        COMMENT ON COLUMN order_lifecycle.pickup_requests.idempotency_key IS
            'Optional caller-supplied idempotency key. When provided, duplicate '
            'requests from the same customer return the existing row rather than '
            'creating a new one. Uniqueness enforced per (customer_id, idempotency_key). '
            'Modelled after commerce.payment_refunds.idempotency_key.';
    END IF;

    -- ─── 2. source ────────────────────────────────────────────────────────────
    -- NOT NULL with DEFAULT so existing rows backfill to ''app'' automatically.
    -- Stored as a column (not in metadata JSONB) for indexing and reporting.
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'order_lifecycle'
          AND table_name   = 'pickup_requests'
          AND column_name  = 'source'
    ) THEN
        ALTER TABLE order_lifecycle.pickup_requests
            ADD COLUMN source VARCHAR(20) NOT NULL DEFAULT 'app'
                CHECK (source IN ('app','web','mcp','whatsapp','pos','call'));

        COMMENT ON COLUMN order_lifecycle.pickup_requests.source IS
            'Channel that originated this booking: app | web | mcp | whatsapp | pos | call. '
            'Defaults to ''app'' for existing mobile-app callers. '
            'Set to ''mcp'' when the booking is placed via the LaundryGhar AI assistant.';
    END IF;
END
$$;

-- ─── 3. Partial unique index for idempotency_key ─────────────────────────────
-- Per-customer uniqueness: two different customers may share the same key string.
-- Partial: NULL keys are excluded (callers without a key are not affected).
-- Mirrors: commerce.payment_refunds_idempotency_key_key pattern.
CREATE UNIQUE INDEX IF NOT EXISTS pickup_requests_customer_idempotency_key
    ON order_lifecycle.pickup_requests (customer_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

-- ─── 4. Index on source for ops dashboards / reporting queries ────────────────
CREATE INDEX IF NOT EXISTS idx_pickupreq_source
    ON order_lifecycle.pickup_requests (source, created_at DESC);
