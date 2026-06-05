-- =============================================================================
-- db/patches/fix_mv_customer_ltv_nulls.sql
--
-- analytics.mv_customer_ltv LEFT JOINs customers -> orders, so customers with
-- ZERO orders produced NULL aggregates (lifetime_revenue, avg_order_value,
-- first/last_order_at, days_since_last_order). The Analytics service maps those
-- columns to non-nullable CLR types, so the /dashboard and /customer-ltv
-- endpoints threw InvalidCastException ("Column 'LifetimeRevenue' is null").
--
-- Fix: a "lifetime value" view should only contain customers who have actually
-- ordered. Adding HAVING count(o.id) > 0 removes the order-less rows, so every
-- remaining row has non-null aggregates. (customer_segment can still legitimately
-- be NULL for an ordering customer — the entity property is nullable.)
--
-- Idempotent. RUN as postgres.
-- =============================================================================

DROP MATERIALIZED VIEW IF EXISTS analytics.mv_customer_ltv;

CREATE MATERIALIZED VIEW analytics.mv_customer_ltv AS
 SELECT c.brand_id,
    c.id AS customer_id,
    c.customer_segment,
    count(o.id) AS lifetime_orders,
    sum(o.grand_total) AS lifetime_revenue,
    avg(o.grand_total) AS avg_order_value,
    min(o.created_at) AS first_order_at,
    max(o.created_at) AS last_order_at,
    EXTRACT(day FROM now() - max(o.created_at)) AS days_since_last_order,
    count(*) FILTER (WHERE o.is_express = true) AS express_orders,
    count(*) FILTER (WHERE o.status::text = 'cancelled'::text) AS cancelled_orders,
    count(DISTINCT cp.id) AS active_packages,
    c.loyalty_points_balance,
    c.wallet_balance
   FROM customer_catalog.customers c
     LEFT JOIN order_lifecycle.orders o ON o.customer_id = c.id AND o.deleted_at IS NULL
     LEFT JOIN commerce.customer_packages cp ON cp.customer_id = c.id AND cp.status::text = 'active'::text
  WHERE c.deleted_at IS NULL
  GROUP BY c.brand_id, c.id, c.customer_segment, c.loyalty_points_balance, c.wallet_balance
  HAVING count(o.id) > 0;

-- Recreate indexes (DROP MATERIALIZED VIEW removed them).
-- The UNIQUE index is required for REFRESH MATERIALIZED VIEW CONCURRENTLY.
CREATE UNIQUE INDEX idx_mvcltv_unique  ON analytics.mv_customer_ltv USING btree (brand_id, customer_id);
CREATE INDEX        idx_mvcltv_revenue ON analytics.mv_customer_ltv USING btree (brand_id, lifetime_revenue DESC);

GRANT SELECT ON analytics.mv_customer_ltv TO app_user;

SELECT 'fix_mv_customer_ltv_nulls.sql applied successfully.' AS result;
