-- =============================================================================
-- rider_kyc_documents.sql  (Laundry + Logistics marketplace — Wave 3)
-- Rider onboarding compliance:
--   1. logistics.rider_documents — KYC document uploads (license/rc/insurance/id/photo)
--      with a per-document review state (pending → approved/rejected).
--   2. riders.vehicle_verification_status — granular vehicle gate
--      (pending → under_review → approved/rejected). Combined with kyc_status it
--      decides whether a rider may receive dispatch offers/assignments.
-- Backward-compat: existing riders are backfilled to 'approved' so live dispatch
-- keeps working; only newly-onboarded riders start 'pending'.
-- Additive + idempotent. Run as superuser (postgres).
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/rider_kyc_documents.sql
-- =============================================================================

BEGIN;

-- ── 1. Vehicle verification gate on riders ───────────────────────────────────
ALTER TABLE logistics.riders
    ADD COLUMN IF NOT EXISTS vehicle_verification_status VARCHAR(20) NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS vehicle_verified_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS vehicle_verified_by UUID NULL,
    ADD COLUMN IF NOT EXISTS vehicle_rejection_reason TEXT NULL;

ALTER TABLE logistics.riders
    DROP CONSTRAINT IF EXISTS riders_vehicle_verification_status_check;
ALTER TABLE logistics.riders
    ADD CONSTRAINT riders_vehicle_verification_status_check
    CHECK (vehicle_verification_status IN ('pending','under_review','approved','rejected'));

-- Backfill: existing riders were already operating → approved (one-time, only rows
-- still at the column default 'pending' AND created before this patch run).
UPDATE logistics.riders
   SET vehicle_verification_status = 'approved',
       vehicle_verified_at = COALESCE(vehicle_verified_at, now())
 WHERE vehicle_verification_status = 'pending';

COMMENT ON COLUMN logistics.riders.vehicle_verification_status IS
    'Vehicle review gate: pending|under_review|approved|rejected. Dispatch requires approved.';

-- ── 2. rider_documents table ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS logistics.rider_documents (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rider_id         UUID NOT NULL REFERENCES logistics.riders(id) ON DELETE CASCADE,
    brand_id         UUID NOT NULL,
    doc_type         VARCHAR(20) NOT NULL
                     CHECK (doc_type IN ('license','rc','insurance','id','photo')),
    storage_key      TEXT NOT NULL,
    file_name        TEXT NOT NULL,
    mime_type        VARCHAR(100) NOT NULL,
    bytes            BIGINT NOT NULL DEFAULT 0,
    status           VARCHAR(20) NOT NULL DEFAULT 'pending'
                     CHECK (status IN ('pending','approved','rejected')),
    rejection_reason TEXT NULL,
    reviewed_by      UUID NULL,
    reviewed_at      TIMESTAMPTZ NULL,
    metadata         JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by       UUID NULL,
    updated_by       UUID NULL
);

CREATE INDEX IF NOT EXISTS idx_rider_documents_rider ON logistics.rider_documents (rider_id, doc_type);
CREATE INDEX IF NOT EXISTS idx_rider_documents_review
    ON logistics.rider_documents (brand_id, status) WHERE status = 'pending';

-- RLS — brand-scoped, mirroring logistics.riders.
ALTER TABLE logistics.rider_documents ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON logistics.rider_documents;
CREATE POLICY rls_brand ON logistics.rider_documents
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));

GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.rider_documents TO app_user, app_admin;

COMMIT;

-- Quick check
SELECT 'riders.vehicle_verification_status' AS what, vehicle_verification_status AS val, count(*)
FROM logistics.riders GROUP BY vehicle_verification_status
UNION ALL
SELECT 'rider_documents exists', 'yes', count(*) FROM logistics.rider_documents;
