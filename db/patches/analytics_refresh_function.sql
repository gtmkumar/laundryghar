-- Analytics materialized-view refresh, callable by the unprivileged app role.
--
-- Problem: the dashboard revenue/LTV/rider/warehouse widgets read analytics.mv_*
-- materialized views, but those are owned by `postgres` and only the owner can
-- REFRESH them. The services connect as `app_user`, so neither the existing
-- manual /refresh endpoint nor any background job could refresh them — the
-- dashboard kept showing whatever snapshot existed at the last manual refresh.
--
-- We also can't just hand ownership to app_user: a refresh runs the view query
-- as the matview owner, and app_user is subject to row-level security, so an
-- app_user-owned refresh would aggregate only an RLS-filtered slice instead of
-- all brands.
--
-- Fix: a SECURITY DEFINER function owned by postgres (RLS-exempt) that refreshes
-- every matview CONCURRENTLY (non-blocking — dashboards stay readable). app_user
-- is granted EXECUTE only; it cannot touch the matviews directly. search_path is
-- pinned per SECURITY DEFINER hardening guidance.

CREATE OR REPLACE FUNCTION analytics.refresh_all_matviews()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = analytics, pg_catalog, pg_temp
AS $$
BEGIN
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_daily_store_revenue;
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_monthly_franchise_revenue;
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_warehouse_throughput;
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_customer_ltv;
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_rider_performance;
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_subscription_mrr;
    REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_franchise_saas_mrr;
END;
$$;

ALTER FUNCTION analytics.refresh_all_matviews() OWNER TO postgres;
REVOKE ALL  ON FUNCTION analytics.refresh_all_matviews() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION analytics.refresh_all_matviews() TO app_user;
