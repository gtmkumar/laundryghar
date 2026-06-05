-- ============================================================================
-- LAUNDRY GHAR — 99 Cross-cutting (SCHEMA-QUALIFIED, error-tolerant variant)
-- ============================================================================
-- Replaces 99_cross_cutting.sql for the schema-split apply.
-- Differences vs original:
--   1. Parent tables are referenced with their bounded-context schema names
--      (audit_logs lives in identity_access, orders in order_lifecycle, etc.).
--   2. Each partman.create_parent() call is wrapped in a DO block that
--      catches errors, so missing PARTITION BY declarations on source tables
--      don't abort the apply. (Source tables have no PARTITION BY clauses;
--      see "Known issues" in README.md.)
-- ============================================================================

-- ---- audit_logs (identity_access) ------------------------------------------
DO $$
BEGIN
    PERFORM partman.create_parent(
        p_parent_table => 'identity_access.audit_logs',
        p_control      => 'occurred_at',
        p_type         => 'range',
        p_interval     => '1 month',
        p_premake      => 6
    );
    RAISE NOTICE 'partman: identity_access.audit_logs configured (monthly)';
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'partman: skipped identity_access.audit_logs — %', SQLERRM;
END $$;

-- ---- orders (order_lifecycle) ----------------------------------------------
DO $$
BEGIN
    PERFORM partman.create_parent(
        p_parent_table => 'order_lifecycle.orders',
        p_control      => 'created_at',
        p_type         => 'range',
        p_interval     => '1 month',
        p_premake      => 6
    );
    RAISE NOTICE 'partman: order_lifecycle.orders configured (monthly)';
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'partman: skipped order_lifecycle.orders — %', SQLERRM;
END $$;

-- ---- process_logs (order_lifecycle) ----------------------------------------
DO $$
BEGIN
    PERFORM partman.create_parent(
        p_parent_table => 'order_lifecycle.process_logs',
        p_control      => 'occurred_at',
        p_type         => 'range',
        p_interval     => '1 month',
        p_premake      => 6
    );
    RAISE NOTICE 'partman: order_lifecycle.process_logs configured (monthly)';
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'partman: skipped order_lifecycle.process_logs — %', SQLERRM;
END $$;

-- ---- notifications_log (engagement_cms) ------------------------------------
DO $$
BEGIN
    PERFORM partman.create_parent(
        p_parent_table => 'engagement_cms.notifications_log',
        p_control      => 'sent_at',
        p_type         => 'range',
        p_interval     => '1 month',
        p_premake      => 3
    );
    RAISE NOTICE 'partman: engagement_cms.notifications_log configured (monthly)';
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'partman: skipped engagement_cms.notifications_log — %', SQLERRM;
END $$;

-- ---- rider_location_pings (logistics) --------------------------------------
DO $$
BEGIN
    PERFORM partman.create_parent(
        p_parent_table => 'logistics.rider_location_pings',
        p_control      => 'pinged_at',
        p_type         => 'range',
        p_interval     => '1 day',
        p_premake      => 7
    );
    UPDATE partman.part_config
       SET retention             = '14 days',
           retention_keep_table  = false
     WHERE parent_table = 'logistics.rider_location_pings';
    RAISE NOTICE 'partman: logistics.rider_location_pings configured (daily, 14d retention)';
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'partman: skipped logistics.rider_location_pings — %', SQLERRM;
END $$;

-- ============================================================================
-- ROW-LEVEL SECURITY — session var pattern (informational)
-- ============================================================================
-- Application must SET LOCAL these per request (via DbConnectionInterceptor):
--   SET LOCAL app.bypass_rls           = 'false';
--   SET LOCAL app.current_brand_id     = '<uuid>';
--   SET LOCAL app.current_franchise_id = '<uuid>';
--   SET LOCAL app.current_store_id     = '<uuid>';
--   SET LOCAL app.current_user_id      = '<uuid>';
-- Background jobs / migrations:
--   SET LOCAL app.bypass_rls = 'true';

-- ============================================================================
-- DATA-RETENTION POLICIES (informational)
-- ============================================================================
-- identity_access.audit_logs            : 7 years (DPDP), archive after 2 yrs
-- order_lifecycle.orders                : retain forever; compress after 2 yrs
-- order_lifecycle.process_logs          : 18 months
-- engagement_cms.notifications_log      : 6 months
-- logistics.rider_location_pings        : 14 days (privacy-sensitive)
-- identity_access.otp_codes             : DELETE WHERE expires_at < now() - INTERVAL '1 day'
-- identity_access.refresh_tokens        : DELETE WHERE expires_at < now() OR revoked_at IS NOT NULL
-- order_lifecycle.garment_inspection_photos : 90 days post-delivery, then archive
