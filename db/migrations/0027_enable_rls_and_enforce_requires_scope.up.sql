-- 0027 — A0.9 and A0.10: the last two standing authority defects.
--
-- A0.9  Nine tables carry brand_id and have row-level security switched OFF. Four of them
--       (kernel.*) already HAVE a correct-looking rls_brand policy — it was written and never
--       enabled, so it has been inert since the day it was added. A policy that exists but does not
--       run is worse than no policy, because every review of pg_policies since has counted it.
--
-- A0.10 permissions.requires_scope is set on rows across the catalogue and read by nothing: a grep
--       of backend/, admin-web/ and pos-web/ finds the EF property, its column mapping, the seeder
--       and test fixtures, and no branch. This migration decides it: ENFORCE, by projecting it onto
--       policy rows rather than by adding a 172nd hand-written check.

-- ── A0.9 · Enable RLS on the nine ─────────────────────────────────────────────────────────────
-- Two shapes, and picking the wrong one is how identity_access.roles ended up invisible to brand
-- admins (AUTHORITY_MODEL.md §10 #8):
--
--   * PLATFORM-VISIBLE — the table holds rows owned by no tenant that every tenant must still read.
--     kernel.system_settings has 7 such rows and finance_royalty.platform_plans has 2. A plain
--     `brand_id = current_brand_id()` hides them from everyone, because NULL = <uuid> is NULL, not
--     true. USING therefore carries `brand_id IS NULL OR …`; WITH CHECK does NOT, so a tenant can
--     read a platform row but can never write one.
--
--   * STRICT — everything else. Rows always belong to exactly one brand.

-- Platform-visible.
ALTER TABLE kernel.system_settings ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON kernel.system_settings;
CREATE POLICY rls_brand_or_platform ON kernel.system_settings FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id IS NULL OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

ALTER TABLE finance_royalty.platform_plans ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON finance_royalty.platform_plans;
CREATE POLICY rls_brand_or_platform ON finance_royalty.platform_plans FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id IS NULL OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- feature_flags holds no rows today, but a platform-wide flag is the obvious future row and hiding
-- it from every tenant would be the same bug arriving later. Same shape, chosen deliberately.
ALTER TABLE kernel.feature_flags ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON kernel.feature_flags;
CREATE POLICY rls_brand_or_platform ON kernel.feature_flags FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id IS NULL OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

-- Strict. The existing kernel policies are already exactly this; they only needed switching on.
ALTER TABLE kernel.file_attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE kernel.outbox_events    ENABLE ROW LEVEL SECURITY;

-- The outbox is drained by background workers, which run with BypassRls = true
-- (WorkerCurrentTenant / CommerceHostCurrentTenant), so enabling RLS here does not blind the
-- drain — verified before switching it on, because an outbox that silently stops draining is an
-- outage that looks like nothing at all.

DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'commerce.subscription_billing_attempts',
        'commerce.subscription_usage_ledger',
        'finance_royalty.franchise_subscription_events',
        'identity_access.oauth_authorization_codes'
    ] LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS rls_brand ON %s', t);
        EXECUTE format(
            'CREATE POLICY rls_brand ON %s FOR ALL TO app_user '
            'USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id()) '
            'WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())', t);
    END LOOP;
END
$$;

-- Guard: every brand_id-bearing base table now has RLS on, or this migration failed.
DO $$
DECLARE v_missing text;
BEGIN
    SELECT string_agg(n.nspname || '.' || c.relname, ', ')
      INTO v_missing
      FROM pg_class c
      JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE c.relkind IN ('r', 'p')
       AND NOT c.relispartition
       AND n.nspname NOT IN ('partman', 'pg_catalog', 'information_schema')
       AND NOT c.relrowsecurity
       AND EXISTS (SELECT 1 FROM information_schema.columns col
                    WHERE col.table_schema = n.nspname
                      AND col.table_name   = c.relname
                      AND col.column_name  = 'brand_id');

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'A0.9: brand_id tables still without RLS: %', v_missing;
    END IF;
END
$$;

-- ── A0.10 · requires_scope, decided ───────────────────────────────────────────────────────────
-- The decision is ENFORCE, not drop.
--
-- Dropping it would discard a per-permission statement of intent that somebody made deliberately
-- for every row in the catalogue, and would leave the franchise/store boundary resting entirely on
-- 95 hand-placed IsWithinScope calls — a set that is provably incomplete, since CreateFranchise was
-- missing one (A0.7) and nothing detected that for as long as it was missing.
--
-- Enforcing it as a 172nd hardcoded check would repeat the mistake. So it is projected onto policy
-- rows: for every permission that requires scope, on a resource type that actually has a table to
-- test against, a DENY policy that fires when the row is outside the caller's scope.
--
-- These are created ACTIVE, and that is safe because NOTHING enforces yet: the engine ships in
-- shadow mode (Abac:Mode = shadow) and Abac:Enabled defaults to false. Every disagreement between
-- this rule and today's behaviour shows up in authz.decision_log for review (A6.2) before any
-- module is cut over. That is the whole reason shadow mode exists — a rule this broad must be
-- measured against real traffic, not reasoned about.
INSERT INTO authz.policy
    (key, brand_id, description, effect, resource_type, action, permission_code, priority)
SELECT
    'scope.deny.' || p.code,
    NULL,
    'A0.10 — permissions.requires_scope on ' || p.code || ', enforced as a policy rather than as a '
        || 'hand-placed IsWithinScope call.',
    'deny',
    p.module,
    p.action,
    p.code,
    50                        -- ahead of the A6.3 role denies; ordering among denies is cosmetic
FROM identity_access.permissions p
JOIN authz.resource_type rt ON rt.key = p.module
WHERE p.status = 'active'
  AND p.requires_scope
  AND rt.schema_name IS NOT NULL          -- only where there is a row to test
  AND rt.attribute_map ? 'resource.brand_id'
ON CONFLICT (key, version) DO NOTHING;

INSERT INTO authz.policy_condition (policy_id, parent_id, node_type, sort_order)
SELECT pol.id, NULL, 'not', 0
FROM authz.policy pol
WHERE pol.key LIKE 'scope.deny.%'
  AND NOT EXISTS (SELECT 1 FROM authz.policy_condition c WHERE c.policy_id = pol.id);

INSERT INTO authz.policy_condition
    (policy_id, parent_id, node_type, left_attribute, operator, right_kind, right_value, sort_order)
SELECT
    root.policy_id, root.id, 'compare', 'subject.scope_nodes', 'within_scope', NULL, NULL, 0
FROM authz.policy_condition root
JOIN authz.policy pol ON pol.id = root.policy_id
WHERE pol.key LIKE 'scope.deny.%'
  AND root.parent_id IS NULL
  AND root.node_type = 'not'
  AND NOT EXISTS (SELECT 1 FROM authz.policy_condition c WHERE c.parent_id = root.id);

COMMENT ON COLUMN identity_access.permissions.requires_scope IS
    'A0.10 — ENFORCED, via the scope.deny.* policies in authz.policy (migration 0027), not by any '
    'branch in application code. Setting this true on a new permission automatically produces a '
    'within_scope deny for it; do not add a hand-written IsWithinScope call as well.';

DO $$
DECLARE v_expected int; v_actual int; v_incomplete int;
BEGIN
    SELECT count(*) INTO v_expected
      FROM identity_access.permissions p
      JOIN authz.resource_type rt ON rt.key = p.module
     WHERE p.status = 'active' AND p.requires_scope
       AND rt.schema_name IS NOT NULL AND rt.attribute_map ? 'resource.brand_id';

    SELECT count(*) INTO v_actual FROM authz.policy WHERE key LIKE 'scope.deny.%';
    IF v_actual <> v_expected THEN
        RAISE EXCEPTION 'A0.10: expected % scope deny policies, found %', v_expected, v_actual;
    END IF;

    -- A `not` node with no child evaluates indeterminate, and an indeterminate DENY denies
    -- everything. Half-built, this rule is an outage.
    SELECT count(*) INTO v_incomplete
      FROM authz.policy pol
      JOIN authz.policy_condition root ON root.policy_id = pol.id AND root.parent_id IS NULL
     WHERE pol.key LIKE 'scope.deny.%'
       AND NOT EXISTS (SELECT 1 FROM authz.policy_condition c WHERE c.parent_id = root.id);
    IF v_incomplete > 0 THEN
        RAISE EXCEPTION 'A0.10: % scope deny policies have an empty NOT node', v_incomplete;
    END IF;

    RAISE NOTICE 'A0.10: % requires_scope deny policies created.', v_actual;
END
$$;
