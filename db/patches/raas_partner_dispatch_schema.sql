-- =============================================================================
-- db/patches/raas_partner_dispatch_schema.sql
--
-- PURPOSE (RaaS full build, FULL-11b / issue #14): create the partner-booking
-- rider dispatch table in the LOGISTICS schema —
--   • logistics.partner_dispatches — the rider lifecycle for a partner booking
--
-- SEPARATE TABLE BY DESIGN: the hot, partitioned order_lifecycle.delivery_assignments
-- table is left UNTOUCHED. The track / OTP / proof / last-known-location columns below are
-- REPLICATED from delivery_assignments (not referenced) so the order-fulfilment spine and the
-- partner-fleet dispatch lifecycle evolve independently.
--
-- DUAL VISIBILITY: the row carries BOTH partner_id (the owning partner, who tracks) AND brand_id
-- (the serving LaundryGhar fleet's brand, whose staff dispatch/manage). The combined
-- rls_partner_or_brand policy that enforces this lives in the companion patch
-- db/patches/rls_partner_dispatch.sql — run it AFTER this one.
--
-- The only FK is partner_booking_id → logistics.partner_bookings(id). brand_id and rider_id are
-- SCALAR cross-references (no FK): brand_id mirrors partner_bookings.brand_id (brands are admin-only
-- / cross-schema), and rider_id is nullable-until-assigned.
--
-- Idempotent. Safe to re-run.
--
-- RUN (manual, as postgres — AFTER raas_partner_schema.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/raas_partner_dispatch_schema.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- Harmless: schema already exists in every deployed environment.
CREATE SCHEMA IF NOT EXISTS logistics;

-- ---------------------------------------------------------------------------
-- logistics.partner_dispatches — rider dispatch for a partner booking
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS logistics.partner_dispatches (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    partner_id          UUID NOT NULL,                 -- rls_partner_or_brand arm #1 (owning partner)
    partner_booking_id  UUID NOT NULL,                 -- FK → partner_bookings(id)
    brand_id            UUID,                          -- rls_partner_or_brand arm #2 (serving fleet's brand); scalar, no FK
    rider_id            UUID,                          -- assigned rider; scalar cross-ref → logistics.riders, no FK (nullable until assigned)
    status              VARCHAR(20) NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending','assigned','en_route_pickup',
                                          'picked_up','en_route_drop','delivered','cancelled')),
    -- verification (replicated from delivery_assignments OTP flow)
    pickup_otp          VARCHAR(10),
    drop_otp            VARCHAR(10),
    pickup_verified_at  TIMESTAMPTZ,
    drop_verified_at    TIMESTAMPTZ,
    -- proof of delivery (replicated)
    proof_photo_url     VARCHAR(1000),
    proof_signature_url VARCHAR(1000),
    -- last-known location (track)
    last_known_lat      NUMERIC(10,7),
    last_known_lng      NUMERIC(10,7),
    last_location_at    TIMESTAMPTZ,
    assigned_at         TIMESTAMPTZ,
    -- audit
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by          UUID,
    updated_by          UUID,
    CONSTRAINT partner_dispatches_partner_booking_id_fkey
        FOREIGN KEY (partner_booking_id) REFERENCES logistics.partner_bookings(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_partner_dispatches_partner ON logistics.partner_dispatches(partner_id);
CREATE INDEX IF NOT EXISTS idx_partner_dispatches_brand   ON logistics.partner_dispatches(brand_id);
CREATE INDEX IF NOT EXISTS idx_partner_dispatches_booking ON logistics.partner_dispatches(partner_booking_id);

-- ---------------------------------------------------------------------------
-- GRANTs — explicit CRUD for the runtime roles. ALTER DEFAULT PRIVILEGES in
-- harden_app_user_and_rls_bypass.sql already covers future logistics tables, but grant
-- explicitly so this patch is self-contained regardless of apply order.
-- ---------------------------------------------------------------------------
DO $grants$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['app_user','app_admin'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA logistics TO %I', r);
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.partner_dispatches TO %I', r);
        END IF;
    END LOOP;
END
$grants$;

SELECT 'raas_partner_dispatch_schema.sql applied successfully.' AS result;
