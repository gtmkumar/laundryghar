-- ============================================================================
-- LAUNDRY GHAR — 04 BC-4 Order Lifecycle
-- ============================================================================
-- Wave:           1
-- Bounded ctx:    BC-4 (Order Lifecycle)
-- Source §:       §5 orders + §6 garments + §7 warehouse
-- Tables:         20  (#36–55)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
--   - 02_bc2_identity_access.sql
--   - 03_bc3_customer_catalog.sql
-- Owning agent:   agent/order-lifecycle
-- Purpose:        The transactional spine: orders (PARTITIONED monthly) → order items → pickup_requests → delivery slots → physical garments with QR tags → inspections + photos → warehouse batches → process_logs (PARTITIONED) → QC → stock reconciliation. Largest BC by design — do NOT split; the FK chain is one invariant.
-- ============================================================================

-- SECTION 5: ORDERS & PICKUPS (9 tables: #36–44)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 36. orders — order header (PARTITIONED monthly by created_at)
-- ----------------------------------------------------------------------------
CREATE TABLE orders (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    order_number            VARCHAR(40) NOT NULL,
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID NOT NULL,
    warehouse_id            UUID,
    customer_id             UUID NOT NULL,
    pickup_address_id       UUID,
    delivery_address_id     UUID,
    pickup_slot_id          UUID,
    delivery_slot_id        UUID,
    pickup_rider_id         UUID,
    delivery_rider_id       UUID,
    channel                 VARCHAR(20) NOT NULL DEFAULT 'walkin'
                            CHECK (channel IN ('walkin','app','whatsapp','call','web','pos')),
    order_type              VARCHAR(20) NOT NULL DEFAULT 'standard'
                            CHECK (order_type IN ('standard','express','rewash','complaint','exchange')),
    is_express              BOOLEAN NOT NULL DEFAULT false,
    requires_pickup         BOOLEAN NOT NULL DEFAULT true,
    requires_delivery       BOOLEAN NOT NULL DEFAULT true,
    pickup_otp              VARCHAR(10),
    delivery_otp            VARCHAR(10),

    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    addon_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    express_surcharge       NUMERIC(14,2) NOT NULL DEFAULT 0,
    pickup_charge           NUMERIC(14,2) NOT NULL DEFAULT 0,
    delivery_charge         NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total          NUMERIC(14,2) NOT NULL DEFAULT 0,
    coupon_discount         NUMERIC(14,2) NOT NULL DEFAULT 0,
    loyalty_discount        NUMERIC(14,2) NOT NULL DEFAULT 0,
    package_discount        NUMERIC(14,2) NOT NULL DEFAULT 0,
    taxable_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    round_off               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    refunded_amount         NUMERIC(14,2) NOT NULL DEFAULT 0,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',

    coupon_id               UUID,
    coupon_code             VARCHAR(50),
    package_id              UUID,
    customer_package_id     UUID,
    loyalty_points_used     INTEGER NOT NULL DEFAULT 0,
    loyalty_points_earned   INTEGER NOT NULL DEFAULT 0,

    total_items             INTEGER NOT NULL DEFAULT 0,
    total_garments          INTEGER NOT NULL DEFAULT 0,
    total_weight_grams      INTEGER,

    status                  VARCHAR(30) NOT NULL DEFAULT 'placed'
                            CHECK (status IN ('placed','pickup_scheduled','pickup_assigned','pickup_in_progress',
                                              'picked_up','received','sorting','in_process','qc','ready',
                                              'delivery_scheduled','delivery_assigned','out_for_delivery',
                                              'delivered','cancelled','returned','rewash','disputed','closed')),
    sub_status              VARCHAR(50),
    payment_status          VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (payment_status IN ('pending','partial','paid','refunded','partial_refund','failed')),

    placed_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    pickup_scheduled_at     TIMESTAMPTZ,
    picked_up_at            TIMESTAMPTZ,
    received_at             TIMESTAMPTZ,
    qc_completed_at         TIMESTAMPTZ,
    ready_at                TIMESTAMPTZ,
    out_for_delivery_at     TIMESTAMPTZ,
    delivered_at            TIMESTAMPTZ,
    cancelled_at            TIMESTAMPTZ,
    cancellation_reason     TEXT,
    cancelled_by_type       VARCHAR(20),
    cancelled_by_id         UUID,
    promised_delivery_at    TIMESTAMPTZ,

    invoice_number          VARCHAR(50),
    invoice_generated_at    TIMESTAMPTZ,
    invoice_s3_key          TEXT,

    notes_customer          TEXT,
    notes_internal          TEXT,
    rating                  SMALLINT CHECK (rating BETWEEN 1 AND 5),
    rating_comment          TEXT,
    rated_at                TIMESTAMPTZ,

    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    source_ip               INET,
    source_user_agent       TEXT,

    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    PRIMARY KEY (id, created_at),
    UNIQUE (order_number, created_at)
) PARTITION BY RANGE (created_at);

CREATE INDEX idx_orders_brand_store_status  ON orders(brand_id, store_id, status, created_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_orders_customer             ON orders(customer_id, created_at DESC);
CREATE INDEX idx_orders_franchise            ON orders(franchise_id, created_at DESC);
CREATE INDEX idx_orders_warehouse            ON orders(warehouse_id, status, created_at DESC) WHERE warehouse_id IS NOT NULL;
CREATE INDEX idx_orders_status_open          ON orders(status, created_at) WHERE status NOT IN ('delivered','cancelled','closed');
CREATE INDEX idx_orders_payment_pending      ON orders(payment_status, created_at) WHERE payment_status IN ('pending','partial');
CREATE INDEX idx_orders_pickup_rider         ON orders(pickup_rider_id, status) WHERE pickup_rider_id IS NOT NULL;
CREATE INDEX idx_orders_delivery_rider       ON orders(delivery_rider_id, status) WHERE delivery_rider_id IS NOT NULL;
CREATE INDEX idx_orders_metadata_gin         ON orders USING GIN (metadata);

-- ----------------------------------------------------------------------------
-- 37. order_items — line items per order
-- ----------------------------------------------------------------------------
CREATE TABLE order_items (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    line_number             SMALLINT NOT NULL,
    service_id              UUID NOT NULL REFERENCES services(id),
    item_id                 UUID NOT NULL REFERENCES items(id),
    item_variant_id         UUID REFERENCES item_variants(id),
    fabric_type_id          UUID REFERENCES fabric_types(id),
    price_list_item_id      UUID,
    item_name_snapshot      VARCHAR(200) NOT NULL,
    service_name_snapshot   VARCHAR(200) NOT NULL,
    unit_price              NUMERIC(14,2) NOT NULL,
    quantity                NUMERIC(10,2) NOT NULL CHECK (quantity > 0),
    unit_of_measure         VARCHAR(10) NOT NULL DEFAULT 'piece'
                            CHECK (unit_of_measure IN ('piece','kg','pair','sqft','side')),
    line_subtotal           NUMERIC(14,2) NOT NULL,
    line_discount           NUMERIC(14,2) NOT NULL DEFAULT 0,
    line_addons_total       NUMERIC(14,2) NOT NULL DEFAULT 0,
    line_tax                NUMERIC(14,2) NOT NULL DEFAULT 0,
    line_total              NUMERIC(14,2) NOT NULL,
    is_express              BOOLEAN NOT NULL DEFAULT false,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_orderitems_order       ON order_items(order_id, order_created_at);
CREATE INDEX idx_orderitems_item        ON order_items(item_id);
CREATE INDEX idx_orderitems_service     ON order_items(service_id);

-- ----------------------------------------------------------------------------
-- 38. order_addons — add-ons (stain removal etc) per line
-- ----------------------------------------------------------------------------
CREATE TABLE order_addons (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    order_item_id           UUID REFERENCES order_items(id) ON DELETE CASCADE,
    addon_id                UUID NOT NULL REFERENCES add_ons(id),
    addon_name_snapshot     VARCHAR(200) NOT NULL,
    pricing_type            VARCHAR(20) NOT NULL,
    unit_price              NUMERIC(14,2) NOT NULL,
    quantity                NUMERIC(10,2) NOT NULL DEFAULT 1,
    total_charge            NUMERIC(14,2) NOT NULL,
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_orderaddons_order      ON order_addons(order_id, order_created_at);
CREATE INDEX idx_orderaddons_item       ON order_addons(order_item_id);

-- ----------------------------------------------------------------------------
-- 39. order_status_history — full audit of status transitions
-- ----------------------------------------------------------------------------
CREATE TABLE order_status_history (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    brand_id                UUID NOT NULL,
    from_status             VARCHAR(30),
    to_status               VARCHAR(30) NOT NULL,
    from_sub_status         VARCHAR(50),
    to_sub_status           VARCHAR(50),
    changed_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    changed_by_type         VARCHAR(20) NOT NULL DEFAULT 'user',
    changed_by_id           UUID,
    changed_by_name         VARCHAR(200),
    reason                  VARCHAR(200),
    notes                   TEXT,
    customer_notified       BOOLEAN NOT NULL DEFAULT false,
    notification_channels   TEXT[],
    location                GEOGRAPHY(POINT, 4326),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_orderstathist_order    ON order_status_history(order_id, changed_at DESC);
CREATE INDEX idx_orderstathist_to_stat  ON order_status_history(to_status, changed_at DESC);

-- ----------------------------------------------------------------------------
-- 40. order_notes — internal + customer notes thread
-- ----------------------------------------------------------------------------
CREATE TABLE order_notes (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    brand_id                UUID NOT NULL,
    note_type               VARCHAR(20) NOT NULL DEFAULT 'internal'
                            CHECK (note_type IN ('internal','customer_facing','complaint','resolution','flag')),
    visibility              VARCHAR(20) NOT NULL DEFAULT 'staff'
                            CHECK (visibility IN ('staff','customer','platform')),
    author_type             VARCHAR(20) NOT NULL DEFAULT 'user',
    author_id               UUID,
    author_name             VARCHAR(200),
    note_text               TEXT NOT NULL,
    attachments             JSONB NOT NULL DEFAULT '[]'::jsonb,
    is_pinned               BOOLEAN NOT NULL DEFAULT false,
    is_resolved             BOOLEAN NOT NULL DEFAULT false,
    resolved_at             TIMESTAMPTZ,
    resolved_by             UUID,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID
);
CREATE INDEX idx_ordernotes_order       ON order_notes(order_id, created_at DESC) WHERE deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 41. pickup_requests — customer-initiated requests (pre-order)
-- ----------------------------------------------------------------------------
CREATE TABLE pickup_requests (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_number          VARCHAR(40) UNIQUE NOT NULL,
    brand_id                UUID NOT NULL,
    franchise_id            UUID,
    store_id                UUID,
    customer_id             UUID NOT NULL REFERENCES customers(id),
    address_id              UUID NOT NULL REFERENCES customer_addresses(id),
    pickup_slot_id          UUID,
    pickup_date             DATE NOT NULL,
    pickup_window_start     TIME NOT NULL,
    pickup_window_end       TIME NOT NULL,
    is_express              BOOLEAN NOT NULL DEFAULT false,
    estimated_items         INTEGER,
    estimated_amount        NUMERIC(14,2),
    services_requested      UUID[] NOT NULL DEFAULT '{}',
    customer_notes          TEXT,
    converted_order_id      UUID,
    converted_order_created_at TIMESTAMPTZ,
    status                  VARCHAR(30) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','assigned','rider_dispatched','arrived',
                                              'completed','converted','cancelled','no_response','rescheduled')),
    cancellation_reason     TEXT,
    cancelled_by_type       VARCHAR(20),
    cancelled_by_id         UUID,
    rescheduled_from_id     UUID REFERENCES pickup_requests(id),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_pickupreq_customer     ON pickup_requests(customer_id, created_at DESC);
CREATE INDEX idx_pickupreq_store_date   ON pickup_requests(store_id, pickup_date, status);
CREATE INDEX idx_pickupreq_slot         ON pickup_requests(pickup_slot_id);
CREATE INDEX idx_pickupreq_status       ON pickup_requests(status, pickup_date) WHERE status IN ('pending','assigned','rider_dispatched');

-- ----------------------------------------------------------------------------
-- 42. delivery_assignments — rider × order × leg (pickup or delivery)
-- ----------------------------------------------------------------------------
CREATE TABLE delivery_assignments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    rider_id                UUID NOT NULL,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    pickup_request_id       UUID,
    leg_type                VARCHAR(20) NOT NULL CHECK (leg_type IN ('pickup','delivery','return')),
    sequence_number         SMALLINT,
    assigned_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    assigned_by             UUID,
    accepted_at             TIMESTAMPTZ,
    started_at              TIMESTAMPTZ,
    arrived_at              TIMESTAMPTZ,
    completed_at            TIMESTAMPTZ,
    cancelled_at            TIMESTAMPTZ,
    cancellation_reason     TEXT,
    address_snapshot        JSONB NOT NULL,
    geo_location            GEOGRAPHY(POINT, 4326),
    distance_km             NUMERIC(6,2),
    duration_minutes        INTEGER,
    otp_verified            BOOLEAN NOT NULL DEFAULT false,
    otp_attempted_at        TIMESTAMPTZ,
    signature_s3_key        TEXT,
    proof_photo_s3_key      TEXT,
    customer_signature      TEXT,
    status                  VARCHAR(30) NOT NULL DEFAULT 'assigned'
                            CHECK (status IN ('assigned','accepted','rejected','started','arrived',
                                              'completed','cancelled','failed','rescheduled')),
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_delivassign_rider      ON delivery_assignments(rider_id, status, assigned_at DESC);
CREATE INDEX idx_delivassign_order      ON delivery_assignments(order_id);
CREATE INDEX idx_delivassign_pickup     ON delivery_assignments(pickup_request_id);
CREATE INDEX idx_delivassign_store_open ON delivery_assignments(store_id, status) WHERE status IN ('assigned','accepted','started','arrived');

-- ----------------------------------------------------------------------------
-- 43. delivery_slots — configurable time slots per store per day
-- ----------------------------------------------------------------------------
CREATE TABLE delivery_slots (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    slot_date               DATE NOT NULL,
    slot_start              TIME NOT NULL,
    slot_end                TIME NOT NULL,
    slot_type               VARCHAR(20) NOT NULL CHECK (slot_type IN ('pickup','delivery')),
    capacity                INTEGER NOT NULL DEFAULT 20 CHECK (capacity >= 0),
    booked_count            INTEGER NOT NULL DEFAULT 0,
    is_express              BOOLEAN NOT NULL DEFAULT false,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    cutoff_at               TIMESTAMPTZ,
    notes                   VARCHAR(255),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    CHECK (booked_count >= 0 AND booked_count <= capacity),
    UNIQUE (store_id, slot_date, slot_start, slot_type)

);
CREATE INDEX idx_slots_lookup           ON delivery_slots(store_id, slot_date, slot_type) WHERE is_active = true;
CREATE INDEX idx_slots_available        ON delivery_slots(store_id, slot_date) WHERE is_active = true AND booked_count < capacity;

-- ----------------------------------------------------------------------------
-- 44. delivery_slot_bookings — slot capacity audit (one per booking event)
-- ----------------------------------------------------------------------------
CREATE TABLE delivery_slot_bookings (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    slot_id                 UUID NOT NULL REFERENCES delivery_slots(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    pickup_request_id       UUID,
    customer_id             UUID NOT NULL,
    booking_type            VARCHAR(20) NOT NULL CHECK (booking_type IN ('pickup','delivery')),
    booked_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    cancelled_at            TIMESTAMPTZ,
    cancelled_reason        TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','cancelled','completed','no_show')),
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_slotbook_slot          ON delivery_slot_bookings(slot_id) WHERE status = 'active';
CREATE INDEX idx_slotbook_customer      ON delivery_slot_bookings(customer_id, booked_at DESC);


-- ============================================================================
-- SECTION 6: GARMENTS & TRACKING (5 tables: #45–49)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 45. garments — physical garment instance with printed tag
-- ----------------------------------------------------------------------------
CREATE TABLE garments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID NOT NULL,
    warehouse_id            UUID,
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    order_item_id           UUID NOT NULL REFERENCES order_items(id) ON DELETE CASCADE,
    customer_id             UUID NOT NULL,
    tag_code                VARCHAR(50) NOT NULL UNIQUE,
    secondary_tag_code      VARCHAR(50),
    item_id                 UUID REFERENCES items(id),
    item_variant_id         UUID REFERENCES item_variants(id),
    item_group_id           UUID REFERENCES item_groups(id),
    fabric_type_id          UUID REFERENCES fabric_types(id),
    color                   VARCHAR(50),
    brand_name              VARCHAR(100),
    size                    VARCHAR(20),
    weight_grams            INTEGER,
    has_ornaments           BOOLEAN NOT NULL DEFAULT false,
    has_lining              BOOLEAN NOT NULL DEFAULT false,
    is_designer_wear        BOOLEAN NOT NULL DEFAULT false,
    declared_value          NUMERIC(14,2),
    current_stage           VARCHAR(30) NOT NULL DEFAULT 'received'
                            CHECK (current_stage IN ('pickup_pending','picked_up','received','sorting',
                                                     'washing','drying','ironing','qc','packing','dispatched',
                                                     'delivered','returned','rewash','lost','damaged')),
    current_location_type   VARCHAR(20) CHECK (current_location_type IN ('store','warehouse','rider','customer')),
    current_location_id     UUID,
    current_batch_id        UUID,
    last_scanned_at         TIMESTAMPTZ,
    last_scanned_by         UUID,
    expected_completion_at  TIMESTAMPTZ,
    actual_completion_at    TIMESTAMPTZ,
    rewash_count            SMALLINT NOT NULL DEFAULT 0,
    notes                   TEXT,
    care_instructions       TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                 INTEGER NOT NULL DEFAULT 1,
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_garments_order             ON garments(order_id, order_created_at);
CREATE INDEX idx_garments_tag               ON garments(tag_code);
CREATE INDEX idx_garments_customer          ON garments(customer_id, created_at DESC);
CREATE INDEX idx_garments_stage_store       ON garments(current_stage, store_id);
CREATE INDEX idx_garments_warehouse_stage   ON garments(warehouse_id, current_stage) WHERE warehouse_id IS NOT NULL;
CREATE INDEX idx_garments_batch             ON garments(current_batch_id) WHERE current_batch_id IS NOT NULL;
CREATE INDEX idx_garments_lost              ON garments(brand_id, current_stage) WHERE current_stage IN ('lost','damaged');

ALTER TABLE garments ENABLE ROW LEVEL SECURITY;
CREATE POLICY garments_tenant ON garments
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 46. garment_tags — printed barcode/QR registry (pre-printed pool)
-- ----------------------------------------------------------------------------
CREATE TABLE garment_tags (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    store_id                UUID,
    tag_code                VARCHAR(50) NOT NULL UNIQUE,
    tag_format              VARCHAR(20) NOT NULL DEFAULT 'qr'
                            CHECK (tag_format IN ('qr','barcode_128','barcode_39','rfid')),
    batch_number            VARCHAR(50),
    printed_at              TIMESTAMPTZ,
    printed_by              UUID,
    printer_id              VARCHAR(100),
    assigned_to_garment_id  UUID REFERENCES garments(id),
    assigned_at             TIMESTAMPTZ,
    assigned_by             UUID,
    is_damaged              BOOLEAN NOT NULL DEFAULT false,
    is_reprinted            BOOLEAN NOT NULL DEFAULT false,
    reprint_count           SMALLINT NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'available'
                            CHECK (status IN ('available','assigned','damaged','retired','reprinted')),
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_garmtags_store_status  ON garment_tags(store_id, status) WHERE status = 'available';
CREATE INDEX idx_garmtags_garment       ON garment_tags(assigned_to_garment_id) WHERE assigned_to_garment_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 47. garment_inspections — pickup/QC inspection sessions
-- ----------------------------------------------------------------------------
CREATE TABLE garment_inspections (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    garment_id              UUID NOT NULL REFERENCES garments(id) ON DELETE CASCADE,
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    inspected_by_user_id    UUID,
    inspected_by_type       VARCHAR(20) NOT NULL DEFAULT 'staff'
                            CHECK (inspected_by_type IN ('rider','store_staff','warehouse_staff','qc_staff')),
    inspection_type         VARCHAR(20) NOT NULL
                            CHECK (inspection_type IN ('pickup','intake','pre_wash','post_wash','qc','packing','delivery')),
    inspected_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    location_type           VARCHAR(20) CHECK (location_type IN ('customer_doorstep','store','warehouse')),
    location_id             UUID,
    geo_location            GEOGRAPHY(POINT, 4326),
    conditions              JSONB NOT NULL DEFAULT '[]'::jsonb,
    overall_condition       VARCHAR(20) CHECK (overall_condition IN ('excellent','good','fair','poor','damaged')),
    issues_count            SMALLINT NOT NULL DEFAULT 0,
    notes                   TEXT,
    customer_acknowledged   BOOLEAN NOT NULL DEFAULT false,
    customer_acknowledged_at TIMESTAMPTZ,
    customer_signature_s3_key TEXT,
    customer_otp_verified   BOOLEAN NOT NULL DEFAULT false,
    qc_result               VARCHAR(20) CHECK (qc_result IN ('pass','fail','rewash','manager_review')),
    qc_failure_reason       TEXT,
    rewash_count            SMALLINT NOT NULL DEFAULT 0,
    requires_special_care   BOOLEAN NOT NULL DEFAULT false,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_inspect_garment        ON garment_inspections(garment_id, inspected_at DESC);
CREATE INDEX idx_inspect_order          ON garment_inspections(order_id, inspection_type);
CREATE INDEX idx_inspect_conditions_gin ON garment_inspections USING GIN (conditions);
CREATE INDEX idx_inspect_qc_fail        ON garment_inspections(brand_id, qc_result) WHERE qc_result IN ('fail','rewash');

-- ----------------------------------------------------------------------------
-- 48. garment_inspection_photos — photo evidence with annotations
-- ----------------------------------------------------------------------------
CREATE TABLE garment_inspection_photos (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    inspection_id           UUID NOT NULL REFERENCES garment_inspections(id) ON DELETE CASCADE,
    garment_id              UUID NOT NULL,
    brand_id                UUID NOT NULL,
    s3_key                  TEXT NOT NULL,
    thumbnail_s3_key        TEXT,
    cdn_url                 TEXT,
    view                    VARCHAR(20) NOT NULL
                            CHECK (view IN ('front','back','left','right','top','bottom','closeup','damage','tag','overall')),
    annotations             JSONB NOT NULL DEFAULT '[]'::jsonb,
    width_px                INTEGER,
    height_px               INTEGER,
    bytes                   INTEGER,
    mime_type               VARCHAR(50) NOT NULL DEFAULT 'image/jpeg',
    is_compressed           BOOLEAN NOT NULL DEFAULT true,
    has_exif                BOOLEAN NOT NULL DEFAULT false,
    exif_data               JSONB,
    captured_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    captured_by             UUID,
    device_id               VARCHAR(255),
    is_primary              BOOLEAN NOT NULL DEFAULT false,
    expires_at              TIMESTAMPTZ,
    deleted_at              TIMESTAMPTZ,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_inspectphoto_inspect   ON garment_inspection_photos(inspection_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_inspectphoto_garment   ON garment_inspection_photos(garment_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_inspectphoto_expires   ON garment_inspection_photos(expires_at) WHERE expires_at IS NOT NULL AND deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 49. garment_conditions — lookup (stain, tear, missing button, fading, etc.)
-- ----------------------------------------------------------------------------
CREATE TABLE garment_conditions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    category                VARCHAR(30) NOT NULL
                            CHECK (category IN ('stain','damage','wear','missing_part','dimensional','color','other')),
    severity_levels         TEXT[] NOT NULL DEFAULT ARRAY['minor','moderate','severe'],
    requires_disclaimer     BOOLEAN NOT NULL DEFAULT true,
    disclaimer_text         TEXT,
    icon_url                TEXT,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_garmcond_brand         ON garment_conditions(brand_id) WHERE is_active = true;


-- ============================================================================
-- SECTION 7: WAREHOUSE OPERATIONS (6 tables: #50–55)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 50. warehouse_batches — group of garments processed together
-- ----------------------------------------------------------------------------
CREATE TABLE warehouse_batches (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    warehouse_id            UUID NOT NULL REFERENCES warehouses(id),
    batch_number            VARCHAR(50) NOT NULL UNIQUE,
    batch_type              VARCHAR(30) NOT NULL
                            CHECK (batch_type IN ('wash_white','wash_color','wash_dark','dry_clean',
                                                  'steam_iron','shoe_clean','specialty','rewash')),
    service_id              UUID REFERENCES services(id),
    machine_id              VARCHAR(50),
    cycle_program           VARCHAR(50),
    expected_garment_count  INTEGER NOT NULL DEFAULT 0,
    actual_garment_count    INTEGER NOT NULL DEFAULT 0,
    total_weight_grams      INTEGER,
    started_at              TIMESTAMPTZ,
    started_by              UUID,
    completed_at            TIMESTAMPTZ,
    completed_by            UUID,
    duration_minutes        INTEGER,
    chemicals_used          JSONB NOT NULL DEFAULT '[]'::jsonb,
    temperature_celsius     NUMERIC(5,2),
    notes                   TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'created'
                            CHECK (status IN ('created','loading','running','paused','completed','failed','aborted')),
    failure_reason          TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_whbatches_wh_status    ON warehouse_batches(warehouse_id, status, created_at DESC);
CREATE INDEX idx_whbatches_type         ON warehouse_batches(batch_type, started_at DESC);

-- ----------------------------------------------------------------------------
-- 51. warehouse_processes — lookup (sort, wash, dry, iron, pack, etc.)
-- ----------------------------------------------------------------------------
CREATE TABLE warehouse_processes (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    process_category        VARCHAR(30) NOT NULL
                            CHECK (process_category IN ('receiving','sorting','pre_treatment','washing',
                                                        'drying','ironing','quality_check','packing','dispatch')),
    sequence_order          SMALLINT NOT NULL,
    expected_duration_min   INTEGER,
    requires_machine        BOOLEAN NOT NULL DEFAULT false,
    requires_supervisor     BOOLEAN NOT NULL DEFAULT false,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_whproc_brand_seq       ON warehouse_processes(brand_id, sequence_order) WHERE is_active = true;

-- ----------------------------------------------------------------------------
-- 52. process_logs — every scan/transition (PARTITIONED monthly)
-- ----------------------------------------------------------------------------
CREATE TABLE process_logs (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    brand_id                UUID NOT NULL,
    warehouse_id            UUID NOT NULL,
    batch_id                UUID,
    garment_id              UUID NOT NULL,
    tag_code                VARCHAR(50) NOT NULL,
    process_id              UUID,
    process_code            VARCHAR(50) NOT NULL,
    action                  VARCHAR(30) NOT NULL
                            CHECK (action IN ('scan_in','scan_out','start','complete','transfer',
                                              'hold','release','flag','rewash')),
    from_stage              VARCHAR(30),
    to_stage                VARCHAR(30),
    performed_by_user_id    UUID,
    performed_by_name       VARCHAR(200),
    duration_seconds        INTEGER,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    PRIMARY KEY (id, occurred_at)

) PARTITION BY RANGE (occurred_at);

CREATE INDEX idx_proclogs_garment       ON process_logs(garment_id, occurred_at DESC);
CREATE INDEX idx_proclogs_batch         ON process_logs(batch_id, occurred_at DESC) WHERE batch_id IS NOT NULL;
CREATE INDEX idx_proclogs_warehouse_day ON process_logs(warehouse_id, occurred_at DESC);

-- ----------------------------------------------------------------------------
-- 53. quality_checks — pre/post photos, pass/fail/rewash
-- ----------------------------------------------------------------------------
CREATE TABLE quality_checks (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    warehouse_id            UUID NOT NULL,
    garment_id              UUID NOT NULL REFERENCES garments(id) ON DELETE CASCADE,
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    batch_id                UUID,
    qc_round                SMALLINT NOT NULL DEFAULT 1,
    inspector_user_id       UUID NOT NULL,
    inspected_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    result                  VARCHAR(20) NOT NULL
                            CHECK (result IN ('pass','fail','rewash','escalate','accept_with_note')),
    issues                  JSONB NOT NULL DEFAULT '[]'::jsonb,
    pre_wash_inspection_id  UUID REFERENCES garment_inspections(id),
    post_wash_inspection_id UUID REFERENCES garment_inspections(id),
    comparison_notes        TEXT,
    requires_rewash         BOOLEAN NOT NULL DEFAULT false,
    rewash_priority         VARCHAR(20) CHECK (rewash_priority IN ('normal','high','urgent')),
    supervisor_approval     BOOLEAN NOT NULL DEFAULT false,
    supervisor_user_id      UUID,
    supervisor_approved_at  TIMESTAMPTZ,
    customer_communicated   BOOLEAN NOT NULL DEFAULT false,
    customer_communicated_at TIMESTAMPTZ,
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_qc_garment             ON quality_checks(garment_id, qc_round);
CREATE INDEX idx_qc_warehouse_date      ON quality_checks(warehouse_id, inspected_at DESC);
CREATE INDEX idx_qc_failed              ON quality_checks(brand_id, result) WHERE result IN ('fail','rewash','escalate');

-- ----------------------------------------------------------------------------
-- 54. stock_reconciliations — daily count session
-- ----------------------------------------------------------------------------
CREATE TABLE stock_reconciliations (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    warehouse_id            UUID,
    store_id                UUID,
    recon_date              DATE NOT NULL,
    recon_type              VARCHAR(20) NOT NULL DEFAULT 'daily'
                            CHECK (recon_type IN ('daily','weekly','monthly','adhoc','dispute')),
    started_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    started_by              UUID NOT NULL,
    completed_at            TIMESTAMPTZ,
    completed_by            UUID,
    expected_count          INTEGER NOT NULL DEFAULT 0,
    scanned_count           INTEGER NOT NULL DEFAULT 0,
    matched_count           INTEGER NOT NULL DEFAULT 0,
    missing_count           INTEGER NOT NULL DEFAULT 0,
    unexpected_count        INTEGER NOT NULL DEFAULT 0,
    damaged_count           INTEGER NOT NULL DEFAULT 0,
    resolved_missing_count  INTEGER NOT NULL DEFAULT 0,
    summary                 JSONB NOT NULL DEFAULT '{}'::jsonb,
    notes                   TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'in_progress'
                            CHECK (status IN ('in_progress','completed','reconciled','disputed','closed')),
    approved_at             TIMESTAMPTZ,
    approved_by             UUID,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    CHECK (warehouse_id IS NOT NULL OR store_id IS NOT NULL)

);
CREATE INDEX idx_stockrecon_wh_date     ON stock_reconciliations(warehouse_id, recon_date DESC);
CREATE INDEX idx_stockrecon_store_date  ON stock_reconciliations(store_id, recon_date DESC);
CREATE INDEX idx_stockrecon_status      ON stock_reconciliations(brand_id, status) WHERE status IN ('in_progress','disputed');

-- ----------------------------------------------------------------------------
-- 55. stock_reconciliation_items — per-garment match/missing
-- ----------------------------------------------------------------------------
CREATE TABLE stock_reconciliation_items (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reconciliation_id       UUID NOT NULL REFERENCES stock_reconciliations(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    garment_id              UUID REFERENCES garments(id),
    tag_code                VARCHAR(50) NOT NULL,
    expected_stage          VARCHAR(30),
    expected_location_type  VARCHAR(20),
    expected_location_id    UUID,
    found_stage             VARCHAR(30),
    found_location_type     VARCHAR(20),
    found_location_id       UUID,
    status                  VARCHAR(20) NOT NULL
                            CHECK (status IN ('matched','missing','unexpected','damaged','resolved','escalated')),
    last_known_holder_type  VARCHAR(20),
    last_known_holder_id    UUID,
    last_scanned_at         TIMESTAMPTZ,
    resolution_action       VARCHAR(30),
    resolution_notes        TEXT,
    resolved_at             TIMESTAMPTZ,
    resolved_by             UUID,
    flagged_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_stockreconitm_recon    ON stock_reconciliation_items(reconciliation_id, status);
CREATE INDEX idx_stockreconitm_garment  ON stock_reconciliation_items(garment_id);
CREATE INDEX idx_stockreconitm_missing  ON stock_reconciliation_items(brand_id, status) WHERE status IN ('missing','unexpected');

-- ============================================================================
-- Forward-reference FKs (post-creation constraints)
-- ----------------------------------------------------------------------------
-- The following columns reference tables defined later in this same file.
-- They are added here so the parent tables exist when the constraint is
-- created. ON DELETE SET NULL because each is a "soft pointer" — the
-- parent row may legitimately be cancelled/deleted without invalidating
-- the child row's existence. Companion unconditional indexes are added
-- because PostgreSQL needs them to enforce the FK on parent DELETE/UPDATE.
-- ============================================================================

ALTER TABLE orders
    ADD CONSTRAINT orders_pickup_slot_id_fkey
    FOREIGN KEY (pickup_slot_id) REFERENCES delivery_slots(id) ON DELETE SET NULL;
ALTER TABLE orders
    ADD CONSTRAINT orders_delivery_slot_id_fkey
    FOREIGN KEY (delivery_slot_id) REFERENCES delivery_slots(id) ON DELETE SET NULL;
ALTER TABLE pickup_requests
    ADD CONSTRAINT pickup_requests_pickup_slot_id_fkey
    FOREIGN KEY (pickup_slot_id) REFERENCES delivery_slots(id) ON DELETE SET NULL;
-- orders has a composite PK (id, created_at) because it is partition-by-range
-- on created_at. The FK must therefore be composite too, using the existing
-- converted_order_created_at companion column.
ALTER TABLE pickup_requests
    ADD CONSTRAINT pickup_requests_converted_order_id_fkey
    FOREIGN KEY (converted_order_id, converted_order_created_at)
    REFERENCES orders(id, created_at) ON DELETE SET NULL;
ALTER TABLE garments
    ADD CONSTRAINT garments_current_batch_id_fkey
    FOREIGN KEY (current_batch_id) REFERENCES warehouse_batches(id) ON DELETE SET NULL;

CREATE INDEX idx_orders_pickup_slot_fk     ON orders(pickup_slot_id);
CREATE INDEX idx_orders_delivery_slot_fk   ON orders(delivery_slot_id);
CREATE INDEX idx_pickupreq_slot_fk         ON pickup_requests(pickup_slot_id);
CREATE INDEX idx_pickupreq_converted_fk    ON pickup_requests(converted_order_id, converted_order_created_at);
CREATE INDEX idx_garments_current_batch_fk ON garments(current_batch_id);


-- ============================================================================
