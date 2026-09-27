-- Down for 0026. Removes the backfilled projection and leaves the (empty) authz schema from 0024.
--
-- Order matters only for readability: policy_condition cascades from policy. The backfilled rows
-- are identified by their key prefix, so a hand-authored policy created after the backfill is left
-- untouched — rolling back the projection must not delete somebody's real rule.

DELETE FROM authz.policy WHERE key LIKE 'rbac.permit.%' OR key LIKE 'rbac.deny.%';

-- Resource types and actions are left in place deliberately: a hand-authored policy may already
-- reference them, and both tables are pure vocabulary — an unused row costs nothing, whereas
-- deleting one that is still referenced fails on the FK and blocks the rollback.

-- Restore the pre-0026 generator, which assumed a backing table always exists.
CREATE OR REPLACE FUNCTION authz.generated_policy_body(p_resource_type text, p_action text)
RETURNS text
LANGUAGE plpgsql STABLE AS
$$
DECLARE
    rt        authz.resource_type%ROWTYPE;
    v_pairs   text;
BEGIN
    SELECT * INTO rt FROM authz.resource_type WHERE key = p_resource_type AND is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'authz.resource_type % is not registered', p_resource_type;
    END IF;

    SELECT string_agg(
               quote_literal(substring(k FROM 10)) || ', ' || quote_ident(v),
               ', ' ORDER BY k)
      INTO v_pairs
      FROM jsonb_each_text(rt.attribute_map) AS m(k, v)
     WHERE k LIKE 'resource.%';

    IF v_pairs IS NULL THEN
        RAISE EXCEPTION 'authz.resource_type % maps no resource.* attributes', p_resource_type;
    END IF;

    RETURN format(
        'kernel.rls_bypass() OR authz.permits(%L, %L, jsonb_build_object(%s))',
        p_resource_type, p_action, v_pairs);
END
$$;

CREATE OR REPLACE VIEW authz.rls_drift AS
SELECT
    rt.key                       AS resource_type,
    rt.schema_name,
    rt.table_name,
    gen.policy_name,
    live.qual                    AS live_using,
    gen.statement                AS expected_statement,
    CASE WHEN live.policyname IS NULL THEN 'missing' ELSE 'modified' END AS drift_kind
FROM authz.resource_type rt
CROSS JOIN LATERAL authz.generated_policy_sql(rt.key) gen
LEFT JOIN pg_policies live
       ON live.schemaname = rt.schema_name
      AND live.tablename  = rt.table_name
      AND live.policyname = gen.policy_name
WHERE rt.is_active
  AND rt.attribute_map ? 'resource.brand_id'
  AND (
        live.policyname IS NULL
        OR regexp_replace(lower(coalesce(live.qual, '')), '\s+', '', 'g')
           NOT LIKE '%authz.permits%'
      );

-- Rows registered by 0026 have no table, so restoring NOT NULL requires clearing them first.
DELETE FROM authz.resource_type WHERE schema_name IS NULL OR table_name IS NULL;

ALTER TABLE authz.resource_type ALTER COLUMN schema_name SET NOT NULL;
ALTER TABLE authz.resource_type ALTER COLUMN table_name  SET NOT NULL;
