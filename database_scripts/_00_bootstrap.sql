-- ============================================================================
-- LAUNDRY GHAR — Schema bootstrap
-- ============================================================================
-- Purpose: Install extensions in `public` and create one PostgreSQL schema
--          per bounded-context SQL file in `database_scripts/`.
--          Each subsequent SQL file is applied with a per-file search_path
--          so its CREATE TABLE statements land in the dedicated schema
--          while cross-schema FK references still resolve.
-- ============================================================================

-- ---- Extensions (extracted from 00_kernel.sql §0) -------------------------
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gin;
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
CREATE EXTENSION IF NOT EXISTS unaccent;

CREATE SCHEMA IF NOT EXISTS partman;
CREATE EXTENSION IF NOT EXISTS pg_partman WITH SCHEMA partman;

-- ---- One schema per source SQL file ---------------------------------------
CREATE SCHEMA IF NOT EXISTS kernel;
CREATE SCHEMA IF NOT EXISTS tenancy_org;
CREATE SCHEMA IF NOT EXISTS identity_access;
CREATE SCHEMA IF NOT EXISTS customer_catalog;
CREATE SCHEMA IF NOT EXISTS order_lifecycle;
CREATE SCHEMA IF NOT EXISTS logistics;
CREATE SCHEMA IF NOT EXISTS commerce;
CREATE SCHEMA IF NOT EXISTS finance_royalty;
CREATE SCHEMA IF NOT EXISTS engagement_cms;
CREATE SCHEMA IF NOT EXISTS analytics;

COMMENT ON SCHEMA kernel           IS 'BC-0 Platform Kernel — system_settings, feature_flags, file_attachments, outbox_events';
COMMENT ON SCHEMA tenancy_org      IS 'BC-1 Tenancy & Organization — platforms, brands, territories, franchises, stores, warehouses';
COMMENT ON SCHEMA identity_access  IS 'BC-2 Identity & Access — users, roles, permissions, otp_codes, refresh_tokens, audit_logs';
COMMENT ON SCHEMA customer_catalog IS 'BC-3 Customer & Catalog — customers, addresses, devices, services, items, price lists';
COMMENT ON SCHEMA order_lifecycle  IS 'BC-4 Order Lifecycle — orders, pickups, deliveries, garments, warehouse batches, QC';
COMMENT ON SCHEMA logistics        IS 'BC-5 Logistics — riders, assignments, location pings, capacity';
COMMENT ON SCHEMA commerce         IS 'BC-6 Commerce — packages, loyalty, coupons, promotions, payments, wallets';
COMMENT ON SCHEMA finance_royalty  IS 'BC-7 Finance & Royalty — cash books, expenses, shift handovers, royalty';
COMMENT ON SCHEMA engagement_cms   IS 'BC-8 Engagement & CMS — notifications, banners, onboarding, app config';
COMMENT ON SCHEMA analytics        IS 'BC-9 Analytics — materialized views for revenue, LTV, throughput';

-- ---- Default privileges so future objects in these schemas are queryable ---
GRANT USAGE ON SCHEMA
    kernel, tenancy_org, identity_access, customer_catalog, order_lifecycle,
    logistics, commerce, finance_royalty, engagement_cms, analytics
TO PUBLIC;
