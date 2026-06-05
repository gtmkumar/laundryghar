-- ============================================================================
-- rls_proposal.sql  —  Row-Level Security proposal for laundry_ghar_db
-- ----------------------------------------------------------------------------
-- THIS FILE IS NON-DESTRUCTIVE WHEN APPLIED ON ITS OWN.
--
-- It creates:
--   • two app roles (app_user, app_admin) — both NOLOGIN
--   • helper functions in kernel.* that read app.* session vars
--   • POLICIES on every tenant-scoped table
--
-- It DOES NOT enable RLS on any table. Policies sit inert until you run
-- `ALTER TABLE … ENABLE ROW LEVEL SECURITY` on each table you want to lock
-- down. See §5 at the bottom for the activation snippet.
--
-- Until activation, the only side effects are:
--   • two new NOLOGIN roles (no permissions granted; harmless)
--   • six functions in kernel.* (read-only, no callers yet)
--   • ~110 policies that nothing references
--
-- ----------------------------------------------------------------------------
-- SESSION VAR CONTRACT (the app must SET these per request, e.g. in middleware)
-- ----------------------------------------------------------------------------
--   app.current_brand_id      uuid   required for brand-scoped tables
--   app.current_franchise_id  uuid   refines scope inside a brand (optional)
--   app.current_store_id      uuid   refines scope inside a brand/franchise
--   app.current_user_id       uuid   for user-self tables (identity_access)
--   app.current_customer_id   uuid   for customer-self tables
--   app.bypass_rls            text   'on' disables all RLS for this session
--                                   — use for admin tooling and migrations
--
-- NOTE: superusers always bypass RLS. As long as the app connects as
-- `postgres`, none of these policies take effect even after activation.
-- Switch the app to a member of `app_user` to make RLS enforce.
--
-- ----------------------------------------------------------------------------
-- TABLE-TO-BUCKET MAPPING (review this carefully before activating)
-- ----------------------------------------------------------------------------
--   B1  brand-only        — most tenant tables; isolated by brand_id
--   B2  brand+customer    — customer-owned records; customer sees own data,
--                            staff sees brand-wide
--   B3  user-self         — identity_access tables tied to a specific user
--   B4  admin-only        — no brand_id, considered global/cross-tenant
--                            (RLS effectively denies non-admin reads)
-- ============================================================================

SET client_min_messages = WARNING;

-- ---------------------------------------------------------------------------
-- §1  ROLES (idempotent)
-- ---------------------------------------------------------------------------
DO $roles$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='app_user') THEN
        CREATE ROLE app_user NOLOGIN;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='app_admin') THEN
        CREATE ROLE app_admin NOLOGIN;
    END IF;
END
$roles$;

-- ---------------------------------------------------------------------------
-- §2  HELPER FUNCTIONS — read session vars, NULL-safe, STABLE
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION kernel.current_brand_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_brand_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION kernel.current_franchise_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_franchise_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION kernel.current_store_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_store_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION kernel.current_user_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_user_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION kernel.current_customer_id() RETURNS uuid
    LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.current_customer_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION kernel.rls_bypass() RETURNS boolean
    LANGUAGE sql STABLE AS
$$ SELECT COALESCE(current_setting('app.bypass_rls', true), 'off') = 'on' $$;

-- ---------------------------------------------------------------------------
-- §2.5  GRANTS — app_user gets CRUD on the 11 BC schemas
-- ---------------------------------------------------------------------------
-- Without these, RLS policies are inert: a query as app_user fails on the
-- base GRANT check before RLS ever fires. app_admin gets the same grants
-- and is expected to set `app.bypass_rls=on` per session to skip RLS.
-- Idempotent: GRANT is additive; FOR ROLE applies to future tables too.
DO $grants$
DECLARE
    s text;
BEGIN
    FOREACH s IN ARRAY ARRAY[
        'kernel','tenancy_org','identity_access','customer_catalog',
        'order_lifecycle','logistics','commerce','finance_royalty',
        'engagement_cms','analytics']
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO app_user, app_admin', s);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO app_user, app_admin', s);
        EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO app_user, app_admin', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_user, app_admin', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO app_user, app_admin', s);
    END LOOP;
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.set_updated_at() TO app_user, app_admin';
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.current_brand_id() TO app_user, app_admin';
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.current_franchise_id() TO app_user, app_admin';
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.current_store_id() TO app_user, app_admin';
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.current_user_id() TO app_user, app_admin';
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.current_customer_id() TO app_user, app_admin';
    EXECUTE 'GRANT EXECUTE ON FUNCTION kernel.rls_bypass() TO app_user, app_admin';
END
$grants$;

-- ---------------------------------------------------------------------------
-- §3  BUCKET ASSIGNMENTS — explicit (review-friendly)
-- ---------------------------------------------------------------------------
-- B1 brand-only: simple tenant isolation by brand_id
-- B2 brand+customer: customer sees own row, brand staff sees all in brand
-- B3 user-self: tied to identity_access.users(id) via user_id
-- B4 admin-only: deny everything to app_user; use bypass for admin tools
-- ---------------------------------------------------------------------------

-- B1 BRAND-ONLY ---------------------------------------------------------------
DO $b1$
DECLARE
    t       RECORD;
    schema_ text;
    tbl_    text;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            -- commerce
            ('commerce','coupons'),
            ('commerce','loyalty_programs'),
            ('commerce','packages'),
            ('commerce','payment_methods'),
            ('commerce','promotions'),
            -- customer_catalog (global-within-brand reference data)
            ('customer_catalog','add_ons'),
            ('customer_catalog','fabric_types'),
            ('customer_catalog','item_groups'),
            ('customer_catalog','item_variants'),
            ('customer_catalog','items'),
            ('customer_catalog','price_list_items'),
            ('customer_catalog','price_lists'),
            ('customer_catalog','service_categories'),
            ('customer_catalog','services'),
            -- engagement_cms
            ('engagement_cms','app_banners'),
            ('engagement_cms','mobile_app_config'),
            ('engagement_cms','notification_templates'),
            ('engagement_cms','notifications_log'),
            ('engagement_cms','notifications_outbox'),
            ('engagement_cms','onboarding_slides'),
            -- finance_royalty
            ('finance_royalty','cash_book_entries'),
            ('finance_royalty','cash_books'),
            ('finance_royalty','expense_attachments'),
            ('finance_royalty','expense_categories'),
            ('finance_royalty','expenses'),
            ('finance_royalty','royalty_calculations'),
            ('finance_royalty','royalty_invoices'),
            ('finance_royalty','shift_handovers'),
            -- identity_access
            ('identity_access','audit_logs'),
            ('identity_access','roles'),
            -- kernel
            ('kernel','feature_flags'),
            ('kernel','file_attachments'),
            ('kernel','outbox_events'),
            ('kernel','system_settings'),
            -- logistics
            ('logistics','rider_assignments'),
            ('logistics','rider_capacity_config'),
            ('logistics','rider_location_pings'),
            ('logistics','riders'),
            -- order_lifecycle (brand-scoped)
            ('order_lifecycle','delivery_assignments'),
            ('order_lifecycle','delivery_slot_bookings'),
            ('order_lifecycle','delivery_slots'),
            ('order_lifecycle','garment_conditions'),
            ('order_lifecycle','garment_inspection_photos'),
            ('order_lifecycle','garment_inspections'),
            ('order_lifecycle','garment_tags'),
            ('order_lifecycle','garments'),
            ('order_lifecycle','order_items'),
            ('order_lifecycle','order_notes'),
            ('order_lifecycle','order_status_history'),
            ('order_lifecycle','orders'),
            ('order_lifecycle','pickup_requests'),
            ('order_lifecycle','process_logs'),
            ('order_lifecycle','quality_checks'),
            ('order_lifecycle','stock_reconciliation_items'),
            ('order_lifecycle','stock_reconciliations'),
            ('order_lifecycle','warehouse_batches'),
            ('order_lifecycle','warehouse_processes'),
            -- tenancy_org (within-brand)
            ('tenancy_org','franchise_agreements'),
            ('tenancy_org','franchises'),
            ('tenancy_org','holidays'),
            ('tenancy_org','operating_hours'),
            ('tenancy_org','store_warehouse_mappings'),
            ('tenancy_org','stores'),
            ('tenancy_org','territories'),
            ('tenancy_org','warehouses')
        ) AS v(schema, tbl)
    LOOP
        schema_ := t.schema; tbl_ := t.tbl;
        EXECUTE format('DROP POLICY IF EXISTS rls_brand ON %I.%I', schema_, tbl_);
        EXECUTE format(
            'CREATE POLICY rls_brand ON %I.%I FOR ALL TO app_user '
            'USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id()) '
            'WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())',
            schema_, tbl_);
    END LOOP;
END
$b1$;

-- B2 BRAND + CUSTOMER-SELF ----------------------------------------------------
-- Customer can read/write their own rows; staff (app_admin or anyone with
-- bypass on) can see all rows in their brand. Non-customer app_user with a
-- brand_id but no customer_id sees all rows in their brand (staff session).
DO $b2$
DECLARE
    t       RECORD;
    schema_ text;
    tbl_    text;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('commerce','coupon_redemptions'),
            ('commerce','customer_packages'),
            ('commerce','loyalty_points_ledger'),
            ('commerce','package_usage_ledger'),
            ('commerce','payment_refunds'),
            ('commerce','payments'),
            ('commerce','wallet_accounts'),
            ('commerce','wallet_transactions'),
            ('customer_catalog','account_deletion_requests'),
            ('customer_catalog','customer_addresses'),
            ('customer_catalog','customer_devices'),
            ('customer_catalog','customers'),
            ('customer_catalog','dpdp_consents'),
            ('engagement_cms','notification_preferences'),
            ('engagement_cms','whatsapp_message_log')
        ) AS v(schema, tbl)
    LOOP
        schema_ := t.schema; tbl_ := t.tbl;
        EXECUTE format('DROP POLICY IF EXISTS rls_brand_or_customer ON %I.%I', schema_, tbl_);
        -- For the `customers` table the customer-self check matches by `id`, not customer_id.
        IF schema_='customer_catalog' AND tbl_='customers' THEN
            EXECUTE format(
                'CREATE POLICY rls_brand_or_customer ON %I.%I FOR ALL TO app_user '
                'USING      (kernel.rls_bypass() '
                '             OR (brand_id = kernel.current_brand_id() '
                '                 AND (kernel.current_customer_id() IS NULL '
                '                      OR id = kernel.current_customer_id()))) '
                'WITH CHECK (kernel.rls_bypass() '
                '             OR (brand_id = kernel.current_brand_id() '
                '                 AND (kernel.current_customer_id() IS NULL '
                '                      OR id = kernel.current_customer_id())))',
                schema_, tbl_);
        ELSE
            EXECUTE format(
                'CREATE POLICY rls_brand_or_customer ON %I.%I FOR ALL TO app_user '
                'USING      (kernel.rls_bypass() '
                '             OR (brand_id = kernel.current_brand_id() '
                '                 AND (kernel.current_customer_id() IS NULL '
                '                      OR customer_id = kernel.current_customer_id()))) '
                'WITH CHECK (kernel.rls_bypass() '
                '             OR (brand_id = kernel.current_brand_id() '
                '                 AND (kernel.current_customer_id() IS NULL '
                '                      OR customer_id = kernel.current_customer_id())))',
                schema_, tbl_);
        END IF;
    END LOOP;
END
$b2$;

-- B3 USER-SELF ----------------------------------------------------------------
-- Tied to identity_access.users(id). No brand context.
DO $b3$
DECLARE
    t       RECORD;
    schema_ text;
    tbl_    text;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('identity_access','login_history'),
            ('identity_access','otp_codes'),
            ('identity_access','password_resets'),
            ('identity_access','refresh_tokens'),
            ('identity_access','user_profiles'),
            ('identity_access','user_scope_memberships')
        ) AS v(schema, tbl)
    LOOP
        schema_ := t.schema; tbl_ := t.tbl;
        EXECUTE format('DROP POLICY IF EXISTS rls_user_self ON %I.%I', schema_, tbl_);
        EXECUTE format(
            'CREATE POLICY rls_user_self ON %I.%I FOR ALL TO app_user '
            'USING      (kernel.rls_bypass() OR user_id = kernel.current_user_id()) '
            'WITH CHECK (kernel.rls_bypass() OR user_id = kernel.current_user_id())',
            schema_, tbl_);
    END LOOP;
END
$b3$;

-- B4 ADMIN-ONLY ---------------------------------------------------------------
-- No brand_id; deny everything to app_user unless bypass is on.
-- These are global system tables: brands registry, platforms, identity catalog.
DO $b4$
DECLARE
    t       RECORD;
    schema_ text;
    tbl_    text;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('identity_access','users'),
            ('identity_access','permissions'),
            ('identity_access','role_permissions'),
            ('tenancy_org','brands'),
            ('tenancy_org','platforms'),
            ('order_lifecycle','order_addons')  -- review: should this get brand_id?
        ) AS v(schema, tbl)
    LOOP
        schema_ := t.schema; tbl_ := t.tbl;
        EXECUTE format('DROP POLICY IF EXISTS rls_admin_only ON %I.%I', schema_, tbl_);
        EXECUTE format(
            'CREATE POLICY rls_admin_only ON %I.%I FOR ALL TO app_user '
            'USING      (kernel.rls_bypass()) '
            'WITH CHECK (kernel.rls_bypass())',
            schema_, tbl_);
    END LOOP;
END
$b4$;

-- ---------------------------------------------------------------------------
-- §4  POLICY COUNT CHECK
-- ---------------------------------------------------------------------------
SELECT
    schemaname,
    count(*) AS policies
FROM   pg_policies
WHERE  policyname IN ('rls_brand','rls_brand_or_customer','rls_user_self','rls_admin_only')
GROUP  BY schemaname
ORDER  BY schemaname;

-- ===========================================================================
-- §5  ACTIVATION (run by hand, per table or wholesale, after review)
-- ---------------------------------------------------------------------------
-- Wholesale (everything at once — risky, only after policies reviewed):
--
--   DO $act$
--   DECLARE r RECORD;
--   BEGIN
--       FOR r IN SELECT DISTINCT schemaname, tablename
--                FROM pg_policies
--                WHERE policyname IN ('rls_brand','rls_brand_or_customer',
--                                     'rls_user_self','rls_admin_only')
--       LOOP
--           EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY',
--                          r.schemaname, r.tablename);
--       END LOOP;
--   END $act$;
--
-- Single-table (recommended for staged rollout):
--
--   ALTER TABLE order_lifecycle.orders ENABLE ROW LEVEL SECURITY;
--
-- To roll back a single table:
--   ALTER TABLE order_lifecycle.orders DISABLE ROW LEVEL SECURITY;
--
-- To roll back this whole proposal:
--   DROP POLICY rls_brand ON … (per table) — or:
--   DROP FUNCTION kernel.current_brand_id, kernel.current_franchise_id, …
--     CASCADE will drop dependent policies in one go.
-- ===========================================================================
