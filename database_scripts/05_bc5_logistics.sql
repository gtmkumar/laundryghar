-- ============================================================================
-- LAUNDRY GHAR — 05 BC-5 Logistics
-- ============================================================================
-- Wave:           1
-- Bounded ctx:    BC-5 (Logistics)
-- Source §:       §8 riders & delivery
-- Tables:         4  (#56–59)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
--   - 02_bc2_identity_access.sql
--   - 04_bc4_order_lifecycle.sql
-- Owning agent:   agent/logistics
-- Purpose:        Rider profiles, shift assignments, GPS pings (PARTITIONED daily, 14-day retention), per-rider capacity caps. Links into delivery_assignments which already live in BC-4.
-- ============================================================================

-- SECTION 8: RIDERS & DELIVERY (4 tables: #56–59)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 56. riders — extended profile for delivery personnel
-- ----------------------------------------------------------------------------
CREATE TABLE riders (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    primary_store_id        UUID REFERENCES stores(id),
    rider_code              VARCHAR(30) NOT NULL,
    employment_type         VARCHAR(20) NOT NULL DEFAULT 'employee'
                            CHECK (employment_type IN ('employee','contractor','gig','outsourced')),
    aadhaar_number_masked   VARCHAR(20),
    pan_number              VARCHAR(10),
    driving_license_number  VARCHAR(50),
    dl_expiry_date          DATE,
    vehicle_type            VARCHAR(20) NOT NULL DEFAULT 'two_wheeler'
                            CHECK (vehicle_type IN ('two_wheeler','three_wheeler','four_wheeler','cycle','foot')),
    vehicle_number          VARCHAR(20),
    vehicle_model           VARCHAR(100),
    insurance_expiry_date   DATE,
    bank_account_number     VARCHAR(50),
    bank_ifsc               VARCHAR(11),
    bank_account_name       VARCHAR(200),
    upi_id                  VARCHAR(100),
    daily_pickup_capacity   INTEGER NOT NULL DEFAULT 30,
    daily_delivery_capacity INTEGER NOT NULL DEFAULT 30,
    service_radius_km       NUMERIC(5,2) NOT NULL DEFAULT 8.00,
    rating_average          NUMERIC(3,2),
    rating_count            INTEGER NOT NULL DEFAULT 0,
    completion_rate         NUMERIC(5,2),
    lifetime_deliveries     INTEGER NOT NULL DEFAULT 0,
    last_known_location     GEOGRAPHY(POINT, 4326),
    last_ping_at            TIMESTAMPTZ,
    is_online               BOOLEAN NOT NULL DEFAULT false,
    is_on_duty              BOOLEAN NOT NULL DEFAULT false,
    on_duty_since           TIMESTAMPTZ,
    current_load            INTEGER NOT NULL DEFAULT 0,
    kyc_status              VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (kyc_status IN ('pending','submitted','verified','rejected','expired')),
    kyc_verified_at         TIMESTAMPTZ,
    onboarded_at            TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','suspended','terminated','on_leave')),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, rider_code)

);
CREATE INDEX idx_riders_store           ON riders(primary_store_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_riders_franchise       ON riders(franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_riders_online          ON riders(brand_id) WHERE is_online = true AND is_on_duty = true;
CREATE INDEX idx_riders_location        ON riders USING GIST (last_known_location) WHERE is_online = true;

-- ----------------------------------------------------------------------------
-- 57. rider_assignments — shift / duty assignments
-- ----------------------------------------------------------------------------
CREATE TABLE rider_assignments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rider_id                UUID NOT NULL REFERENCES riders(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    shift_date              DATE NOT NULL,
    shift_start             TIME NOT NULL,
    shift_end               TIME NOT NULL,
    actual_start_at         TIMESTAMPTZ,
    actual_end_at           TIMESTAMPTZ,
    max_pickups             INTEGER NOT NULL DEFAULT 20,
    max_deliveries          INTEGER NOT NULL DEFAULT 20,
    completed_pickups       INTEGER NOT NULL DEFAULT 0,
    completed_deliveries    INTEGER NOT NULL DEFAULT 0,
    failed_attempts         INTEGER NOT NULL DEFAULT 0,
    total_distance_km       NUMERIC(8,2),
    earnings                NUMERIC(14,2),
    status                  VARCHAR(20) NOT NULL DEFAULT 'scheduled'
                            CHECK (status IN ('scheduled','active','on_break','completed','cancelled','no_show')),
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (rider_id, shift_date, shift_start)

);
CREATE INDEX idx_riderassign_rider_date ON rider_assignments(rider_id, shift_date DESC);
CREATE INDEX idx_riderassign_store_date ON rider_assignments(store_id, shift_date, status);

-- ----------------------------------------------------------------------------
-- 58. rider_location_pings — GPS time series (PARTITIONED daily)
-- ----------------------------------------------------------------------------
CREATE TABLE rider_location_pings (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    pinged_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    rider_id                UUID NOT NULL,
    brand_id                UUID NOT NULL,
    location                GEOGRAPHY(POINT, 4326) NOT NULL,
    accuracy_meters         NUMERIC(8,2),
    speed_kmph              NUMERIC(6,2),
    heading_degrees         NUMERIC(5,2),
    battery_percent         SMALLINT,
    is_moving               BOOLEAN,
    activity_type           VARCHAR(20),
    current_assignment_id   UUID,
    metadata                JSONB,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    PRIMARY KEY (id, pinged_at)

) PARTITION BY RANGE (pinged_at);

CREATE INDEX idx_riderping_rider_time   ON rider_location_pings(rider_id, pinged_at DESC);
CREATE INDEX idx_riderping_geo          ON rider_location_pings USING GIST (location);

-- ----------------------------------------------------------------------------
-- 59. rider_capacity_config — per-rider per-slot caps
-- ----------------------------------------------------------------------------
CREATE TABLE rider_capacity_config (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rider_id                UUID NOT NULL REFERENCES riders(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    store_id                UUID,
    day_of_week             SMALLINT CHECK (day_of_week BETWEEN 0 AND 6),
    slot_start              TIME,
    slot_end                TIME,
    max_pickups_per_slot    INTEGER NOT NULL DEFAULT 8,
    max_deliveries_per_slot INTEGER NOT NULL DEFAULT 8,
    max_concurrent_orders   INTEGER NOT NULL DEFAULT 5,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    effective_from          DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_to            DATE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_ridercap_rider_day     ON rider_capacity_config(rider_id, day_of_week) WHERE is_active = true;


-- ============================================================================
