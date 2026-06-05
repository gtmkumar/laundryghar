-- ============================================================================
-- LAUNDRY GHAR — 00 BC-0 Platform Kernel
-- ============================================================================
-- Wave:           0
-- Bounded ctx:    BC-0 (Platform Kernel)
-- Source §:       §0 extensions + §13 system
-- Tables:         4  (#89–92)
-- Apply after:
--   (none — Wave 0 root)
-- Owning agent:   agent/foundation
-- Purpose:        PostgreSQL extensions, naming conventions, and platform-wide infrastructure tables (settings, feature flags, file registry, domain outbox). Must run FIRST — every other file depends on the extensions block.
-- ============================================================================

-- ============================================================================
-- LAUNDRY GHAR — COMPLETE PRODUCTION SCHEMA
-- ============================================================================
-- Database:    PostgreSQL 16+
-- Tables:      92
-- Model:       Multi-tenant franchise (Platform → Brand → Franchise → Store/Warehouse)
-- Isolation:   Row-Level Security (RLS)
-- Version:     1.0
-- ============================================================================

-- ============================================================================
-- CONVENTIONS
-- ============================================================================
-- PKs:         UUID v7 (sortable) via gen_random_uuid()
-- Money:       NUMERIC(14,2). Never float.
-- Timestamps:  TIMESTAMPTZ. Never plain TIMESTAMP.
-- Soft delete: deleted_at TIMESTAMPTZ NULL + partial indexes WHERE deleted_at IS NULL
-- Audit cols:  created_at, updated_at, created_by, updated_by, version
-- Enums:       Lookup tables, not PG enum types (alter-friendly)
-- JSONB:       Flexible/sparse data, GIN-indexed
-- Partitions:  Range by month/day on hot tables (orders, audit, ping, logs)
-- Tenant cols: brand_id always, franchise_id/store_id where scoped
-- RLS:         Enforced via session vars: app.current_brand_id, app.bypass_rls
-- ============================================================================

-- ============================================================================
-- 0. EXTENSIONS
-- ============================================================================
CREATE EXTENSION IF NOT EXISTS pgcrypto;            -- gen_random_uuid()
CREATE EXTENSION IF NOT EXISTS citext;              -- case-insensitive text
CREATE EXTENSION IF NOT EXISTS postgis;             -- geo queries
CREATE EXTENSION IF NOT EXISTS pg_partman;          -- partition automation
CREATE EXTENSION IF NOT EXISTS pg_trgm;             -- fuzzy text search
CREATE EXTENSION IF NOT EXISTS btree_gin;           -- composite GIN indexes
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;  -- query telemetry
CREATE EXTENSION IF NOT EXISTS unaccent;            -- diacritic-insensitive search
CREATE SCHEMA IF NOT EXISTS partman;
CREATE EXTENSION IF NOT EXISTS pg_partman WITH SCHEMA partman;

-- ============================================================================

-- SECTION 13: SYSTEM (4 tables: #89–92)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 89. system_settings — brand/franchise-scoped config (singleton key-value)
-- ----------------------------------------------------------------------------
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
    created_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (scope_type, brand_id, franchise_id, store_id, category, setting_key)

);
CREATE INDEX idx_syssett_brand          ON system_settings(brand_id, category) WHERE brand_id IS NOT NULL;
CREATE INDEX idx_syssett_franchise      ON system_settings(franchise_id, category) WHERE franchise_id IS NOT NULL;
CREATE INDEX idx_syssett_store          ON system_settings(store_id, category) WHERE store_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 90. feature_flags — gradual rollout / kill-switch toggles
-- ----------------------------------------------------------------------------
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
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, flag_key)

);
CREATE INDEX idx_flags_brand            ON feature_flags(brand_id, flag_key) WHERE is_enabled = true;

-- ----------------------------------------------------------------------------
-- 91. file_attachments — generic polymorphic file registry
-- ----------------------------------------------------------------------------
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
    deleted_at              TIMESTAMPTZ,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_files_owner            ON file_attachments(owner_type, owner_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_files_purpose          ON file_attachments(purpose) WHERE deleted_at IS NULL;
CREATE INDEX idx_files_expires          ON file_attachments(expires_at) WHERE expires_at IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_files_brand            ON file_attachments(brand_id) WHERE brand_id IS NOT NULL AND deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 92. outbox_events — domain event outbox for reliable async messaging
-- ----------------------------------------------------------------------------
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
    idempotency_key         VARCHAR(100) UNIQUE,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_outbox_pending         ON outbox_events(occurred_at, status)
    WHERE status = 'pending';
CREATE INDEX idx_outbox_events_retry    ON outbox_events(next_attempt_at)
    WHERE status = 'failed' AND publish_attempts < 10;
CREATE INDEX idx_outbox_aggregate       ON outbox_events(aggregate_type, aggregate_id, occurred_at);


-- ============================================================================
