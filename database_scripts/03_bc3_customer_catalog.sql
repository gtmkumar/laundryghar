-- ============================================================================
-- LAUNDRY GHAR — 03 BC-3 Customer & Catalog
-- ============================================================================
-- Wave:           1
-- Bounded ctx:    BC-3 (Customer & Catalog)
-- Source §:       §3 customers + §4 catalog & pricing
-- Tables:         14  (#22–35)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
--   - 02_bc2_identity_access.sql
-- Owning agent:   agent/customer-catalog
-- Purpose:        Mobile-app customers, addresses, devices, DPDP consent + deletion. Service catalog: categories, services, fabrics, item groups/items/variants, versioned price lists with scope, add-ons. Owns everything the mobile app reads before checkout.
-- ============================================================================

-- SECTION 3: CUSTOMERS (5 tables: #22–26)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 22. customers — end-users (mobile app users)
-- ----------------------------------------------------------------------------
CREATE TABLE customers (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    customer_code           VARCHAR(30) NOT NULL,
    phone_e164              VARCHAR(20) NOT NULL,
    email                   CITEXT,
    first_name              VARCHAR(100),
    last_name               VARCHAR(100),
    display_name            VARCHAR(200),
    gender                  VARCHAR(20) CHECK (gender IN ('male','female','other','prefer_not_to_say')),
    date_of_birth           DATE,
    avatar_url              TEXT,
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    timezone                VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    primary_store_id        UUID REFERENCES stores(id),
    referral_code           VARCHAR(20) UNIQUE,
    referred_by_customer_id UUID REFERENCES customers(id),
    lifetime_orders         INTEGER NOT NULL DEFAULT 0,
    lifetime_spend          NUMERIC(14,2) NOT NULL DEFAULT 0,
    avg_order_value         NUMERIC(14,2),
    last_order_at           TIMESTAMPTZ,
    first_order_at          TIMESTAMPTZ,
    loyalty_points_balance  INTEGER NOT NULL DEFAULT 0,
    wallet_balance          NUMERIC(14,2) NOT NULL DEFAULT 0,
    customer_segment        VARCHAR(30),
    risk_flag               VARCHAR(20) DEFAULT 'normal'
                            CHECK (risk_flag IN ('normal','watchlist','blocked','vip')),
    tags                    TEXT[] NOT NULL DEFAULT '{}',
    phone_verified_at       TIMESTAMPTZ,
    email_verified_at       TIMESTAMPTZ,
    onboarding_completed_at TIMESTAMPTZ,
    last_active_at          TIMESTAMPTZ,
    marketing_opt_in        BOOLEAN NOT NULL DEFAULT true,
    sms_opt_in              BOOLEAN NOT NULL DEFAULT true,
    whatsapp_opt_in         BOOLEAN NOT NULL DEFAULT true,
    email_opt_in            BOOLEAN NOT NULL DEFAULT true,
    push_opt_in             BOOLEAN NOT NULL DEFAULT true,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','blocked','deletion_requested','deleted')),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, phone_e164),
    UNIQUE (brand_id, customer_code)

);
CREATE INDEX idx_customers_brand_phone  ON customers(brand_id, phone_e164) WHERE deleted_at IS NULL;
CREATE INDEX idx_customers_email        ON customers(email) WHERE email IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_customers_segment      ON customers(brand_id, customer_segment);
CREATE INDEX idx_customers_lastorder    ON customers(brand_id, last_order_at DESC NULLS LAST);
CREATE INDEX idx_customers_referral     ON customers(referral_code) WHERE referral_code IS NOT NULL;
CREATE INDEX idx_customers_tags         ON customers USING GIN (tags);
CREATE INDEX idx_customers_metadata_gin ON customers USING GIN (metadata);

ALTER TABLE customers ENABLE ROW LEVEL SECURITY;
CREATE POLICY customers_tenant ON customers
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 23. customer_addresses — multi-address with default, geofence
-- ----------------------------------------------------------------------------
CREATE TABLE customer_addresses (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id             UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    label                   VARCHAR(50) NOT NULL DEFAULT 'home'
                            CHECK (label IN ('home','office','other')),
    custom_label            VARCHAR(100),
    recipient_name          VARCHAR(200),
    recipient_phone         VARCHAR(20),
    address_line1           VARCHAR(255) NOT NULL,
    address_line2           VARCHAR(255),
    landmark                VARCHAR(200),
    floor                   VARCHAR(20),
    flat_number             VARCHAR(50),
    building_name           VARCHAR(200),
    society                 VARCHAR(200),
    area                    VARCHAR(200),
    city                    VARCHAR(100) NOT NULL,
    state                   VARCHAR(100) NOT NULL,
    pincode                 VARCHAR(10) NOT NULL,
    country_code            CHAR(2) NOT NULL DEFAULT 'IN',
    geo_location            GEOGRAPHY(POINT, 4326),
    delivery_instructions   TEXT,
    is_default              BOOLEAN NOT NULL DEFAULT false,
    is_verified             BOOLEAN NOT NULL DEFAULT false,
    serviceable_store_id    UUID REFERENCES stores(id),
    last_used_at            TIMESTAMPTZ,
    use_count               INTEGER NOT NULL DEFAULT 0,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_custaddr_customer      ON customer_addresses(customer_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_custaddr_default       ON customer_addresses(customer_id) WHERE is_default = true AND deleted_at IS NULL;
CREATE INDEX idx_custaddr_geo           ON customer_addresses USING GIST (geo_location);
CREATE INDEX idx_custaddr_pincode       ON customer_addresses(pincode);

-- ----------------------------------------------------------------------------
-- 24. customer_devices — FCM/APNs tokens, app version per device
-- ----------------------------------------------------------------------------
CREATE TABLE customer_devices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id             UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    device_id               VARCHAR(255) NOT NULL,
    platform                VARCHAR(20) NOT NULL CHECK (platform IN ('android','ios','web')),
    os_version              VARCHAR(50),
    device_model            VARCHAR(100),
    device_name             VARCHAR(200),
    app_version             VARCHAR(20),
    app_build               VARCHAR(50),
    fcm_token               TEXT,
    apns_token              TEXT,
    push_enabled            BOOLEAN NOT NULL DEFAULT true,
    language                VARCHAR(10),
    timezone                VARCHAR(50),
    last_seen_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    first_seen_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_active               BOOLEAN NOT NULL DEFAULT true,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (customer_id, device_id)

);
CREATE INDEX idx_custdev_customer       ON customer_devices(customer_id) WHERE is_active = true;
CREATE INDEX idx_custdev_fcm            ON customer_devices(fcm_token) WHERE fcm_token IS NOT NULL AND is_active = true;
CREATE INDEX idx_custdev_apns           ON customer_devices(apns_token) WHERE apns_token IS NOT NULL AND is_active = true;

-- ----------------------------------------------------------------------------
-- 25. account_deletion_requests — DPDP/Play Store compliant
-- ----------------------------------------------------------------------------
CREATE TABLE account_deletion_requests (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id             UUID REFERENCES customers(id) ON DELETE SET NULL,
    user_id                 UUID REFERENCES users(id) ON DELETE SET NULL,
    brand_id                UUID NOT NULL,
    request_source          VARCHAR(20) NOT NULL
                            CHECK (request_source IN ('mobile_app','web','support','email','phone')),
    reason                  VARCHAR(50),
    reason_text             TEXT,
    requested_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    grace_period_ends_at    TIMESTAMPTZ NOT NULL,
    cancelled_at            TIMESTAMPTZ,
    cancelled_reason        TEXT,
    soft_deleted_at         TIMESTAMPTZ,
    hard_deleted_at         TIMESTAMPTZ,
    anonymized_at           TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','grace_period','soft_deleted','hard_deleted','cancelled','failed')),
    pending_orders_count    INTEGER NOT NULL DEFAULT 0,
    pending_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    data_export_url         TEXT,
    data_export_expires_at  TIMESTAMPTZ,
    ip_address              INET,
    user_agent              TEXT,
    processed_by            UUID,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    CHECK (customer_id IS NOT NULL OR user_id IS NOT NULL)

);
CREATE INDEX idx_acctdel_customer       ON account_deletion_requests(customer_id);
CREATE INDEX idx_acctdel_status         ON account_deletion_requests(status, grace_period_ends_at);
CREATE INDEX idx_acctdel_brand          ON account_deletion_requests(brand_id, requested_at DESC);

-- ----------------------------------------------------------------------------
-- 26. dpdp_consents — DPDP Act 2023 purpose-bound consent log
-- ----------------------------------------------------------------------------
CREATE TABLE dpdp_consents (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id             UUID REFERENCES customers(id) ON DELETE CASCADE,
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    purpose                 VARCHAR(50) NOT NULL,
    purpose_description     TEXT NOT NULL,
    data_categories         TEXT[] NOT NULL,
    consent_status          VARCHAR(20) NOT NULL
                            CHECK (consent_status IN ('granted','denied','withdrawn','expired')),
    consent_method          VARCHAR(30) NOT NULL
                            CHECK (consent_method IN ('explicit_checkbox','implicit','imported','signed_form','phone_otp')),
    privacy_policy_version  VARCHAR(20) NOT NULL,
    terms_version           VARCHAR(20),
    consent_text_snapshot   TEXT,
    granted_at              TIMESTAMPTZ,
    withdrawn_at            TIMESTAMPTZ,
    expires_at              TIMESTAMPTZ,
    ip_address              INET,
    user_agent              TEXT,
    geo_location            VARCHAR(100),
    evidence_s3_key         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    CHECK (customer_id IS NOT NULL OR user_id IS NOT NULL)

);
CREATE INDEX idx_dpdp_customer_purpose  ON dpdp_consents(customer_id, purpose, created_at DESC);
CREATE INDEX idx_dpdp_user_purpose      ON dpdp_consents(user_id, purpose, created_at DESC);
CREATE INDEX idx_dpdp_active            ON dpdp_consents(customer_id, purpose)
    WHERE consent_status = 'granted' AND withdrawn_at IS NULL;


-- ============================================================================
-- SECTION 4: SERVICE CATALOG & PRICING (9 tables: #27–35)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 27. service_categories — Dry Clean, Laundry, Steam Iron, Shoe, Bag, Carpet, Curtain
-- ----------------------------------------------------------------------------
CREATE TABLE service_categories (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    icon_url                TEXT,
    image_url               TEXT,
    color_hex               CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_visible_mobile       BOOLEAN NOT NULL DEFAULT true,
    is_visible_pos          BOOLEAN NOT NULL DEFAULT true,
    requires_warehouse_cap  TEXT[] NOT NULL DEFAULT '{}',
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled','seasonal')),
    seasonal_from           DATE,
    seasonal_to             DATE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_svccat_brand_visible   ON service_categories(brand_id, display_order)
    WHERE deleted_at IS NULL AND status = 'active';

ALTER TABLE service_categories ENABLE ROW LEVEL SECURITY;
CREATE POLICY svccat_tenant ON service_categories
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 28. services — sub-services under a category
-- ----------------------------------------------------------------------------
CREATE TABLE services (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    category_id             UUID NOT NULL REFERENCES service_categories(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    pricing_model           VARCHAR(30) NOT NULL DEFAULT 'per_item'
                            CHECK (pricing_model IN ('per_item','per_kg','per_sqft','per_pair','per_side','flat')),
    base_tat_hours          INTEGER NOT NULL DEFAULT 48,
    express_tat_hours       INTEGER NOT NULL DEFAULT 24,
    express_multiplier      NUMERIC(4,2) NOT NULL DEFAULT 1.50,
    is_express_available    BOOLEAN NOT NULL DEFAULT true,
    requires_inspection     BOOLEAN NOT NULL DEFAULT true,
    requires_qc             BOOLEAN NOT NULL DEFAULT true,
    icon_url                TEXT,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_services_category      ON services(category_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_services_brand_active  ON services(brand_id) WHERE status = 'active' AND deleted_at IS NULL;

ALTER TABLE services ENABLE ROW LEVEL SECURITY;
CREATE POLICY services_tenant ON services
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 29. fabric_types — Cotton, Silk, Woolen, Synthetic, etc.
-- ----------------------------------------------------------------------------
CREATE TABLE fabric_types (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    care_instructions       TEXT,
    price_multiplier        NUMERIC(4,2) NOT NULL DEFAULT 1.00,
    requires_special_care   BOOLEAN NOT NULL DEFAULT false,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_fabric_brand           ON fabric_types(brand_id) WHERE deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 30. item_groups — MEN, WOMEN, KIDS, SHOES, HOME, ACCESSORIES
-- ----------------------------------------------------------------------------
CREATE TABLE item_groups (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    icon_url                TEXT,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_visible_mobile       BOOLEAN NOT NULL DEFAULT true,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_itemgrp_brand          ON item_groups(brand_id) WHERE deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 31. items — Shirt, Pants, Saree, Sport Shoe, etc.
-- ----------------------------------------------------------------------------
CREATE TABLE items (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    item_group_id           UUID REFERENCES item_groups(id),
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    icon_url                TEXT,
    image_url               TEXT,
    typical_weight_grams    INTEGER,
    requires_per_side_price BOOLEAN NOT NULL DEFAULT false,
    search_tokens           TSVECTOR,
    aliases                 TEXT[] NOT NULL DEFAULT '{}',
    display_order           SMALLINT NOT NULL DEFAULT 100,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled','seasonal')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_items_group            ON items(item_group_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_items_brand_active     ON items(brand_id) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_items_search           ON items USING GIN (search_tokens);
CREATE INDEX idx_items_aliases          ON items USING GIN (aliases);

ALTER TABLE items ENABLE ROW LEVEL SECURITY;
CREATE POLICY items_tenant ON items
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 32. item_variants — Shirt+Cotton vs Shirt+Silk; or Sport Shoe Left vs Right
-- ----------------------------------------------------------------------------
CREATE TABLE item_variants (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    item_id                 UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    fabric_type_id          UUID REFERENCES fabric_types(id),
    code                    VARCHAR(50) NOT NULL,
    variant_name            VARCHAR(100) NOT NULL,
    side                    VARCHAR(10) CHECK (side IN ('left','right','pair','single')),
    size                    VARCHAR(20),
    color                   VARCHAR(50),
    sku                     VARCHAR(50),
    barcode                 VARCHAR(50),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_itemvar_item           ON item_variants(item_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_itemvar_fabric         ON item_variants(fabric_type_id);
CREATE INDEX idx_itemvar_barcode        ON item_variants(barcode) WHERE barcode IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 33. price_lists — versioned, scoped to brand/franchise/store
-- ----------------------------------------------------------------------------
CREATE TABLE price_lists (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    franchise_id            UUID REFERENCES franchises(id),
    store_id                UUID REFERENCES stores(id),
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    scope_type              VARCHAR(20) NOT NULL
                            CHECK (scope_type IN ('brand','franchise','store')),
    version_number          INTEGER NOT NULL DEFAULT 1,
    parent_price_list_id    UUID REFERENCES price_lists(id),
    effective_from          TIMESTAMPTZ NOT NULL,
    effective_to            TIMESTAMPTZ,
    is_default              BOOLEAN NOT NULL DEFAULT false,
    is_published            BOOLEAN NOT NULL DEFAULT false,
    published_at            TIMESTAMPTZ,
    published_by            UUID,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','published','archived')),
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code, version_number)
);
CREATE INDEX idx_pricelist_brand_active ON price_lists(brand_id) WHERE status = 'published' AND deleted_at IS NULL;
CREATE INDEX idx_pricelist_store_active ON price_lists(store_id) WHERE status = 'published' AND deleted_at IS NULL;
CREATE INDEX idx_pricelist_franchise    ON price_lists(franchise_id) WHERE status = 'published' AND deleted_at IS NULL;
CREATE INDEX idx_pricelist_effective    ON price_lists(effective_from, effective_to);

ALTER TABLE price_lists ENABLE ROW LEVEL SECURITY;
CREATE POLICY pricelist_tenant ON price_lists
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 34. price_list_items — item × service × price entry
-- ----------------------------------------------------------------------------
CREATE TABLE price_list_items (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    price_list_id           UUID NOT NULL REFERENCES price_lists(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    service_id              UUID NOT NULL REFERENCES services(id) ON DELETE RESTRICT,
    item_id                 UUID NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    item_variant_id         UUID REFERENCES item_variants(id) ON DELETE RESTRICT,
    fabric_type_id          UUID REFERENCES fabric_types(id),
    item_group_id           UUID REFERENCES item_groups(id),
    base_price              NUMERIC(14,2) NOT NULL CHECK (base_price >= 0),
    express_price           NUMERIC(14,2),
    minimum_quantity        INTEGER NOT NULL DEFAULT 1,
    tax_rate_percent        NUMERIC(5,2) NOT NULL DEFAULT 0,
    is_taxable              BOOLEAN NOT NULL DEFAULT true,
    display_label           VARCHAR(200),
    notes                   TEXT,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (price_list_id, service_id, item_id, item_variant_id, fabric_type_id)

);
CREATE INDEX idx_pli_lookup             ON price_list_items(price_list_id, service_id, item_id) WHERE is_active = true;
CREATE INDEX idx_pli_item               ON price_list_items(item_id);
CREATE INDEX idx_pli_service            ON price_list_items(service_id);
CREATE INDEX idx_pli_group              ON price_list_items(item_group_id);

-- ----------------------------------------------------------------------------
-- 35. add_ons — stain removal, premium wash (surcharges)
-- ----------------------------------------------------------------------------
CREATE TABLE add_ons (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    pricing_type            VARCHAR(20) NOT NULL
                            CHECK (pricing_type IN ('flat','percent','per_item','per_kg')),
    price_value             NUMERIC(14,2) NOT NULL,
    min_charge              NUMERIC(14,2),
    max_charge              NUMERIC(14,2),
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    applicable_categories   UUID[] NOT NULL DEFAULT '{}',
    is_taxable              BOOLEAN NOT NULL DEFAULT true,
    tax_rate_percent        NUMERIC(5,2) NOT NULL DEFAULT 0,
    requires_approval       BOOLEAN NOT NULL DEFAULT false,
    icon_url                TEXT,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','disabled')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_addons_brand           ON add_ons(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_addons_services_gin    ON add_ons USING GIN (applicable_services);


-- ============================================================================
