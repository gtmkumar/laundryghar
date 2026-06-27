-- Wipe demo/transactional data: orders, customers, riders, and all non-super-admin
-- users + every dependent row. Keeps: roles/permissions/modules, org hierarchy
-- (platforms/brands/franchises/stores/warehouses), catalog config (services/items/
-- price lists), commerce config (coupons/packages/plans/promotions), finance config
-- (cash_books/expense_categories/platform_plans), engagement config (banners/
-- templates/app config), kernel.system_settings (WhatsApp/SMS/payment creds), and
-- the upsert super-admin (admin@laundryghar.local).
--
-- TRUNCATE ... CASCADE on the three roots clears every transactional table that
-- FKs them (payments, wallets, assignments, pings, fulfilment units, invoices, pickups,
-- login_history, otp_codes, refresh_tokens, notification logs, cash_book_entries,
-- shift_handovers, etc.). Only user_profiles + user_scope_memberships have no
-- customer FK, so they are cleared explicitly for non-admin users.

BEGIN;

TRUNCATE
    customer_catalog.customers,
    logistics.riders,
    order_lifecycle.orders
CASCADE;

-- Drop identity-only child rows for every user except the super-admin.
DELETE FROM identity_access.user_scope_memberships
WHERE user_id IN (
    SELECT id FROM identity_access.users WHERE email <> 'admin@laundryghar.local'
);

DELETE FROM identity_access.user_profiles
WHERE user_id IN (
    SELECT id FROM identity_access.users WHERE email <> 'admin@laundryghar.local'
);

DELETE FROM identity_access.users
WHERE email <> 'admin@laundryghar.local';

COMMIT;

-- Analytics rollups are materialized views (cached snapshots) — they do NOT
-- auto-update when the base tables are emptied, so the dashboard would keep
-- showing pre-wipe revenue/LTV/rider numbers until refreshed. Recompute them
-- from the now-empty base tables (→ all zero). REFRESH cannot run inside the
-- transaction above, so it follows the COMMIT.
REFRESH MATERIALIZED VIEW analytics.mv_daily_store_revenue;
REFRESH MATERIALIZED VIEW analytics.mv_monthly_franchise_revenue;
REFRESH MATERIALIZED VIEW analytics.mv_customer_ltv;
REFRESH MATERIALIZED VIEW analytics.mv_rider_performance;
REFRESH MATERIALIZED VIEW analytics.mv_warehouse_throughput;
REFRESH MATERIALIZED VIEW analytics.mv_subscription_mrr;
REFRESH MATERIALIZED VIEW analytics.mv_franchise_saas_mrr;
