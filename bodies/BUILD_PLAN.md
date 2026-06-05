# Laundry Ghar — Production Build Plan

**Version:** 1.0
**Stack:** .NET 10 (Clean Architecture) · React 19 + TS · React Native (Expo) · PostgreSQL 16+
**Model:** Multi-tenant franchise SaaS (Platform → Brand → Franchise → Store/Warehouse)
**Scope:** Supersedes original BRD v2.0 stack (PHP/Laravel/MySQL → .NET/PG)

---

## 0. What changed vs BRD v2.0

| Concern    | BRD v2.0             | This Plan                                                          | Why                                                               |
| ---------- | -------------------- | ------------------------------------------------------------------ | ----------------------------------------------------------------- |
| Backend    | PHP 8.2 / Laravel 10 | .NET 10 / Clean Arch / MediatR                                     | Type safety, team strength, perf, Aspire orchestration            |
| DB         | MySQL 8              | PostgreSQL 16+                                                     | JSONB, RLS, partitioning, GIN indexes, materialized views         |
| Org model  | Super Admin → Store  | Platform → Brand → Franchise Owner → Franchise → Store → Warehouse | Real franchise model needs royalty, territory, white-label        |
| Tables     | ~25                  | ~90                                                                | Production needs audit, RBAC, RLS, ledger, compliance, partitions |
| Tenancy    | Single               | Multi-tenant w/ RLS                                                | Franchisee data isolation is non-negotiable                       |
| Mobile     | Android only         | React Native (iOS + Android)                                       | Single codebase, faster iteration                                 |
| Compliance | GDPR mention         | DPDP Act 2023 + Play Store + PCI-DSS SAQ-A                         | India-first                                                       |

---

## 1. Organizational Hierarchy (Franchise Model)

```
Platform (Laundry Ghar HQ)
  │
  ├─ Brand (white-label support; usually 1, but architecture supports N)
  │    │
  │    ├─ Territory (geographic exclusivity zones)
  │    │
  │    └─ Franchise Owner (legal entity owning the franchise rights)
  │          │
  │          └─ Franchise (operational business unit; can own multiple stores)
  │                │
  │                ├─ Store (physical pickup/walk-in location)
  │                │     └─ Store Staff, POS users
  │                │
  │                └─ Warehouse (processing facility; can serve N stores)
  │                      └─ Warehouse Staff, Riders
  │
  └─ Customers (belong to Brand, can transact at any Franchise/Store)
```

### Roles per Scope

| Role                   | Scope                                | Key Permissions                                         |
| ---------------------- | ------------------------------------ | ------------------------------------------------------- |
| `platform_admin`       | Platform                             | Everything, all brands                                  |
| `brand_admin`          | Brand                                | All franchises within brand, brand-wide pricing/promos  |
| `regional_manager`     | Territory                            | All franchises in territory, roll-up reports            |
| `franchise_owner`      | Franchise                            | Franchise-level KPIs, royalty invoices, store config    |
| `store_admin`          | Store                                | Store orders, staff, local pricing overrides, cash book |
| `store_staff`          | Store                                | POS, walk-in orders, garment inspection, tagging        |
| `warehouse_supervisor` | Warehouse                            | Sorting, washing status, QC, stock recon                |
| `warehouse_staff`      | Warehouse                            | Process logs, tag scans                                 |
| `rider`                | Store/Warehouse                      | Assigned pickups/deliveries, GPS, OTP                   |
| `customer`             | Brand                                | Mobile app surfaces                                     |
| `auditor`              | Platform/Brand/Franchise (read-only) | All financial + ops reports                             |

RBAC is **database-driven** (not hardcoded enums). Roles can be scoped at any node in the org tree.

---

## 2. System Architecture

### 2.1 High-Level

```
┌──────────────────────────────────────────────────────────────────┐
│  Clients                                                          │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────────┐ │
│  │ Customer    │ │ Admin Web   │ │ POS Web     │ │ Rider App  │ │
│  │ Mobile (RN) │ │ (React 19)  │ │ (React 19)  │ │ (RN)       │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ └────────────┘ │
└──────────────────────────┬────────────────────────────────────────┘
                           │ HTTPS / TLS 1.3
                ┌──────────▼──────────┐
                │ YARP API Gateway    │ ← rate limit, tenant resolve, auth
                └──────────┬──────────┘
                           │
        ┌──────────────────┼──────────────────────┐
        │                  │                      │
┌───────▼──────┐   ┌───────▼──────┐      ┌────────▼────────┐
│ Identity API │   │ Core API     │      │ Notification    │
│ (JWT + OTP)  │   │ (CQRS)       │      │ Worker          │
└──────┬───────┘   └──────┬───────┘      └────────┬────────┘
       │                  │                       │
       │           ┌──────▼─────────┐             │
       │           │ MassTransit    │◄────────────┘
       │           │ + RabbitMQ     │
       │           └──────┬─────────┘
       │                  │
       │           ┌──────▼─────────────────────────────────┐
       │           │ Workers (Hangfire / background)        │
       │           │  • Order lifecycle • Royalty calc      │
       │           │  • Cash book close • Stock recon       │
       │           │  • Photo compress  • WhatsApp dispatch │
       │           └──────┬─────────────────────────────────┘
       │                  │
       └────────┬─────────┘
                │
       ┌────────▼──────────┐     ┌──────────────┐
       │ PostgreSQL 16     │     │ Redis        │
       │ (RLS, partitions, │     │ (cache, OTP, │
       │  materialized vw) │     │  rate limit) │
       └───────────────────┘     └──────────────┘

       External: Razorpay │ MSG91 │ WhatsApp BSP │ Google Maps │ S3
```

### 2.2 .NET Solution Layout (Clean Architecture)

```
LaundryGhar.sln
├── src/
│   ├── LaundryGhar.Domain/                  ← Entities, Value Objects, Domain Events
│   ├── LaundryGhar.Application/             ← Use cases (MediatR handlers), DTOs, validators
│   ├── LaundryGhar.Infrastructure/          ← EF Core, repos, external integrations
│   │   ├── Persistence/                     ← DbContext, migrations, RLS interceptor
│   │   ├── Identity/                        ← JWT, OTP
│   │   ├── Integrations/                    ← Razorpay, MSG91, WhatsApp, Maps, S3
│   │   └── Messaging/                       ← MassTransit, outbox
│   ├── LaundryGhar.Api/                     ← Minimal API endpoints, OpenAPI
│   ├── LaundryGhar.Worker/                  ← Hangfire jobs, MassTransit consumers
│   ├── LaundryGhar.Gateway/                 ← YARP, tenant resolution
│   └── LaundryGhar.AppHost/                 ← .NET Aspire orchestration
└── tests/
    ├── LaundryGhar.Domain.Tests/
    ├── LaundryGhar.Application.Tests/
    ├── LaundryGhar.Infrastructure.Tests/
    └── LaundryGhar.Api.IntegrationTests/    ← Testcontainers (PG, Redis, RabbitMQ)
```

### 2.3 Frontend Layouts

**Admin Web (`admin-web/`)** — React 19 + Vite + TS + TanStack Query + Zustand + React Router + TailwindCSS + shadcn/ui + React Hook Form + Zod. Used by Platform/Brand/Franchise/Store admins. Has tenant switcher in topbar.

**POS Web (`pos-web/`)** — Same stack, stripped down. Optimized for touch tablet + barcode scanner + label printer. Offline-first with IndexedDB queue + sync.

**Customer Mobile (`customer-mobile/`)** — React Native + Expo SDK 52 + TS + TanStack Query + Zustand + NativeWind + React Navigation + Expo Camera + Expo Notifications + Expo Location.

**Rider Mobile (`rider-mobile/`)** — Same RN stack. Heavy on background location, MapView (react-native-maps), camera (garment inspection), and offline order queue.

---

## 3. Multi-Tenancy Strategy

### 3.1 Tenant Isolation: PostgreSQL Row-Level Security

Every tenant-scoped table has these columns:

```sql
brand_id        UUID NOT NULL
franchise_id    UUID NULL    -- NULL = brand-wide record
store_id        UUID NULL    -- NULL = franchise-wide record
```

Connection string sets session variables on each request:

```sql
SET LOCAL app.current_brand_id = '...';
SET LOCAL app.current_franchise_id = '...';
SET LOCAL app.current_store_id = '...';
SET LOCAL app.current_user_id = '...';
SET LOCAL app.bypass_rls = 'false';
```

Enforce via RLS policies (example for `orders`):

```sql
CREATE POLICY orders_tenant_isolation ON orders
USING (
  current_setting('app.bypass_rls', true) = 'true'
  OR brand_id = current_setting('app.current_brand_id')::uuid
  AND (
    current_setting('app.current_franchise_id', true) = ''
    OR franchise_id = current_setting('app.current_franchise_id')::uuid
  )
);
```

Implemented via EF Core `SaveChangesInterceptor` + `DbConnectionInterceptor`.

### 3.2 Why not schema-per-tenant?

- Hundreds of franchises in 3 years → unmanageable migrations.
- Cross-franchise customer (relocation) needs single namespace.
- Platform roll-up reports need union queries.
- RLS gives isolation without operational overhead.

---

## 4. Database Design (Production PostgreSQL)

### 4.1 Conventions

- **PKs:** `UUID` v7 (sortable, no hot spots) — generated app-side or via `gen_random_uuid()`.
- **Money:** `NUMERIC(14,2)` — never `float`. Currency code `CHAR(3)` per brand.
- **Timestamps:** `TIMESTAMPTZ` always. Never `TIMESTAMP`.
- **Soft delete:** `deleted_at TIMESTAMPTZ NULL` + partial index `WHERE deleted_at IS NULL`.
- **Audit columns:** `created_at`, `updated_at`, `created_by`, `updated_by`, `version` (optimistic concurrency).
- **Enums:** Lookup tables, not PG enum types (enum types are painful to alter).
- **JSONB:** For flexible/sparse data (photo metadata, address details, notification payloads, feature flags). Indexed via GIN.
- **Partitioning:**
  - `orders` → range partition by `created_at` monthly.
  - `notifications_log` → range partition monthly.
  - `audit_logs` → range partition monthly.
  - `rider_location_pings` → range partition daily.
  - `process_logs` → range partition monthly.
- **Indexes:** B-tree on FKs; composite for hot query paths; GIN for JSONB and tsvector; partial indexes for `WHERE deleted_at IS NULL` and `WHERE status = 'active'`.
- **Constraints:** FK with explicit `ON DELETE` (`RESTRICT` default, `CASCADE` only for child detail rows).

### 4.2 Table Inventory (~90 tables, grouped)

#### Tenancy & Org (10)

1. `platforms` — top-level (usually 1 row)
2. `brands` — white-label brands under platform
3. `territories` — geographic exclusivity zones
4. `franchise_agreements` — legal contract terms (royalty %, term, exclusivity)
5. `franchises` — operational franchise entity
6. `stores` — physical pickup/walk-in
7. `warehouses` — processing facilities
8. `store_warehouse_mappings` — N:M (one warehouse can serve many stores)
9. `operating_hours` — per store/warehouse (day-of-week + open/close)
10. `holidays` — closed dates per scope

#### Identity & Access (11)

11. `users` — all system users (staff, admin, rider; not customers)
12. `user_profiles` — name, avatar, phone, locale, FCM token
13. `user_scope_memberships` — user × (brand|franchise|store|warehouse), with role
14. `roles` — system + custom roles
15. `permissions` — granular permission codes (e.g., `order.refund`)
16. `role_permissions` — N:M
17. `otp_codes` — phone/email OTP with attempt counter
18. `refresh_tokens` — hashed, revocable
19. `login_history` — IP, UA, device, geo, success/fail
20. `audit_logs` (partitioned) — every state change
21. `password_resets` — tokens with expiry

#### Customers (5)

22. `customers` — phone (PK semantic), brand_id, kyc flags
23. `customer_addresses` — multi-address, default flag, lat/lng, geofence
24. `customer_devices` — FCM/APNs tokens, app version, OS
25. `account_deletion_requests` — DPDP/Play Store compliant
26. `dpdp_consents` — purpose-bound consent log

#### Service Catalog & Pricing (9)

27. `service_categories` — Dry Clean, Laundry, Steam Iron, Shoe, Bag, Carpet, Curtain
28. `services` — sub-services with category FK
29. `fabric_types` — Cotton, Silk, Woolen, Synthetic, etc.
30. `item_groups` — MEN, WOMEN, KIDS, SHOES, HOME
31. `items` — Shirt, Pants, Saree, Sport Shoe, etc.
32. `item_variants` — Shirt+Cotton vs Shirt+Silk vs Shirt+Woolen
33. `price_lists` — versioned, scoped to brand/franchise/store
34. `price_list_items` — item × service × price
35. `add_ons` — stain removal, premium wash (surcharges)

#### Orders & Pickups (9)

36. `orders` (partitioned by month) — header
37. `order_items` — line items
38. `order_addons` — add-ons per line
39. `order_status_history` — full audit of status transitions
40. `order_notes` — internal/customer notes
41. `pickup_requests` — customer-initiated requests (pre-order)
42. `delivery_assignments` — rider × order × leg (pickup or delivery)
43. `delivery_slots` — configurable slots per store per day
44. `delivery_slot_bookings` — capacity tracking (atomic increment)

#### Garments & Tracking (5)

45. `garments` — physical garment instance with tag
46. `garment_tags` — printed barcode/QR registry
47. `garment_inspections` — pickup/QC inspection record
48. `garment_inspection_photos` — S3 keys, annotations (JSONB)
49. `garment_conditions` — lookup (stain, tear, missing button, fading)

#### Warehouse Operations (6)

50. `warehouse_batches` — group of garments processed together
51. `warehouse_processes` — lookup (sort, wash, dry, iron, pack)
52. `process_logs` (partitioned) — every scan/transition
53. `quality_checks` — pre/post photos, pass/fail/rewash
54. `stock_reconciliations` — daily count session
55. `stock_reconciliation_items` — per-garment match/missing

#### Riders & Delivery (4)

56. `riders` — extended user profile + vehicle + KYC
57. `rider_assignments` — current shift, capacity, store
58. `rider_location_pings` (partitioned by day) — GPS time series
59. `rider_capacity_config` — max pickups/deliveries per slot per rider

#### Packages, Loyalty, Coupons (8)

60. `packages` — Diamond/Gold/Silver tiers, brand-scoped
61. `customer_packages` — purchased subscriptions
62. `package_usage_ledger` — credit debits per order
63. `loyalty_programs` — earn/burn config per brand
64. `loyalty_points_ledger` — append-only points journal
65. `coupons` — codes, usage limits, scope
66. `coupon_redemptions` — applied to orders
67. `promotions` — first-order, cashback, banner campaigns

#### Payments & Wallet (5)

68. `payment_methods` — lookup (UPI, card, wallet, COD, prepaid)
69. `payments` — every transaction with gateway ref
70. `payment_refunds`
71. `wallet_accounts` — customer wallet header
72. `wallet_transactions` — append-only ledger

#### Finance & Franchise Revenue (7)

73. `cash_books` — daily per store
74. `cash_book_entries` — opening, income, expense, closing
75. `expense_categories`
76. `expenses` — per store/franchise
77. `expense_attachments` — S3 receipts
78. `shift_handovers` — cash handover between shifts
79. `royalty_invoices` — franchise → platform per period
80. `royalty_calculations` — derived from orders (auto-monthly)

#### Notifications & CMS (8)

81. `notification_templates` — versioned, per-channel
82. `notification_preferences` — per customer per event per channel
83. `notifications_outbox` — pending (transactional outbox pattern)
84. `notifications_log` (partitioned) — sent history
85. `whatsapp_message_log` — BSP-specific tracking
86. `onboarding_slides` — admin-configurable per brand
87. `app_banners` — hero carousel per brand
88. `mobile_app_config` — feature toggles per brand

#### System (4)

89. `system_settings` — scoped k/v
90. `feature_flags` — per scope (platform/brand/franchise/store/user)
91. `file_attachments` — generic file registry (S3)
92. `outbox_events` — domain event outbox for MassTransit

> Final count: **92 tables**. Add `__migrations` table = 93.

### 4.3 Sample DDL — Critical Tables

#### Tenancy backbone

```sql
-- ============================================================
-- 01. brands
-- ============================================================
CREATE TABLE brands (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    platform_id     UUID NOT NULL REFERENCES platforms(id) ON DELETE RESTRICT,
    code            VARCHAR(50) NOT NULL UNIQUE,
    name            VARCHAR(200) NOT NULL,
    legal_name      VARCHAR(200),
    logo_url        TEXT,
    primary_color   CHAR(7),
    secondary_color CHAR(7),
    currency_code   CHAR(3) NOT NULL DEFAULT 'INR',
    timezone        VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    locale_default  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    locales_enabled TEXT[] NOT NULL DEFAULT ARRAY['en-IN', 'hi-IN'],
    config          JSONB NOT NULL DEFAULT '{}'::jsonb,
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active','suspended','archived')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by      UUID,
    updated_by      UUID,
    version         INTEGER NOT NULL DEFAULT 1,
    deleted_at      TIMESTAMPTZ
);
CREATE INDEX idx_brands_platform ON brands(platform_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_brands_config_gin ON brands USING GIN (config);

-- ============================================================
-- 02. franchises
-- ============================================================
CREATE TABLE franchises (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id              UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    territory_id          UUID REFERENCES territories(id) ON DELETE SET NULL,
    franchise_agreement_id UUID REFERENCES franchise_agreements(id),
    owner_user_id         UUID REFERENCES users(id),
    code                  VARCHAR(50) NOT NULL,
    legal_name            VARCHAR(200) NOT NULL,
    gstin                 VARCHAR(15),
    pan                   VARCHAR(10),
    contact_phone         VARCHAR(20) NOT NULL,
    contact_email         VARCHAR(200),
    address               JSONB NOT NULL,
    royalty_percent       NUMERIC(5,2) NOT NULL DEFAULT 0.00
                          CHECK (royalty_percent BETWEEN 0 AND 100),
    marketing_fee_percent NUMERIC(5,2) NOT NULL DEFAULT 0.00,
    onboarding_status     VARCHAR(30) NOT NULL DEFAULT 'active',
    onboarded_at          TIMESTAMPTZ,
    suspended_at          TIMESTAMPTZ,
    suspended_reason      TEXT,
    config                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by            UUID, updated_by UUID,
    version               INTEGER NOT NULL DEFAULT 1,
    deleted_at            TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_franchises_brand     ON franchises(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_franchises_territory ON franchises(territory_id);
CREATE INDEX idx_franchises_owner     ON franchises(owner_user_id);

ALTER TABLE franchises ENABLE ROW LEVEL SECURITY;
CREATE POLICY franchises_tenant ON franchises
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ============================================================
-- 03. stores
-- ============================================================
CREATE TABLE stores (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    franchise_id    UUID NOT NULL REFERENCES franchises(id) ON DELETE RESTRICT,
    code            VARCHAR(50) NOT NULL,
    name            VARCHAR(200) NOT NULL,
    store_type      VARCHAR(30) NOT NULL DEFAULT 'walkin'
                    CHECK (store_type IN ('walkin','pickup_only','express','hub')),
    address         JSONB NOT NULL,
    geo_location    GEOGRAPHY(POINT, 4326),
    service_radius_km NUMERIC(5,2) DEFAULT 5.00,
    contact_phone   VARCHAR(20),
    toll_free       VARCHAR(20),
    whatsapp_number VARCHAR(20),
    timezone        VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    daily_capacity  INTEGER NOT NULL DEFAULT 200,
    slot_duration_minutes INTEGER NOT NULL DEFAULT 120,
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active','paused','closed')),
    opened_at       TIMESTAMPTZ,
    config          JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by      UUID, updated_by UUID,
    version         INTEGER NOT NULL DEFAULT 1,
    deleted_at      TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_stores_brand_franchise ON stores(brand_id, franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_stores_geo             ON stores USING GIST (geo_location);

ALTER TABLE stores ENABLE ROW LEVEL SECURITY;
CREATE POLICY stores_tenant ON stores
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

#### Identity

```sql
-- ============================================================
-- users + scope memberships
-- ============================================================
CREATE TABLE users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    phone_e164      VARCHAR(20) UNIQUE,
    email           CITEXT UNIQUE,
    password_hash   TEXT,  -- argon2id; NULL for OTP-only
    mfa_enabled     BOOLEAN NOT NULL DEFAULT false,
    locale          VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active','locked','suspended','deleted')),
    last_login_at   TIMESTAMPTZ,
    last_login_ip   INET,
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    locked_until    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ,
    CHECK (phone_e164 IS NOT NULL OR email IS NOT NULL)
);

CREATE TABLE user_scope_memberships (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    scope_type      VARCHAR(20) NOT NULL
                    CHECK (scope_type IN ('platform','brand','franchise','store','warehouse')),
    scope_id        UUID,  -- NULL when scope_type='platform'
    role_id         UUID NOT NULL REFERENCES roles(id),
    is_primary      BOOLEAN NOT NULL DEFAULT false,
    granted_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    granted_by      UUID,
    revoked_at      TIMESTAMPTZ,
    revoked_by      UUID,
    UNIQUE (user_id, scope_type, scope_id, role_id)
);
CREATE INDEX idx_usm_user           ON user_scope_memberships(user_id) WHERE revoked_at IS NULL;
CREATE INDEX idx_usm_scope          ON user_scope_memberships(scope_type, scope_id) WHERE revoked_at IS NULL;
```

#### Orders (partitioned)

```sql
-- ============================================================
-- orders -- range partition by month on created_at
-- ============================================================
CREATE TABLE orders (
    id                  UUID NOT NULL DEFAULT gen_random_uuid(),
    order_number        VARCHAR(30) NOT NULL,    -- human-readable: LG-2026-GGN001-000123
    brand_id            UUID NOT NULL,
    franchise_id        UUID NOT NULL,
    store_id            UUID NOT NULL,
    warehouse_id        UUID,
    customer_id         UUID NOT NULL,
    pickup_address_id   UUID,
    delivery_address_id UUID,
    pickup_slot_id      UUID,
    delivery_slot_id    UUID,
    channel             VARCHAR(20) NOT NULL DEFAULT 'walkin'
                        CHECK (channel IN ('walkin','app','whatsapp','call','web')),
    is_express          BOOLEAN NOT NULL DEFAULT false,
    express_surcharge   NUMERIC(14,2) NOT NULL DEFAULT 0,
    subtotal            NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total      NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total           NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total         NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid         NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due          NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code       CHAR(3) NOT NULL DEFAULT 'INR',
    coupon_id           UUID,
    package_id          UUID,                    -- if paid from prepaid package
    loyalty_points_used INTEGER NOT NULL DEFAULT 0,
    status              VARCHAR(30) NOT NULL DEFAULT 'placed'
                        CHECK (status IN ('placed','picked_up','received','sorting',
                                          'in_process','qc','ready','out_for_delivery',
                                          'delivered','cancelled','returned','rewash')),
    placed_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    picked_up_at        TIMESTAMPTZ,
    received_at         TIMESTAMPTZ,
    ready_at            TIMESTAMPTZ,
    delivered_at        TIMESTAMPTZ,
    cancelled_at        TIMESTAMPTZ,
    notes               TEXT,
    metadata            JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by          UUID, updated_by UUID,
    version             INTEGER NOT NULL DEFAULT 1,
    deleted_at          TIMESTAMPTZ,
    PRIMARY KEY (id, created_at),
    UNIQUE (order_number, created_at)
) PARTITION BY RANGE (created_at);

-- Monthly partitions auto-created via pg_partman
SELECT partman.create_parent(
    p_parent_table => 'public.orders',
    p_control      => 'created_at',
    p_type         => 'range',
    p_interval     => '1 month',
    p_premake      => 6
);

CREATE INDEX idx_orders_brand_store_status ON orders(brand_id, store_id, status, created_at DESC)
    WHERE deleted_at IS NULL;
CREATE INDEX idx_orders_customer            ON orders(customer_id, created_at DESC);
CREATE INDEX idx_orders_franchise           ON orders(franchise_id, created_at DESC);
CREATE INDEX idx_orders_status_open         ON orders(status, created_at)
    WHERE status NOT IN ('delivered','cancelled');
CREATE INDEX idx_orders_metadata_gin        ON orders USING GIN (metadata);

ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
CREATE POLICY orders_tenant ON orders
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);
```

#### Garment inspection (photo evidence)

```sql
CREATE TABLE garments (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_item_id   UUID NOT NULL REFERENCES order_items(id) ON DELETE CASCADE,
    brand_id        UUID NOT NULL,
    store_id        UUID NOT NULL,
    warehouse_id    UUID,
    tag_code        VARCHAR(50) NOT NULL UNIQUE,    -- printed barcode/QR
    item_id         UUID REFERENCES items(id),
    item_variant_id UUID REFERENCES item_variants(id),
    color           VARCHAR(50),
    brand_name      VARCHAR(100),
    current_stage   VARCHAR(30) NOT NULL DEFAULT 'received'
                    CHECK (current_stage IN ('received','sorting','washing','drying',
                                             'ironing','qc','packing','dispatched','delivered','lost')),
    weight_grams    INTEGER,
    notes           TEXT,
    metadata        JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_garments_order_item ON garments(order_item_id);
CREATE INDEX idx_garments_stage      ON garments(current_stage, store_id);

CREATE TABLE garment_inspections (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    garment_id      UUID NOT NULL REFERENCES garments(id) ON DELETE CASCADE,
    inspected_by    UUID NOT NULL REFERENCES users(id),
    inspection_type VARCHAR(20) NOT NULL
                    CHECK (inspection_type IN ('pickup','intake','pre_wash','post_wash','qc','delivery')),
    inspected_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    conditions      JSONB NOT NULL DEFAULT '[]'::jsonb,  -- [{type:'stain', area:'collar', severity:'minor'}]
    notes           TEXT,
    customer_acknowledged BOOLEAN NOT NULL DEFAULT false,
    customer_signature_url TEXT
);
CREATE INDEX idx_inspections_garment ON garment_inspections(garment_id, inspected_at DESC);
CREATE INDEX idx_inspections_conditions_gin ON garment_inspections USING GIN (conditions);

CREATE TABLE garment_inspection_photos (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    inspection_id   UUID NOT NULL REFERENCES garment_inspections(id) ON DELETE CASCADE,
    s3_key          TEXT NOT NULL,
    thumbnail_s3_key TEXT NOT NULL,
    view            VARCHAR(20) NOT NULL
                    CHECK (view IN ('front','back','left','right','top','closeup','damage')),
    annotations     JSONB NOT NULL DEFAULT '[]'::jsonb,  -- circles, arrows over photo
    width_px        INTEGER, height_px INTEGER, bytes INTEGER,
    captured_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_photos_inspection ON garment_inspection_photos(inspection_id);
```

#### Delivery slot capacity (atomic booking)

```sql
CREATE TABLE delivery_slots (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        UUID NOT NULL,
    store_id        UUID NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    slot_date       DATE NOT NULL,
    slot_start      TIME NOT NULL,
    slot_end        TIME NOT NULL,
    slot_type       VARCHAR(20) NOT NULL CHECK (slot_type IN ('pickup','delivery')),
    capacity        INTEGER NOT NULL DEFAULT 20,
    booked_count    INTEGER NOT NULL DEFAULT 0,
    is_express      BOOLEAN NOT NULL DEFAULT false,
    is_active       BOOLEAN NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (booked_count >= 0 AND booked_count <= capacity),
    UNIQUE (store_id, slot_date, slot_start, slot_type)
);
CREATE INDEX idx_slots_lookup ON delivery_slots(store_id, slot_date, slot_type)
    WHERE is_active = true;

-- Atomic increment in a transaction:
-- UPDATE delivery_slots SET booked_count = booked_count + 1
-- WHERE id = $1 AND booked_count < capacity AND is_active = true
-- RETURNING id;
-- If 0 rows: slot full, fail booking.
```

#### Royalty (franchise revenue share)

```sql
CREATE TABLE royalty_invoices (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        UUID NOT NULL,
    franchise_id    UUID NOT NULL REFERENCES franchises(id),
    period_year     INTEGER NOT NULL,
    period_month    INTEGER NOT NULL CHECK (period_month BETWEEN 1 AND 12),
    invoice_number  VARCHAR(40) UNIQUE NOT NULL,
    gross_revenue   NUMERIC(14,2) NOT NULL,
    royalty_base    NUMERIC(14,2) NOT NULL,  -- after exclusions
    royalty_percent NUMERIC(5,2) NOT NULL,
    royalty_amount  NUMERIC(14,2) NOT NULL,
    marketing_fee   NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_amount      NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_due       NUMERIC(14,2) NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'draft'
                    CHECK (status IN ('draft','issued','paid','overdue','disputed','waived')),
    issued_at       TIMESTAMPTZ,
    due_at          TIMESTAMPTZ,
    paid_at         TIMESTAMPTZ,
    breakdown       JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (franchise_id, period_year, period_month)
);
CREATE INDEX idx_royalty_franchise ON royalty_invoices(franchise_id, period_year DESC, period_month DESC);
```

#### Cash book (Dhobi Cart parity)

```sql
CREATE TABLE cash_books (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        UUID NOT NULL,
    franchise_id    UUID NOT NULL,
    store_id        UUID NOT NULL REFERENCES stores(id),
    book_date       DATE NOT NULL,
    opening_balance NUMERIC(14,2) NOT NULL DEFAULT 0,
    closing_balance NUMERIC(14,2),
    total_income    NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_expenses  NUMERIC(14,2) NOT NULL DEFAULT 0,
    cash_in_hand    NUMERIC(14,2),
    is_closed       BOOLEAN NOT NULL DEFAULT false,
    closed_at       TIMESTAMPTZ,
    closed_by       UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (store_id, book_date)
);
CREATE INDEX idx_cashbooks_store_date ON cash_books(store_id, book_date DESC);

CREATE TABLE cash_book_entries (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cash_book_id    UUID NOT NULL REFERENCES cash_books(id) ON DELETE CASCADE,
    entry_type      VARCHAR(20) NOT NULL CHECK (entry_type IN ('income','expense','adjustment','handover')),
    category        VARCHAR(50),
    amount          NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    direction       SMALLINT NOT NULL CHECK (direction IN (-1, 1)),  -- +1 in, -1 out
    reference_type  VARCHAR(30),    -- order, expense, refund
    reference_id    UUID,
    payment_method  VARCHAR(30),
    notes           TEXT,
    receipt_s3_key  TEXT,
    entered_by      UUID NOT NULL,
    entered_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_cb_entries_book ON cash_book_entries(cash_book_id, entered_at);
```

#### Notification outbox (transactional)

```sql
CREATE TABLE notifications_outbox (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id        UUID NOT NULL,
    recipient_type  VARCHAR(20) NOT NULL CHECK (recipient_type IN ('customer','user','rider')),
    recipient_id    UUID NOT NULL,
    channel         VARCHAR(20) NOT NULL CHECK (channel IN ('sms','email','whatsapp','push')),
    template_code   VARCHAR(50) NOT NULL,
    payload         JSONB NOT NULL,
    priority        SMALLINT NOT NULL DEFAULT 5 CHECK (priority BETWEEN 1 AND 9),
    status          VARCHAR(20) NOT NULL DEFAULT 'pending'
                    CHECK (status IN ('pending','sending','sent','failed','cancelled')),
    attempts        INTEGER NOT NULL DEFAULT 0,
    max_attempts    INTEGER NOT NULL DEFAULT 3,
    next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_error      TEXT,
    sent_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_outbox_pending ON notifications_outbox(next_attempt_at, priority)
    WHERE status IN ('pending','failed') AND attempts < max_attempts;
```

#### Audit log (partitioned)

```sql
CREATE TABLE audit_logs (
    id              UUID NOT NULL DEFAULT gen_random_uuid(),
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    brand_id        UUID,
    franchise_id    UUID,
    store_id        UUID,
    actor_user_id   UUID,
    actor_type      VARCHAR(20) NOT NULL DEFAULT 'user',  -- user|system|customer|api
    action          VARCHAR(50) NOT NULL,                 -- order.created, price.updated, etc.
    resource_type   VARCHAR(50) NOT NULL,
    resource_id     UUID,
    old_values      JSONB,
    new_values      JSONB,
    ip_address      INET,
    user_agent      TEXT,
    request_id      UUID,
    PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

SELECT partman.create_parent('public.audit_logs', 'occurred_at', 'range', '1 month', p_premake => 6);

CREATE INDEX idx_audit_resource ON audit_logs(resource_type, resource_id, occurred_at DESC);
CREATE INDEX idx_audit_actor    ON audit_logs(actor_user_id, occurred_at DESC);
```

### 4.4 Materialized Views for Franchise Roll-Up Reporting

```sql
-- Daily revenue per store (refreshed nightly)
CREATE MATERIALIZED VIEW mv_daily_store_revenue AS
SELECT
    brand_id,
    franchise_id,
    store_id,
    DATE(placed_at AT TIME ZONE 'Asia/Kolkata') AS revenue_date,
    COUNT(*) FILTER (WHERE status != 'cancelled') AS order_count,
    SUM(grand_total) FILTER (WHERE status != 'cancelled') AS gross_revenue,
    SUM(discount_total) AS discounts_given,
    AVG(grand_total) FILTER (WHERE status = 'delivered') AS avg_order_value
FROM orders
WHERE deleted_at IS NULL
GROUP BY brand_id, franchise_id, store_id, DATE(placed_at AT TIME ZONE 'Asia/Kolkata');

CREATE UNIQUE INDEX ON mv_daily_store_revenue (brand_id, franchise_id, store_id, revenue_date);
CREATE INDEX ON mv_daily_store_revenue (franchise_id, revenue_date DESC);

-- Refresh via Hangfire job: REFRESH MATERIALIZED VIEW CONCURRENTLY mv_daily_store_revenue;
```

Similar views: `mv_monthly_franchise_revenue`, `mv_warehouse_throughput`, `mv_customer_ltv`, `mv_rider_performance`.

### 4.5 Required PostgreSQL Extensions

```sql
CREATE EXTENSION IF NOT EXISTS pgcrypto;       -- gen_random_uuid
CREATE EXTENSION IF NOT EXISTS citext;         -- case-insensitive email
CREATE EXTENSION IF NOT EXISTS postgis;        -- geo queries
CREATE EXTENSION IF NOT EXISTS pg_partman;     -- partition management
CREATE EXTENSION IF NOT EXISTS pg_trgm;        -- fuzzy text search
CREATE EXTENSION IF NOT EXISTS btree_gin;      -- composite GIN
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
```

---

## 5. API Surface (Versioned)

Base: `/api/v1`. All endpoints require JWT. Tenant resolution from JWT claims + path or header.

### Customer Mobile API (`/api/v1/customer/...`)

- `POST /auth/otp/send`, `POST /auth/otp/verify`
- `GET /onboarding/slides`
- `GET /home/banners`, `GET /home/services`, `GET /home/open-orders`
- `GET /price-list?service=&group=MEN&search=`
- `GET /pickup/slots?date=&store_id=`
- `POST /pickup/schedule`
- `GET /orders`, `GET /orders/{id}`, `POST /orders/{id}/cancel`
- `GET /packages/available`, `GET /packages/active`, `POST /packages/purchase`
- `GET /loyalty/points`, `GET /loyalty/history`
- `POST /payments/initiate`, `POST /payments/verify`
- `GET /profile`, `PATCH /profile`, `POST /addresses`, `DELETE /account`

### Staff/Admin API (`/api/v1/admin/...`)

- `/orders`, `/pos`, `/garments`, `/inspections`, `/warehouse/*`, `/stock-recon`
- `/cash-book`, `/expenses`, `/royalty`
- `/pricing`, `/packages`, `/coupons`, `/banners`, `/onboarding-slides`
- `/reports/*`, `/analytics/*`
- `/stores`, `/franchises`, `/users`, `/roles`

### Rider API (`/api/v1/rider/...`)

- `GET /assignments/today`
- `POST /pickup/{order_id}/arrived`
- `POST /pickup/{order_id}/photos`
- `POST /pickup/{order_id}/inspection`
- `POST /pickup/{order_id}/complete`
- `POST /delivery/{order_id}/complete` (OTP verified)
- `POST /location/ping` (batched)

### Webhooks (`/webhooks/...`)

- `/razorpay`, `/whatsapp-bsp`, `/msg91-dlr`

### Real-time (SignalR hubs)

- `OrderHub` — order status to customer + store
- `RiderHub` — location pings + assignments
- `WarehouseHub` — stage transitions

---

## 6. Build Strategy (Wave-Based Multi-Agent)

### Wave 0 — Foundation (1 week, sequential, you + 1 agent)

- Solution skeleton (Clean Arch + Aspire)
- PostgreSQL schema migrations (Flyway or EF Core migrations)
- Tenancy middleware (RLS session var setter)
- Identity service (JWT + OTP via MSG91)
- Audit infrastructure (interceptor + outbox)
- Notification fabric (templates + outbox + WhatsApp BSP + MSG91 + FCM)
- CI/CD (GitHub Actions + DockerHub + Terraform for infra)

### Wave 1 — Parallel Agents (4-5 weeks, 6 agents)

| Agent           | Module                                           | Tables Owned                                                                            | Key Endpoints                                                         |
| --------------- | ------------------------------------------------ | --------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| **A** Catalog   | Service catalog + pricing                        | services, items, item_variants, price_lists, add_ons (9)                                | `/admin/pricing`, `/customer/price-list`                              |
| **B** Orders    | Pickups + orders + delivery slots                | orders, order_items, status_history, pickup_requests, delivery_slots, slot_bookings (9) | `/customer/pickup`, `/customer/orders`, `/admin/orders`, `/admin/pos` |
| **C** Warehouse | Garments + inspections + processes + stock recon | garments, tags, inspections, photos, batches, process_logs, qc, stock_recon (11)        | `/admin/warehouse/*`, `/admin/garments/*`                             |
| **D** Delivery  | Riders + assignments + GPS + capacity            | riders, assignments, location_pings, rider_capacity (4)                                 | `/rider/*`, `/admin/dispatch`                                         |
| **E** Commerce  | Payments + wallet + packages + loyalty + coupons | payments, refunds, wallet*, packages, loyalty*, coupons (13)                            | `/customer/payments`, `/customer/packages`, `/admin/promotions`       |
| **F** Finance   | Cash book + expenses + royalty + shift handover  | cash_books, entries, expenses, handovers, royalty_invoices (7)                          | `/admin/cashbook`, `/admin/expenses`, `/franchise/royalty`            |

### Wave 2 — Integration (2 weeks, 2 agents)

- **Agent G — Analytics:** materialized views, dashboards, reports (`/reports/*`), export to Excel/PDF.
- **Agent H — CMS & Mobile:** onboarding slides, banners, mobile config, feature flags, app review screens.

### Wave 3 — Surface (3 weeks, 3 agents in parallel)

- **Agent I — Admin Web** (React 19, tenant switcher, all admin/franchise/store screens).
- **Agent J — Customer Mobile** (React Native, onboarding → home → schedule → orders → profile).
- **Agent K — Rider Mobile** (React Native, today's assignments, scan, camera, GPS, OTP).

### Wave 4 — Hardening (2 weeks, 1 lead + QA)

- Load testing (k6, 10k orders/day, 50 concurrent per store)
- Security audit (OWASP top 10, SAST via Snyk, DAST via OWASP ZAP)
- DPDP/Play Store compliance review
- Penetration test
- Disaster recovery drill (PITR restore)
- Documentation + training videos

**Total: ~12-13 weeks** (faster than BRD's 15-17 weeks due to parallelization).

---

## 7. Cross-Cutting Concerns

### 7.1 Security

- TLS 1.3 everywhere. HSTS preload.
- JWT: RS256, 15-min access + 30-day refresh (rotating, hashed in DB).
- Password hashing: Argon2id.
- OTP: 6-digit, 5-min TTL, 3 attempt cap, Redis-backed.
- Rate limiting: IP + user + endpoint (Redis sliding window).
- Secrets: Azure Key Vault / AWS Secrets Manager. Never in code/env files in git.
- Input validation: FluentValidation at the Application layer. Reject unknown JSON properties.
- SQL injection: parametrized only (EF Core enforces this; raw SQL goes through `FormattableString`).
- File uploads: virus scan (ClamAV sidecar), MIME sniff, magic-byte check, signed S3 URLs.
- CORS: explicit origins per surface.
- PCI-DSS SAQ-A: tokenize via Razorpay, never store card data.
- DPDP Act 2023: purpose-bound consent, right to erasure, data localization (India region).

### 7.2 Observability

- Structured logging: Serilog + Elastic/OpenSearch sink.
- Metrics: OpenTelemetry + Prometheus + Grafana.
- Traces: OpenTelemetry → Jaeger/Tempo.
- Health checks: `/health/live`, `/health/ready` (DB, Redis, RabbitMQ, external APIs).
- Sentry for client-side error tracking (RN + React).
- Slack alerts on: 5xx spike, queue depth >1000, slot booking failures, payment webhook failures.

### 7.3 Reliability

- Transactional outbox for events (no event lost on commit).
- Idempotency keys on POST endpoints (`Idempotency-Key` header → Redis 24h).
- Circuit breakers (Polly) around external APIs.
- Hangfire for retry-with-backoff jobs.
- Postgres: streaming replication (1 primary, 2 replicas), PITR via WAL archive.
- Daily logical backups (`pg_dump`) + 30-day retention.
- Multi-AZ deployment minimum.

### 7.4 Performance Targets (BRD aligned + tightened)

- API p50 < 150ms, p95 < 500ms, p99 < 1s.
- Mobile cold start < 2s.
- Order placement end-to-end < 800ms.
- Slot availability lookup < 100ms (Redis cache, 30s TTL).
- Reports generation: async via Hangfire if estimated > 5s.

### 7.5 DPDP / Play Store Compliance

- Delete account flow: 30-day soft window with reversal, then hard delete + anonymization of order PII (keep order numbers + amounts for tax records, scrub names/phones/photos).
- Consent log: every data-processing purpose has explicit recorded consent.
- Data export: customer can request full personal data dump (signed S3 URL).
- Privacy policy + terms versioned in DB, customer accepts each version.

### 7.6 Localization

- All user-facing strings via i18n (i18next on web/RN, RESX or JSON catalogs on backend templates).
- English + Hindi at launch. Marathi, Punjabi, Tamil queued.
- Number format, date format, currency per brand locale.
- RTL not needed for Indian languages but architecture-ready.

---

## 8. DevOps / Infrastructure

### 8.1 Environments

- `local` — Docker Compose / Aspire dashboard
- `dev` — shared, auto-deployed on merge to `develop`
- `staging` — UAT, mirrors prod size, anonymized data refresh weekly
- `prod` — multi-AZ, replicas, WAF

### 8.2 Suggested Cloud (India region)

- Azure South India / Central India, or AWS Mumbai (ap-south-1).
- App: Azure Container Apps / AWS ECS Fargate.
- DB: Azure Database for PostgreSQL Flexible Server / AWS RDS PostgreSQL.
- Cache: Azure Cache for Redis / AWS ElastiCache.
- Queue: Azure Service Bus or self-hosted RabbitMQ on container.
- Object storage: Azure Blob Storage / AWS S3.
- CDN: Azure Front Door / AWS CloudFront for static + images.
- WAF: in front of API gateway.

### 8.3 CI/CD

- GitHub Actions: lint → test → build → SAST → push image → deploy via Terraform.
- DB migrations: dedicated migration job runs before app deploy, blocking on failure.
- Blue-green for API. Canary for mobile (10% → 50% → 100%).
- Feature flags (LaunchDarkly or homegrown table) gate risky features.

### 8.4 Cost Optimization (Early Stage)

- Single AZ + scheduled scaling in `dev` and `staging`.
- Reserved instances for prod after 3 months of usage data.
- S3/Blob lifecycle: garment photos move to cool tier at 30 days, deleted at 90 days (BRD requirement).
- Materialized views refresh nightly (off-peak).

---

## 9. Open Decisions for You

| #   | Decision                                               | Options                                                             |
| --- | ------------------------------------------------------ | ------------------------------------------------------------------- |
| 1   | Cloud provider                                         | Azure (better .NET integration) vs AWS (broader services)           |
| 2   | Queue                                                  | RabbitMQ self-hosted (cheaper) vs Azure Service Bus / SQS (managed) |
| 3   | WhatsApp BSP                                           | Gupshup, AiSensy, Interakt, Wati, Twilio                            |
| 4   | Multiple brands at launch?                             | If no → skip `brands` table complexity in UI for v1                 |
| 5   | Offline POS scope                                      | Full offline-first vs online-only with reconnection grace           |
| 6   | Customer login                                         | OTP-only vs OTP + password vs OTP + social                          |
| 7   | Franchise onboarding self-serve or assisted?           | Affects KYC flow complexity                                         |
| 8   | Rider app: employee-only vs gig (Borzo/Dunzo fallback) | Affects assignment logic                                            |
| 9   | Reports format                                         | Server-rendered PDF (QuestPDF) vs client-side Excel export only     |

---

## 10. Next Steps

1. **Sign off** on org hierarchy and decisions in §9.
2. **Wave 0** kicks off: I'll scaffold the .NET solution + initial migrations + auth.
3. Set up `CLAUDE.md` + per-agent `SKILL.md` files for each Wave 1 agent (you already have this pattern from DocSlot/SnapAccount).
4. Provision PG cluster + Redis + RabbitMQ in dev (Aspire locally).
5. Lock the API contract OpenAPI doc before Wave 1 starts so agents don't drift.

---

_Document end. ~92 tables. Stack: .NET 10 / React 19 / React Native / PostgreSQL 16. Tenant model: RLS-isolated multi-brand multi-franchise._
