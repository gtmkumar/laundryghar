-- ============================================================================
-- LAUNDRY GHAR — 09 BC-9 Analytics
-- ============================================================================
-- Wave:           2
-- Bounded ctx:    BC-9 (Analytics)
-- Source §:       §14 materialized views
-- Tables:         0  (#0 tables · 5 MVs)
-- Apply after:
--   - All Wave-1 files (BC-3 through BC-7)
-- Owning agent:   agent/integrator
-- Purpose:        Read-only materialized views joining across Wave-1 BCs: daily store revenue, monthly franchise revenue (royalty basis), warehouse throughput, customer LTV, rider performance. Refresh via Hangfire jobs.
-- ============================================================================

-- SECTION 14: MATERIALIZED VIEWS (analytics, refreshed by Hangfire jobs)
-- ============================================================================

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


