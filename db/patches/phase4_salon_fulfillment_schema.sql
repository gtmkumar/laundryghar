-- =============================================================================
-- db/patches/phase4_salon_fulfillment_schema.sql
--
-- Multi-vertical Phase 4 — the salon vertical's PRIVATE fulfilment schema (blueprint §7.4
-- "New salon_fulfillment schema: Appointment, StaffMember, Resource, ResourceBooking (RLS
-- reused verbatim)"). This is the proof the platform is multi-vertical: salon ships as a NEW
-- strategy (SalonAppointmentStrategy) + a NEW private schema, with ZERO change to the shared
-- spine — exactly mirroring how laundry_fulfillment was relocated in Phase 1.
--
-- An appointment links to the shared order spine via the same composite FK (order_id,
-- order_created_at) the laundry fulfillment_unit uses — the spine stays vertical-neutral.
--
-- Brand-scoped RLS uses the SAME policy shape as every other tenant table.
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_salon_fulfillment_schema.sql
-- =============================================================================

BEGIN;

CREATE SCHEMA IF NOT EXISTS salon_fulfillment;

-- Staff members who perform services -----------------------------------------
CREATE TABLE IF NOT EXISTS salon_fulfillment.staff_members (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id      uuid NOT NULL,
    store_id      uuid NOT NULL,
    user_id       uuid,                       -- optional link to identity_access.users
    display_name  varchar(120) NOT NULL,
    staff_tier    varchar(40)  NOT NULL DEFAULT 'standard',
    skills        text[]       NOT NULL DEFAULT '{}',
    status        varchar(20)  NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz  NOT NULL DEFAULT now()
);

-- Bookable resources (chairs, rooms, equipment) ------------------------------
CREATE TABLE IF NOT EXISTS salon_fulfillment.resources (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id      uuid NOT NULL,
    store_id      uuid NOT NULL,
    name          varchar(120) NOT NULL,
    resource_type varchar(40)  NOT NULL DEFAULT 'station',
    capacity      smallint     NOT NULL DEFAULT 1,
    status        varchar(20)  NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz  NOT NULL DEFAULT now()
);

-- The appointment itself — the salon analogue of a laundry fulfillment_unit ----
CREATE TABLE IF NOT EXISTS salon_fulfillment.appointments (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id         uuid NOT NULL,
    franchise_id     uuid NOT NULL,
    store_id         uuid NOT NULL,
    order_id         uuid NOT NULL,
    order_created_at timestamptz NOT NULL,    -- partition key carried for the composite FK
    customer_id      uuid NOT NULL,
    staff_member_id  uuid REFERENCES salon_fulfillment.staff_members(id),
    -- the strategy-owned salon status (booked/confirmed/checked_in/in_service/completed/…)
    appointment_status varchar(30) NOT NULL DEFAULT 'booked'
                       CHECK (char_length(appointment_status) > 0),
    scheduled_start  timestamptz NOT NULL,
    scheduled_end    timestamptz NOT NULL,
    -- salon-private attributes (duration/service tier/notes) off the generic spine
    attributes       jsonb       NOT NULL DEFAULT '{}'::jsonb,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_appointments_order ON salon_fulfillment.appointments(order_id, order_created_at);
CREATE INDEX IF NOT EXISTS idx_appointments_staff_slot ON salon_fulfillment.appointments(staff_member_id, scheduled_start);

-- A resource reservation for an appointment ----------------------------------
CREATE TABLE IF NOT EXISTS salon_fulfillment.resource_bookings (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id       uuid NOT NULL,
    appointment_id uuid NOT NULL REFERENCES salon_fulfillment.appointments(id) ON DELETE CASCADE,
    resource_id    uuid NOT NULL REFERENCES salon_fulfillment.resources(id),
    booked_from    timestamptz NOT NULL,
    booked_to      timestamptz NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);

-- Brand-scoped RLS (same shape as every other tenant table; reused verbatim) --
DO $rls$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['staff_members','resources','appointments','resource_bookings'] LOOP
        EXECUTE format('ALTER TABLE salon_fulfillment.%I ENABLE ROW LEVEL SECURITY;', t);
        EXECUTE format('DROP POLICY IF EXISTS %I_tenant ON salon_fulfillment.%I;', t, t);
        EXECUTE format($p$
            CREATE POLICY %I_tenant ON salon_fulfillment.%I
            USING (current_setting('app.bypass_rls', true) = 'true'
                   OR brand_id = current_setting('app.current_brand_id', true)::uuid);
        $p$, t, t);
    END LOOP;
END
$rls$;

-- Verification gate -----------------------------------------------------------
DO $verify$
DECLARE tbls int; pols int;
BEGIN
    SELECT count(*) INTO tbls FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='salon_fulfillment' AND c.relkind='r'
      AND c.relname IN ('staff_members','resources','appointments','resource_bookings');
    IF tbls <> 4 THEN RAISE EXCEPTION 'Phase 4: expected 4 salon_fulfillment tables, found %', tbls; END IF;

    SELECT count(*) INTO pols FROM pg_policies WHERE schemaname='salon_fulfillment';
    IF pols <> 4 THEN RAISE EXCEPTION 'Phase 4: expected 4 RLS policies, found %', pols; END IF;

    RAISE NOTICE 'Phase 4 verification passed: salon_fulfillment schema (4 tables + RLS) created; shared spine untouched.';
END
$verify$;

COMMIT;

SELECT 'phase4_salon_fulfillment_schema.sql applied successfully.' AS result;
