-- =============================================================================
-- rider_payouts_incentives.sql  (Laundry + Logistics marketplace — Wave 4)
-- Rider earnings & engagement:
--   1. logistics.rider_payout_requests — rider-initiated withdrawal of earned payout,
--      reviewed by admin (requested → approved/rejected → paid, posts to cash_book).
--   2. logistics.incentive_rules — admin-configured bonus rules (trips_target / surge_bonus).
--   3. logistics.rider_incentive_awards — bonuses awarded to riders at task completion.
-- Additive + idempotent. Run as superuser (postgres).
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/rider_payouts_incentives.sql
-- =============================================================================

BEGIN;

-- ── 1. Rider payout (withdrawal) requests ────────────────────────────────────
CREATE TABLE IF NOT EXISTS logistics.rider_payout_requests (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rider_id          UUID NOT NULL REFERENCES logistics.riders(id) ON DELETE CASCADE,
    brand_id          UUID NOT NULL,
    franchise_id      UUID NULL,
    store_id          UUID NULL,
    amount            NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    status            VARCHAR(20) NOT NULL DEFAULT 'requested'
                      CHECK (status IN ('requested','approved','rejected','paid')),
    rejection_reason  TEXT NULL,
    payment_reference TEXT NULL,
    requested_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    reviewed_by       UUID NULL,
    reviewed_at       TIMESTAMPTZ NULL,
    paid_by           UUID NULL,
    paid_at           TIMESTAMPTZ NULL,
    metadata          JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by        UUID NULL,
    updated_by        UUID NULL
);
CREATE INDEX IF NOT EXISTS idx_rider_payout_requests_rider ON logistics.rider_payout_requests (rider_id, status);
CREATE INDEX IF NOT EXISTS idx_rider_payout_requests_queue
    ON logistics.rider_payout_requests (brand_id, status) WHERE status = 'requested';

-- ── 2. Incentive rules ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS logistics.incentive_rules (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id      UUID NOT NULL,
    name          VARCHAR(120) NOT NULL,
    rule_type     VARCHAR(20) NOT NULL
                  CHECK (rule_type IN ('trips_target','surge_bonus')),
    -- trips_target: award reward_amount when a rider's daily completed deliveries hits threshold.
    -- surge_bonus:  award reward_amount per delivery completed within a fare surge window.
    threshold     INT NOT NULL DEFAULT 0,
    reward_amount NUMERIC(14,2) NOT NULL CHECK (reward_amount >= 0),
    "window"      VARCHAR(20) NOT NULL DEFAULT 'daily' CHECK ("window" IN ('daily')),
    is_active     BOOLEAN NOT NULL DEFAULT true,
    valid_from    TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_until   TIMESTAMPTZ NULL,
    metadata      JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by    UUID NULL,
    updated_by    UUID NULL
);
CREATE INDEX IF NOT EXISTS idx_incentive_rules_active
    ON logistics.incentive_rules (brand_id, is_active) WHERE is_active;

-- ── 3. Rider incentive awards ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS logistics.rider_incentive_awards (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rider_id              UUID NOT NULL REFERENCES logistics.riders(id) ON DELETE CASCADE,
    brand_id              UUID NOT NULL,
    rule_id               UUID NULL REFERENCES logistics.incentive_rules(id) ON DELETE SET NULL,
    rule_name_snapshot    VARCHAR(120) NOT NULL,
    rule_type             VARCHAR(20) NOT NULL,
    amount                NUMERIC(14,2) NOT NULL,
    period_key            VARCHAR(20) NOT NULL,   -- e.g. IST day 'yyyy-MM-dd' for daily rules
    delivery_assignment_id UUID NULL,
    awarded_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    metadata              JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by            UUID NULL
);
-- Idempotency for daily target rules: at most one award per (rider, rule, period).
CREATE UNIQUE INDEX IF NOT EXISTS uq_rider_incentive_award_period
    ON logistics.rider_incentive_awards (rider_id, rule_id, period_key)
    WHERE rule_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_rider_incentive_awards_rider
    ON logistics.rider_incentive_awards (rider_id, awarded_at);

-- ── RLS (brand-scoped) + grants, mirroring logistics.riders ──────────────────
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['rider_payout_requests','incentive_rules','rider_incentive_awards'] LOOP
        EXECUTE format('ALTER TABLE logistics.%I ENABLE ROW LEVEL SECURITY;', t);
        EXECUTE format('DROP POLICY IF EXISTS rls_brand ON logistics.%I;', t);
        EXECUTE format('CREATE POLICY rls_brand ON logistics.%I USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));', t);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.%I TO app_user, app_admin;', t);
    END LOOP;
END $$;

COMMIT;

-- Quick check
SELECT table_name FROM information_schema.tables
WHERE table_schema='logistics'
  AND table_name IN ('rider_payout_requests','incentive_rules','rider_incentive_awards')
ORDER BY table_name;
