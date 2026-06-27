-- =============================================================================
-- db/patches/phase2_slice_i_matview_registry.sql
--
-- Multi-vertical Phase 2 · Slice 2I — Analytics matview registry + refresh-function rewrite
-- (blueprint §7.2 Analytics "7-matview registry + refresh-function rewrite covering the full
-- shared set; gate rider-perf by FulfillmentMode").
--
--   * analytics.matview_registry catalogs all 7 shared matviews with refresh order, concurrency,
--     and per-matview vertical / fulfilment-mode applicability (so warehouse-throughput is
--     laundry-only and rider-performance only applies to delivery fulfilment modes).
--   * analytics.refresh_all_matviews() is rewritten to ITERATE the registry instead of a hardcoded
--     list — adding/retiring a matview is now a registry row, not a function edit.
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase2_slice_i_matview_registry.sql
-- =============================================================================

BEGIN;

CREATE SCHEMA IF NOT EXISTS analytics;

-- 1. Registry table -----------------------------------------------------------
CREATE TABLE IF NOT EXISTS analytics.matview_registry (
    matview_name          text PRIMARY KEY,
    refresh_order         int  NOT NULL DEFAULT 100,
    refresh_concurrently  boolean NOT NULL DEFAULT true,
    -- null = vertical-neutral; otherwise the matview only makes sense for that vertical.
    vertical_key          varchar(20)
        CHECK (vertical_key IS NULL OR vertical_key IN ('laundry','salon','logistics')),
    -- comma-separated fulfilment modes this matview's metrics apply to (null = all).
    fulfillment_modes     text,
    status                varchar(20) NOT NULL DEFAULT 'active',
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now()
);

-- 2. Seed the 7 shared matviews ----------------------------------------------
INSERT INTO analytics.matview_registry
    (matview_name, refresh_order, refresh_concurrently, vertical_key, fulfillment_modes)
VALUES
    ('mv_daily_store_revenue',       10, true, NULL,      NULL),
    ('mv_monthly_franchise_revenue', 20, true, NULL,      NULL),
    ('mv_customer_ltv',              30, true, NULL,      NULL),
    ('mv_subscription_mrr',          40, true, NULL,      NULL),
    ('mv_franchise_saas_mrr',        50, true, NULL,      NULL),
    -- warehouse throughput is the laundry processing board; rider-perf only applies to the
    -- delivery fulfilment modes (a salon appointment has no rider leg).
    ('mv_warehouse_throughput',      60, true, 'laundry', NULL),
    ('mv_rider_performance',         70, true, NULL,      'process_deliver,point_to_point')
ON CONFLICT (matview_name) DO UPDATE
    SET refresh_order = EXCLUDED.refresh_order,
        vertical_key  = EXCLUDED.vertical_key,
        fulfillment_modes = EXCLUDED.fulfillment_modes,
        updated_at    = now();

DO $g$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='app_user') THEN
        GRANT SELECT ON analytics.matview_registry TO app_user;
    END IF;
END $g$;

-- 3. Rewrite the refresh function to iterate the registry ---------------------
CREATE OR REPLACE FUNCTION analytics.refresh_all_matviews()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = analytics, pg_catalog, pg_temp
AS $$
DECLARE r record;
BEGIN
    FOR r IN
        SELECT matview_name, refresh_concurrently
        FROM analytics.matview_registry
        WHERE status = 'active'
          -- only refresh matviews that actually exist (registry may list not-yet-created views)
          AND to_regclass('analytics.' || matview_name) IS NOT NULL
        ORDER BY refresh_order
    LOOP
        IF r.refresh_concurrently THEN
            EXECUTE format('REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.%I', r.matview_name);
        ELSE
            EXECUTE format('REFRESH MATERIALIZED VIEW analytics.%I', r.matview_name);
        END IF;
    END LOOP;
END;
$$;

ALTER FUNCTION analytics.refresh_all_matviews() OWNER TO postgres;
REVOKE ALL ON FUNCTION analytics.refresh_all_matviews() FROM PUBLIC;
DO $g$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='app_user') THEN
        GRANT EXECUTE ON FUNCTION analytics.refresh_all_matviews() TO app_user;
    END IF;
END $g$;

-- 4. Verification gate --------------------------------------------------------
DO $verify$
DECLARE n int; rider_modes text;
BEGIN
    SELECT count(*) INTO n FROM analytics.matview_registry WHERE status='active';
    IF n < 7 THEN RAISE EXCEPTION 'Slice 2I: expected >= 7 registry rows, found %', n; END IF;

    SELECT fulfillment_modes INTO rider_modes FROM analytics.matview_registry WHERE matview_name='mv_rider_performance';
    IF rider_modes IS NULL OR rider_modes NOT LIKE '%point_to_point%' THEN
        RAISE EXCEPTION 'Slice 2I: rider-performance not gated by fulfilment mode';
    END IF;

    RAISE NOTICE 'Slice 2I verification passed: matview registry seeded (% rows); refresh rewritten to iterate it.', n;
END
$verify$;

COMMIT;

SELECT 'phase2_slice_i_matview_registry.sql applied successfully.' AS result;
