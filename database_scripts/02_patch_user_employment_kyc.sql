-- ============================================================================
-- PATCH: employment + KYC + bank details on user_profiles
-- ----------------------------------------------------------------------------
-- People (HQ employees, franchise owners, franchise staff) are employees too,
-- so they carry the same employment / KYC / payout shape Riders already have.
-- All columns are optional (never required) and live on the 1-to-1 profile row.
-- Idempotent: safe to re-run. Folded into 02_bc2_identity_access.sql for fresh
-- installs; this patch exists to migrate already-provisioned databases.
-- ============================================================================
SET search_path = identity_access, public;

ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS employment_type       VARCHAR(20),
    ADD COLUMN IF NOT EXISTS pan_number            VARCHAR(10),
    ADD COLUMN IF NOT EXISTS aadhaar_number_masked VARCHAR(20),
    ADD COLUMN IF NOT EXISTS kyc_status            VARCHAR(20),
    ADD COLUMN IF NOT EXISTS kyc_verified_at       TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS bank_account_name     VARCHAR(200),
    ADD COLUMN IF NOT EXISTS bank_account_number   VARCHAR(50),
    ADD COLUMN IF NOT EXISTS bank_ifsc             VARCHAR(11),
    ADD COLUMN IF NOT EXISTS upi_id                VARCHAR(100);

-- CHECK constraints (added separately so re-runs don't trip on duplicates).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'user_profiles_employment_type_chk') THEN
        ALTER TABLE user_profiles ADD CONSTRAINT user_profiles_employment_type_chk
            CHECK (employment_type IS NULL OR employment_type IN
                ('full_time','part_time','contractual','consultant','intern'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'user_profiles_kyc_status_chk') THEN
        ALTER TABLE user_profiles ADD CONSTRAINT user_profiles_kyc_status_chk
            CHECK (kyc_status IS NULL OR kyc_status IN ('pending','verified','rejected'));
    END IF;
END $$;
