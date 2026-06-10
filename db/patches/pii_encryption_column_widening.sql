-- ============================================================
-- Patch: pii_encryption_column_widening.sql
-- Purpose:
--   Widen PII columns on user_profiles and logistics.riders from
--   fixed-length varchar to text so they can hold AES-256-GCM
--   base64 ciphertext (prefix "enc:v1:" + ~60-100 chars for typical values).
--
--   Existing plaintext values are preserved in-place and remain
--   readable via the legacy-passthrough logic in AesGcmFieldCipher.
--   New writes from the application will be encrypted.
--
-- Idempotent: ALTER COLUMN TYPE on PostgreSQL is a no-op when the column
-- is already text (implicit cast text→text). Safe to re-run.
-- ============================================================

-- ── identity_access.user_profiles ───────────────────────────────────────────

ALTER TABLE identity_access.user_profiles
    ALTER COLUMN pan_number         TYPE text,
    ALTER COLUMN bank_account_number TYPE text,
    ALTER COLUMN upi_id             TYPE text;

-- ── logistics.riders ─────────────────────────────────────────────────────────

ALTER TABLE logistics.riders
    ALTER COLUMN pan_number         TYPE text,
    ALTER COLUMN bank_account_number TYPE text,
    ALTER COLUMN upi_id             TYPE text;
