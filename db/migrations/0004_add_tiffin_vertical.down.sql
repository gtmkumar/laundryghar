-- 0004_add_tiffin_vertical — rollback: must exactly undo the .up.sql
--
-- Narrows the vertical vocabulary back to {laundry, salon, logistics}, rebuilding each constraint
-- from a canonical predicate for the same rendering-independence reason the .up.sql does.
--
-- This FAILS BY DESIGN if any tiffin row exists: re-adding the CHECK validates it against the
-- table, so a tiffin brand (or anything that inherited its vertical) blocks the rollback with a
-- constraint violation rather than being silently orphaned into a state the schema forbids.
-- Find them first:
--   SELECT 'brands'  t, id::text v FROM tenancy_org.brands       WHERE vertical_key = 'tiffin'
--   UNION ALL SELECT 'orders',  id::text FROM order_lifecycle.orders  WHERE vertical_key = 'tiffin'
--   UNION ALL SELECT 'modules', key      FROM identity_access.modules WHERE vertical_key = 'tiffin'
--   UNION ALL SELECT 'features', key     FROM identity_access.features WHERE vertical_key = 'tiffin'
--   UNION ALL SELECT 'roles',   code     FROM identity_access.roles    WHERE vertical_key = 'tiffin';

DO $$
DECLARE
    t        record;
    nullable boolean;
    new_def  text;
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
          AND  pg_get_constraintdef(con.oid) ILIKE '%tiffin%'
          AND  NOT rel.relispartition
    LOOP
        nullable := t.condef ILIKE '%IS NULL%';

        new_def := CASE WHEN nullable THEN 'CHECK (vertical_key IS NULL OR ' ELSE 'CHECK (' END
                || 'vertical_key::text = ANY (ARRAY[''laundry'',''salon'',''logistics'']::text[]))';

        EXECUTE format('ALTER TABLE %I.%I DROP CONSTRAINT %I',
                       t.schema_name, t.table_name, t.constraint_name);
        EXECUTE format('ALTER TABLE %I.%I ADD CONSTRAINT %I %s',
                       t.schema_name, t.table_name, t.constraint_name, new_def);
    END LOOP;
END $$;
