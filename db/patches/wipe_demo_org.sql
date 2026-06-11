-- Stage 2 of the demo wipe (after wipe_demo_data.sql): remove the demo ORG TREE —
-- all franchises, stores, warehouses, agreements — plus the suspended demo brand
-- LG-BRAND2, and the store-scoped demo rows that block those deletes (delivery
-- slots, stock reconciliations, warehouse batches, store cash books, royalty
-- invoices).
--
-- KEEPS: the platform, the LG-MAIN brand (mobile apps pin DEFAULT_BRAND_CODE=
-- LG-MAIN), brand-level catalog (services/items/price lists — all 3 price lists
-- are brand-scoped, store_id IS NULL), brand-level kernel.system_settings
-- (WhatsApp/SMS/payment/maps/payout — all 8 rows are brand/platform scoped),
-- roles/permissions, and the super-admin. Real franchises/stores are then
-- onboarded through the admin UI (Access Control -> Onboard franchise).
--
-- Ordered child -> parent; idempotent (plain DELETEs, re-running is a no-op).

BEGIN;

-- Store/warehouse-scoped demo rows that hold FKs into the org tree.
DELETE FROM order_lifecycle.delivery_slots;
DELETE FROM order_lifecycle.stock_reconciliation_items;
DELETE FROM order_lifecycle.stock_reconciliations;
DELETE FROM order_lifecycle.warehouse_batches;
DELETE FROM finance_royalty.royalty_invoices;
DELETE FROM finance_royalty.royalty_calculations;
DELETE FROM finance_royalty.cash_books;
DELETE FROM finance_royalty.franchise_subscription_events;
DELETE FROM finance_royalty.franchise_subscription_invoices;
DELETE FROM finance_royalty.franchise_subscriptions;

-- Org tree, children first.
DELETE FROM tenancy_org.store_warehouse_mappings;
DELETE FROM tenancy_org.operating_hours;
DELETE FROM tenancy_org.holidays;
DELETE FROM tenancy_org.stores;
DELETE FROM tenancy_org.warehouses;
-- franchises.franchise_agreement_id references agreements, so franchises go first.
DELETE FROM tenancy_org.franchises;
DELETE FROM tenancy_org.franchise_agreements;
DELETE FROM tenancy_org.territories;

-- Demo second brand (suspended LG-BRAND2) + its only referencing rows.
DELETE FROM engagement_cms.mobile_app_config
WHERE brand_id IN (SELECT id FROM tenancy_org.brands WHERE code = 'LG-BRAND2');
DELETE FROM tenancy_org.brands WHERE code = 'LG-BRAND2';

COMMIT;

-- Recompute analytics rollups (cannot run inside the transaction).
SELECT analytics.refresh_all_matviews();
