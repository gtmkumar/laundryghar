-- 0031 — give the franchise / store / warehouse boundary a database-level defence.
--
-- Audit finding A-6: "Session variables for franchise and warehouse scope are published on every
-- connection but read by zero hand-written RLS policies today — that boundary is enforced only in
-- application code (roughly 95 call sites), not the database."
--
-- Measured before writing this, against the running system, because the finding understates it.
-- Four principals at four scope levels asked the orders API for the same brand:
--
--     platform_admin   nodes=[platform brand]        6 orders
--     brand_admin      nodes=[brand:5b37…]           6 orders
--     store_admin      nodes=[store:db41…]           6 orders
--     warehouse_staff  nodes=[warehouse:0e70…]       6 orders
--
-- Identical. The sub-brand boundary is not enforced at the database (this migration's subject) and
-- it is not enforced on the list path either — the 95 IsWithinScope call sites are mutating
-- handlers, so a read simply never meets the rule. This is therefore the first layer of any kind to
-- express the boundary on reads.
--
-- ── The rule ─────────────────────────────────────────────────────────────────────────────────
--
-- kernel.within_scope_cols() is the SQL sibling of ICurrentUser.IsWithinScope and of the ABAC
-- engine's authz.within_scope(jsonb) — same scope_nodes claim, same node-match semantics, same
-- three-state handling (unresolved → NULL → deny; resolved-but-empty → deny). It differs from both
-- in exactly one way, deliberately:
--
--     a row whose column for the caller's scope LEVEL is NULL does not constrain that caller.
--
-- IsWithinScope denies in that case. It can afford to, because it is called from handlers that have
-- already loaded a resource which HAS those ids. An RLS policy has no such luxury: it sees every
-- row of every table, including rows that are legitimately brand-level. Measured on live data, a
-- policy without this arm would have hidden 977 of 977 audit_logs rows, all 16 kernel.system_settings
-- rows, all 4 price_lists and all 3 fulfillment_unit_tags from every franchise- and store-scoped
-- user — and, worse, every order from warehouse staff, because order_lifecycle.orders.warehouse_id
-- is NULL on all 9 rows and tenancy_org.store_warehouse_mappings is empty, so no data-model path
-- from a warehouse membership to an order exists at all today.
--
-- What the arm costs is nothing that was ever protected: a row that names no store is not some
-- other store's row. What it keeps is the whole point — a row that names a DIFFERENT franchise or
-- store is now refused by the database. That is a strict tightening of current behaviour and never
-- a loosening, which is the property a backstop has to have.
--
-- ── Why RESTRICTIVE ──────────────────────────────────────────────────────────────────────────
--
-- The existing rls_brand policies are PERMISSIVE, and PostgreSQL ORs permissive policies together —
-- so adding another permissive policy could only ever WIDEN access. A RESTRICTIVE policy is ANDed
-- with them instead. The effective rule per row becomes
--
--     (brand policy) AND (kernel.rls_bypass() OR within-scope)
--
-- which leaves all 136 existing policies untouched and unread, and cannot grant anything.
--
-- Platform sessions are unaffected: rls_bypass() short-circuits, and a 'platform' node returns true
-- on its own merits.

SET client_min_messages = WARNING;

-- ── The predicate ────────────────────────────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION kernel.within_scope_cols(
    p_brand     uuid,
    p_franchise uuid,
    p_store     uuid,
    p_warehouse uuid
) RETURNS boolean
LANGUAGE plpgsql
STABLE PARALLEL SAFE
AS $fn$
DECLARE
    v_nodes text[];
    v_node  text;
    v_type  text;
    v_id    text;
    v_col   uuid;
    v_known boolean;
BEGIN
    v_nodes := kernel.current_scope_nodes();

    -- Three-state, matching authz.within_scope exactly. An UNRESOLVED claim is not the same as a
    -- resolved empty one: unresolved means nobody has told us, which must not read as a decision.
    -- Under a RESTRICTIVE policy a NULL is not satisfied, so both arms fail closed.
    IF v_nodes IS NULL THEN RETURN NULL; END IF;
    IF cardinality(v_nodes) = 0 THEN RETURN false; END IF;

    FOREACH v_node IN ARRAY v_nodes LOOP
        IF v_node = 'platform' THEN RETURN true; END IF;   -- ancestor of every node

        v_type := split_part(v_node, ':', 1);
        v_id   := split_part(v_node, ':', 2);
        CONTINUE WHEN v_id = '';

        v_known := true;
        v_col := CASE v_type
            WHEN 'brand'     THEN p_brand
            WHEN 'franchise' THEN p_franchise
            WHEN 'store'     THEN p_store
            WHEN 'warehouse' THEN p_warehouse
            ELSE NULL
        END;

        -- 'territory' and any future scope type land here: not a column this rule knows how to
        -- test, so it neither grants nor denies and the next node is tried.
        IF v_type NOT IN ('brand','franchise','store','warehouse') THEN CONTINUE; END IF;

        -- The row is not scoped at this caller's level, so this caller's level says nothing about
        -- it; the brand policy ANDed with this one still confines it to the caller's tenant.
        IF v_col IS NULL THEN RETURN true; END IF;

        IF v_col::text = v_id THEN RETURN true; END IF;
    END LOOP;

    RETURN false;
END
$fn$;

COMMENT ON FUNCTION kernel.within_scope_cols(uuid, uuid, uuid, uuid) IS
    'Ancestor-or-self scope test for RLS, from the app.current_scope_nodes GUC. SQL sibling of '
    'ICurrentUser.IsWithinScope and authz.within_scope(jsonb); differs only in that a NULL column '
    'means "this row is not scoped at the caller''s level" and does not deny. See migration 0031.';

GRANT EXECUTE ON FUNCTION kernel.within_scope_cols(uuid, uuid, uuid, uuid) TO app_user, app_admin;

-- ── The policy, on every RLS-enabled table carrying a sub-brand column ────────────────────────
--
-- The table list is explicit (a new table must be added deliberately, not inherited by accident);
-- which of the four columns each one actually has is read from the catalog, so the generated call
-- can never name a column that is not there.

DO $apply$
DECLARE
    t         RECORD;
    has_brand boolean;
    has_fran  boolean;
    has_store boolean;
    has_wh    boolean;
    pred      text;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('commerce','payments'),
            ('customer_catalog','price_lists'),
            ('finance_royalty','cash_book_entries'),
            ('finance_royalty','cash_books'),
            ('finance_royalty','expenses'),
            ('finance_royalty','franchise_subscription_events'),
            ('finance_royalty','franchise_subscription_invoices'),
            ('finance_royalty','franchise_subscriptions'),
            ('finance_royalty','royalty_calculations'),
            ('finance_royalty','royalty_invoices'),
            ('finance_royalty','shift_handovers'),
            ('identity_access','audit_logs'),
            ('kernel','system_settings'),
            ('laundry_fulfillment','fulfillment_unit'),
            ('laundry_fulfillment','fulfillment_unit_tags'),
            ('laundry_fulfillment','process_logs'),
            ('laundry_fulfillment','quality_checks'),
            ('laundry_fulfillment','stock_reconciliations'),
            ('laundry_fulfillment','warehouse_batches'),
            ('logistics','rider_assignments'),
            ('logistics','rider_capacity_config'),
            ('logistics','rider_payout_requests'),
            ('logistics','rider_settlements'),
            ('logistics','riders'),
            ('order_lifecycle','delivery_assignments'),
            ('order_lifecycle','delivery_schedules'),
            ('order_lifecycle','delivery_slot_bookings'),
            ('order_lifecycle','delivery_slots'),
            ('order_lifecycle','invoice_number_sequences'),
            ('order_lifecycle','order_items'),
            ('order_lifecycle','order_number_sequences'),
            ('order_lifecycle','orders'),
            ('order_lifecycle','pickup_requests'),
            ('salon_fulfillment','appointments'),
            ('salon_fulfillment','resources'),
            ('salon_fulfillment','staff_members'),
            ('tenancy_org','store_warehouse_mappings'),
            ('tenancy_org','stores'),
            ('tenancy_org','warehouses')
        ) AS v(schema, tbl)
    LOOP
        CONTINUE WHEN to_regclass(format('%I.%I', t.schema, t.tbl)) IS NULL;

        SELECT bool_or(column_name='brand_id'),     bool_or(column_name='franchise_id'),
               bool_or(column_name='store_id'),     bool_or(column_name='warehouse_id')
          INTO has_brand, has_fran, has_store, has_wh
          FROM information_schema.columns
         WHERE table_schema = t.schema AND table_name = t.tbl;

        pred := format(
            'kernel.rls_bypass() OR kernel.within_scope_cols(%s, %s, %s, %s)',
            CASE WHEN has_brand THEN 'brand_id'     ELSE 'NULL::uuid' END,
            CASE WHEN has_fran  THEN 'franchise_id' ELSE 'NULL::uuid' END,
            CASE WHEN has_store THEN 'store_id'     ELSE 'NULL::uuid' END,
            CASE WHEN has_wh    THEN 'warehouse_id' ELSE 'NULL::uuid' END);

        EXECUTE format('DROP POLICY IF EXISTS rls_subbrand_scope ON %I.%I', t.schema, t.tbl);
        EXECUTE format(
            'CREATE POLICY rls_subbrand_scope ON %I.%I AS RESTRICTIVE FOR ALL TO app_user '
            'USING (%s) WITH CHECK (%s)',
            t.schema, t.tbl, pred, pred);
    END LOOP;
END
$apply$;
