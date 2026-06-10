-- ============================================================
-- Patch: proof_photo_taken_at.sql
-- Purpose:
--   Add proof_photo_taken_at column to order_lifecycle.delivery_assignments.
--   Records when the rider uploaded the proof-of-delivery photo.
--
-- Idempotent: safe to re-run (IF NOT EXISTS guard).
-- Additive only: no drops or updates to existing rows.
-- ============================================================

SET search_path TO order_lifecycle, public;

ALTER TABLE order_lifecycle.delivery_assignments
    ADD COLUMN IF NOT EXISTS proof_photo_taken_at TIMESTAMPTZ;

-- Index: useful for queries like "find all deliveries with a proof photo today"
CREATE INDEX IF NOT EXISTS idx_da_proof_photo
    ON order_lifecycle.delivery_assignments(proof_photo_taken_at)
    WHERE proof_photo_taken_at IS NOT NULL;

-- Ensure app_user has UPDATE permission on the column (inherits from table-level grant,
-- but explicit column grant ensures forward compatibility with column-level security).
-- app_user already has INSERT/SELECT/UPDATE/DELETE on this table from app_user_role.sql.
