-- Down for 0031. Drops the restrictive sub-brand policies and the predicate, returning the
-- franchise/store/warehouse boundary to application code alone — the state audit finding A-6
-- describes.

SET client_min_messages = WARNING;

DO $revert$
DECLARE t RECORD;
BEGIN
    FOR t IN
        SELECT schemaname AS schema, tablename AS tbl
          FROM pg_policies
         WHERE policyname = 'rls_subbrand_scope'
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS rls_subbrand_scope ON %I.%I', t.schema, t.tbl);
    END LOOP;
END
$revert$;

DROP FUNCTION IF EXISTS kernel.within_scope_cols(uuid, uuid, uuid, uuid);
