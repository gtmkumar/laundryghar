-- Patch: otp_harden_salted_hash.sql
-- Adds code_salt column to identity_access.otp_codes for HMAC-SHA256 per-row salting.
-- Idempotent: uses ADD COLUMN IF NOT EXISTS.
-- Rows with NULL code_salt are legacy SHA-256 unsalted — the application falls back to
-- the old comparison path for those rows until they naturally expire (TTL ≤ 5 min).

ALTER TABLE identity_access.otp_codes
    ADD COLUMN IF NOT EXISTS code_salt text NULL;

-- Index supports the rolling-window lockout query:
--   WHERE identifier = $1 AND created_at > $2
-- The existing idx_otp_identifier_active covers identifier+purpose but filters on
-- verified_at IS NULL; lockout scanning wants ALL rows (including verified/expired),
-- so a partial-index won't help. A plain composite on (identifier, created_at) serves both
-- the lockout count and the existing active-code look-ups well.
CREATE INDEX IF NOT EXISTS idx_otp_lockout_window
    ON identity_access.otp_codes (identifier, created_at DESC);
