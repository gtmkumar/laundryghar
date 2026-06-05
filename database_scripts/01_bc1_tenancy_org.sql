-- ============================================================================
-- LAUNDRY GHAR — 01 BC-1 Tenancy & Organization
-- ============================================================================
-- Wave:           0
-- Bounded ctx:    BC-1 (Tenancy & Organization)
-- Source §:       §1
-- Tables:         10  (#1–10)
-- Apply after:
--   - 00_kernel.sql
-- Owning agent:   agent/foundation
-- Purpose:        Multi-tenant hierarchy: Platform → Brand → Territory → Franchise Owner → Franchise → Store / Warehouse. `brands` is the central FK target (referenced 31× across the schema) so this file blocks all of Wave 1.
-- ============================================================================

-- SECTION 1: TENANCY & ORGANIZATION (10 tables: #1–10)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 01. platforms — top-level system owner (usually 1 row)
-- ----------------------------------------------------------------------------
CREATE TABLE platforms (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code                    VARCHAR(50) NOT NULL UNIQUE,
    name                    VARCHAR(200) NOT NULL,
    legal_name              VARCHAR(200),
    domain                  VARCHAR(200),
    support_email           CITEXT,
    support_phone           VARCHAR(20),
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','suspended','archived')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ
);

-- ----------------------------------------------------------------------------
-- 02. brands — white-label brand under platform (1 by default; N for white-label)
-- ----------------------------------------------------------------------------
CREATE TABLE brands (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    platform_id             UUID NOT NULL REFERENCES platforms(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL UNIQUE,
    name                    VARCHAR(200) NOT NULL,
    legal_name              VARCHAR(200),
    tagline                 VARCHAR(300),
    description             TEXT,
    logo_url                TEXT,
    favicon_url             TEXT,
    primary_color           CHAR(7),
    secondary_color         CHAR(7),
    accent_color            CHAR(7),
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    country_code            CHAR(2) NOT NULL DEFAULT 'IN',
    timezone                VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    locale_default          VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    locales_enabled         TEXT[] NOT NULL DEFAULT ARRAY['en-IN','hi-IN'],
    support_email           CITEXT,
    support_phone           VARCHAR(20),
    toll_free_number        VARCHAR(20),
    whatsapp_number         VARCHAR(20),
    website_url             TEXT,
    privacy_policy_url      TEXT,
    terms_url               TEXT,
    play_store_url          TEXT,
    app_store_url           TEXT,
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','suspended','archived')),
    launched_at             TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_brands_platform        ON brands(platform_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_brands_status          ON brands(status) WHERE deleted_at IS NULL;
CREATE INDEX idx_brands_config_gin      ON brands USING GIN (config);

-- ----------------------------------------------------------------------------
-- 03. territories — geographic exclusivity zones per brand
-- ----------------------------------------------------------------------------
CREATE TABLE territories (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    country_code            CHAR(2) NOT NULL DEFAULT 'IN',
    state                   VARCHAR(100),
    cities                  TEXT[] NOT NULL DEFAULT '{}',
    pincodes                TEXT[] NOT NULL DEFAULT '{}',
    boundary                GEOGRAPHY(MULTIPOLYGON, 4326),
    exclusivity_type        VARCHAR(20) NOT NULL DEFAULT 'exclusive'
                            CHECK (exclusivity_type IN ('exclusive','non_exclusive','first_right')),
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','reserved','available','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_territories_brand      ON territories(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_territories_boundary   ON territories USING GIST (boundary);
CREATE INDEX idx_territories_pincodes   ON territories USING GIN (pincodes);
CREATE INDEX idx_territories_cities     ON territories USING GIN (cities);

ALTER TABLE territories ENABLE ROW LEVEL SECURITY;
CREATE POLICY territories_tenant ON territories
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 04. franchise_agreements — legal contract between platform/brand and franchisee
-- ----------------------------------------------------------------------------
CREATE TABLE franchise_agreements (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    agreement_number        VARCHAR(50) NOT NULL UNIQUE,
    agreement_type          VARCHAR(30) NOT NULL DEFAULT 'unit'
                            CHECK (agreement_type IN ('unit','multi_unit','master','area_developer')),
    franchisee_legal_name   VARCHAR(200) NOT NULL,
    franchisee_pan          VARCHAR(10),
    franchisee_gstin        VARCHAR(15),
    franchisee_phone        VARCHAR(20),
    franchisee_email        CITEXT,
    initial_franchise_fee   NUMERIC(14,2) NOT NULL DEFAULT 0,
    royalty_percent         NUMERIC(5,2) NOT NULL DEFAULT 0
                            CHECK (royalty_percent BETWEEN 0 AND 100),
    marketing_fee_percent   NUMERIC(5,2) NOT NULL DEFAULT 0,
    technology_fee_monthly  NUMERIC(14,2) NOT NULL DEFAULT 0,
    territory_id            UUID REFERENCES territories(id),
    term_years              SMALLINT NOT NULL DEFAULT 5,
    renewal_option          BOOLEAN NOT NULL DEFAULT true,
    exclusivity_clause      BOOLEAN NOT NULL DEFAULT true,
    minimum_stores          SMALLINT NOT NULL DEFAULT 1,
    maximum_stores          SMALLINT,
    sla_terms               JSONB NOT NULL DEFAULT '{}'::jsonb,
    document_s3_key         TEXT,
    signed_at               TIMESTAMPTZ,
    effective_from          DATE NOT NULL,
    effective_to            DATE NOT NULL,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','signed','active','expired','terminated','renewed')),
    terminated_at           TIMESTAMPTZ,
    termination_reason      TEXT,
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_franagree_brand        ON franchise_agreements(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_franagree_territory    ON franchise_agreements(territory_id);
CREATE INDEX idx_franagree_status       ON franchise_agreements(status) WHERE deleted_at IS NULL;

ALTER TABLE franchise_agreements ENABLE ROW LEVEL SECURITY;
CREATE POLICY franagree_tenant ON franchise_agreements
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 05. franchises — operational franchise business entity
-- ----------------------------------------------------------------------------
CREATE TABLE franchises (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    territory_id            UUID REFERENCES territories(id) ON DELETE SET NULL,
    franchise_agreement_id  UUID REFERENCES franchise_agreements(id),
    owner_user_id           UUID,
    code                    VARCHAR(50) NOT NULL,
    legal_name              VARCHAR(200) NOT NULL,
    display_name            VARCHAR(200),
    gstin                   VARCHAR(15),
    pan                     VARCHAR(10),
    cin                     VARCHAR(21),
    contact_phone           VARCHAR(20) NOT NULL,
    contact_email           CITEXT,
    billing_address         JSONB NOT NULL,
    operational_address     JSONB,
    bank_account_name       VARCHAR(200),
    bank_account_number     VARCHAR(50),
    bank_ifsc               VARCHAR(11),
    bank_name               VARCHAR(100),
    royalty_percent         NUMERIC(5,2) NOT NULL DEFAULT 0
                            CHECK (royalty_percent BETWEEN 0 AND 100),
    marketing_fee_percent   NUMERIC(5,2) NOT NULL DEFAULT 0,
    onboarding_status       VARCHAR(30) NOT NULL DEFAULT 'pending'
                            CHECK (onboarding_status IN ('pending','documentation','training','setup','active','suspended','terminated')),
    onboarded_at            TIMESTAMPTZ,
    suspended_at            TIMESTAMPTZ,
    suspended_reason        TEXT,
    terminated_at           TIMESTAMPTZ,
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_franchises_brand       ON franchises(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_franchises_territory   ON franchises(territory_id);
CREATE INDEX idx_franchises_owner       ON franchises(owner_user_id);
CREATE INDEX idx_franchises_status      ON franchises(onboarding_status) WHERE deleted_at IS NULL;
CREATE INDEX idx_franchises_config_gin  ON franchises USING GIN (config);

ALTER TABLE franchises ENABLE ROW LEVEL SECURITY;
CREATE POLICY franchises_tenant ON franchises
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 06. stores — physical walk-in / pickup retail locations
-- ----------------------------------------------------------------------------
CREATE TABLE stores (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    franchise_id            UUID NOT NULL REFERENCES franchises(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    store_type              VARCHAR(30) NOT NULL DEFAULT 'walkin'
                            CHECK (store_type IN ('walkin','pickup_only','express','hub','collection_point')),
    address_line1           VARCHAR(255) NOT NULL,
    address_line2           VARCHAR(255),
    landmark                VARCHAR(200),
    city                    VARCHAR(100) NOT NULL,
    state                   VARCHAR(100) NOT NULL,
    pincode                 VARCHAR(10) NOT NULL,
    country_code            CHAR(2) NOT NULL DEFAULT 'IN',
    geo_location            GEOGRAPHY(POINT, 4326),
    service_radius_km       NUMERIC(5,2) NOT NULL DEFAULT 5.00,
    contact_phone           VARCHAR(20),
    contact_email           CITEXT,
    toll_free_number        VARCHAR(20),
    whatsapp_number         VARCHAR(20),
    manager_user_id         UUID,
    timezone                VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    daily_pickup_capacity   INTEGER NOT NULL DEFAULT 200,
    daily_delivery_capacity INTEGER NOT NULL DEFAULT 200,
    slot_duration_minutes   INTEGER NOT NULL DEFAULT 120,
    accepts_express         BOOLEAN NOT NULL DEFAULT true,
    accepts_cod             BOOLEAN NOT NULL DEFAULT true,
    accepts_walkin          BOOLEAN NOT NULL DEFAULT true,
    google_place_id         VARCHAR(100),
    rating_average          NUMERIC(3,2),
    rating_count            INTEGER NOT NULL DEFAULT 0,
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','paused','closed','coming_soon')),
    opened_at               TIMESTAMPTZ,
    closed_at               TIMESTAMPTZ,
    closure_reason          TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_stores_brand_franchise ON stores(brand_id, franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_stores_geo             ON stores USING GIST (geo_location);
CREATE INDEX idx_stores_pincode         ON stores(pincode) WHERE deleted_at IS NULL AND status = 'active';
CREATE INDEX idx_stores_city            ON stores(city) WHERE deleted_at IS NULL;
CREATE INDEX idx_stores_status          ON stores(status) WHERE deleted_at IS NULL;

ALTER TABLE stores ENABLE ROW LEVEL SECURITY;
CREATE POLICY stores_tenant ON stores
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 07. warehouses — processing facilities (one warehouse serves N stores)
-- ----------------------------------------------------------------------------
CREATE TABLE warehouses (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    franchise_id            UUID NOT NULL REFERENCES franchises(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    warehouse_type          VARCHAR(30) NOT NULL DEFAULT 'central'
                            CHECK (warehouse_type IN ('central','satellite','express','specialty')),
    address_line1           VARCHAR(255) NOT NULL,
    address_line2           VARCHAR(255),
    city                    VARCHAR(100) NOT NULL,
    state                   VARCHAR(100) NOT NULL,
    pincode                 VARCHAR(10) NOT NULL,
    country_code            CHAR(2) NOT NULL DEFAULT 'IN',
    geo_location            GEOGRAPHY(POINT, 4326),
    contact_phone           VARCHAR(20),
    contact_email           CITEXT,
    manager_user_id         UUID,
    daily_throughput_target INTEGER NOT NULL DEFAULT 1000,
    current_load_count      INTEGER NOT NULL DEFAULT 0,
    has_dry_clean           BOOLEAN NOT NULL DEFAULT true,
    has_steam_iron          BOOLEAN NOT NULL DEFAULT true,
    has_shoe_cleaning       BOOLEAN NOT NULL DEFAULT false,
    has_carpet_cleaning     BOOLEAN NOT NULL DEFAULT false,
    capabilities            TEXT[] NOT NULL DEFAULT '{}',
    operating_hours_config  JSONB NOT NULL DEFAULT '{}'::jsonb,
    timezone                VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','paused','maintenance','closed')),
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_warehouses_franchise   ON warehouses(franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_warehouses_geo         ON warehouses USING GIST (geo_location);
CREATE INDEX idx_warehouses_capability  ON warehouses USING GIN (capabilities);
CREATE INDEX idx_warehouses_status      ON warehouses(status) WHERE deleted_at IS NULL;

ALTER TABLE warehouses ENABLE ROW LEVEL SECURITY;
CREATE POLICY warehouses_tenant ON warehouses
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 08. store_warehouse_mappings — N:M between stores and warehouses
-- ----------------------------------------------------------------------------
CREATE TABLE store_warehouse_mappings (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    store_id                UUID NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    warehouse_id            UUID NOT NULL REFERENCES warehouses(id) ON DELETE CASCADE,
    is_primary              BOOLEAN NOT NULL DEFAULT true,
    service_types           TEXT[] NOT NULL DEFAULT '{}',
    priority                SMALLINT NOT NULL DEFAULT 1,
    cutoff_time             TIME,
    travel_time_minutes     INTEGER,
    distance_km             NUMERIC(6,2),
    is_active               BOOLEAN NOT NULL DEFAULT true,
    effective_from          TIMESTAMPTZ NOT NULL DEFAULT now(),
    effective_to            TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (store_id, warehouse_id, service_types)
);
CREATE INDEX idx_swm_store              ON store_warehouse_mappings(store_id) WHERE is_active = true;
CREATE INDEX idx_swm_warehouse          ON store_warehouse_mappings(warehouse_id) WHERE is_active = true;

-- ----------------------------------------------------------------------------
-- 09. operating_hours — weekly schedule per store/warehouse
-- ----------------------------------------------------------------------------
CREATE TABLE operating_hours (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    scope_type              VARCHAR(20) NOT NULL CHECK (scope_type IN ('store','warehouse')),
    scope_id                UUID NOT NULL,
    day_of_week             SMALLINT NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
    is_closed               BOOLEAN NOT NULL DEFAULT false,
    open_time               TIME,
    close_time              TIME,
    break_start             TIME,
    break_end               TIME,
    notes                   VARCHAR(255),
    effective_from          DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_to            DATE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (scope_type, scope_id, day_of_week, effective_from)

);
CREATE INDEX idx_ophours_scope          ON operating_hours(scope_type, scope_id, day_of_week);

-- ----------------------------------------------------------------------------
-- 10. holidays — closed dates per scope (overrides operating_hours)
-- ----------------------------------------------------------------------------
CREATE TABLE holidays (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    scope_type              VARCHAR(20) NOT NULL CHECK (scope_type IN ('brand','franchise','store','warehouse')),
    scope_id                UUID,
    holiday_date            DATE NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    is_full_day             BOOLEAN NOT NULL DEFAULT true,
    partial_open_from       TIME,
    partial_open_to         TIME,
    accepts_orders          BOOLEAN NOT NULL DEFAULT false,
    is_recurring            BOOLEAN NOT NULL DEFAULT false,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_holidays_scope_date    ON holidays(scope_type, scope_id, holiday_date);
CREATE INDEX idx_holidays_brand_date    ON holidays(brand_id, holiday_date);


-- ============================================================================
