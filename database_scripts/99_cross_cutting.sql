-- ============================================================================
-- LAUNDRY GHAR — 99 — Cross-cutting (partitions, RLS, retention)
-- ============================================================================
-- Wave:           2
-- Bounded ctx:    — (Cross-cutting (partitions, RLS, retention))
-- Source §:       post-§14
-- Tables:         0  (#0 new tables)
-- Apply after:
--   - All files above
-- Owning agent:   agent/integrator
-- Purpose:        pg_partman create_parent() calls for the 5 partitioned tables (orders, audit_logs, process_logs, notifications_log, rider_location_pings), RLS session-var documentation, data-retention policy reference. Must run AFTER every CREATE TABLE so pg_partman can attach to existing parents.
-- ============================================================================

-- ============================================================================
-- PARTITION CREATION (initial set; pg_partman maintains thereafter)
-- ============================================================================

-- audit_logs: monthly partitions for current + next 3 months
SELECT partman.create_parent(
    p_parent_table     => 'public.audit_logs',
    p_control          => 'occurred_at',
    p_type             => 'range',
    p_interval         => '1 month',
    p_premake          => 6
);

-- orders: monthly partitions
SELECT partman.create_parent(
    p_parent_table     => 'public.orders',
    p_control          => 'created_at',
    p_type             => 'range',
    p_interval         => '1 month',
    p_premake          => 6
);

-- process_logs: monthly
SELECT partman.create_parent(
    p_parent_table     => 'public.process_logs',
    p_control          => 'occurred_at',
    p_type             => 'range',
    p_interval         => '1 month',
    p_premake          => 6
);

-- notifications_log: monthly
SELECT partman.create_parent(
    p_parent_table     => 'public.notifications_log',
    p_control          => 'sent_at',
    p_type             => 'range',
    p_interval         => '1 month',
    p_premake          => 3
);

-- rider_location_pings: daily, 14 day retention
SELECT partman.create_parent(
    p_parent_table     => 'public.rider_location_pings',
    p_control          => 'pinged_at',
    p_type             => 'range',
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
