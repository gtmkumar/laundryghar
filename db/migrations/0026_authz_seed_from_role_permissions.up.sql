-- 0026 — A6.3: project the existing role_permissions rows onto authz.policy.
--
-- The point of this migration is that it changes NOTHING about who can do what. It is a projection
-- of the authority model that is already live, not a rewrite of it, so that when the engine is
-- switched from shadow to enforce for a module, the answer it gives is the answer the RBAC gate was
-- already giving. Every tightening — a refund ceiling, a time window, an ownership rule — is a
-- later, deliberate edit to a policy row, made against a baseline that provably matched.
--
-- The projection:
--   * one PERMIT policy per permission code, conditioned on `subject.permissions contains <code>`;
--   * one DENY policy per role-level deny row, conditioned on `subject.roles contains <role>`.
--
-- resource_type comes from permissions.module and action from permissions.action, both of which
-- already exist and are already maintained — inventing a new taxonomy here would create a second
-- thing to keep in sync, which is the failure this whole plan is trying to end.

-- ── 1. A resource type does not always have a table ───────────────────────────────────────────
-- `analytics`, `report`, `saas` and friends are legitimate policy targets with no single backing
-- row. Forcing a table name for them would make authz.generated_policy_sql emit a CREATE POLICY
-- against a table that does not exist, so the columns become nullable and the generator refuses
-- (rather than guesses) when they are null.
ALTER TABLE authz.resource_type ALTER COLUMN schema_name DROP NOT NULL;
ALTER TABLE authz.resource_type ALTER COLUMN table_name  DROP NOT NULL;

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
    IF rt.schema_name IS NULL OR rt.table_name IS NULL THEN
        RAISE EXCEPTION 'authz.resource_type % has no backing table; RLS cannot be generated for it',
            p_resource_type;
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

-- The drift view must skip types with no table, or every CROSS JOIN LATERAL raises.
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
  AND rt.schema_name IS NOT NULL
  AND rt.table_name  IS NOT NULL
  AND rt.attribute_map ? 'resource.brand_id'
  AND (
        live.policyname IS NULL
        OR regexp_replace(lower(coalesce(live.qual, '')), '\s+', '', 'g')
           NOT LIKE '%authz.permits%'
      );

-- ── 2. Action vocabulary ──────────────────────────────────────────────────────────────────────
-- 0024 seeded ten generic actions. The live catalogue uses 72, including compound ones like
-- `pricelist.publish`. Taking them verbatim keeps the projection lossless: collapsing
-- `item.delete` and `category.delete` onto a single `delete` would merge two policies that gate
-- different things and silently widen both.
INSERT INTO authz.action (key, description)
SELECT DISTINCT p.action, 'Backfilled from identity_access.permissions.action'
FROM identity_access.permissions p
WHERE p.status = 'active'
ON CONFLICT (key) DO NOTHING;

-- ── 3. Resource types ─────────────────────────────────────────────────────────────────────────
INSERT INTO authz.resource_type (key, schema_name, table_name, attribute_map, description)
SELECT DISTINCT p.module, NULL, NULL, '{}'::jsonb,
       'Backfilled from identity_access.permissions.module'
FROM identity_access.permissions p
WHERE p.status = 'active'
ON CONFLICT (key) DO NOTHING;

-- Bind the modules that DO have one obvious backing table, and build attribute_map from the
-- columns that actually exist rather than from what we expect to exist. A map naming a column that
-- is not there would produce a policy body that fails at query time on that table only — the kind
-- of breakage that shows up in production on one endpoint and nowhere in a test.
DO $$
DECLARE
    m record;
    v_map jsonb;
BEGIN
    FOR m IN
        SELECT * FROM (VALUES
            ('orders',      'order_lifecycle',  'orders'),
            ('payment',     'commerce',         'payments'),
            ('coupons',     'commerce',         'coupons'),
            ('customer',    'customer_catalog', 'customers'),
            ('expense',     'finance_royalty',  'expenses'),
            ('brands',      'tenancy_org',      'brands'),
            ('franchises',  'tenancy_org',      'franchises'),
            ('stores',      'tenancy_org',      'stores'),
            ('warehouses',  'tenancy_org',      'warehouses'),
            ('users',       'identity_access',  'users')
        ) AS t(module, schema_name, table_name)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM authz.resource_type WHERE key = m.module) THEN
            CONTINUE;
        END IF;

        SELECT coalesce(jsonb_object_agg('resource.' || c.column_name, c.column_name), '{}'::jsonb)
          INTO v_map
          FROM information_schema.columns c
         WHERE c.table_schema = m.schema_name
           AND c.table_name   = m.table_name
           AND c.column_name IN ('id', 'brand_id', 'franchise_id', 'store_id', 'warehouse_id',
                                 'customer_id', 'status', 'created_at');

        -- No brand column means no tenant boundary to generate against; leave it unbound rather
        -- than register a table the generator would emit an unscoped policy for.
        IF NOT (v_map ? 'resource.brand_id') THEN
            CONTINUE;
        END IF;

        UPDATE authz.resource_type
           SET schema_name   = m.schema_name,
               table_name    = m.table_name,
               attribute_map = v_map,
               updated_at    = now()
         WHERE key = m.module;
    END LOOP;
END
$$;

-- ── 4. Permit policies — one per permission code ──────────────────────────────────────────────
-- `permission_code` is carried on the header so "what can this role do?" stays answerable by a
-- join, which is what the admin console's matrix needs and what a pure-ABAC model would have
-- thrown away.
INSERT INTO authz.policy
    (key, brand_id, description, effect, resource_type, action, permission_code, priority)
SELECT
    'rbac.permit.' || p.code,
    NULL,
    'A6.3 projection of permission ' || p.code || '. Behaviourally identical to the RBAC gate.',
    'permit',
    p.module,
    p.action,
    p.code,
    500                       -- well below any hand-authored policy, so a real rule outranks it
FROM identity_access.permissions p
WHERE p.status = 'active'
ON CONFLICT (key, version) DO NOTHING;

INSERT INTO authz.policy_condition
    (policy_id, parent_id, node_type, left_attribute, operator, right_kind, right_value, sort_order)
SELECT
    pol.id, NULL, 'compare', 'subject.permissions', 'contains', 'literal',
    to_jsonb(pol.permission_code), 0
FROM authz.policy pol
WHERE pol.key LIKE 'rbac.permit.%'
  AND pol.permission_code IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM authz.policy_condition c WHERE c.policy_id = pol.id);

-- ── 5. Deny policies — one per role-level deny row ────────────────────────────────────────────
-- Strictly speaking these are redundant today: ScopeResolver already subtracts the union of denies
-- from the union of allows before the token is minted, so a denied code never reaches the
-- `permissions` claim and the permit policy above cannot match. They are written anyway, because
-- "the deny is enforced somewhere upstream" is exactly the assumption that made A0.1 possible —
-- two grant-propagation scripts read a DENY row as evidence of HOLDING a permission, and a
-- read-only auditor ended up able to rewrite a brand's custom domain. A deny that is stated
-- explicitly at the point of decision cannot be lost by a change to how tokens are minted.
INSERT INTO authz.policy
    (key, brand_id, description, effect, resource_type, action, permission_code, priority)
SELECT DISTINCT
    'rbac.deny.' || r.code || '.' || p.code,
    -- Cast is load-bearing: SELECT DISTINCT has to resolve every output column's type in order to
    -- dedupe, and it settles an untyped NULL as `text` BEFORE the INSERT gets a chance to infer
    -- uuid from the target column. The permit projection above is the same shape without DISTINCT,
    -- which is why it needs no cast and why this only surfaced on the second statement.
    NULL::uuid,
    'A6.3 projection of the DENY row on role ' || r.code || ' for ' || p.code || '.',
    'deny',
    p.module,
    p.action,
    p.code,
    100                       -- denies evaluate first regardless; priority only orders among denies
FROM identity_access.role_permissions rp
JOIN identity_access.roles       r ON r.id = rp.role_id
JOIN identity_access.permissions p ON p.id = rp.permission_id
WHERE rp.effect = 'deny'
  AND p.status = 'active'
ON CONFLICT (key, version) DO NOTHING;

INSERT INTO authz.policy_condition
    (policy_id, parent_id, node_type, left_attribute, operator, right_kind, right_value, sort_order)
SELECT
    pol.id, NULL, 'compare', 'subject.roles', 'contains', 'literal',
    to_jsonb(split_part(substring(pol.key FROM 11), '.', 1)), 0
FROM authz.policy pol
WHERE pol.key LIKE 'rbac.deny.%'
  AND NOT EXISTS (SELECT 1 FROM authz.policy_condition c WHERE c.policy_id = pol.id);

-- ── 6. Guards ─────────────────────────────────────────────────────────────────────────────────
-- A backfill that silently half-ran is worse than one that failed: the missing half is a permission
-- that now denies. Each of these turns that into a failed migration.
DO $$
DECLARE
    v_perms int; v_permits int; v_orphans int; v_denies int; v_deny_rows int;
BEGIN
    SELECT count(*) INTO v_perms   FROM identity_access.permissions WHERE status = 'active';
    SELECT count(*) INTO v_permits FROM authz.policy WHERE key LIKE 'rbac.permit.%';
    IF v_permits <> v_perms THEN
        RAISE EXCEPTION 'A6.3: expected % permit policies, found %', v_perms, v_permits;
    END IF;

    SELECT count(*) INTO v_orphans
      FROM authz.policy pol
     WHERE pol.key LIKE 'rbac.%'
       AND NOT EXISTS (SELECT 1 FROM authz.policy_condition c WHERE c.policy_id = pol.id);
    IF v_orphans > 0 THEN
        -- An unconditional permit policy is a policy that permits EVERYONE. This is the one that
        -- must never pass silently.
        RAISE EXCEPTION 'A6.3: % backfilled policies have no condition and would match every caller',
            v_orphans;
    END IF;

    SELECT count(DISTINCT (r.code, p.code)) INTO v_deny_rows
      FROM identity_access.role_permissions rp
      JOIN identity_access.roles       r ON r.id = rp.role_id
      JOIN identity_access.permissions p ON p.id = rp.permission_id
     WHERE rp.effect = 'deny' AND p.status = 'active';
    SELECT count(*) INTO v_denies FROM authz.policy WHERE key LIKE 'rbac.deny.%';
    IF v_denies <> v_deny_rows THEN
        RAISE EXCEPTION 'A6.3: expected % deny policies, found %', v_deny_rows, v_denies;
    END IF;

    RAISE NOTICE 'A6.3 backfill: % permit policies, % deny policies.', v_permits, v_denies;
END
$$;
