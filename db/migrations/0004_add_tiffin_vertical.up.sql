-- 0004_add_tiffin_vertical — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- OQ-1 answered by Goutam 2026-08-25: "keep salon and add tiffin".
-- The vertical set goes from {laundry, salon, logistics} to {laundry, salon, logistics, tiffin}.
--
-- `vertical_key` is pinned by a CHECK on eight tables (plus every `orders` partition, which
-- inherits its parent's constraint). Widening a CHECK is not a data change — every existing row
-- already satisfies the wider predicate — so this cannot fail on existing data.
--
-- ─── Why this REBUILDS each constraint instead of splicing its text ──────────────────────────
-- The first version of this migration did `replace(condef, '''logistics''…', '''logistics''…, ''tiffin''…')`.
-- It applied fine, but was NOT reversible: Postgres re-renders a constraint when it stores it, so
-- what comes back from pg_get_constraintdef is not the text that went in
--   written:  ARRAY['laundry'::character varying, …]::text[]
--   returned: ARRAY[('laundry'::character varying)::text, …]
-- and the down migration's inverse replace then found nothing to match and aborted. Emitting a
-- canonical predicate from scratch is rendering-independent, so up→down→up is clean in both
-- directions and stays clean no matter how a future Postgres chooses to print it.
--
-- ─── What this migration does NOT do ────────────────────────────────────────────────────────
-- It registers tiffin as a legal vertical. It does NOT make tiffin *operable*: tiffin is strategy
-- Mode 3 ("Recurring Schedule"), and `orders.fulfillment_mode` still allows only
-- process_deliver / appointment / point_to_point. There is no `recurring` strategy, because OQ-2
-- (one order per occurrence, or one long-lived order with occurrence rows?) is unanswered — and
-- that answer decides whether Mode 3 extends the existing aggregate or introduces a new one.
--
-- So a brand may now be created with vertical_key='tiffin' and gets the vertical-neutral spine
-- (catalog, customers, orders, fleet, payments). Its recurring delivery calendar arrives with T-14.
-- Registering the vertical first is deliberate: it lets the feature catalogue, bundles and
-- templates be authored for tiffin without waiting on the Mode-3 design.

DO $$
DECLARE
    t          record;
    nullable   boolean;
    new_def    text;
    widened    int := 0;
BEGIN
    FOR t IN
        SELECT n.nspname   AS schema_name,
               rel.relname AS table_name,
               con.conname AS constraint_name,
               pg_get_constraintdef(con.oid) AS condef
        FROM   pg_constraint con
        JOIN   pg_class      rel ON rel.oid = con.conrelid
        JOIN   pg_namespace  n   ON n.oid   = rel.relnamespace
        WHERE  con.contype = 'c'
          AND  pg_get_constraintdef(con.oid) ILIKE '%laundry%salon%logistics%'
          AND  pg_get_constraintdef(con.oid) NOT ILIKE '%tiffin%'
          -- Partition children inherit the parent's constraint; Postgres refuses a direct DROP.
          AND  NOT rel.relispartition
    LOOP
        -- Some columns are nullable (a NULL vertical_key means "vertical-neutral"), some are NOT
        -- NULL with a default. Preserve whichever this table had.
        nullable := t.condef ILIKE '%IS NULL%';

        new_def := CASE WHEN nullable THEN 'CHECK (vertical_key IS NULL OR ' ELSE 'CHECK (' END
                || 'vertical_key::text = ANY (ARRAY[''laundry'',''salon'',''logistics'',''tiffin'']::text[]))';

        EXECUTE format('ALTER TABLE %I.%I DROP CONSTRAINT %I',
                       t.schema_name, t.table_name, t.constraint_name);
        EXECUTE format('ALTER TABLE %I.%I ADD CONSTRAINT %I %s',
                       t.schema_name, t.table_name, t.constraint_name, new_def);

        widened := widened + 1;
        RAISE NOTICE 'widened %.% (%s)', t.schema_name, t.table_name, t.constraint_name;
    END LOOP;

    RAISE NOTICE 'vertical CHECKs widened: %', widened;
END $$;

-- Assert the outcome rather than trusting the loop. A silent no-op would leave tiffin brands
-- un-creatable with no error until someone tried it in production.
DO $$
DECLARE missing text;
BEGIN
    SELECT string_agg(n.nspname || '.' || rel.relname, ', ')
    INTO   missing
    FROM   pg_constraint con
    JOIN   pg_class      rel ON rel.oid = con.conrelid
    JOIN   pg_namespace  n   ON n.oid   = rel.relnamespace
    WHERE  con.contype = 'c'
      AND  pg_get_constraintdef(con.oid) ILIKE '%laundry%salon%logistics%'
      AND  pg_get_constraintdef(con.oid) NOT ILIKE '%tiffin%'
      AND  NOT rel.relispartition;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'vertical widening missed: %', missing;
    END IF;
END $$;
