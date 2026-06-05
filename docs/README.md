# Laundry Ghar — Database Schema (`database/`)

> **CANONICAL SOURCE.** The `.sql` files in this folder are the single source of truth for the schema. Any `.md` file (including `PRODUCTION_SPEC.md`) that disagrees is wrong — fix the markdown, not the SQL. This README is generated *from* the SQL and is safe to read, but the `.sql` files win on any conflict.

## Files in this folder

Load them in order — foreign keys reference tables defined in earlier files.

| Order | File | Tables | Community | Built in |
|---|---|---|---|---|
| 1 | `01_tenancy_identity.sql` | 21 | Tenancy, Org, Identity, Customers (foundation) | Wave 0 |
| 2 | `02_customers_catalog.sql` | 14 | Customers + Service Catalog & Pricing | Wave 1 |
| 3 | `03_orders_garments.sql` | 14 | Orders, Pickups, Garment Tracking | Wave 1 |
| 4 | `04_warehouse_riders.sql` | 10 | Warehouse Ops, Riders, Delivery | Wave 1 |
| 5 | `05_commerce_payments.sql` | 13 | Packages, Loyalty, Coupons, Payments, Wallet | Wave 1 |
| 6 | `06_finance_cms.sql` | 16 | Finance, Royalty, Notifications, CMS | Wave 1 |
| 7 | `07_system_views.sql` | 4 + 5 MV | System settings, flags, files, events + analytics views | Wave 2 |
| 8 | `08_subscriptions_customer.sql` | 6 + 1 MV | Customer recurring subscriptions (plans, mandates, invoices, dunning, usage) | Wave 1 (Commerce) |
| 9 | `09_subscriptions_franchise.sql` | 4 + 1 MV | Franchise SaaS plans (tiers, auto-renew, overage, suspend-on-nonpay) | Wave 1 (Finance) |

> **Note on file vs section numbering.** The section numbers below (Section 1–14) describe the *logical* grouping used in the reference. The *physical* files above bundle those sections for parallel-agent ownership. Table #1–92 identity is stable across both.

```bash
for f in 0*.sql; do psql -d laundryghar_dev -f "$f"; done
```

---


## Conventions

| Convention | Rule |
|---|---|
| Primary Keys | `UUID` v7 (sortable) via `gen_random_uuid()` |
| Money | `NUMERIC(14,2)` — never float |
| Timestamps | `TIMESTAMPTZ` — never plain `TIMESTAMP` |
| Soft delete | `deleted_at TIMESTAMPTZ NULL` + partial indexes |
| Audit cols | `created_at`, `updated_at`, `created_by`, `updated_by`, `version` |
| Enums | Lookup tables, not PG enum types |
| JSONB | Flexible/sparse data, GIN-indexed |
| Partitions | Range by month/day on hot tables (orders, audit, ping, logs) |
| Tenant cols | `brand_id` always; `franchise_id`/`store_id` where scoped |
| RLS | Session vars: `app.current_brand_id`, `app.bypass_rls` |
| FK default | `ON DELETE RESTRICT` |

---

## Table Inventory (A → Z)

| #  | Table | Section | Purpose |
|----|-------|---------|---------|
| 1  | `platforms` | Tenancy | Top-level system owner |
| 2  | `brands` | Tenancy | White-label brand (1 default, N for multi-brand) |
| 3  | `territories` | Tenancy | Geographic exclusivity zones |
| 4  | `franchise_agreements` | Tenancy | Legal contracts |
| 5  | `franchises` | Tenancy | Operational franchise entity |
| 6  | `stores` | Tenancy | Physical walk-in / pickup locations |
| 7  | `warehouses` | Tenancy | Processing facilities |
| 8  | `store_warehouse_mappings` | Tenancy | N:M store ↔ warehouse |
| 9  | `operating_hours` | Tenancy | Weekly schedule per scope |
| 10 | `holidays` | Tenancy | Closed dates |
| 11 | `users` | Identity | Staff/admins/riders |
| 12 | `user_profiles` | Identity | Extended user info, FCM tokens |
| 13 | `user_scope_memberships` | Identity | User × scope × role |
| 14 | `roles` | Identity | System + custom roles |
| 15 | `permissions` | Identity | Granular permission codes |
| 16 | `role_permissions` | Identity | N:M role × permission |
| 17 | `otp_codes` | Identity | Phone/email OTP |
| 18 | `refresh_tokens` | Identity | JWT refresh, revocable |
| 19 | `login_history` | Identity | Login audit |
| 20 | `audit_logs` | Identity | Every state-change (**partitioned**) |
| 21 | `password_resets` | Identity | Reset tokens |
| 22 | `customers` | Customers | Mobile app users |
| 23 | `customer_addresses` | Customers | Multi-address, default flag |
| 24 | `customer_devices` | Customers | FCM/APNs, app version |
| 25 | `account_deletion_requests` | Customers | DPDP / Play Store compliant |
| 26 | `dpdp_consents` | Customers | DPDP Act purpose-bound consent |
| 27 | `service_categories` | Catalog | Dry Clean / Laundry / Steam Iron / etc. |
| 28 | `services` | Catalog | Sub-services per category |
| 29 | `fabric_types` | Catalog | Cotton / Silk / Woolen / etc. |
| 30 | `item_groups` | Catalog | MEN / WOMEN / KIDS / SHOES / HOME |
| 31 | `items` | Catalog | Shirt / Pants / Saree / Shoe |
| 32 | `item_variants` | Catalog | Shirt+Cotton, Shoe Left vs Right |
| 33 | `price_lists` | Catalog | Versioned, scoped |
| 34 | `price_list_items` | Catalog | item × service × price |
| 35 | `add_ons` | Catalog | Stain removal, premium wash |
| 36 | `orders` | Orders | Header (**partitioned monthly**) |
| 37 | `order_items` | Orders | Line items |
| 38 | `order_addons` | Orders | Add-ons per line |
| 39 | `order_status_history` | Orders | Status transitions audit |
| 40 | `order_notes` | Orders | Internal + customer notes thread |
| 41 | `pickup_requests` | Orders | Customer-initiated pre-orders |
| 42 | `delivery_assignments` | Orders | Rider × order × leg |
| 43 | `delivery_slots` | Orders | Configurable time slots |
| 44 | `delivery_slot_bookings` | Orders | Slot capacity audit |
| 45 | `garments` | Garments | Physical garment + tag |
| 46 | `garment_tags` | Garments | Printed barcode/QR pool |
| 47 | `garment_inspections` | Garments | Pickup/QC inspection sessions |
| 48 | `garment_inspection_photos` | Garments | Photo evidence |
| 49 | `garment_conditions` | Garments | Lookup (stain, tear, etc.) |
| 50 | `warehouse_batches` | Warehouse | Group of garments processed together |
| 51 | `warehouse_processes` | Warehouse | Lookup (sort, wash, dry, etc.) |
| 52 | `process_logs` | Warehouse | Every scan (**partitioned monthly**) |
| 53 | `quality_checks` | Warehouse | Pre/post photos, pass/fail/rewash |
| 54 | `stock_reconciliations` | Warehouse | Daily count sessions |
| 55 | `stock_reconciliation_items` | Warehouse | Per-garment match/missing |
| 56 | `riders` | Riders | Extended profile |
| 57 | `rider_assignments` | Riders | Shift assignments |
| 58 | `rider_location_pings` | Riders | GPS time series (**partitioned daily**) |
| 59 | `rider_capacity_config` | Riders | Per-rider per-slot caps |
| 60 | `packages` | Loyalty | Diamond/Gold/Silver prepaid |
| 61 | `customer_packages` | Loyalty | Purchased instances |
| 62 | `package_usage_ledger` | Loyalty | Credit debits per order |
| 63 | `loyalty_programs` | Loyalty | Earn/burn config per brand |
| 64 | `loyalty_points_ledger` | Loyalty | Append-only points journal |
| 65 | `coupons` | Loyalty | Promo codes with usage limits |
| 66 | `coupon_redemptions` | Loyalty | Coupons applied to orders |
| 67 | `promotions` | Loyalty | First-order, cashback campaigns |
| 68 | `payment_methods` | Payments | UPI/card/wallet/COD lookup |
| 69 | `payments` | Payments | Every transaction with gateway ref |
| 70 | `payment_refunds` | Payments | Refund tracking |
| 71 | `wallet_accounts` | Payments | Customer wallet header |
| 72 | `wallet_transactions` | Payments | Append-only wallet ledger |
| 73 | `cash_books` | Finance | Daily cash session per store/shift |
| 74 | `cash_book_entries` | Finance | Individual cash transactions |
| 75 | `expense_categories` | Finance | Lookup (rent, utility, salary) |
| 76 | `expenses` | Finance | Store/franchise expenses |
| 77 | `expense_attachments` | Finance | Receipts / bills |
| 78 | `shift_handovers` | Finance | Staff shift transitions |
| 79 | `royalty_invoices` | Finance | Monthly royalty billing |
| 80 | `royalty_calculations` | Finance | Line-item royalty breakdown |
| 81 | `notification_templates` | CMS | Versioned templates per channel |
| 82 | `notification_preferences` | CMS | Per-customer channel toggles |
| 83 | `notifications_outbox` | CMS | Transactional outbox |
| 84 | `notifications_log` | CMS | Send log (**partitioned monthly**) |
| 85 | `whatsapp_message_log` | CMS | Full WhatsApp conversation log |
| 86 | `onboarding_slides` | CMS | Mobile app onboarding carousel |
| 87 | `app_banners` | CMS | Home screen banners |
| 88 | `mobile_app_config` | CMS | Remote config per app/platform |
| 89 | `system_settings` | System | Brand/franchise-scoped config |
| 90 | `feature_flags` | System | Gradual rollout / kill switches |
| 91 | `file_attachments` | System | Generic polymorphic file registry |
| 92 | `outbox_events` | System | Domain event outbox |
| 93 | `subscription_plans` | Subscriptions (A) | Customer recurring plan catalog |
| 94 | `payment_mandates` | Subscriptions (A) | UPI AutoPay / e-mandate authorization |
| 95 | `customer_subscriptions` | Subscriptions (A) | Active recurring subscription instance |
| 96 | `subscription_invoices` | Subscriptions (A) | One invoice per billing cycle |
| 97 | `subscription_billing_attempts` | Subscriptions (A) | Charge attempts / dunning (append-only) |
| 98 | `subscription_usage_ledger` | Subscriptions (A) | Per-cycle quota allocate/consume (append-only) |
| 99 | `platform_plans` | Subscriptions (B) | SaaS tiers offered to franchises |
| 100 | `franchise_subscriptions` | Subscriptions (B) | Franchise SaaS subscription instance |
| 101 | `franchise_subscription_invoices` | Subscriptions (B) | Monthly SaaS invoice (base + overage) |
| 102 | `franchise_subscription_events` | Subscriptions (B) | SaaS lifecycle audit (append-only) |

**Materialized views:** `mv_daily_store_revenue`, `mv_monthly_franchise_revenue`, `mv_warehouse_throughput`, `mv_customer_ltv`, `mv_rider_performance`, `mv_subscription_mrr` (customer MRR), `mv_franchise_saas_mrr` (platform MRR/ARR)

---

## Extensions Required

```sql
CREATE EXTENSION IF NOT EXISTS pgcrypto;            -- gen_random_uuid()
CREATE EXTENSION IF NOT EXISTS citext;              -- case-insensitive text
CREATE EXTENSION IF NOT EXISTS postgis;             -- geo queries
CREATE EXTENSION IF NOT EXISTS pg_partman;          -- partition automation
CREATE EXTENSION IF NOT EXISTS pg_trgm;             -- fuzzy text search
CREATE EXTENSION IF NOT EXISTS btree_gin;           -- composite GIN indexes
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;  -- query telemetry
CREATE EXTENSION IF NOT EXISTS unaccent;            -- diacritic-insensitive
```

---


## Section 1: TENANCY & ORGANIZATION (10 tables: #1–10)

### 01. `platforms` — top-level system owner (usually 1 row)
```sql
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
```

### 02. `brands` — white-label brand under platform (1 by default; N for white-label)
```sql
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
```

### 03. `territories` — geographic exclusivity zones per brand
```sql
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
```

### 04. `franchise_agreements` — legal contract between platform/brand and franchisee
```sql
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
```

### 05. `franchises` — operational franchise business entity
```sql
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
```

### 06. `stores` — physical walk-in / pickup retail locations
```sql
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
```

### 07. `warehouses` — processing facilities (one warehouse serves N stores)
```sql
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
```

### 08. `store_warehouse_mappings` — N:M between stores and warehouses
```sql
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
```

### 09. `operating_hours` — weekly schedule per store/warehouse
```sql
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
    UNIQUE (scope_type, scope_id, day_of_week, effective_from)
);
CREATE INDEX idx_ophours_scope          ON operating_hours(scope_type, scope_id, day_of_week);
```

### 10. `holidays` — closed dates per scope (overrides operating_hours)
```sql
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
    created_by              UUID
);
CREATE INDEX idx_holidays_scope_date    ON holidays(scope_type, scope_id, holiday_date);
CREATE INDEX idx_holidays_brand_date    ON holidays(brand_id, holiday_date);
```

## Section 2: IDENTITY & ACCESS (11 tables: #11–21)

### 11. `users` — system users (staff, admins, riders; customers are separate)
```sql
CREATE TABLE users (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    phone_e164              VARCHAR(20) UNIQUE,
    email                   CITEXT UNIQUE,
    password_hash           TEXT,
    password_changed_at     TIMESTAMPTZ,
    must_change_password    BOOLEAN NOT NULL DEFAULT false,
    mfa_enabled             BOOLEAN NOT NULL DEFAULT false,
    mfa_secret              TEXT,
    mfa_backup_codes        TEXT[],
    user_type               VARCHAR(30) NOT NULL DEFAULT 'staff'
                            CHECK (user_type IN ('platform_admin','brand_admin','franchise_owner',
                                                 'store_admin','staff','warehouse_staff','rider','auditor','support')),
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    timezone                VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','invited','locked','suspended','deleted')),
    last_login_at           TIMESTAMPTZ,
    last_login_ip           INET,
    last_active_at          TIMESTAMPTZ,
    failed_attempts         SMALLINT NOT NULL DEFAULT 0,
    locked_until            TIMESTAMPTZ,
    email_verified_at       TIMESTAMPTZ,
    phone_verified_at       TIMESTAMPTZ,
    invitation_token        TEXT,
    invitation_sent_at      TIMESTAMPTZ,
    invitation_accepted_at  TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    CHECK (phone_e164 IS NOT NULL OR email IS NOT NULL)
);
CREATE INDEX idx_users_status           ON users(status) WHERE deleted_at IS NULL;
CREATE INDEX idx_users_type             ON users(user_type) WHERE deleted_at IS NULL;
CREATE INDEX idx_users_last_active      ON users(last_active_at DESC);
```

### 12. `user_profiles` — extended user info, FCM tokens, settings
```sql
CREATE TABLE user_profiles (
    user_id                 UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    first_name              VARCHAR(100),
    last_name               VARCHAR(100),
    display_name            VARCHAR(200),
    avatar_url              TEXT,
    date_of_birth           DATE,
    gender                  VARCHAR(20) CHECK (gender IN ('male','female','other','prefer_not_to_say')),
    designation             VARCHAR(100),
    department              VARCHAR(100),
    employee_id             VARCHAR(50),
    joined_at               DATE,
    emergency_contact_name  VARCHAR(200),
    emergency_contact_phone VARCHAR(20),
    address                 JSONB,
    fcm_token               TEXT,
    fcm_token_updated_at    TIMESTAMPTZ,
    apns_token              TEXT,
    apns_token_updated_at   TIMESTAMPTZ,
    preferences             JSONB NOT NULL DEFAULT '{}'::jsonb,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_userprof_employee      ON user_profiles(employee_id) WHERE employee_id IS NOT NULL;
```

### 13. `user_scope_memberships` — user × (brand|franchise|store|warehouse) × role
```sql
CREATE TABLE user_scope_memberships (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    scope_type              VARCHAR(20) NOT NULL
                            CHECK (scope_type IN ('platform','brand','franchise','store','warehouse','territory')),
    scope_id                UUID,
    role_id                 UUID NOT NULL,
    is_primary              BOOLEAN NOT NULL DEFAULT false,
    granted_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    granted_by              UUID,
    revoked_at              TIMESTAMPTZ,
    revoked_by              UUID,
    revoked_reason          TEXT,
    expires_at              TIMESTAMPTZ,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (user_id, scope_type, scope_id, role_id)
);
CREATE INDEX idx_usm_user_active        ON user_scope_memberships(user_id)
    WHERE revoked_at IS NULL AND (expires_at IS NULL OR expires_at > now());
CREATE INDEX idx_usm_scope              ON user_scope_memberships(scope_type, scope_id)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_usm_role               ON user_scope_memberships(role_id) WHERE revoked_at IS NULL;
```

### 14. `roles` — system + custom roles
```sql
CREATE TABLE roles (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    description             TEXT,
    scope_type              VARCHAR(20) NOT NULL
                            CHECK (scope_type IN ('platform','brand','franchise','store','warehouse')),
    is_system               BOOLEAN NOT NULL DEFAULT false,
    is_assignable           BOOLEAN NOT NULL DEFAULT true,
    priority                SMALLINT NOT NULL DEFAULT 100,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_roles_brand            ON roles(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_roles_scope            ON roles(scope_type) WHERE deleted_at IS NULL;
```

### 15. `permissions` — granular permission codes (e.g., order.refund)
```sql
CREATE TABLE permissions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code                    VARCHAR(100) NOT NULL UNIQUE,
    module                  VARCHAR(50) NOT NULL,
    action                  VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    is_system               BOOLEAN NOT NULL DEFAULT true,
    requires_scope          BOOLEAN NOT NULL DEFAULT true,
    risk_level              VARCHAR(20) NOT NULL DEFAULT 'normal'
                            CHECK (risk_level IN ('low','normal','high','critical')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_permissions_module     ON permissions(module);
```

### 16. `role_permissions` — N:M role × permission
```sql
CREATE TABLE role_permissions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id                 UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id           UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    granted_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    granted_by              UUID,
    UNIQUE (role_id, permission_id)
);
CREATE INDEX idx_roleperm_role          ON role_permissions(role_id);
CREATE INDEX idx_roleperm_permission    ON role_permissions(permission_id);
```

### 17. `otp_codes` — phone/email OTP with attempt counter
```sql
CREATE TABLE otp_codes (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    purpose                 VARCHAR(30) NOT NULL
                            CHECK (purpose IN ('login','signup','verify_phone','verify_email',
                                               'reset_password','transaction','delivery_otp','sensitive_action')),
    identifier              VARCHAR(255) NOT NULL,
    identifier_type         VARCHAR(10) NOT NULL CHECK (identifier_type IN ('phone','email')),
    code_hash               TEXT NOT NULL,
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    customer_id             UUID,
    reference_id            UUID,
    reference_type          VARCHAR(50),
    attempts                SMALLINT NOT NULL DEFAULT 0,
    max_attempts            SMALLINT NOT NULL DEFAULT 3,
    verified_at             TIMESTAMPTZ,
    expires_at              TIMESTAMPTZ NOT NULL,
    ip_address              INET,
    user_agent              TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_otp_identifier_active  ON otp_codes(identifier, purpose)
    WHERE verified_at IS NULL AND expires_at > now();
CREATE INDEX idx_otp_cleanup            ON otp_codes(expires_at);
```

### 18. `refresh_tokens` — JWT refresh tokens (hashed, revocable)
```sql
CREATE TABLE refresh_tokens (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    customer_id             UUID,
    token_hash              TEXT NOT NULL UNIQUE,
    family_id               UUID NOT NULL,
    parent_token_id         UUID REFERENCES refresh_tokens(id),
    device_id               VARCHAR(255),
    device_name             VARCHAR(200),
    device_os               VARCHAR(50),
    ip_address              INET,
    user_agent              TEXT,
    issued_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at              TIMESTAMPTZ NOT NULL,
    last_used_at            TIMESTAMPTZ,
    revoked_at              TIMESTAMPTZ,
    revoked_reason          VARCHAR(50),
    CHECK (user_id IS NOT NULL OR customer_id IS NOT NULL)
);
CREATE INDEX idx_refresh_user_active    ON refresh_tokens(user_id, expires_at)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_refresh_customer_active ON refresh_tokens(customer_id, expires_at)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_refresh_family         ON refresh_tokens(family_id);
```

### 19. `login_history` — successful + failed login attempts
```sql
CREATE TABLE login_history (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID REFERENCES users(id) ON DELETE SET NULL,
    customer_id             UUID,
    identifier              VARCHAR(255) NOT NULL,
    auth_method             VARCHAR(20) NOT NULL
                            CHECK (auth_method IN ('password','otp','oauth','mfa','refresh','impersonation')),
    success                 BOOLEAN NOT NULL,
    failure_reason          VARCHAR(100),
    ip_address              INET,
    user_agent              TEXT,
    device_id               VARCHAR(255),
    country_code            CHAR(2),
    city                    VARCHAR(100),
    is_suspicious           BOOLEAN NOT NULL DEFAULT false,
    risk_score              SMALLINT,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_loginhist_user         ON login_history(user_id, occurred_at DESC);
CREATE INDEX idx_loginhist_customer     ON login_history(customer_id, occurred_at DESC);
CREATE INDEX idx_loginhist_identifier   ON login_history(identifier, occurred_at DESC);
CREATE INDEX idx_loginhist_suspicious   ON login_history(occurred_at DESC) WHERE is_suspicious = true;
```

### 20. `audit_logs` — every state-changing action (PARTITIONED monthly)
```sql
CREATE TABLE audit_logs (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    brand_id                UUID,
    franchise_id            UUID,
    store_id                UUID,
    warehouse_id            UUID,
    actor_user_id           UUID,
    actor_customer_id       UUID,
    actor_type              VARCHAR(20) NOT NULL DEFAULT 'user'
                            CHECK (actor_type IN ('user','customer','system','api','webhook','job')),
    actor_display           VARCHAR(200),
    action                  VARCHAR(100) NOT NULL,
    resource_type           VARCHAR(50) NOT NULL,
    resource_id             UUID,
    resource_display        VARCHAR(200),
    old_values              JSONB,
    new_values              JSONB,
    changed_fields          TEXT[],
    ip_address              INET,
    user_agent              TEXT,
    request_id              UUID,
    correlation_id          UUID,
    success                 BOOLEAN NOT NULL DEFAULT true,
    error_message           TEXT,
    PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX idx_audit_resource         ON audit_logs(resource_type, resource_id, occurred_at DESC);
CREATE INDEX idx_audit_actor_user       ON audit_logs(actor_user_id, occurred_at DESC) WHERE actor_user_id IS NOT NULL;
CREATE INDEX idx_audit_brand_action     ON audit_logs(brand_id, action, occurred_at DESC);
CREATE INDEX idx_audit_correlation      ON audit_logs(correlation_id) WHERE correlation_id IS NOT NULL;
```

### 21. `password_resets` — password reset tokens with TTL
```sql
CREATE TABLE password_resets (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    customer_id             UUID,
    token_hash              TEXT NOT NULL UNIQUE,
    requested_ip            INET,
    requested_user_agent    TEXT,
    used_at                 TIMESTAMPTZ,
    used_ip                 INET,
    expires_at              TIMESTAMPTZ NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (user_id IS NOT NULL OR customer_id IS NOT NULL)
);
CREATE INDEX idx_pwreset_active         ON password_resets(token_hash) WHERE used_at IS NULL;
CREATE INDEX idx_pwreset_cleanup        ON password_resets(expires_at);
```

## Section 3: CUSTOMERS (5 tables: #22–26)

### 22. `customers` — end-users (mobile app users)
```sql
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
```

### 23. `customer_addresses` — multi-address with default, geofence
```sql
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
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_custaddr_customer      ON customer_addresses(customer_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_custaddr_default       ON customer_addresses(customer_id) WHERE is_default = true AND deleted_at IS NULL;
CREATE INDEX idx_custaddr_geo           ON customer_addresses USING GIST (geo_location);
CREATE INDEX idx_custaddr_pincode       ON customer_addresses(pincode);
```

### 24. `customer_devices` — FCM/APNs tokens, app version per device
```sql
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
    UNIQUE (customer_id, device_id)
);
CREATE INDEX idx_custdev_customer       ON customer_devices(customer_id) WHERE is_active = true;
CREATE INDEX idx_custdev_fcm            ON customer_devices(fcm_token) WHERE fcm_token IS NOT NULL AND is_active = true;
CREATE INDEX idx_custdev_apns           ON customer_devices(apns_token) WHERE apns_token IS NOT NULL AND is_active = true;
```

### 25. `account_deletion_requests` — DPDP/Play Store compliant
```sql
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
    CHECK (customer_id IS NOT NULL OR user_id IS NOT NULL)
);
CREATE INDEX idx_acctdel_customer       ON account_deletion_requests(customer_id);
CREATE INDEX idx_acctdel_status         ON account_deletion_requests(status, grace_period_ends_at);
CREATE INDEX idx_acctdel_brand          ON account_deletion_requests(brand_id, requested_at DESC);
```

### 26. `dpdp_consents` — DPDP Act 2023 purpose-bound consent log
```sql
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
    CHECK (customer_id IS NOT NULL OR user_id IS NOT NULL)
);
CREATE INDEX idx_dpdp_customer_purpose  ON dpdp_consents(customer_id, purpose, created_at DESC);
CREATE INDEX idx_dpdp_user_purpose      ON dpdp_consents(user_id, purpose, created_at DESC);
CREATE INDEX idx_dpdp_active            ON dpdp_consents(customer_id, purpose)
    WHERE consent_status = 'granted' AND withdrawn_at IS NULL;
```

## Section 4: SERVICE CATALOG & PRICING (9 tables: #27–35)

### 27. `service_categories` — Dry Clean, Laundry, Steam Iron, Shoe, Bag, Carpet, Curtain
```sql
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
```

### 28. `services` — sub-services under a category
```sql
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
```

### 29. `fabric_types` — Cotton, Silk, Woolen, Synthetic, etc.
```sql
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
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_fabric_brand           ON fabric_types(brand_id) WHERE deleted_at IS NULL;
```

### 30. `item_groups` — MEN, WOMEN, KIDS, SHOES, HOME, ACCESSORIES
```sql
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
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_itemgrp_brand          ON item_groups(brand_id) WHERE deleted_at IS NULL;
```

### 31. `items` — Shirt, Pants, Saree, Sport Shoe, etc.
```sql
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
```

### 32. `item_variants` — Shirt+Cotton vs Shirt+Silk; or Sport Shoe Left vs Right
```sql
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
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_itemvar_item           ON item_variants(item_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_itemvar_fabric         ON item_variants(fabric_type_id);
CREATE INDEX idx_itemvar_barcode        ON item_variants(barcode) WHERE barcode IS NOT NULL;
```

### 33. `price_lists` — versioned, scoped to brand/franchise/store
```sql
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
```

### 34. `price_list_items` — item × service × price entry
```sql
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
    UNIQUE (price_list_id, service_id, item_id, item_variant_id, fabric_type_id)
);
CREATE INDEX idx_pli_lookup             ON price_list_items(price_list_id, service_id, item_id) WHERE is_active = true;
CREATE INDEX idx_pli_item               ON price_list_items(item_id);
CREATE INDEX idx_pli_service            ON price_list_items(service_id);
CREATE INDEX idx_pli_group              ON price_list_items(item_group_id);
```

### 35. `add_ons` — stain removal, premium wash (surcharges)
```sql
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
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_addons_brand           ON add_ons(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_addons_services_gin    ON add_ons USING GIN (applicable_services);
```

## Section 5: ORDERS & PICKUPS (9 tables: #36–44)

### 36. `orders` — order header (PARTITIONED monthly by created_at)
```sql
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
```

### 37. `order_items` — line items per order
```sql
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
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_orderitems_order       ON order_items(order_id, order_created_at);
CREATE INDEX idx_orderitems_item        ON order_items(item_id);
CREATE INDEX idx_orderitems_service     ON order_items(service_id);
```

### 38. `order_addons` — add-ons (stain removal etc) per line
```sql
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_orderaddons_order      ON order_addons(order_id, order_created_at);
CREATE INDEX idx_orderaddons_item       ON order_addons(order_item_id);
```

### 39. `order_status_history` — full audit of status transitions
```sql
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
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX idx_orderstathist_order    ON order_status_history(order_id, changed_at DESC);
CREATE INDEX idx_orderstathist_to_stat  ON order_status_history(to_status, changed_at DESC);
```

### 40. `order_notes` — internal + customer notes thread
```sql
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
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_ordernotes_order       ON order_notes(order_id, created_at DESC) WHERE deleted_at IS NULL;
```

### 41. `pickup_requests` — customer-initiated requests (pre-order)
```sql
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
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_pickupreq_customer     ON pickup_requests(customer_id, created_at DESC);
CREATE INDEX idx_pickupreq_store_date   ON pickup_requests(store_id, pickup_date, status);
CREATE INDEX idx_pickupreq_slot         ON pickup_requests(pickup_slot_id);
CREATE INDEX idx_pickupreq_status       ON pickup_requests(status, pickup_date) WHERE status IN ('pending','assigned','rider_dispatched');
```

### 42. `delivery_assignments` — rider × order × leg (pickup or delivery)
```sql
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
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX idx_delivassign_rider      ON delivery_assignments(rider_id, status, assigned_at DESC);
CREATE INDEX idx_delivassign_order      ON delivery_assignments(order_id);
CREATE INDEX idx_delivassign_pickup     ON delivery_assignments(pickup_request_id);
CREATE INDEX idx_delivassign_store_open ON delivery_assignments(store_id, status) WHERE status IN ('assigned','accepted','started','arrived');
```

### 43. `delivery_slots` — configurable time slots per store per day
```sql
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
    CHECK (booked_count >= 0 AND booked_count <= capacity),
    UNIQUE (store_id, slot_date, slot_start, slot_type)
);
CREATE INDEX idx_slots_lookup           ON delivery_slots(store_id, slot_date, slot_type) WHERE is_active = true;
CREATE INDEX idx_slots_available        ON delivery_slots(store_id, slot_date) WHERE is_active = true AND booked_count < capacity;
```

### 44. `delivery_slot_bookings` — slot capacity audit (one per booking event)
```sql
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
                            CHECK (status IN ('active','cancelled','completed','no_show'))
);
CREATE INDEX idx_slotbook_slot          ON delivery_slot_bookings(slot_id) WHERE status = 'active';
CREATE INDEX idx_slotbook_customer      ON delivery_slot_bookings(customer_id, booked_at DESC);
```

## Section 6: GARMENTS & TRACKING (5 tables: #45–49)

### 45. `garments` — physical garment instance with printed tag
```sql
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
    version                 INTEGER NOT NULL DEFAULT 1
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
```

### 46. `garment_tags` — printed barcode/QR registry (pre-printed pool)
```sql
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
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_garmtags_store_status  ON garment_tags(store_id, status) WHERE status = 'available';
CREATE INDEX idx_garmtags_garment       ON garment_tags(assigned_to_garment_id) WHERE assigned_to_garment_id IS NOT NULL;
```

### 47. `garment_inspections` — pickup/QC inspection sessions
```sql
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_inspect_garment        ON garment_inspections(garment_id, inspected_at DESC);
CREATE INDEX idx_inspect_order          ON garment_inspections(order_id, inspection_type);
CREATE INDEX idx_inspect_conditions_gin ON garment_inspections USING GIN (conditions);
CREATE INDEX idx_inspect_qc_fail        ON garment_inspections(brand_id, qc_result) WHERE qc_result IN ('fail','rewash');
```

### 48. `garment_inspection_photos` — photo evidence with annotations
```sql
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
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_inspectphoto_inspect   ON garment_inspection_photos(inspection_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_inspectphoto_garment   ON garment_inspection_photos(garment_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_inspectphoto_expires   ON garment_inspection_photos(expires_at) WHERE expires_at IS NOT NULL AND deleted_at IS NULL;
```

### 49. `garment_conditions` — lookup (stain, tear, missing button, fading, etc.)
```sql
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
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_garmcond_brand         ON garment_conditions(brand_id) WHERE is_active = true;
```

## Section 7: WAREHOUSE OPERATIONS (6 tables: #50–55)

### 50. `warehouse_batches` — group of garments processed together
```sql
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
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_whbatches_wh_status    ON warehouse_batches(warehouse_id, status, created_at DESC);
CREATE INDEX idx_whbatches_type         ON warehouse_batches(batch_type, started_at DESC);
```

### 51. `warehouse_processes` — lookup (sort, wash, dry, iron, pack, etc.)
```sql
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
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_whproc_brand_seq       ON warehouse_processes(brand_id, sequence_order) WHERE is_active = true;
```

### 52. `process_logs` — every scan/transition (PARTITIONED monthly)
```sql
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
    PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX idx_proclogs_garment       ON process_logs(garment_id, occurred_at DESC);
CREATE INDEX idx_proclogs_batch         ON process_logs(batch_id, occurred_at DESC) WHERE batch_id IS NOT NULL;
CREATE INDEX idx_proclogs_warehouse_day ON process_logs(warehouse_id, occurred_at DESC);
```

### 53. `quality_checks` — pre/post photos, pass/fail/rewash
```sql
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_qc_garment             ON quality_checks(garment_id, qc_round);
CREATE INDEX idx_qc_warehouse_date      ON quality_checks(warehouse_id, inspected_at DESC);
CREATE INDEX idx_qc_failed              ON quality_checks(brand_id, result) WHERE result IN ('fail','rewash','escalate');
```

### 54. `stock_reconciliations` — daily count session
```sql
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
    CHECK (warehouse_id IS NOT NULL OR store_id IS NOT NULL)
);
CREATE INDEX idx_stockrecon_wh_date     ON stock_reconciliations(warehouse_id, recon_date DESC);
CREATE INDEX idx_stockrecon_store_date  ON stock_reconciliations(store_id, recon_date DESC);
CREATE INDEX idx_stockrecon_status      ON stock_reconciliations(brand_id, status) WHERE status IN ('in_progress','disputed');
```

### 55. `stock_reconciliation_items` — per-garment match/missing
```sql
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_stockreconitm_recon    ON stock_reconciliation_items(reconciliation_id, status);
CREATE INDEX idx_stockreconitm_garment  ON stock_reconciliation_items(garment_id);
CREATE INDEX idx_stockreconitm_missing  ON stock_reconciliation_items(brand_id, status) WHERE status IN ('missing','unexpected');
```

## Section 8: RIDERS & DELIVERY (4 tables: #56–59)

### 56. `riders` — extended profile for delivery personnel
```sql
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
    UNIQUE (brand_id, rider_code)
);
CREATE INDEX idx_riders_store           ON riders(primary_store_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_riders_franchise       ON riders(franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_riders_online          ON riders(brand_id) WHERE is_online = true AND is_on_duty = true;
CREATE INDEX idx_riders_location        ON riders USING GIST (last_known_location) WHERE is_online = true;
```

### 57. `rider_assignments` — shift / duty assignments
```sql
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
    UNIQUE (rider_id, shift_date, shift_start)
);
CREATE INDEX idx_riderassign_rider_date ON rider_assignments(rider_id, shift_date DESC);
CREATE INDEX idx_riderassign_store_date ON rider_assignments(store_id, shift_date, status);
```

### 58. `rider_location_pings` — GPS time series (PARTITIONED daily)
```sql
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
    PRIMARY KEY (id, pinged_at)
) PARTITION BY RANGE (pinged_at);

CREATE INDEX idx_riderping_rider_time   ON rider_location_pings(rider_id, pinged_at DESC);
CREATE INDEX idx_riderping_geo          ON rider_location_pings USING GIST (location);
```

### 59. `rider_capacity_config` — per-rider per-slot caps
```sql
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
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_ridercap_rider_day     ON rider_capacity_config(rider_id, day_of_week) WHERE is_active = true;
```

## Section 9: PACKAGES, LOYALTY, COUPONS (8 tables: #60–67)

### 60. `packages` — Diamond/Gold/Silver prepaid packages
```sql
CREATE TABLE packages (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    tier                    VARCHAR(30) NOT NULL
                            CHECK (tier IN ('silver','gold','diamond','platinum','custom')),
    description             TEXT,
    price                   NUMERIC(14,2) NOT NULL CHECK (price > 0),
    credit_value            NUMERIC(14,2) NOT NULL CHECK (credit_value > 0),
    discount_percent        NUMERIC(5,2) NOT NULL DEFAULT 0
                            CHECK (discount_percent BETWEEN 0 AND 100),
    credit_multiplier       NUMERIC(4,2) NOT NULL DEFAULT 1.00,
    validity_days           INTEGER,
    is_unlimited_validity   BOOLEAN NOT NULL DEFAULT false,
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    excluded_services       UUID[] NOT NULL DEFAULT '{}',
    minimum_order_value     NUMERIC(14,2),
    max_usage_per_order     NUMERIC(14,2),
    max_purchases_per_cust  INTEGER,
    icon_url                TEXT,
    color_hex               CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_featured             BOOLEAN NOT NULL DEFAULT false,
    terms_and_conditions    TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('draft','active','paused','retired')),
    available_from          TIMESTAMPTZ,
    available_to            TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_packages_brand_active  ON packages(brand_id, tier) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_packages_services_gin  ON packages USING GIN (applicable_services);

ALTER TABLE packages ENABLE ROW LEVEL SECURITY;
CREATE POLICY packages_tenant ON packages
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 61. `customer_packages` — purchased subscription instances
```sql
CREATE TABLE customer_packages (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id),
    package_id              UUID NOT NULL REFERENCES packages(id),
    purchase_order_id       UUID,
    purchase_order_created_at TIMESTAMPTZ,
    payment_id              UUID,
    purchase_amount         NUMERIC(14,2) NOT NULL,
    credit_value_total      NUMERIC(14,2) NOT NULL,
    credit_value_used       NUMERIC(14,2) NOT NULL DEFAULT 0,
    credit_value_remaining  NUMERIC(14,2) GENERATED ALWAYS AS (credit_value_total - credit_value_used) STORED,
    activated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at              TIMESTAMPTZ,
    is_unlimited_validity   BOOLEAN NOT NULL DEFAULT false,
    last_used_at            TIMESTAMPTZ,
    usage_count             INTEGER NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','exhausted','expired','suspended','refunded','cancelled')),
    suspended_at            TIMESTAMPTZ,
    suspended_reason        TEXT,
    refunded_at             TIMESTAMPTZ,
    refunded_amount         NUMERIC(14,2),
    refund_reason           TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_custpkg_customer_active ON customer_packages(customer_id, expires_at) WHERE status = 'active';
CREATE INDEX idx_custpkg_package        ON customer_packages(package_id);
CREATE INDEX idx_custpkg_expiring       ON customer_packages(expires_at) WHERE status = 'active' AND expires_at IS NOT NULL;
```

### 62. `package_usage_ledger` — credit debits per order (append-only)
```sql
CREATE TABLE package_usage_ledger (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_package_id     UUID NOT NULL REFERENCES customer_packages(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('debit','credit','refund','adjustment','expiry','bonus')),
    amount                  NUMERIC(14,2) NOT NULL,
    balance_before          NUMERIC(14,2) NOT NULL,
    balance_after           NUMERIC(14,2) NOT NULL,
    notes                   TEXT,
    reference_type          VARCHAR(30),
    reference_id            UUID,
    performed_by            UUID,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_pkguse_custpkg         ON package_usage_ledger(customer_package_id, occurred_at DESC);
CREATE INDEX idx_pkguse_customer        ON package_usage_ledger(customer_id, occurred_at DESC);
CREATE INDEX idx_pkguse_order           ON package_usage_ledger(order_id) WHERE order_id IS NOT NULL;
```

### 63. `loyalty_programs` — earn/burn config per brand
```sql
CREATE TABLE loyalty_programs (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL UNIQUE REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    earn_rate               NUMERIC(8,4) NOT NULL DEFAULT 1.0,
    earn_basis              VARCHAR(20) NOT NULL DEFAULT 'spend'
                            CHECK (earn_basis IN ('spend','order_count','garment_count')),
    burn_rate               NUMERIC(8,4) NOT NULL DEFAULT 0.10,
    min_burn_points         INTEGER NOT NULL DEFAULT 100,
    max_burn_per_order_pct  NUMERIC(5,2) NOT NULL DEFAULT 30.00,
    min_order_for_earn      NUMERIC(14,2) NOT NULL DEFAULT 0,
    excluded_services       UUID[] NOT NULL DEFAULT '{}',
    point_expiry_months     SMALLINT,
    welcome_bonus           INTEGER NOT NULL DEFAULT 0,
    referral_bonus_referrer INTEGER NOT NULL DEFAULT 0,
    referral_bonus_referee  INTEGER NOT NULL DEFAULT 0,
    birthday_bonus          INTEGER NOT NULL DEFAULT 0,
    tier_config             JSONB NOT NULL DEFAULT '{}'::jsonb,
    terms                   TEXT,
    launched_at             TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

### 64. `loyalty_points_ledger` — append-only points journal
```sql
CREATE TABLE loyalty_points_ledger (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id),
    loyalty_program_id      UUID NOT NULL REFERENCES loyalty_programs(id),
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('earn','burn','expire','adjust','refund','bonus','referral','welcome','birthday')),
    direction               SMALLINT NOT NULL CHECK (direction IN (-1, 1)),
    points                  INTEGER NOT NULL CHECK (points > 0),
    balance_before          INTEGER NOT NULL,
    balance_after           INTEGER NOT NULL,
    monetary_equivalent     NUMERIC(14,2),
    reference_type          VARCHAR(30),
    reference_id            UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    expires_at              TIMESTAMPTZ,
    notes                   TEXT,
    performed_by            UUID,
    performed_by_type       VARCHAR(20) DEFAULT 'system',
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_loyledg_customer       ON loyalty_points_ledger(customer_id, occurred_at DESC);
CREATE INDEX idx_loyledg_order          ON loyalty_points_ledger(order_id) WHERE order_id IS NOT NULL;
CREATE INDEX idx_loyledg_expiring       ON loyalty_points_ledger(expires_at)
    WHERE transaction_type = 'earn' AND expires_at IS NOT NULL;
```

### 65. `coupons` — promo codes with usage limits
```sql
CREATE TABLE coupons (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    coupon_type             VARCHAR(20) NOT NULL DEFAULT 'percent'
                            CHECK (coupon_type IN ('percent','flat','free_pickup','free_delivery','bogo','tiered')),
    discount_value          NUMERIC(14,2) NOT NULL,
    max_discount_amount     NUMERIC(14,2),
    min_order_value         NUMERIC(14,2) NOT NULL DEFAULT 0,
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    applicable_stores       UUID[] NOT NULL DEFAULT '{}',
    applicable_franchises   UUID[] NOT NULL DEFAULT '{}',
    customer_eligibility    VARCHAR(30) NOT NULL DEFAULT 'all'
                            CHECK (customer_eligibility IN ('all','new','returning','vip','segment','specific')),
    eligible_customer_ids   UUID[],
    eligible_segments       TEXT[],
    is_first_order_only     BOOLEAN NOT NULL DEFAULT false,
    is_single_use_per_cust  BOOLEAN NOT NULL DEFAULT false,
    max_total_uses          INTEGER,
    max_uses_per_customer   SMALLINT NOT NULL DEFAULT 1,
    current_usage_count     INTEGER NOT NULL DEFAULT 0,
    is_stackable            BOOLEAN NOT NULL DEFAULT false,
    is_public               BOOLEAN NOT NULL DEFAULT true,
    is_auto_apply           BOOLEAN NOT NULL DEFAULT false,
    valid_from              TIMESTAMPTZ NOT NULL,
    valid_until             TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('draft','active','paused','exhausted','expired','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_coupons_brand_active   ON coupons(brand_id, code)
    WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_coupons_valid          ON coupons(valid_from, valid_until)
    WHERE status = 'active' AND deleted_at IS NULL;
```

### 66. `coupon_redemptions` — coupons applied to orders
```sql
CREATE TABLE coupon_redemptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    coupon_id               UUID NOT NULL REFERENCES coupons(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL,
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    coupon_code             VARCHAR(50) NOT NULL,
    discount_amount         NUMERIC(14,2) NOT NULL,
    order_subtotal_snapshot NUMERIC(14,2) NOT NULL,
    redeemed_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    reverted_at             TIMESTAMPTZ,
    reverted_reason         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX idx_couponred_coupon       ON coupon_redemptions(coupon_id, redeemed_at DESC);
CREATE INDEX idx_couponred_customer     ON coupon_redemptions(customer_id, redeemed_at DESC);
CREATE INDEX idx_couponred_order        ON coupon_redemptions(order_id);
```

### 67. `promotions` — first-order, cashback, banner campaigns
```sql
CREATE TABLE promotions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    promotion_type          VARCHAR(30) NOT NULL
                            CHECK (promotion_type IN ('first_order_discount','cashback','referral','bundle',
                                                      'flat_discount','percent_discount','free_service','seasonal')),
    target_audience         VARCHAR(30) NOT NULL DEFAULT 'all'
                            CHECK (target_audience IN ('all','new_users','returning','dormant','vip','segment')),
    eligible_segments       TEXT[],
    rules                   JSONB NOT NULL,
    reward_config           JSONB NOT NULL,
    coupon_id               UUID REFERENCES coupons(id),
    banner_image_url        TEXT,
    deeplink_url            TEXT,
    valid_from              TIMESTAMPTZ NOT NULL,
    valid_until             TIMESTAMPTZ,
    total_budget            NUMERIC(14,2),
    spent_budget            NUMERIC(14,2) NOT NULL DEFAULT 0,
    impressions_count       INTEGER NOT NULL DEFAULT 0,
    redemptions_count       INTEGER NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','scheduled','active','paused','completed','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_promo_brand_active     ON promotions(brand_id, valid_from, valid_until)
    WHERE status IN ('scheduled','active');
```

## Section 10: PAYMENTS & WALLET (5 tables: #68–72)

### 68. `payment_methods` — lookup (UPI, card, wallet, COD, prepaid, etc.)
```sql
CREATE TABLE payment_methods (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    method_type             VARCHAR(30) NOT NULL
                            CHECK (method_type IN ('upi','card','netbanking','wallet','cod','prepaid_package',
                                                   'loyalty_points','store_credit','bank_transfer')),
    gateway                 VARCHAR(30) CHECK (gateway IN ('razorpay','payu','cashfree','phonepe','paytm','none')),
    icon_url                TEXT,
    minimum_amount          NUMERIC(14,2),
    maximum_amount          NUMERIC(14,2),
    convenience_fee_type    VARCHAR(20) CHECK (convenience_fee_type IN ('flat','percent')),
    convenience_fee_value   NUMERIC(14,2),
    is_online               BOOLEAN NOT NULL DEFAULT true,
    is_refundable           BOOLEAN NOT NULL DEFAULT true,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_paymethod_brand        ON payment_methods(brand_id) WHERE is_active = true;
```

### 69. `payments` — every transaction with gateway ref
```sql
CREATE TABLE payments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID,
    store_id                UUID,
    customer_id             UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    payment_method_id       UUID REFERENCES payment_methods(id),
    payment_purpose         VARCHAR(30) NOT NULL DEFAULT 'order'
                            CHECK (payment_purpose IN ('order','package','wallet_topup','tip','adjustment','refund','royalty')),
    payment_number          VARCHAR(40) UNIQUE NOT NULL,
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    convenience_fee         NUMERIC(14,2) NOT NULL DEFAULT 0,
    gateway_charge          NUMERIC(14,2) NOT NULL DEFAULT 0,
    net_amount              NUMERIC(14,2) NOT NULL,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    direction               SMALLINT NOT NULL DEFAULT 1 CHECK (direction IN (-1, 1)),
    gateway                 VARCHAR(30),
    gateway_order_id        VARCHAR(100),
    gateway_payment_id      VARCHAR(100),
    gateway_signature       TEXT,
    gateway_response        JSONB,
    upi_vpa                 VARCHAR(100),
    card_last4              CHAR(4),
    card_network            VARCHAR(20),
    bank_name               VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','initiated','authorized','captured','succeeded',
                                              'failed','cancelled','refunded','partially_refunded','disputed')),
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    initiated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at            TIMESTAMPTZ,
    failed_at               TIMESTAMPTZ,
    reconciled_at           TIMESTAMPTZ,
    settlement_id           VARCHAR(100),
    settled_at              TIMESTAMPTZ,
    settled_amount          NUMERIC(14,2),
    idempotency_key         VARCHAR(100) UNIQUE,
    ip_address              INET,
    user_agent              TEXT,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_payments_order         ON payments(order_id, status);
CREATE INDEX idx_payments_customer      ON payments(customer_id, created_at DESC);
CREATE INDEX idx_payments_gateway       ON payments(gateway, gateway_payment_id) WHERE gateway_payment_id IS NOT NULL;
CREATE INDEX idx_payments_status        ON payments(brand_id, status, created_at DESC);
CREATE INDEX idx_payments_settlement    ON payments(settlement_id) WHERE settlement_id IS NOT NULL;
```

### 70. `payment_refunds` — refund tracking
```sql
CREATE TABLE payment_refunds (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    original_payment_id     UUID NOT NULL REFERENCES payments(id),
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    customer_id             UUID,
    refund_number           VARCHAR(40) UNIQUE NOT NULL,
    refund_type             VARCHAR(20) NOT NULL DEFAULT 'full'
                            CHECK (refund_type IN ('full','partial','goodwill','dispute_loss')),
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    reason                  VARCHAR(100) NOT NULL,
    reason_text             TEXT,
    refund_method           VARCHAR(30) CHECK (refund_method IN ('original','wallet','bank_transfer','store_credit')),
    gateway_refund_id       VARCHAR(100),
    gateway_response        JSONB,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','approved','processing','succeeded','failed','rejected')),
    requested_by            UUID,
    requested_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    processed_at            TIMESTAMPTZ,
    completed_at            TIMESTAMPTZ,
    failure_reason          TEXT,
    customer_notified_at    TIMESTAMPTZ,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_refunds_original       ON payment_refunds(original_payment_id);
CREATE INDEX idx_refunds_order          ON payment_refunds(order_id);
CREATE INDEX idx_refunds_status         ON payment_refunds(brand_id, status, requested_at DESC);
```

### 71. `wallet_accounts` — customer wallet header
```sql
CREATE TABLE wallet_accounts (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL UNIQUE REFERENCES customers(id) ON DELETE CASCADE,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    balance                 NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (balance >= 0),
    locked_balance          NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (locked_balance >= 0),
    available_balance       NUMERIC(14,2) GENERATED ALWAYS AS (balance - locked_balance) STORED,
    lifetime_credit         NUMERIC(14,2) NOT NULL DEFAULT 0,
    lifetime_debit          NUMERIC(14,2) NOT NULL DEFAULT 0,
    last_transaction_at     TIMESTAMPTZ,
    is_frozen               BOOLEAN NOT NULL DEFAULT false,
    frozen_at               TIMESTAMPTZ,
    frozen_reason           TEXT,
    version                 INTEGER NOT NULL DEFAULT 1,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_wallet_customer        ON wallet_accounts(customer_id);
```

### 72. `wallet_transactions` — append-only wallet ledger
```sql
CREATE TABLE wallet_transactions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    wallet_account_id       UUID NOT NULL REFERENCES wallet_accounts(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL,
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('topup','debit','refund','cashback','bonus','adjustment','reversal','lock','unlock')),
    direction               SMALLINT NOT NULL CHECK (direction IN (-1, 1)),
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    balance_before          NUMERIC(14,2) NOT NULL,
    balance_after           NUMERIC(14,2) NOT NULL,
    reference_type          VARCHAR(30),
    reference_id            UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    payment_id              UUID,
    refund_id               UUID,
    description             VARCHAR(255),
    notes                   TEXT,
    performed_by_type       VARCHAR(20) DEFAULT 'system',
    performed_by_id         UUID,
    idempotency_key         VARCHAR(100) UNIQUE,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_wallettxn_wallet       ON wallet_transactions(wallet_account_id, occurred_at DESC);
CREATE INDEX idx_wallettxn_customer     ON wallet_transactions(customer_id, occurred_at DESC);
CREATE INDEX idx_wallettxn_order        ON wallet_transactions(order_id) WHERE order_id IS NOT NULL;
```

## Section 11: FINANCE & FRANCHISE REVENUE (8 tables: #73–80)

### 73. `cash_books` — daily cash session per store/shift (Dhobi Cart pattern)
```sql
CREATE TABLE cash_books (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID NOT NULL REFERENCES stores(id),
    book_date               DATE NOT NULL,
    shift_label             VARCHAR(30) NOT NULL DEFAULT 'full_day'
                            CHECK (shift_label IN ('morning','afternoon','evening','night','full_day')),
    opening_user_id         UUID NOT NULL,
    closing_user_id         UUID,
    opening_balance         NUMERIC(14,2) NOT NULL DEFAULT 0,
    closing_balance         NUMERIC(14,2),
    expected_closing        NUMERIC(14,2),
    variance                NUMERIC(14,2) GENERATED ALWAYS AS (closing_balance - expected_closing) STORED,
    cash_inflow             NUMERIC(14,2) NOT NULL DEFAULT 0,
    cash_outflow            NUMERIC(14,2) NOT NULL DEFAULT 0,
    upi_inflow              NUMERIC(14,2) NOT NULL DEFAULT 0,
    card_inflow             NUMERIC(14,2) NOT NULL DEFAULT 0,
    other_inflow            NUMERIC(14,2) NOT NULL DEFAULT 0,
    deposit_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    deposit_reference       VARCHAR(100),
    total_orders            INTEGER NOT NULL DEFAULT 0,
    new_orders              INTEGER NOT NULL DEFAULT 0,
    delivered_orders        INTEGER NOT NULL DEFAULT 0,
    cancelled_orders        INTEGER NOT NULL DEFAULT 0,
    opened_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    closed_at               TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'open'
                            CHECK (status IN ('open','closing','closed','reviewed','disputed','finalized')),
    variance_reason         TEXT,
    notes                   TEXT,
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (store_id, book_date, shift_label)
);
CREATE INDEX idx_cashbk_store_date      ON cash_books(store_id, book_date DESC);
CREATE INDEX idx_cashbk_open            ON cash_books(brand_id, status) WHERE status = 'open';
CREATE INDEX idx_cashbk_variance        ON cash_books(brand_id, book_date) WHERE variance != 0;
```

### 74. `cash_book_entries` — individual transactions in a cash book
```sql
CREATE TABLE cash_book_entries (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cash_book_id            UUID NOT NULL REFERENCES cash_books(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    entry_type              VARCHAR(20) NOT NULL
                            CHECK (entry_type IN ('cash_in','cash_out','deposit','withdrawal','adjustment','opening','closing')),
    category                VARCHAR(30) NOT NULL
                            CHECK (category IN ('order_payment','refund','expense','salary','utility','rent',
                                                'maintenance','supply','tip','adjustment','deposit','other')),
    direction               SMALLINT NOT NULL CHECK (direction IN (-1, 1)),
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    payment_mode            VARCHAR(20) NOT NULL DEFAULT 'cash'
                            CHECK (payment_mode IN ('cash','upi','card','bank_transfer','other')),
    reference_type          VARCHAR(30),
    reference_id            UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    expense_id              UUID,
    customer_id             UUID,
    payee_name              VARCHAR(200),
    description             VARCHAR(500),
    receipt_number          VARCHAR(100),
    receipt_s3_key          TEXT,
    performed_by            UUID NOT NULL,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    reversed_at             TIMESTAMPTZ,
    reversed_by             UUID,
    reversed_reason         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX idx_cbentry_book           ON cash_book_entries(cash_book_id, occurred_at);
CREATE INDEX idx_cbentry_order          ON cash_book_entries(order_id) WHERE order_id IS NOT NULL;
CREATE INDEX idx_cbentry_category       ON cash_book_entries(store_id, category, occurred_at DESC);
```

### 75. `expense_categories` — lookup (rent, utilities, salary, supplies, etc.)
```sql
CREATE TABLE expense_categories (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    parent_id               UUID REFERENCES expense_categories(id),
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    is_tax_deductible       BOOLEAN NOT NULL DEFAULT true,
    requires_approval       BOOLEAN NOT NULL DEFAULT false,
    approval_threshold      NUMERIC(14,2),
    accounting_code         VARCHAR(50),
    icon_url                TEXT,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_expcat_brand           ON expense_categories(brand_id) WHERE is_active = true;
CREATE INDEX idx_expcat_parent          ON expense_categories(parent_id);
```

### 76. `expenses` — store/franchise expense records
```sql
CREATE TABLE expenses (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID,
    warehouse_id            UUID,
    category_id             UUID NOT NULL REFERENCES expense_categories(id),
    cash_book_entry_id      UUID,
    expense_number          VARCHAR(40) UNIQUE NOT NULL,
    expense_date            DATE NOT NULL,
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    tax_amount              NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(14,2) GENERATED ALWAYS AS (amount + tax_amount) STORED,
    payment_mode            VARCHAR(20) NOT NULL DEFAULT 'cash'
                            CHECK (payment_mode IN ('cash','upi','card','bank_transfer','cheque','credit')),
    vendor_name             VARCHAR(200),
    vendor_gstin            VARCHAR(15),
    vendor_phone            VARCHAR(20),
    bill_number             VARCHAR(100),
    bill_date               DATE,
    description             TEXT NOT NULL,
    notes                   TEXT,
    is_recurring            BOOLEAN NOT NULL DEFAULT false,
    recurrence_frequency    VARCHAR(20) CHECK (recurrence_frequency IN ('weekly','monthly','quarterly','yearly')),
    is_reimbursable         BOOLEAN NOT NULL DEFAULT false,
    submitted_by            UUID NOT NULL,
    submitted_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    requires_approval       BOOLEAN NOT NULL DEFAULT false,
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    rejected_by             UUID,
    rejected_at             TIMESTAMPTZ,
    rejection_reason        TEXT,
    paid_at                 TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'submitted'
                            CHECK (status IN ('draft','submitted','approved','rejected','paid','reconciled','disputed')),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_expenses_franchise     ON expenses(franchise_id, expense_date DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_expenses_store         ON expenses(store_id, expense_date DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_expenses_category      ON expenses(category_id, expense_date DESC);
CREATE INDEX idx_expenses_status        ON expenses(brand_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_expenses_pending       ON expenses(brand_id, status)
    WHERE status IN ('submitted','approved') AND deleted_at IS NULL;
```

### 77. `expense_attachments` — receipts / bills attached to expenses
```sql
CREATE TABLE expense_attachments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    expense_id              UUID NOT NULL REFERENCES expenses(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    s3_key                  TEXT NOT NULL,
    thumbnail_s3_key        TEXT,
    cdn_url                 TEXT,
    file_name               VARCHAR(255) NOT NULL,
    mime_type               VARCHAR(100) NOT NULL,
    bytes                   INTEGER,
    document_type           VARCHAR(30) DEFAULT 'receipt'
                            CHECK (document_type IN ('receipt','invoice','bill','quotation','other')),
    is_primary              BOOLEAN NOT NULL DEFAULT false,
    uploaded_by             UUID,
    uploaded_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_expatt_expense         ON expense_attachments(expense_id) WHERE deleted_at IS NULL;
```

### 78. `shift_handovers` — staff shift transitions with cash count
```sql
CREATE TABLE shift_handovers (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    from_user_id            UUID NOT NULL REFERENCES users(id),
    to_user_id              UUID REFERENCES users(id),
    cash_book_id            UUID REFERENCES cash_books(id),
    handover_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    cash_handed_over        NUMERIC(14,2) NOT NULL,
    cash_counted_by_to_user NUMERIC(14,2),
    cash_variance           NUMERIC(14,2) GENERATED ALWAYS AS
                            (COALESCE(cash_counted_by_to_user, 0) - cash_handed_over) STORED,
    pending_orders_count    INTEGER NOT NULL DEFAULT 0,
    open_complaints_count   INTEGER NOT NULL DEFAULT 0,
    pickups_remaining       INTEGER NOT NULL DEFAULT 0,
    deliveries_remaining    INTEGER NOT NULL DEFAULT 0,
    notes_from              TEXT,
    notes_to                TEXT,
    pending_items           JSONB NOT NULL DEFAULT '[]'::jsonb,
    acknowledged_at         TIMESTAMPTZ,
    acknowledged_by         UUID,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','acknowledged','disputed','closed')),
    dispute_reason          TEXT,
    resolved_by             UUID,
    resolved_at             TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_handover_store_time    ON shift_handovers(store_id, handover_at DESC);
CREATE INDEX idx_handover_from_user     ON shift_handovers(from_user_id, handover_at DESC);
CREATE INDEX idx_handover_disputed      ON shift_handovers(brand_id, status) WHERE status = 'disputed';
```

### 79. `royalty_invoices` — monthly royalty billing to franchisee
```sql
CREATE TABLE royalty_invoices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL REFERENCES franchises(id),
    franchise_agreement_id  UUID REFERENCES franchise_agreements(id),
    invoice_number          VARCHAR(40) UNIQUE NOT NULL,
    period_start            DATE NOT NULL,
    period_end              DATE NOT NULL,
    gross_revenue           NUMERIC(14,2) NOT NULL DEFAULT 0,
    eligible_revenue        NUMERIC(14,2) NOT NULL DEFAULT 0,
    royalty_percent         NUMERIC(5,2) NOT NULL,
    royalty_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    marketing_fee_percent   NUMERIC(5,2) NOT NULL DEFAULT 0,
    marketing_fee_amount    NUMERIC(14,2) NOT NULL DEFAULT 0,
    technology_fee_amount   NUMERIC(14,2) NOT NULL DEFAULT 0,
    other_charges           NUMERIC(14,2) NOT NULL DEFAULT 0,
    adjustments             NUMERIC(14,2) NOT NULL DEFAULT 0,
    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    total_orders            INTEGER NOT NULL DEFAULT 0,
    invoice_date            DATE NOT NULL DEFAULT CURRENT_DATE,
    due_date                DATE NOT NULL,
    sent_at                 TIMESTAMPTZ,
    paid_at                 TIMESTAMPTZ,
    invoice_s3_key          TEXT,
    invoice_pdf_url         TEXT,
    line_items              JSONB NOT NULL DEFAULT '[]'::jsonb,
    notes                   TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','issued','sent','viewed','partial','paid','overdue','disputed','void')),
    disputed_at             TIMESTAMPTZ,
    dispute_reason          TEXT,
    resolved_at             TIMESTAMPTZ,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (franchise_id, period_start, period_end)
);
CREATE INDEX idx_royinv_franchise       ON royalty_invoices(franchise_id, period_start DESC);
CREATE INDEX idx_royinv_status          ON royalty_invoices(brand_id, status, due_date);
CREATE INDEX idx_royinv_overdue         ON royalty_invoices(due_date) WHERE status IN ('issued','sent','viewed','partial');
```

### 80. `royalty_calculations` — line-item breakdown of revenue used for royalty
```sql
CREATE TABLE royalty_calculations (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    royalty_invoice_id      UUID NOT NULL REFERENCES royalty_invoices(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    calculation_date        DATE NOT NULL,
    service_category_id     UUID,
    revenue_type            VARCHAR(30) NOT NULL DEFAULT 'order'
                            CHECK (revenue_type IN ('order','package','adjustment','refund')),
    gross_amount            NUMERIC(14,2) NOT NULL DEFAULT 0,
    excluded_amount         NUMERIC(14,2) NOT NULL DEFAULT 0,
    exclusion_reason        VARCHAR(100),
    eligible_amount         NUMERIC(14,2) NOT NULL DEFAULT 0,
    royalty_rate            NUMERIC(5,2) NOT NULL,
    royalty_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_roycalc_invoice        ON royalty_calculations(royalty_invoice_id);
CREATE INDEX idx_roycalc_franchise_date ON royalty_calculations(franchise_id, calculation_date DESC);
CREATE INDEX idx_roycalc_order          ON royalty_calculations(order_id) WHERE order_id IS NOT NULL;
```

## Section 12: NOTIFICATIONS & CMS (8 tables: #81–88)

### 81. `notification_templates` — versioned templates per channel
```sql
CREATE TABLE notification_templates (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(100) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    channel                 VARCHAR(20) NOT NULL
                            CHECK (channel IN ('sms','whatsapp','email','push','in_app','voice')),
    category                VARCHAR(50) NOT NULL,
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    subject_template        VARCHAR(500),
    body_template           TEXT NOT NULL,
    sms_sender_id           VARCHAR(20),
    whatsapp_template_name  VARCHAR(200),
    whatsapp_template_id    VARCHAR(200),
    whatsapp_lang_code      VARCHAR(20),
    whatsapp_namespace      VARCHAR(100),
    push_title_template     VARCHAR(200),
    push_action_deeplink    TEXT,
    push_icon_url           TEXT,
    push_sound              VARCHAR(50),
    variables               JSONB NOT NULL DEFAULT '[]'::jsonb,
    version_number          INTEGER NOT NULL DEFAULT 1,
    parent_template_id      UUID REFERENCES notification_templates(id),
    is_transactional        BOOLEAN NOT NULL DEFAULT true,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    approved_at             TIMESTAMPTZ,
    approved_by             UUID,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (brand_id, code, channel, locale, version_number)
);
CREATE INDEX idx_notiftpl_lookup        ON notification_templates(brand_id, code, channel, locale) WHERE is_active = true;
```

### 82. `notification_preferences` — per-customer channel toggles
```sql
CREATE TABLE notification_preferences (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id             UUID REFERENCES customers(id) ON DELETE CASCADE,
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    notification_category   VARCHAR(50) NOT NULL,
    sms_enabled             BOOLEAN NOT NULL DEFAULT true,
    whatsapp_enabled        BOOLEAN NOT NULL DEFAULT true,
    email_enabled           BOOLEAN NOT NULL DEFAULT true,
    push_enabled            BOOLEAN NOT NULL DEFAULT true,
    in_app_enabled          BOOLEAN NOT NULL DEFAULT true,
    voice_enabled           BOOLEAN NOT NULL DEFAULT false,
    quiet_hours_start       TIME,
    quiet_hours_end         TIME,
    timezone                VARCHAR(50),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (customer_id IS NOT NULL OR user_id IS NOT NULL),
    UNIQUE (customer_id, notification_category),
    UNIQUE (user_id, notification_category)
);
CREATE INDEX idx_notifpref_customer     ON notification_preferences(customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX idx_notifpref_user         ON notification_preferences(user_id) WHERE user_id IS NOT NULL;
```

### 83. `notifications_outbox` — transactional outbox for reliable send
```sql
CREATE TABLE notifications_outbox (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    template_id             UUID REFERENCES notification_templates(id),
    template_code           VARCHAR(100) NOT NULL,
    channel                 VARCHAR(20) NOT NULL,
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    recipient_type          VARCHAR(20) NOT NULL CHECK (recipient_type IN ('customer','user','rider','franchisee','manual')),
    recipient_id            UUID,
    recipient_phone         VARCHAR(20),
    recipient_email         CITEXT,
    recipient_fcm_token     TEXT,
    recipient_apns_token    TEXT,
    subject                 VARCHAR(500),
    body                    TEXT NOT NULL,
    variables_resolved      JSONB,
    push_title              VARCHAR(200),
    push_deeplink           TEXT,
    push_payload            JSONB,
    reference_type          VARCHAR(50),
    reference_id            UUID,
    correlation_id          UUID,
    priority                SMALLINT NOT NULL DEFAULT 5 CHECK (priority BETWEEN 1 AND 10),
    scheduled_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at              TIMESTAMPTZ,
    attempts                SMALLINT NOT NULL DEFAULT 0,
    max_attempts            SMALLINT NOT NULL DEFAULT 5,
    next_attempt_at         TIMESTAMPTZ,
    last_attempt_at         TIMESTAMPTZ,
    last_error              TEXT,
    sent_at                 TIMESTAMPTZ,
    provider                VARCHAR(50),
    provider_message_id     VARCHAR(200),
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','queued','sending','sent','failed','expired','suppressed','cancelled')),
    suppression_reason      VARCHAR(100),
    idempotency_key         VARCHAR(100) UNIQUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_outbox_due             ON notifications_outbox(scheduled_at, priority)
    WHERE status IN ('pending','queued');
CREATE INDEX idx_outbox_retry           ON notifications_outbox(next_attempt_at, priority)
    WHERE status = 'failed' AND attempts < max_attempts;
CREATE INDEX idx_outbox_reference       ON notifications_outbox(reference_type, reference_id);
CREATE INDEX idx_outbox_recipient       ON notifications_outbox(recipient_type, recipient_id, created_at DESC);
```

### 84. `notifications_log` — successful send log (PARTITIONED monthly)
```sql
CREATE TABLE notifications_log (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    sent_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    brand_id                UUID NOT NULL,
    outbox_id               UUID,
    channel                 VARCHAR(20) NOT NULL,
    template_code           VARCHAR(100),
    recipient_type          VARCHAR(20) NOT NULL,
    recipient_id            UUID,
    recipient_address       VARCHAR(255),
    provider                VARCHAR(50),
    provider_message_id     VARCHAR(200),
    status                  VARCHAR(20) NOT NULL
                            CHECK (status IN ('sent','delivered','read','clicked','failed','bounced','blocked')),
    delivered_at            TIMESTAMPTZ,
    read_at                 TIMESTAMPTZ,
    clicked_at              TIMESTAMPTZ,
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    cost                    NUMERIC(10,4),
    reference_type          VARCHAR(50),
    reference_id            UUID,
    PRIMARY KEY (id, sent_at)
) PARTITION BY RANGE (sent_at);

CREATE INDEX idx_notiflog_brand_time    ON notifications_log(brand_id, sent_at DESC);
CREATE INDEX idx_notiflog_recipient     ON notifications_log(recipient_type, recipient_id, sent_at DESC);
CREATE INDEX idx_notiflog_reference     ON notifications_log(reference_type, reference_id);
CREATE INDEX idx_notiflog_provider      ON notifications_log(provider, provider_message_id);
```

### 85. `whatsapp_message_log` — full WhatsApp conversation log
```sql
CREATE TABLE whatsapp_message_log (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    direction               VARCHAR(10) NOT NULL CHECK (direction IN ('inbound','outbound')),
    customer_id             UUID,
    user_id                 UUID,
    phone_e164              VARCHAR(20) NOT NULL,
    provider                VARCHAR(50) NOT NULL DEFAULT 'meta',
    wa_message_id           VARCHAR(200) UNIQUE,
    wa_conversation_id      VARCHAR(200),
    template_name           VARCHAR(200),
    message_type            VARCHAR(20)
                            CHECK (message_type IN ('text','template','image','document','audio','video','button','list','location','contact')),
    body_text               TEXT,
    media_s3_key            TEXT,
    media_mime_type         VARCHAR(100),
    button_payload          VARCHAR(500),
    reference_type          VARCHAR(50),
    reference_id            UUID,
    status                  VARCHAR(20)
                            CHECK (status IN ('sent','delivered','read','failed','received')),
    sent_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    delivered_at            TIMESTAMPTZ,
    read_at                 TIMESTAMPTZ,
    failed_at               TIMESTAMPTZ,
    error_code              VARCHAR(50),
    error_message           TEXT,
    cost_units              NUMERIC(10,4),
    raw_payload             JSONB,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_walog_phone_time       ON whatsapp_message_log(phone_e164, sent_at DESC);
CREATE INDEX idx_walog_customer         ON whatsapp_message_log(customer_id, sent_at DESC);
CREATE INDEX idx_walog_reference        ON whatsapp_message_log(reference_type, reference_id);
```

### 86. `onboarding_slides` — mobile app onboarding carousel content
```sql
CREATE TABLE onboarding_slides (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    app_type                VARCHAR(20) NOT NULL DEFAULT 'customer'
                            CHECK (app_type IN ('customer','rider','staff','pos')),
    title                   VARCHAR(200) NOT NULL,
    title_localized         JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    description_localized   JSONB NOT NULL DEFAULT '{}'::jsonb,
    image_url               TEXT NOT NULL,
    image_dark_url          TEXT,
    animation_url           TEXT,
    cta_text                VARCHAR(50),
    cta_deeplink            TEXT,
    background_color        CHAR(7),
    text_color              CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    show_from               TIMESTAMPTZ,
    show_until              TIMESTAMPTZ,
    min_app_version         VARCHAR(20),
    max_app_version         VARCHAR(20),
    target_segments         TEXT[],
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_onbslide_active        ON onboarding_slides(brand_id, app_type, display_order)
    WHERE is_active = true;
```

### 87. `app_banners` — home screen banners / promotional cards
```sql
CREATE TABLE app_banners (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    app_type                VARCHAR(20) NOT NULL DEFAULT 'customer',
    placement               VARCHAR(50) NOT NULL
                            CHECK (placement IN ('home_top','home_middle','home_bottom','services_top','cart_top','order_success','profile')),
    title                   VARCHAR(200),
    title_localized         JSONB NOT NULL DEFAULT '{}'::jsonb,
    subtitle                VARCHAR(300),
    subtitle_localized      JSONB NOT NULL DEFAULT '{}'::jsonb,
    image_url               TEXT NOT NULL,
    image_dark_url          TEXT,
    cta_text                VARCHAR(50),
    cta_deeplink            TEXT,
    external_url            TEXT,
    promotion_id            UUID REFERENCES promotions(id),
    coupon_id               UUID REFERENCES coupons(id),
    background_color        CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    show_from               TIMESTAMPTZ,
    show_until              TIMESTAMPTZ,
    target_audience         VARCHAR(30) DEFAULT 'all',
    target_segments         TEXT[],
    target_cities           TEXT[],
    impressions_count       INTEGER NOT NULL DEFAULT 0,
    clicks_count            INTEGER NOT NULL DEFAULT 0,
    min_app_version         VARCHAR(20),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_banner_active          ON app_banners(brand_id, placement, display_order)
    WHERE is_active = true;
CREATE INDEX idx_banner_active_range    ON app_banners(brand_id, show_from, show_until) WHERE is_active = true;
```

### 88. `mobile_app_config` — remote config per app per platform
```sql
CREATE TABLE mobile_app_config (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    app_type                VARCHAR(20) NOT NULL,
    platform                VARCHAR(10) NOT NULL CHECK (platform IN ('android','ios','web')),
    config_key              VARCHAR(100) NOT NULL,
    config_value            JSONB NOT NULL,
    description             TEXT,
    is_force_update         BOOLEAN NOT NULL DEFAULT false,
    min_app_version         VARCHAR(20),
    max_app_version         VARCHAR(20),
    target_segments         TEXT[],
    rollout_percent         SMALLINT DEFAULT 100 CHECK (rollout_percent BETWEEN 0 AND 100),
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              UUID,
    UNIQUE (brand_id, app_type, platform, config_key)
);
CREATE INDEX idx_mobilecfg_lookup       ON mobile_app_config(brand_id, app_type, platform) WHERE is_active = true;
```

## Section 13: SYSTEM (4 tables: #89–92)

### 89. `system_settings` — brand/franchise-scoped config (singleton key-value)
```sql
CREATE TABLE system_settings (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES brands(id) ON DELETE CASCADE,
    franchise_id            UUID REFERENCES franchises(id) ON DELETE CASCADE,
    store_id                UUID REFERENCES stores(id) ON DELETE CASCADE,
    scope_type              VARCHAR(20) NOT NULL DEFAULT 'platform'
                            CHECK (scope_type IN ('platform','brand','franchise','store')),
    category                VARCHAR(50) NOT NULL,
    setting_key             VARCHAR(100) NOT NULL,
    setting_value           JSONB NOT NULL,
    data_type               VARCHAR(20) NOT NULL DEFAULT 'object'
                            CHECK (data_type IN ('string','number','boolean','object','array')),
    description             TEXT,
    is_encrypted            BOOLEAN NOT NULL DEFAULT false,
    is_readonly             BOOLEAN NOT NULL DEFAULT false,
    requires_restart        BOOLEAN NOT NULL DEFAULT false,
    validation_schema       JSONB,
    default_value           JSONB,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    UNIQUE (scope_type, brand_id, franchise_id, store_id, category, setting_key)
);
CREATE INDEX idx_syssett_brand          ON system_settings(brand_id, category) WHERE brand_id IS NOT NULL;
CREATE INDEX idx_syssett_franchise      ON system_settings(franchise_id, category) WHERE franchise_id IS NOT NULL;
CREATE INDEX idx_syssett_store          ON system_settings(store_id, category) WHERE store_id IS NOT NULL;
```

### 90. `feature_flags` — gradual rollout / kill-switch toggles
```sql
CREATE TABLE feature_flags (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES brands(id) ON DELETE CASCADE,
    flag_key                VARCHAR(100) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    flag_type               VARCHAR(20) NOT NULL DEFAULT 'boolean'
                            CHECK (flag_type IN ('boolean','percentage','segment','variant','kill_switch')),
    default_value           BOOLEAN NOT NULL DEFAULT false,
    is_enabled              BOOLEAN NOT NULL DEFAULT false,
    rollout_percent         SMALLINT DEFAULT 0 CHECK (rollout_percent BETWEEN 0 AND 100),
    target_segments         TEXT[],
    target_franchise_ids    UUID[],
    target_store_ids        UUID[],
    target_user_ids         UUID[],
    target_customer_ids     UUID[],
    target_cities           TEXT[],
    variants                JSONB,
    starts_at               TIMESTAMPTZ,
    ends_at                 TIMESTAMPTZ,
    last_evaluated_at       TIMESTAMPTZ,
    evaluation_count        BIGINT NOT NULL DEFAULT 0,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, flag_key)
);
CREATE INDEX idx_flags_brand            ON feature_flags(brand_id, flag_key) WHERE is_enabled = true;
```

### 91. `file_attachments` — generic polymorphic file registry
```sql
CREATE TABLE file_attachments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID,
    owner_type              VARCHAR(50) NOT NULL,
    owner_id                UUID NOT NULL,
    purpose                 VARCHAR(50) NOT NULL,
    s3_bucket               VARCHAR(100),
    s3_key                  TEXT NOT NULL,
    storage_provider        VARCHAR(20) NOT NULL DEFAULT 's3'
                            CHECK (storage_provider IN ('s3','azure_blob','gcs','local')),
    cdn_url                 TEXT,
    thumbnail_s3_key        TEXT,
    file_name               VARCHAR(500) NOT NULL,
    mime_type               VARCHAR(100) NOT NULL,
    bytes                   BIGINT NOT NULL,
    sha256                  CHAR(64),
    width_px                INTEGER,
    height_px               INTEGER,
    duration_seconds        INTEGER,
    page_count              SMALLINT,
    is_public               BOOLEAN NOT NULL DEFAULT false,
    is_encrypted            BOOLEAN NOT NULL DEFAULT true,
    kms_key_id              VARCHAR(200),
    virus_scanned_at        TIMESTAMPTZ,
    virus_scan_result       VARCHAR(20) CHECK (virus_scan_result IN ('clean','infected','pending','skipped')),
    expires_at              TIMESTAMPTZ,
    uploaded_by_type        VARCHAR(20),
    uploaded_by_id          UUID,
    uploaded_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_accessed_at        TIMESTAMPTZ,
    access_count            INTEGER NOT NULL DEFAULT 0,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    deleted_at              TIMESTAMPTZ
);
CREATE INDEX idx_files_owner            ON file_attachments(owner_type, owner_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_files_purpose          ON file_attachments(purpose) WHERE deleted_at IS NULL;
CREATE INDEX idx_files_expires          ON file_attachments(expires_at) WHERE expires_at IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_files_brand            ON file_attachments(brand_id) WHERE brand_id IS NOT NULL AND deleted_at IS NULL;
```

### 92. `outbox_events` — domain event outbox for reliable async messaging
```sql
CREATE TABLE outbox_events (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID,
    aggregate_type          VARCHAR(100) NOT NULL,
    aggregate_id            UUID NOT NULL,
    event_type              VARCHAR(100) NOT NULL,
    event_version           SMALLINT NOT NULL DEFAULT 1,
    payload                 JSONB NOT NULL,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    correlation_id          UUID,
    causation_id            UUID,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at            TIMESTAMPTZ,
    publish_attempts        SMALLINT NOT NULL DEFAULT 0,
    next_attempt_at         TIMESTAMPTZ,
    last_error              TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','publishing','published','failed','dead_letter')),
    routing_key             VARCHAR(200),
    target_exchange         VARCHAR(100),
    idempotency_key         VARCHAR(100) UNIQUE
);
CREATE INDEX idx_outbox_pending         ON outbox_events(occurred_at, status)
    WHERE status = 'pending';
CREATE INDEX idx_outbox_retry           ON outbox_events(next_attempt_at)
    WHERE status = 'failed' AND publish_attempts < 10;
CREATE INDEX idx_outbox_aggregate       ON outbox_events(aggregate_type, aggregate_id, occurred_at);
```

## Section 14: MATERIALIZED VIEWS (analytics, refreshed by Hangfire jobs)
```sql
-- ----------------------------------------------------------------------------
-- MV-1: daily store revenue (refresh every 15 minutes)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_daily_store_revenue AS
SELECT
    o.brand_id,
    o.franchise_id,
    o.store_id,
    DATE(o.created_at AT TIME ZONE 'Asia/Kolkata') AS revenue_date,
    COUNT(*)                                       AS orders_count,
    COUNT(*) FILTER (WHERE o.status = 'delivered') AS delivered_orders,
    COUNT(*) FILTER (WHERE o.status = 'cancelled') AS cancelled_orders,
    COUNT(*) FILTER (WHERE o.is_express = true)    AS express_orders,
    SUM(o.grand_total)                             AS gross_revenue,
    SUM(o.amount_paid)                             AS collected_amount,
    SUM(o.amount_due)                              AS outstanding_amount,
    SUM(o.refunded_amount)                         AS refund_amount,
    SUM(o.discount_total)                          AS total_discount,
    SUM(o.tax_total)                               AS total_tax,
    AVG(o.grand_total)                             AS avg_order_value,
    COUNT(DISTINCT o.customer_id)                  AS unique_customers
FROM orders o
WHERE o.deleted_at IS NULL
GROUP BY o.brand_id, o.franchise_id, o.store_id, DATE(o.created_at AT TIME ZONE 'Asia/Kolkata');

CREATE UNIQUE INDEX idx_mvdsr_unique ON mv_daily_store_revenue(brand_id, store_id, revenue_date);
CREATE INDEX idx_mvdsr_franchise     ON mv_daily_store_revenue(franchise_id, revenue_date DESC);

-- ----------------------------------------------------------------------------
-- MV-2: monthly franchise revenue (refresh hourly; basis for royalty)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_monthly_franchise_revenue AS
SELECT
    o.brand_id,
    o.franchise_id,
    DATE_TRUNC('month', o.created_at AT TIME ZONE 'Asia/Kolkata')::DATE AS revenue_month,
    COUNT(DISTINCT o.id)                                                AS orders_count,
    COUNT(DISTINCT o.customer_id)                                       AS unique_customers,
    SUM(o.grand_total)                                                  AS gross_revenue,
    SUM(o.subtotal)                                                     AS net_revenue,
    SUM(o.amount_paid)                                                  AS collected_amount,
    SUM(o.refunded_amount)                                              AS refund_amount,
    SUM(o.tax_total)                                                    AS total_tax,
    AVG(o.grand_total)                                                  AS avg_order_value,
    COUNT(*) FILTER (WHERE o.is_express = true)                         AS express_orders
FROM orders o
WHERE o.deleted_at IS NULL AND o.status NOT IN ('cancelled')
GROUP BY o.brand_id, o.franchise_id, DATE_TRUNC('month', o.created_at AT TIME ZONE 'Asia/Kolkata')::DATE;

CREATE UNIQUE INDEX idx_mvmfr_unique ON mv_monthly_franchise_revenue(brand_id, franchise_id, revenue_month);

-- ----------------------------------------------------------------------------
-- MV-3: warehouse throughput (refresh hourly)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_warehouse_throughput AS
SELECT
    g.brand_id,
    g.warehouse_id,
    DATE(g.created_at AT TIME ZONE 'Asia/Kolkata') AS throughput_date,
    COUNT(*)                                                            AS garments_received,
    COUNT(*) FILTER (WHERE g.current_stage = 'delivered')                AS garments_delivered,
    COUNT(*) FILTER (WHERE g.current_stage IN ('lost','damaged'))        AS issues_count,
    COUNT(*) FILTER (WHERE g.rewash_count > 0)                           AS rewash_count,
    AVG(EXTRACT(EPOCH FROM (g.actual_completion_at - g.created_at))/3600)
        FILTER (WHERE g.actual_completion_at IS NOT NULL)                AS avg_tat_hours
FROM garments g
WHERE g.warehouse_id IS NOT NULL
GROUP BY g.brand_id, g.warehouse_id, DATE(g.created_at AT TIME ZONE 'Asia/Kolkata');

CREATE UNIQUE INDEX idx_mvwt_unique ON mv_warehouse_throughput(brand_id, warehouse_id, throughput_date);

-- ----------------------------------------------------------------------------
-- MV-4: customer lifetime value (refresh daily)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_customer_ltv AS
SELECT
    c.brand_id,
    c.id                                                                  AS customer_id,
    c.customer_segment,
    COUNT(o.id)                                                           AS lifetime_orders,
    SUM(o.grand_total)                                                    AS lifetime_revenue,
    AVG(o.grand_total)                                                    AS avg_order_value,
    MIN(o.created_at)                                                     AS first_order_at,
    MAX(o.created_at)                                                     AS last_order_at,
    EXTRACT(DAY FROM (now() - MAX(o.created_at)))                         AS days_since_last_order,
    COUNT(*) FILTER (WHERE o.is_express = true)                           AS express_orders,
    COUNT(*) FILTER (WHERE o.status = 'cancelled')                        AS cancelled_orders,
    COUNT(DISTINCT cp.id)                                                 AS active_packages,
    c.loyalty_points_balance,
    c.wallet_balance
FROM customers c
LEFT JOIN orders o ON o.customer_id = c.id AND o.deleted_at IS NULL
LEFT JOIN customer_packages cp ON cp.customer_id = c.id AND cp.status = 'active'
WHERE c.deleted_at IS NULL
GROUP BY c.brand_id, c.id, c.customer_segment, c.loyalty_points_balance, c.wallet_balance;

CREATE UNIQUE INDEX idx_mvcltv_unique ON mv_customer_ltv(brand_id, customer_id);
CREATE INDEX idx_mvcltv_revenue       ON mv_customer_ltv(brand_id, lifetime_revenue DESC);

-- ----------------------------------------------------------------------------
-- MV-5: rider performance (refresh hourly)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_rider_performance AS
SELECT
    r.brand_id,
    r.franchise_id,
    r.id                                                              AS rider_id,
    r.rider_code,
    DATE(da.assigned_at AT TIME ZONE 'Asia/Kolkata')                  AS perf_date,
    COUNT(da.id)                                                      AS assignments_total,
    COUNT(*) FILTER (WHERE da.status = 'completed')                   AS assignments_completed,
    COUNT(*) FILTER (WHERE da.status IN ('cancelled','failed'))       AS assignments_failed,
    COUNT(*) FILTER (WHERE da.leg_type = 'pickup' AND da.status = 'completed')   AS pickups_done,
    COUNT(*) FILTER (WHERE da.leg_type = 'delivery' AND da.status = 'completed') AS deliveries_done,
    SUM(da.distance_km)                                               AS total_km,
    AVG(da.duration_minutes)                                          AS avg_duration_min,
    r.rating_average,
    r.completion_rate
FROM riders r
LEFT JOIN delivery_assignments da ON da.rider_id = r.id
WHERE r.deleted_at IS NULL
GROUP BY r.brand_id, r.franchise_id, r.id, r.rider_code, r.rating_average, r.completion_rate,
         DATE(da.assigned_at AT TIME ZONE 'Asia/Kolkata');

CREATE UNIQUE INDEX idx_mvrp_unique  ON mv_rider_performance(brand_id, rider_id, perf_date);
CREATE INDEX idx_mvrp_franchise_date ON mv_rider_performance(franchise_id, perf_date DESC);


-- ============================================================================
-- PARTITION CREATION (initial set; pg_partman maintains thereafter)
-- ============================================================================

-- audit_logs: monthly partitions for current + next 3 months
SELECT partman.create_parent(
    p_parent_table     => 'public.audit_logs',
    p_control          => 'occurred_at',
    p_type             => 'native',
    p_interval         => '1 month',
    p_premake          => 6
);

-- orders: monthly partitions
SELECT partman.create_parent(
    p_parent_table     => 'public.orders',
    p_control          => 'created_at',
    p_type             => 'native',
    p_interval         => '1 month',
    p_premake          => 6
);

-- process_logs: monthly
SELECT partman.create_parent(
    p_parent_table     => 'public.process_logs',
    p_control          => 'occurred_at',
    p_type             => 'native',
    p_interval         => '1 month',
    p_premake          => 6
);

-- notifications_log: monthly
SELECT partman.create_parent(
    p_parent_table     => 'public.notifications_log',
    p_control          => 'sent_at',
    p_type             => 'native',
    p_interval         => '1 month',
    p_premake          => 3
);

-- rider_location_pings: daily, 14 day retention
SELECT partman.create_parent(
    p_parent_table     => 'public.rider_location_pings',
    p_control          => 'pinged_at',
    p_type             => 'native',
    p_interval         => '1 day',
    p_premake          => 7
);
UPDATE partman.part_config SET retention = '14 days', retention_keep_table = false
WHERE parent_table = 'public.rider_location_pings';


-- ============================================================================
-- ROW-LEVEL SECURITY — session var pattern
-- ============================================================================
-- Application must SET LOCAL these per request (via DbConnectionInterceptor):
--   SET LOCAL app.bypass_rls         = 'false';
--   SET LOCAL app.current_brand_id   = '<uuid>';
--   SET LOCAL app.current_franchise_id = '<uuid>';
--   SET LOCAL app.current_store_id   = '<uuid>';
--   SET LOCAL app.current_user_id    = '<uuid>';
-- Background jobs / migrations:
--   SET LOCAL app.bypass_rls = 'true';
-- RLS is enabled and policies defined per-table above for tenant-scoped tables.


-- ============================================================================
-- DATA-RETENTION POLICIES (informational — implement via pg_partman + jobs)
-- ============================================================================
-- audit_logs           : retain 7 years (DPDP), archive to cold storage after 2 yrs
-- orders               : retain forever (financial records); compress after 2 yrs
-- process_logs         : retain 18 months
-- notifications_log    : retain 6 months
-- rider_location_pings : retain 14 days (privacy-sensitive)
-- otp_codes            : DELETE WHERE expires_at < now() - INTERVAL '1 day'
-- refresh_tokens       : DELETE WHERE expires_at < now() OR revoked_at IS NOT NULL
-- garment_inspection_photos : retain 90 days post-delivery, then archive


-- ============================================================================
-- END OF SCHEMA — 92 TABLES + 5 MATERIALIZED VIEWS
-- ============================================================================
-- Counts:
--   Section 1  Tenancy & Org              10 tables   (#1–10)
--   Section 2  Identity & Access          11 tables   (#11–21)
--   Section 3  Customers                   5 tables   (#22–26)
--   Section 4  Catalog & Pricing           9 tables   (#27–35)
--   Section 5  Orders & Pickups            9 tables   (#36–44)
--   Section 6  Garments & Tracking         5 tables   (#45–49)
--   Section 7  Warehouse Operations        6 tables   (#50–55)
--   Section 8  Riders & Delivery           4 tables   (#56–59)
--   Section 9  Packages, Loyalty, Coupons  8 tables   (#60–67)
--   Section 10 Payments & Wallet           5 tables   (#68–72)
--   Section 11 Finance & Royalty           8 tables   (#73–80)
--   Section 12 Notifications & CMS         8 tables   (#81–88)
--   Section 13 System                      4 tables   (#89–92)
--   --------------------------------------------
--   TOTAL                                 92 tables
--   + 5 materialized views (analytics)
--   + 5 partitioned tables (orders, audit_logs, process_logs, notifications_log, rider_location_pings)
-- ============================================================================
```

## Section 15: Customer Subscriptions (module A)

### 93. `subscription_plans` — recurring plan catalog (brand-scoped)
```sql
CREATE TABLE subscription_plans (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    tier                    VARCHAR(30) NOT NULL DEFAULT 'standard'
                            CHECK (tier IN ('basic','standard','premium','custom')),
    billing_interval        VARCHAR(20) NOT NULL DEFAULT 'monthly'
                            CHECK (billing_interval IN ('weekly','monthly','quarterly','half_yearly','yearly')),
    interval_count          SMALLINT NOT NULL DEFAULT 1 CHECK (interval_count > 0),
    price                   NUMERIC(14,2) NOT NULL CHECK (price >= 0),
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    trial_days              SMALLINT NOT NULL DEFAULT 0,
    -- quota model: what the plan includes per billing cycle
    quota_type              VARCHAR(20) NOT NULL DEFAULT 'credit'
                            CHECK (quota_type IN ('credit','order_count','weight_kg','unlimited')),
    quota_value             NUMERIC(14,2),                  -- credits / orders / kg per cycle; NULL if unlimited
    rollover_unused         BOOLEAN NOT NULL DEFAULT false, -- unused quota carries to next cycle?
    max_rollover            NUMERIC(14,2),
    overage_discount_percent NUMERIC(5,2) NOT NULL DEFAULT 0, -- discount on pay-as-you-go beyond quota
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    excluded_services       UUID[] NOT NULL DEFAULT '{}',
    pickup_included         BOOLEAN NOT NULL DEFAULT true,
    delivery_included       BOOLEAN NOT NULL DEFAULT true,
    express_included        BOOLEAN NOT NULL DEFAULT false,
    max_active_subscribers  INTEGER,                        -- cap on concurrent subscribers; NULL = unlimited
    current_subscriber_count INTEGER NOT NULL DEFAULT 0,
    gateway                 VARCHAR(30) CHECK (gateway IN ('razorpay','payu','cashfree','phonepe','none')),
    gateway_plan_id         VARCHAR(100),                   -- Razorpay plan_id
    terms_and_conditions    TEXT,
    icon_url                TEXT,
    color_hex               CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_public               BOOLEAN NOT NULL DEFAULT true,
    is_featured             BOOLEAN NOT NULL DEFAULT false,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','active','paused','retired')),
    available_from          TIMESTAMPTZ,
    available_to            TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_subplan_brand_active   ON subscription_plans(brand_id, tier)
    WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_subplan_services_gin   ON subscription_plans USING GIN (applicable_services);

ALTER TABLE subscription_plans ENABLE ROW LEVEL SECURITY;
CREATE POLICY subplan_tenant ON subscription_plans
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 94. `payment_mandates` — customer recurring-payment authorization (UPI AutoPay / e-mandate / NACH)
```sql
CREATE TABLE payment_mandates (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    mandate_type            VARCHAR(20) NOT NULL
                            CHECK (mandate_type IN ('upi_autopay','emandate','card','nach')),
    gateway                 VARCHAR(30) NOT NULL DEFAULT 'razorpay',
    gateway_mandate_id      VARCHAR(100),
    gateway_token           VARCHAR(200),
    gateway_customer_id     VARCHAR(100),
    max_amount              NUMERIC(14,2) NOT NULL CHECK (max_amount > 0),   -- per-debit cap authorized
    debit_frequency         VARCHAR(20) NOT NULL DEFAULT 'as_presented'
                            CHECK (debit_frequency IN ('as_presented','weekly','monthly','quarterly','yearly')),
    upi_vpa                 VARCHAR(100),
    card_last4              CHAR(4),
    card_network            VARCHAR(20),
    bank_name               VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'created'
                            CHECK (status IN ('created','pending','active','paused','revoked','expired','failed')),
    start_at                TIMESTAMPTZ,
    end_at                  TIMESTAMPTZ,
    authenticated_at        TIMESTAMPTZ,
    revoked_at              TIMESTAMPTZ,
    revoked_reason          TEXT,
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    gateway_response        JSONB,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_mandate_customer       ON payment_mandates(customer_id) WHERE status = 'active';
CREATE INDEX idx_mandate_gateway        ON payment_mandates(gateway, gateway_mandate_id) WHERE gateway_mandate_id IS NOT NULL;
CREATE INDEX idx_mandate_status         ON payment_mandates(brand_id, status);

ALTER TABLE payment_mandates ENABLE ROW LEVEL SECURITY;
CREATE POLICY mandate_tenant ON payment_mandates
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 95. `customer_subscriptions` — an active recurring subscription instance
```sql
CREATE TABLE customer_subscriptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    plan_id                 UUID NOT NULL REFERENCES subscription_plans(id) ON DELETE RESTRICT,
    mandate_id              UUID REFERENCES payment_mandates(id),
    subscription_number     VARCHAR(40) NOT NULL,
    -- snapshots (plan can change later; subscription keeps what it was sold)
    price_snapshot          NUMERIC(14,2) NOT NULL,
    billing_interval        VARCHAR(20) NOT NULL,
    interval_count          SMALLINT NOT NULL DEFAULT 1,
    quota_type              VARCHAR(20) NOT NULL,
    quota_value             NUMERIC(14,2),
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    -- lifecycle
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','trialing','active','past_due','paused','cancelled','expired')),
    auto_renew              BOOLEAN NOT NULL DEFAULT true,
    current_period_start    TIMESTAMPTZ,
    current_period_end      TIMESTAMPTZ,
    next_billing_at         TIMESTAMPTZ,
    trial_ends_at           TIMESTAMPTZ,
    credits_remaining       NUMERIC(14,2) NOT NULL DEFAULT 0,   -- quota left in current cycle
    started_at              TIMESTAMPTZ,
    activated_at            TIMESTAMPTZ,
    cancel_at_period_end    BOOLEAN NOT NULL DEFAULT false,
    cancelled_at            TIMESTAMPTZ,
    cancel_reason           TEXT,
    paused_at               TIMESTAMPTZ,
    pause_resumes_at        TIMESTAMPTZ,
    ended_at                TIMESTAMPTZ,
    -- dunning
    past_due_since          TIMESTAMPTZ,
    dunning_attempts        SMALLINT NOT NULL DEFAULT 0,
    failed_payment_count    SMALLINT NOT NULL DEFAULT 0,
    total_cycles_billed     INTEGER NOT NULL DEFAULT 0,
    gateway_subscription_id VARCHAR(100),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                 INTEGER NOT NULL DEFAULT 1,
    UNIQUE (brand_id, subscription_number)
);
CREATE INDEX idx_custsub_customer_active ON customer_subscriptions(customer_id)
    WHERE status IN ('trialing','active','past_due');
CREATE INDEX idx_custsub_plan            ON customer_subscriptions(plan_id);
CREATE INDEX idx_custsub_due             ON customer_subscriptions(next_billing_at)
    WHERE status IN ('active','trialing') AND auto_renew = true;
CREATE INDEX idx_custsub_pastdue         ON customer_subscriptions(brand_id, past_due_since)
    WHERE status = 'past_due';

ALTER TABLE customer_subscriptions ENABLE ROW LEVEL SECURITY;
CREATE POLICY custsub_tenant ON customer_subscriptions
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 96. `subscription_invoices` — one invoice per billing cycle
```sql
CREATE TABLE subscription_invoices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES customer_subscriptions(id) ON DELETE RESTRICT,
    customer_id             UUID NOT NULL,
    invoice_number          VARCHAR(40) NOT NULL UNIQUE,
    billing_period_start    TIMESTAMPTZ NOT NULL,
    billing_period_end      TIMESTAMPTZ NOT NULL,
    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total          NUMERIC(14,2) NOT NULL DEFAULT 0,
    taxable_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    payment_id              UUID REFERENCES payments(id),
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','issued','paid','past_due','failed','void','refunded')),
    attempt_count           SMALLINT NOT NULL DEFAULT 0,
    issued_at               TIMESTAMPTZ,
    due_at                  TIMESTAMPTZ,
    paid_at                 TIMESTAMPTZ,
    gateway_invoice_id      VARCHAR(100),
    invoice_s3_key          TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (customer_subscription_id, billing_period_start)
);
CREATE INDEX idx_subinv_subscription    ON subscription_invoices(customer_subscription_id, billing_period_start DESC);
CREATE INDEX idx_subinv_customer        ON subscription_invoices(customer_id, created_at DESC);
CREATE INDEX idx_subinv_status          ON subscription_invoices(brand_id, status, due_at);

ALTER TABLE subscription_invoices ENABLE ROW LEVEL SECURITY;
CREATE POLICY subinv_tenant ON subscription_invoices
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 97. `subscription_billing_attempts` — each charge attempt against the mandate (dunning, append-only)
```sql
CREATE TABLE subscription_billing_attempts (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES customer_subscriptions(id) ON DELETE CASCADE,
    subscription_invoice_id UUID NOT NULL REFERENCES subscription_invoices(id) ON DELETE CASCADE,
    mandate_id              UUID REFERENCES payment_mandates(id),
    attempt_number          SMALLINT NOT NULL DEFAULT 1,
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    gateway                 VARCHAR(30),
    gateway_payment_id      VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'initiated'
                            CHECK (status IN ('initiated','success','failed','cancelled')),
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    gateway_response        JSONB,
    attempted_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    next_retry_at           TIMESTAMPTZ,
    idempotency_key         VARCHAR(100) UNIQUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_subattempt_invoice     ON subscription_billing_attempts(subscription_invoice_id, attempt_number);
CREATE INDEX idx_subattempt_subscription ON subscription_billing_attempts(customer_subscription_id, attempted_at DESC);
CREATE INDEX idx_subattempt_retry       ON subscription_billing_attempts(next_retry_at) WHERE status = 'failed';
```

### 98. `subscription_usage_ledger` — per-cycle quota allocation & consumption (append-only)
```sql
CREATE TABLE subscription_usage_ledger (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES customer_subscriptions(id) ON DELETE RESTRICT,
    customer_id             UUID NOT NULL,
    billing_period_start    TIMESTAMPTZ NOT NULL,
    billing_period_end      TIMESTAMPTZ NOT NULL,
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('allocate','consume','rollover','expire','adjustment','refund')),
    amount                  NUMERIC(14,2) NOT NULL,         -- in quota units (credit/order/kg)
    balance_before          NUMERIC(14,2) NOT NULL,
    balance_after           NUMERIC(14,2) NOT NULL,
    order_id                UUID,                            -- set when consumed by an order
    order_created_at        TIMESTAMPTZ,
    reference_type          VARCHAR(30),
    reference_id            UUID,
    notes                   TEXT,
    performed_by_type       VARCHAR(20) DEFAULT 'system',
    performed_by_id         UUID,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_subusage_subscription  ON subscription_usage_ledger(customer_subscription_id, occurred_at DESC);
CREATE INDEX idx_subusage_period        ON subscription_usage_ledger(customer_subscription_id, billing_period_start);
CREATE INDEX idx_subusage_order         ON subscription_usage_ledger(order_id) WHERE order_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- MV: customer subscription MRR (refresh hourly)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_subscription_mrr AS
SELECT
    cs.brand_id,
    sp.tier,
    DATE_TRUNC('month', now())::DATE                                    AS as_of_month,
    COUNT(*) FILTER (WHERE cs.status = 'active')                        AS active_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'trialing')                      AS trialing_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'past_due')                      AS past_due_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'cancelled')                     AS cancelled_subscriptions,
    -- normalize every interval to a monthly figure
    SUM(
      CASE cs.billing_interval
        WHEN 'weekly'      THEN cs.price_snapshot * 52.0/12.0 / cs.interval_count
        WHEN 'monthly'     THEN cs.price_snapshot / cs.interval_count
        WHEN 'quarterly'   THEN cs.price_snapshot / (3 * cs.interval_count)
        WHEN 'half_yearly' THEN cs.price_snapshot / (6 * cs.interval_count)
        WHEN 'yearly'      THEN cs.price_snapshot / (12 * cs.interval_count)
        ELSE cs.price_snapshot
      END
    ) FILTER (WHERE cs.status IN ('active','past_due'))                 AS mrr
FROM customer_subscriptions cs
JOIN subscription_plans sp ON sp.id = cs.plan_id
GROUP BY cs.brand_id, sp.tier;

CREATE UNIQUE INDEX idx_mvsubmrr_unique ON mv_subscription_mrr(brand_id, tier, as_of_month);
```

## Section 16: Franchise SaaS Subscriptions (module B)

### 99. `platform_plans` — SaaS tiers the platform offers to franchises (global catalog)
```sql
CREATE TABLE platform_plans (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES brands(id) ON DELETE CASCADE,  -- NULL = platform-global; set = white-label brand's own tiers
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    description             TEXT,
    tier                    VARCHAR(30) NOT NULL DEFAULT 'starter'
                            CHECK (tier IN ('starter','growth','pro','enterprise','custom')),
    billing_interval        VARCHAR(20) NOT NULL DEFAULT 'monthly'
                            CHECK (billing_interval IN ('monthly','quarterly','yearly')),
    interval_count          SMALLINT NOT NULL DEFAULT 1 CHECK (interval_count > 0),
    price                   NUMERIC(14,2) NOT NULL CHECK (price >= 0),
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    annual_discount_percent NUMERIC(5,2) NOT NULL DEFAULT 0,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    trial_days              SMALLINT NOT NULL DEFAULT 0,
    -- quotas (NULL = unlimited)
    max_stores              INTEGER,
    max_warehouses          INTEGER,
    max_users               INTEGER,
    max_orders_per_month    INTEGER,
    max_riders              INTEGER,
    -- overage rates (charged when a quota is exceeded)
    overage_per_order       NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_per_store       NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_per_user        NUMERIC(14,2) NOT NULL DEFAULT 0,
    -- what's included
    features                JSONB NOT NULL DEFAULT '{}'::jsonb,    -- feature_flag keys enabled by this plan
    support_level           VARCHAR(20) NOT NULL DEFAULT 'email'
                            CHECK (support_level IN ('community','email','priority','dedicated')),
    is_public               BOOLEAN NOT NULL DEFAULT true,
    is_featured             BOOLEAN NOT NULL DEFAULT false,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    gateway                 VARCHAR(30),
    gateway_plan_id         VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','active','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_platplan_active        ON platform_plans(tier) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_platplan_brand         ON platform_plans(brand_id) WHERE brand_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_platplan_features_gin  ON platform_plans USING GIN (features);
```

### 100. `franchise_subscriptions` — a franchise's SaaS subscription instance
```sql
CREATE TABLE franchise_subscriptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL REFERENCES franchises(id) ON DELETE RESTRICT,
    platform_plan_id        UUID NOT NULL REFERENCES platform_plans(id) ON DELETE RESTRICT,
    subscription_number     VARCHAR(40) NOT NULL,
    -- snapshots
    price_snapshot          NUMERIC(14,2) NOT NULL,
    billing_interval        VARCHAR(20) NOT NULL,
    interval_count          SMALLINT NOT NULL DEFAULT 1,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    -- quota snapshots (so a mid-term plan change doesn't retroactively shift limits)
    max_stores              INTEGER,
    max_warehouses          INTEGER,
    max_users               INTEGER,
    max_orders_per_month    INTEGER,
    max_riders              INTEGER,
    -- lifecycle
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','trialing','active','past_due','suspended','cancelled','expired')),
    auto_renew              BOOLEAN NOT NULL DEFAULT true,
    payment_method          VARCHAR(20) NOT NULL DEFAULT 'invoice'
                            CHECK (payment_method IN ('invoice','auto_debit')),
    gateway_mandate_id      VARCHAR(100),
    gateway_subscription_id VARCHAR(100),
    current_period_start    TIMESTAMPTZ,
    current_period_end      TIMESTAMPTZ,
    next_billing_at         TIMESTAMPTZ,
    trial_ends_at           TIMESTAMPTZ,
    current_period_orders   INTEGER NOT NULL DEFAULT 0,   -- usage counter for overage
    started_at              TIMESTAMPTZ,
    activated_at            TIMESTAMPTZ,
    cancel_at_period_end    BOOLEAN NOT NULL DEFAULT false,
    cancelled_at            TIMESTAMPTZ,
    cancel_reason           TEXT,
    -- dunning / suspension
    past_due_since          TIMESTAMPTZ,
    dunning_attempts        SMALLINT NOT NULL DEFAULT 0,
    suspend_grace_until     TIMESTAMPTZ,
    suspended_at            TIMESTAMPTZ,
    suspended_reason        TEXT,
    reactivated_at          TIMESTAMPTZ,
    ended_at                TIMESTAMPTZ,
    total_cycles_billed     INTEGER NOT NULL DEFAULT 0,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                 INTEGER NOT NULL DEFAULT 1,
    UNIQUE (brand_id, subscription_number)
);
-- one live SaaS subscription per franchise
CREATE UNIQUE INDEX idx_fransub_one_live ON franchise_subscriptions(franchise_id)
    WHERE status IN ('pending','trialing','active','past_due','suspended');
CREATE INDEX idx_fransub_plan            ON franchise_subscriptions(platform_plan_id);
CREATE INDEX idx_fransub_due             ON franchise_subscriptions(next_billing_at)
    WHERE status IN ('active','trialing') AND auto_renew = true;
CREATE INDEX idx_fransub_suspend         ON franchise_subscriptions(brand_id, status)
    WHERE status IN ('past_due','suspended');

ALTER TABLE franchise_subscriptions ENABLE ROW LEVEL SECURITY;
CREATE POLICY fransub_tenant ON franchise_subscriptions
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 101. `franchise_subscription_invoices` — monthly SaaS invoice (base + overage)
```sql
CREATE TABLE franchise_subscription_invoices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    franchise_subscription_id UUID NOT NULL REFERENCES franchise_subscriptions(id) ON DELETE RESTRICT,
    invoice_number          VARCHAR(40) NOT NULL UNIQUE,
    billing_period_start    TIMESTAMPTZ NOT NULL,
    billing_period_end      TIMESTAMPTZ NOT NULL,
    base_amount             NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total          NUMERIC(14,2) NOT NULL DEFAULT 0,
    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    usage_snapshot          JSONB NOT NULL DEFAULT '{}'::jsonb,  -- {orders, stores, users, warehouses, riders}
    payment_id              UUID REFERENCES payments(id),
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','issued','sent','paid','past_due','failed','void')),
    attempt_count           SMALLINT NOT NULL DEFAULT 0,
    issued_at               TIMESTAMPTZ,
    due_at                  TIMESTAMPTZ,
    paid_at                 TIMESTAMPTZ,
    invoice_s3_key          TEXT,
    invoice_pdf_url         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (franchise_subscription_id, billing_period_start)
);
CREATE INDEX idx_fransubinv_subscription ON franchise_subscription_invoices(franchise_subscription_id, billing_period_start DESC);
CREATE INDEX idx_fransubinv_franchise    ON franchise_subscription_invoices(franchise_id, created_at DESC);
CREATE INDEX idx_fransubinv_status       ON franchise_subscription_invoices(brand_id, status, due_at);
CREATE INDEX idx_fransubinv_overdue      ON franchise_subscription_invoices(due_at)
    WHERE status IN ('issued','sent','past_due');

ALTER TABLE franchise_subscription_invoices ENABLE ROW LEVEL SECURITY;
CREATE POLICY fransubinv_tenant ON franchise_subscription_invoices
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

### 102. `franchise_subscription_events` — lifecycle audit (append-only)
```sql
CREATE TABLE franchise_subscription_events (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_subscription_id UUID NOT NULL REFERENCES franchise_subscriptions(id) ON DELETE CASCADE,
    franchise_id            UUID NOT NULL,
    event_type              VARCHAR(30) NOT NULL
                            CHECK (event_type IN ('created','trial_started','activated','renewed','upgraded',
                                                  'downgraded','payment_failed','past_due','suspended',
                                                  'reactivated','cancelled','expired','plan_changed')),
    from_plan_id            UUID,
    to_plan_id              UUID,
    from_status             VARCHAR(20),
    to_status               VARCHAR(20),
    amount                  NUMERIC(14,2),
    reason                  VARCHAR(200),
    notes                   TEXT,
    actor_type              VARCHAR(20) NOT NULL DEFAULT 'system'
                            CHECK (actor_type IN ('system','platform_admin','brand_admin','franchise_owner','job','webhook')),
    actor_id                UUID,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_fransubevt_subscription ON franchise_subscription_events(franchise_subscription_id, occurred_at DESC);
CREATE INDEX idx_fransubevt_type         ON franchise_subscription_events(brand_id, event_type, occurred_at DESC);

-- ----------------------------------------------------------------------------
-- MV: franchise SaaS MRR / ARR (platform revenue from franchises; refresh hourly)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_franchise_saas_mrr AS
SELECT
    fs.brand_id,
    pp.tier,
    DATE_TRUNC('month', now())::DATE                                    AS as_of_month,
    COUNT(*) FILTER (WHERE fs.status = 'active')                        AS active_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'trialing')                      AS trialing_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'past_due')                      AS past_due_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'suspended')                     AS suspended_subscriptions,
    SUM(
      CASE fs.billing_interval
        WHEN 'monthly'   THEN fs.price_snapshot / fs.interval_count
        WHEN 'quarterly' THEN fs.price_snapshot / (3 * fs.interval_count)
        WHEN 'yearly'    THEN fs.price_snapshot / (12 * fs.interval_count)
        ELSE fs.price_snapshot
      END
    ) FILTER (WHERE fs.status IN ('active','past_due'))                 AS mrr,
    SUM(
      CASE fs.billing_interval
        WHEN 'monthly'   THEN fs.price_snapshot * 12 / fs.interval_count
        WHEN 'quarterly' THEN fs.price_snapshot * 4 / fs.interval_count
        WHEN 'yearly'    THEN fs.price_snapshot / fs.interval_count
        ELSE fs.price_snapshot * 12
      END
    ) FILTER (WHERE fs.status IN ('active','past_due'))                 AS arr
FROM franchise_subscriptions fs
JOIN platform_plans pp ON pp.id = fs.platform_plan_id
GROUP BY fs.brand_id, pp.tier;

CREATE UNIQUE INDEX idx_mvfransaasmrr_unique ON mv_franchise_saas_mrr(brand_id, tier, as_of_month);
```
