-- =============================================================================
-- dispatch_offer_states.sql  (Laundry + Logistics marketplace — Wave 1.4)
-- Adds the offer→accept dispatch sub-states and offer-tracking columns to
-- order_lifecycle.delivery_assignments, plus an 'offered' status on pickup_requests.
--   offered  : the job has been offered to a rider, awaiting accept/decline/expiry
--   expired  : the offer lapsed without acceptance (re-offered to the next rider)
-- Push-assign mode is unchanged ('assigned' path). Additive + idempotent.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/dispatch_offer_states.sql
-- =============================================================================

BEGIN;

-- delivery_assignments: widen status CHECK + add offer-tracking columns.
ALTER TABLE order_lifecycle.delivery_assignments
    ADD COLUMN IF NOT EXISTS offered_at      TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS offer_expires_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS offer_round     SMALLINT NULL;

ALTER TABLE order_lifecycle.delivery_assignments
    DROP CONSTRAINT IF EXISTS delivery_assignments_status_check;
ALTER TABLE order_lifecycle.delivery_assignments
    ADD CONSTRAINT delivery_assignments_status_check
    CHECK (status IN ('offered','assigned','accepted','rejected','started','arrived',
                      'completed','cancelled','failed','rescheduled','expired'));

COMMENT ON COLUMN order_lifecycle.delivery_assignments.offered_at IS
    'offer_accept mode: when this job was offered to the rider.';
COMMENT ON COLUMN order_lifecycle.delivery_assignments.offer_expires_at IS
    'offer_accept mode: when an un-accepted offer lapses and is re-offered.';
COMMENT ON COLUMN order_lifecycle.delivery_assignments.offer_round IS
    'offer_accept mode: 1-based round number; re-offers increment this.';

-- Index to let the expiry sweep find live offers cheaply.
CREATE INDEX IF NOT EXISTS idx_delivery_assignments_offer_expiry
    ON order_lifecycle.delivery_assignments (offer_expires_at)
    WHERE status = 'offered';

-- pickup_requests: allow an 'offered' status (job offered but not yet accepted).
ALTER TABLE order_lifecycle.pickup_requests
    DROP CONSTRAINT IF EXISTS pickup_requests_status_check;
ALTER TABLE order_lifecycle.pickup_requests
    ADD CONSTRAINT pickup_requests_status_check
    CHECK (status IN ('pending','offered','assigned','rider_dispatched','arrived',
                      'completed','converted','cancelled','no_response','rescheduled'));

COMMIT;

-- Quick check
SELECT column_name FROM information_schema.columns
WHERE table_schema='order_lifecycle' AND table_name='delivery_assignments'
  AND column_name IN ('offered_at','offer_expires_at','offer_round')
ORDER BY column_name;
