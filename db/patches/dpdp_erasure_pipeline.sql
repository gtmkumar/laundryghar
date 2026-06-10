-- ─── DPDP Erasure Pipeline — idempotent patch ─────────────────────────────────
-- Task #14: no new columns required — account_deletion_requests table already has
-- all necessary columns (status, anonymized_at, cancelled_at, grace_period_ends_at, etc.)
-- and the status CHECK constraint already includes: pending, grace_period, soft_deleted,
-- hard_deleted, cancelled, failed.
--
-- This patch:
--   1. Ensures the idx_acctdel_status index exists (covers the erasure worker's WHERE clause).
--   2. Ensures app_user has SELECT/INSERT/UPDATE on engagement_cms.push_tokens
--      and engagement_cms.notification_preferences (needed by erasure worker — but Worker
--      connects as postgres/superuser, so this is belt-and-suspenders for future app_user use).
--   3. Refreshes GRANTs on tables written to by the new Worker services.
-- ─────────────────────────────────────────────────────────────────────────────────────────

BEGIN;

-- ── 1. Ensure erasure-worker index on account_deletion_requests ───────────────
-- idx_acctdel_status already exists per schema inspection; this is a no-op guard.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'customer_catalog'
          AND tablename  = 'account_deletion_requests'
          AND indexname  = 'idx_acctdel_status'
    ) THEN
        CREATE INDEX idx_acctdel_status
            ON customer_catalog.account_deletion_requests (status, grace_period_ends_at);
        RAISE NOTICE 'Created idx_acctdel_status';
    ELSE
        RAISE NOTICE 'idx_acctdel_status already exists — skipping';
    END IF;
END;
$$;

-- ── 2. GRANTs for app_user on tables touched by erasure/retention workers ─────
-- Worker connects as postgres (superuser), bypassing RLS.  These GRANTs guard
-- the rare case where the connection string is rotated to app_user in future.

GRANT SELECT, UPDATE ON engagement_cms.push_tokens            TO app_user;
GRANT SELECT, DELETE  ON engagement_cms.notification_preferences TO app_user;
GRANT SELECT, DELETE  ON engagement_cms.notifications_outbox   TO app_user;
GRANT SELECT, DELETE  ON identity_access.otp_codes             TO app_user;
GRANT SELECT, DELETE  ON identity_access.refresh_tokens        TO app_user;

-- ── 3. No schema changes needed — all columns are already present ─────────────
-- account_deletion_requests already has:
--   status (CHECK: pending|grace_period|soft_deleted|hard_deleted|cancelled|failed)
--   anonymized_at, cancelled_at, grace_period_ends_at, processed_by, notes, metadata
-- customers already has:
--   status CHECK: active|blocked|deletion_requested|deleted

COMMIT;
