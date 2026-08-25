-- 0012_recurring_fulfillment_mode — rollback: must exactly undo the .up.sql
--
-- FAILS BY DESIGN if any recurring order exists — narrowing the CHECK is validated against the
-- table, so live occurrences block the rollback rather than being orphaned into a state the schema
-- forbids. Check first:
--   SELECT id, scheduled_for FROM order_lifecycle.orders WHERE fulfillment_mode = 'recurring';

UPDATE identity_access.vertical_templates SET is_public = false WHERE key = 'tiffin';

DROP TABLE IF EXISTS order_lifecycle.delivery_schedule_occurrences;
ALTER TABLE order_lifecycle.orders DROP COLUMN IF EXISTS scheduled_for;
ALTER TABLE order_lifecycle.orders DROP COLUMN IF EXISTS delivery_schedule_id;

DROP TABLE IF EXISTS order_lifecycle.delivery_schedules;

DO $$
DECLARE c record;
BEGIN
    FOR c IN
        SELECT con.conname, rel.relname, nsp.nspname
        FROM   pg_constraint con
        JOIN   pg_class rel     ON rel.oid = con.conrelid
        JOIN   pg_namespace nsp ON nsp.oid = rel.relnamespace
        WHERE  con.contype = 'c'
          AND  pg_get_constraintdef(con.oid) ILIKE '%recurring%'
          AND  pg_get_constraintdef(con.oid) ILIKE '%process_deliver%'
          -- Scope to the orders spine ONLY. identity_access.vertical_templates carries its own
          -- fulfillment_mode CHECK (migration 0011) that legitimately includes 'recurring' for the
          -- tiffin template; sweeping it up here made the rollback fail against its own seed row.
          AND  rel.relname = 'orders'
          AND  NOT rel.relispartition
    LOOP
        EXECUTE format('ALTER TABLE %I.%I DROP CONSTRAINT %I', c.nspname, c.relname, c.conname);
        EXECUTE format(
            'ALTER TABLE %I.%I ADD CONSTRAINT %I CHECK (fulfillment_mode::text = ANY '
            '(ARRAY[''process_deliver'',''appointment'',''point_to_point'']::text[]))',
            c.nspname, c.relname, c.conname);
    END LOOP;
END $$;
