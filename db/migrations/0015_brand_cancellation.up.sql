-- 0015_brand_cancellation — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- §9: "Cancellation = export offered, wind-down retention, then deletion per DPDP."
-- §8.2: a company may "export their data at any time; take it with them if they leave."
--
-- The existing DPDP machinery (dpdp_erasure_pipeline.sql, CustomerErasureService) is
-- CUSTOMER-level: one person asks to be forgotten. There is no brand-level wind-down at all, so a
-- provider who leaves either stays live forever or gets deleted by hand.
--
-- ─── The shape, and why ──────────────────────────────────────────────────────────────────────
-- Cancellation is three states, not one:
--
--   active ──cancel──▶ cancelled ──retention elapses──▶ archived
--                          │
--                          └──withdraw──▶ active
--
--   `cancelled`  operations frozen, but LOGIN, BILLING and EXPORT stay open — the same
--                login-only shape as suspension (§9), for the same reason: the whole point of a
--                retention window is that the customer can still get their data out of it. A
--                wind-down they are locked out of is just deletion with a delay.
--   `archived`   tenant data purged. The brand ROW survives as a tombstone (see below).
--
-- Withdrawal is deliberately possible right up to the purge. People cancel by mistake, and the
-- cost of allowing a change of mind is one boolean; the cost of not allowing it is a business.
--
-- ─── Why the brand row is not deleted ────────────────────────────────────────────────────────
-- `identity_access.audit_logs.brand_id` is an FK with ON DELETE RESTRICT into a 7-year append-only
-- ledger. Deleting the brand row would either fail or require destroying the audit trail. So the
-- tenant's DATA is purged and the tenant RECORD becomes a tombstone: status='archived',
-- deleted_at set, and everything identifying scrubbed to a placeholder.
--
-- ─── What the purge deliberately does NOT delete ─────────────────────────────────────────────
-- Two categories survive, both because law outranks preference:
--   * `identity_access.audit_logs`   — the security/consent ledger (7-year retention).
--   * `identity_access.brand_platform_invoice` / `brand_platform_subscription` — our own financial
--     records of what we billed them, which carry statutory retention.
--   * `tenancy_org.brand_cancellations` — the record OF the deletion. A purge that erases the proof
--     it happened leaves nobody able to answer "did we actually delete this tenant, and when?".
-- Everything else that carries brand_id goes. This is a judgement call the strategy does not make;
-- it is logged as OQ-10 and is one constant away from changing.

-- ─── 1. The `cancelled` state ────────────────────────────────────────────────────────────────
-- Rebuilt from the canonical predicate rather than string-spliced: Postgres re-renders CHECK
-- constraints, so editing the rendered text is how a down-migration ends up unable to find what it
-- wrote (learned the hard way in 0004).
ALTER TABLE tenancy_org.brands DROP CONSTRAINT IF EXISTS brands_status_check;
ALTER TABLE tenancy_org.brands ADD CONSTRAINT brands_status_check
    CHECK (status IN ('active', 'suspended', 'cancelled', 'archived'));

-- ─── 2. The wind-down record ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenancy_org.brand_cancellations (
    id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id              uuid NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,

    requested_by_user_id  uuid REFERENCES identity_access.users(id) ON DELETE SET NULL,
    reason                text,
    requested_at          timestamptz NOT NULL DEFAULT now(),

    -- When the data may be destroyed. Never null: a wind-down with no end is not a wind-down.
    retention_until       timestamptz NOT NULL,

    status                varchar(16) NOT NULL DEFAULT 'retention'
                              CHECK (status IN ('retention', 'purged', 'withdrawn')),

    withdrawn_at          timestamptz,
    withdrawn_by_user_id  uuid REFERENCES identity_access.users(id) ON DELETE SET NULL,
    purged_at             timestamptz,

    -- Proof the §9 "export offered" step actually happened, rather than being a checkbox in a UI.
    export_count          int NOT NULL DEFAULT 0,
    last_exported_at      timestamptz,

    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now(),
    created_by            uuid,
    updated_by            uuid,

    CONSTRAINT brand_cancellations_retention_is_future
        CHECK (retention_until > requested_at),
    CONSTRAINT brand_cancellations_withdrawn_is_complete
        CHECK (status <> 'withdrawn' OR withdrawn_at IS NOT NULL),
    CONSTRAINT brand_cancellations_purged_is_complete
        CHECK (status <> 'purged' OR purged_at IS NOT NULL)
);

-- One live wind-down per brand.
CREATE UNIQUE INDEX IF NOT EXISTS idx_brand_cancellations_one_live
    ON tenancy_org.brand_cancellations (brand_id)
    WHERE status = 'retention';

CREATE INDEX IF NOT EXISTS idx_brand_cancellations_due
    ON tenancy_org.brand_cancellations (retention_until)
    WHERE status = 'retention';

ALTER TABLE tenancy_org.brand_cancellations ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy_org.brand_cancellations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON tenancy_org.brand_cancellations;
CREATE POLICY rls_brand ON tenancy_org.brand_cancellations
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT, INSERT, UPDATE ON tenancy_org.brand_cancellations TO app_user;

DROP TRIGGER IF EXISTS trg_brand_cancellations_updated_at ON tenancy_org.brand_cancellations;
CREATE TRIGGER trg_brand_cancellations_updated_at
    BEFORE UPDATE ON tenancy_org.brand_cancellations
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ─── 3. What "all of this tenant's data" actually means ──────────────────────────────────────
-- Computed from the catalogue rather than hand-listed. A hand-maintained list of ~287 tables is
-- guaranteed to be wrong within a month, and being wrong here means either exporting less than the
-- customer is owed or leaving data behind after we said it was deleted.
CREATE OR REPLACE FUNCTION kernel.brand_scoped_tables()
RETURNS TABLE (schema_name text, table_name text)
LANGUAGE sql
STABLE
AS $$
    SELECT c.table_schema::text, c.table_name::text
    FROM   information_schema.columns c
    JOIN   information_schema.tables t
           ON t.table_schema = c.table_schema AND t.table_name = c.table_name
    WHERE  c.column_name = 'brand_id'
      AND  t.table_type = 'BASE TABLE'
      AND  c.table_schema NOT IN ('pg_catalog', 'information_schema', 'partman')
      -- Partitions are reached through their parent; listing both would double every row.
      AND  NOT EXISTS (SELECT 1 FROM pg_inherits i
                       JOIN pg_class ch ON ch.oid = i.inhrelid
                       JOIN pg_namespace n ON n.oid = ch.relnamespace
                       WHERE n.nspname = c.table_schema AND ch.relname = c.table_name)
    ORDER BY 1, 2;
$$;

GRANT EXECUTE ON FUNCTION kernel.brand_scoped_tables() TO app_user;

-- ─── 4. Export — §8.2 "take it with them if they leave" ──────────────────────────────────────
-- Emits one row per record as (table, json). SECURITY DEFINER because a full export must not depend
-- on the caller's RLS reach: `tenancy_org.brands` is admin-only, and a company's own registration
-- details are exactly the sort of thing an export is expected to contain. The caller's right to the
-- brand id is checked ABOVE this, in the endpoint — this function trusts its argument, so it must
-- never be reachable by anything that does not.
CREATE OR REPLACE FUNCTION kernel.export_brand(p_brand_id uuid)
RETURNS TABLE (source text, row_data jsonb)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE t record;
BEGIN
    -- The brand's own record first, so the export opens with who this is.
    RETURN QUERY
        SELECT 'tenancy_org.brands'::text, to_jsonb(b) FROM tenancy_org.brands b WHERE b.id = p_brand_id;

    FOR t IN SELECT * FROM kernel.brand_scoped_tables() LOOP
        RETURN QUERY EXECUTE format(
            'SELECT %L::text, to_jsonb(x) FROM %I.%I x WHERE x.brand_id = $1',
            t.schema_name || '.' || t.table_name, t.schema_name, t.table_name)
        USING p_brand_id;
    END LOOP;
END $$;

REVOKE ALL ON FUNCTION kernel.export_brand(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.export_brand(uuid) TO app_user;

-- ─── 5. Deletion ─────────────────────────────────────────────────────────────────────────────
-- Deletes every brand-scoped row, then tombstones the brand.
--
-- The ordering problem: ~287 tables with foreign keys between them, in no order this function can
-- know. Rather than hand-maintain a dependency list that will rot, it deletes by FIXPOINT — repeat
-- passes, skipping tables that still have dependents, until a pass achieves nothing. If tables
-- remain at that point it RAISES, because a purge that half-finished and reported success is worse
-- than one that failed: we would have told a customer their data was gone.
CREATE OR REPLACE FUNCTION kernel.purge_brand(p_brand_id uuid)
RETURNS TABLE (source text, rows_deleted bigint)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    remaining  text[];
    still      text[];
    qualified  text;
    t          record;
    n          bigint;
    progress   boolean;
    kept       constant text[] := ARRAY[
        -- Law outranks preference; see the header. Changing this list changes what "deleted" means.
        'identity_access.audit_logs',
        'identity_access.brand_platform_invoice',
        'identity_access.brand_platform_subscription',
        -- The record of the deletion must survive the deletion. Without this the purge destroys its
        -- own wind-down row, the worker's "mark it purged" update then matches nothing, and the
        -- question asked six months later — "did we actually delete this tenant, and when?" — has no
        -- answer anywhere. Found by purging a real brand and watching the row disappear.
        'tenancy_org.brand_cancellations'
    ];
BEGIN
    SELECT array_agg(schema_name || '.' || table_name)
    INTO   remaining
    FROM   kernel.brand_scoped_tables()
    WHERE  schema_name || '.' || table_name <> ALL (kept);

    LOOP
        progress := false;
        still := ARRAY[]::text[];

        FOREACH qualified IN ARRAY COALESCE(remaining, ARRAY[]::text[]) LOOP
            BEGIN
                EXECUTE format('DELETE FROM %s WHERE brand_id = $1', qualified) USING p_brand_id;
                GET DIAGNOSTICS n = ROW_COUNT;
                progress := true;
                IF n > 0 THEN
                    source := qualified; rows_deleted := n; RETURN NEXT;
                END IF;
            EXCEPTION WHEN foreign_key_violation THEN
                -- Something still points here. Try again once its dependents are gone.
                still := still || qualified;
            END;
        END LOOP;

        remaining := still;
        EXIT WHEN cardinality(remaining) = 0;

        IF NOT progress THEN
            RAISE EXCEPTION 'purge stalled — these tables still hold data for brand % : %',
                            p_brand_id, array_to_string(remaining, ', ');
        END IF;
    END LOOP;

    -- The tombstone. The row survives for the audit ledger's FK; everything identifying does not.
    UPDATE tenancy_org.brands
       SET status = 'archived',
           deleted_at = COALESCE(deleted_at, now()),
           name = 'Deleted brand',
           legal_name = NULL, tagline = NULL, description = NULL,
           logo_url = NULL, favicon_url = NULL, website_url = NULL,
           support_email = NULL, support_phone = NULL, toll_free_number = NULL,
           whatsapp_number = NULL, play_store_url = NULL, app_store_url = NULL,
           config = '{}'::jsonb,
           updated_at = now()
     WHERE id = p_brand_id;

    source := 'tenancy_org.brands'; rows_deleted := 0; RETURN NEXT;
END $$;

REVOKE ALL ON FUNCTION kernel.purge_brand(uuid) FROM PUBLIC;
-- NOT granted to app_user. Deletion is the worker's job, running as a superuser on a schedule the
-- retention window sets — never something a live request can trigger, however well authorised.

-- ─── 5b. Moving a brand into and out of the wind-down ────────────────────────────────────────
-- THE BRANDS-RLS TRAP, FOR THE FOURTH TIME. `tenancy_org.brands` carries
-- `rls_admin_only USING (kernel.rls_bypass())`, so ordinary tenant traffic reads it as EMPTY. An
-- owner cancelling their own account is ordinary tenant traffic: the handler's
-- `_db.Brands.FirstOrDefault(...)` returned null and the endpoint answered 404 — "your brand does
-- not exist" — to the person who owns it. Found by driving it live; every unit test passed.
--
-- Fixed the same way as the previous three (resolve_brand_domain, brand_status,
-- impersonation_grant_state): grant ONE capability rather than a request-wide bypass. Note how
-- narrow it is — this function can only ever move a brand between `active` and `cancelled`. It
-- cannot suspend (that is billing's decision), and it cannot archive (that is the purge's, and only
-- after a window has elapsed). An owner cancelling can therefore never be turned into an owner
-- deleting.
CREATE OR REPLACE FUNCTION kernel.set_brand_cancellation_state(p_brand_id uuid, p_status text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = tenancy_org, kernel, pg_catalog
AS $$
DECLARE current_status text;
BEGIN
    IF p_status NOT IN ('active', 'cancelled') THEN
        RAISE EXCEPTION 'set_brand_cancellation_state only moves between active and cancelled, not %', p_status;
    END IF;

    SELECT b.status INTO current_status FROM tenancy_org.brands b WHERE b.id = p_brand_id;
    IF current_status IS NULL THEN
        RETURN NULL;              -- unknown brand; the caller answers 404
    END IF;

    -- An archived brand is already deleted; a suspended one has a billing problem that withdrawing
    -- a cancellation does not solve. Neither is this function's business.
    IF current_status = 'archived' THEN
        RAISE EXCEPTION 'brand % is archived — its data has already been deleted', p_brand_id
              USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;

    UPDATE tenancy_org.brands SET status = p_status, updated_at = now() WHERE id = p_brand_id;
    RETURN current_status;        -- what it WAS, so the caller can audit the transition
END $$;

REVOKE ALL ON FUNCTION kernel.set_brand_cancellation_state(uuid, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.set_brand_cancellation_state(uuid, text) TO app_user;

-- ─── 6. Assertions ───────────────────────────────────────────────────────────────────────────
-- Two different kinds of check, and the difference matters.
--
-- The first is a real invariant: the function must not MISS a table that carries brand_id. If it
-- does, the export shortchanges the customer and the purge leaves data behind after we told them it
-- was deleted. That aborts the migration.
--
-- The second is a heuristic: a production database has ~120 brand-scoped tables, so a much smaller
-- number suggests the function is looking in the wrong place. But trimmed test schemas legitimately
-- have very few, so this WARNS rather than aborts — an assertion that fails on a valid database
-- teaches people to ignore assertions.
DO $$
DECLARE
    found  int;
    missed text;
BEGIN
    SELECT count(*) INTO found FROM kernel.brand_scoped_tables();

    SELECT string_agg(c.table_schema || '.' || c.table_name, ', ')
    INTO   missed
    FROM   information_schema.columns c
    JOIN   information_schema.tables t
           ON t.table_schema = c.table_schema AND t.table_name = c.table_name
    WHERE  c.column_name = 'brand_id'
      AND  t.table_type = 'BASE TABLE'
      AND  c.table_schema NOT IN ('pg_catalog', 'information_schema', 'partman')
      AND  NOT EXISTS (SELECT 1 FROM pg_inherits i
                       JOIN pg_class ch ON ch.oid = i.inhrelid
                       JOIN pg_namespace n ON n.oid = ch.relnamespace
                       WHERE n.nspname = c.table_schema AND ch.relname = c.table_name)
      AND  NOT EXISTS (SELECT 1 FROM kernel.brand_scoped_tables() k
                       WHERE k.schema_name = c.table_schema AND k.table_name = c.table_name);

    IF missed IS NOT NULL THEN
        RAISE EXCEPTION 'brand_scoped_tables() misses brand-scoped table(s) — export and purge would '
                        'both be incomplete: %', missed;
    END IF;

    IF found < 50 THEN
        RAISE WARNING 'brand wind-down covers only % brand-scoped table(s) — expected ~120 on a full '
                      'schema. Fine for a trimmed test database, suspicious anywhere else.', found;
    ELSE
        RAISE NOTICE 'brand wind-down covers % brand-scoped tables', found;
    END IF;
END $$;
