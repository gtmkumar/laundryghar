-- 0025 — A5: the same policy rows, evaluated inside PostgreSQL.
--
-- docs/ABAC_IMPLEMENTATION_PLAN.md §4 turns on one claim: the application and the database must
-- reach the SAME decision from the SAME rows. Today they cannot, because the 136 RLS policies are
-- hand-written predicates that no application code reads and no application code can contradict —
-- so the two layers drift and nobody finds out until a row is visible in a list it should not be.
--
-- This migration adds the SQL half of the evaluator (A5.1), lifts the subject into session GUCs so
-- the per-row test is set membership rather than a graph walk (A5.2), generates RLS policy bodies
-- from authz.resource_type (A5.3), and exposes the comparison a CI test uses to fail the build when
-- someone hand-edits a generated policy (A5.4).
--
-- IT CHANGES NO EXISTING POLICY. Every function here is additive; authz.apply_generated_rls() is
-- the only thing that would rewrite a policy and it is never called by this migration. Cutting a
-- table over is a deliberate act, gated on A9.1's benchmark.

-- ── 1. Subject GUCs (A5.2) ────────────────────────────────────────────────────────────────────
-- RlsConnectionInterceptor already sets six of these per request. These five extend that block so
-- a policy predicate can read the subject's type, lane, memberships, roles and permissions without
-- joining identity_access on every row — which is the whole reason the hand-written policies never
-- attempted a franchise-level rule.
--
-- Each returns SQL NULL when the GUC was never set, and that distinction is load-bearing: NULL
-- means "unresolved", which the evaluator below propagates as indeterminate and the combining
-- algorithm turns into a denial. An empty string, by contrast, means "resolved, and empty" — a
-- principal with no memberships at all, which correctly matches nothing.

CREATE OR REPLACE FUNCTION kernel.current_user_type() RETURNS text
LANGUAGE sql STABLE PARALLEL SAFE AS
$$ SELECT NULLIF(current_setting('app.current_user_type', true), '') $$;

CREATE OR REPLACE FUNCTION kernel.current_token_use() RETURNS text
LANGUAGE sql STABLE PARALLEL SAFE AS
$$ SELECT NULLIF(current_setting('app.current_token_use', true), '') $$;

-- Splits a space-separated GUC into a text[], keeping THREE states apart:
--   never set, or '?' → NULL  (unresolved; comparisons go indeterminate, and the combining
--                              algorithm turns an indeterminate deny into a denial)
--   set to ''         → '{}'  (resolved and empty; matches nothing, which is a real answer)
--   set to "a b c"    → {a,b,c}
--
-- The '?' sentinel exists because connections are pooled: RlsConnectionInterceptor must write EVERY
-- variable on every open, or a previous request's memberships linger on the physical connection and
-- get evaluated as the next caller's. Since it cannot simply omit a null, it writes '?' — which is
-- not a legal scope node, role code or permission code, so it cannot collide with real data.
--
-- string_to_array('', ' ') would return {''} — a one-element array holding an empty string, which
-- would read as "this principal holds a membership named ''". Hence the explicit filter.
CREATE OR REPLACE FUNCTION kernel.split_setting(p_name text) RETURNS text[]
LANGUAGE sql STABLE PARALLEL SAFE AS
$$
    SELECT CASE
        WHEN current_setting(p_name, true) IS NULL THEN NULL
        WHEN current_setting(p_name, true) = '?'   THEN NULL
        ELSE coalesce(
            (SELECT array_agg(t ORDER BY ord)
             FROM regexp_split_to_table(current_setting(p_name, true), '\s+')
                  WITH ORDINALITY AS s(t, ord)
             WHERE t <> ''),
            '{}'::text[])
    END
$$;

-- Space-separated, matching the JWT scope_nodes claim byte for byte so there is one format to
-- reason about across the JWT, the C# resolver and this layer.
CREATE OR REPLACE FUNCTION kernel.current_scope_nodes() RETURNS text[]
LANGUAGE sql STABLE PARALLEL SAFE AS
$$ SELECT kernel.split_setting('app.current_scope_nodes') $$;

CREATE OR REPLACE FUNCTION kernel.current_roles() RETURNS text[]
LANGUAGE sql STABLE PARALLEL SAFE AS
$$ SELECT kernel.split_setting('app.current_roles') $$;

CREATE OR REPLACE FUNCTION kernel.current_permissions() RETURNS text[]
LANGUAGE sql STABLE PARALLEL SAFE AS
$$ SELECT kernel.split_setting('app.current_permissions') $$;

-- ── 2. Attribute resolution (A5.1) ────────────────────────────────────────────────────────────
-- The SQL twin of AbacAttributeProvider. Returns jsonb so one function can carry every data type,
-- and — critically — distinguishes SQL NULL (attribute unresolved) from 'null'::jsonb (attribute
-- resolved to null). Collapsing those two is exactly the fail-open bug the C# bag is built to
-- avoid, and it would be just as wrong here.
CREATE OR REPLACE FUNCTION authz.attr(p_key text, p_row jsonb, p_action text DEFAULT NULL)
RETURNS jsonb
LANGUAGE plpgsql STABLE PARALLEL SAFE AS
$$
DECLARE
    v_arr text[];
BEGIN
    -- resource.*: read straight off the candidate row. `->` yields SQL NULL for a missing key and
    -- 'null'::jsonb for a present-but-null column, which is the distinction we want, for free.
    IF p_key LIKE 'resource.%' THEN
        RETURN p_row -> substring(p_key FROM 10);
    END IF;

    IF p_key = 'action.key' THEN
        RETURN CASE WHEN p_action IS NULL THEN NULL ELSE to_jsonb(p_action) END;
    END IF;

    -- SCALAR subject attributes are always RESOLVED, to 'null'::jsonb when the GUC is unset.
    -- to_jsonb(NULL::uuid) already returns 'null'::jsonb, so this matches SubjectAttributeResolver,
    -- which likewise sets these keys unconditionally — a staff token genuinely HAS no customer_id,
    -- and that is a known null, not an unknown one.
    CASE p_key
        WHEN 'subject.user_id'       THEN RETURN to_jsonb(kernel.current_user_id());
        WHEN 'subject.customer_id'   THEN RETURN to_jsonb(kernel.current_customer_id());
        WHEN 'subject.brand_id'      THEN RETURN to_jsonb(kernel.current_brand_id());
        WHEN 'subject.franchise_id'  THEN RETURN to_jsonb(kernel.current_franchise_id());
        WHEN 'subject.store_id'      THEN RETURN to_jsonb(kernel.current_store_id());
        WHEN 'subject.partner_id'    THEN RETURN to_jsonb(kernel.current_partner_id());
        WHEN 'subject.user_type'     THEN RETURN to_jsonb(kernel.current_user_type());
        WHEN 'subject.token_use'     THEN RETURN to_jsonb(kernel.current_token_use());
        WHEN 'env.now'               THEN RETURN to_jsonb(now());
        ELSE NULL;  -- fall through to the array attributes below
    END CASE;

    -- ARRAY subject attributes are UNRESOLVED when their GUC was never set, mirroring the C#
    -- resolver, which omits scope_nodes on a token that carries no such claim and omits roles when
    -- the data source could not answer. to_jsonb(NULL::text[]) would return 'null'::jsonb here and
    -- quietly convert "I don't know your memberships" into "you have none" — a fail-open the whole
    -- three-valued design exists to prevent — so the NULL is checked explicitly.
    CASE p_key
        WHEN 'subject.scope_nodes' THEN v_arr := kernel.current_scope_nodes();
        WHEN 'subject.roles'       THEN v_arr := kernel.current_roles();
        WHEN 'subject.permissions' THEN v_arr := kernel.current_permissions();
        ELSE RETURN NULL;   -- unknown key = unresolved = indeterminate = deny
    END CASE;

    RETURN CASE WHEN v_arr IS NULL THEN NULL ELSE to_jsonb(v_arr) END;
END
$$;

-- ── 3. Comparison (A5.1) ──────────────────────────────────────────────────────────────────────
-- Three-valued: NULL out means indeterminate. SQL's own NULL propagation does most of the work,
-- but each branch still has to guard the "operand absent" case explicitly, because `'a' = NULL`
-- and `absent = 'a'` must both be indeterminate rather than false.
-- ValuesEqual() — numeric first, then temporal, then case-insensitive text. The numeric pass is
-- what makes 5 and 5.0 equal, and the text fallback is what makes a uuid literal from jsonb equal
-- a uuid column regardless of the case it was written in.
CREATE OR REPLACE FUNCTION authz.values_equal(p_a jsonb, p_b jsonb)
RETURNS boolean
LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE AS
$$
DECLARE
    a_txt text; b_txt text;
BEGIN
    IF p_a IS NULL AND p_b IS NULL THEN RETURN true;  END IF;
    IF p_a IS NULL OR  p_b IS NULL THEN RETURN false; END IF;

    IF jsonb_typeof(p_a) = 'null' OR jsonb_typeof(p_b) = 'null' THEN
        RETURN jsonb_typeof(p_a) = 'null' AND jsonb_typeof(p_b) = 'null';
    END IF;

    a_txt := p_a #>> '{}';
    b_txt := p_b #>> '{}';

    BEGIN RETURN a_txt::numeric = b_txt::numeric;     EXCEPTION WHEN others THEN NULL; END;
    BEGIN RETURN a_txt::timestamptz = b_txt::timestamptz; EXCEPTION WHEN others THEN NULL; END;

    RETURN lower(a_txt) = lower(b_txt);
END
$$;

-- Every branch below is a deliberate mirror of a named method in ConditionEvaluator.cs. Where the
-- two could plausibly differ the C# is authoritative and the divergence is called out, because a
-- rule that means one thing in the API and another in a WHERE clause is worse than no rule.
CREATE OR REPLACE FUNCTION authz.compare(
    p_op text, p_left jsonb, p_right jsonb, p_now timestamptz DEFAULT now())
RETURNS boolean
LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE AS
$$
DECLARE
    v_l_num numeric; v_r_num numeric;
    v_l_ts  timestamptz; v_r_ts timestamptz;
    v_secs  numeric;
    v_l_txt text; v_r_txt text;
BEGIN
    -- is_null / is_not_null — ConditionEvaluator lines 89-90. These read the RESOLVED value, so an
    -- absent attribute is still indeterminate: "did it resolve to null" and "did it resolve at all"
    -- are different questions and only the first one is being asked here.
    IF p_op IN ('is_null', 'is_not_null') THEN
        IF p_left IS NULL THEN RETURN NULL; END IF;
        RETURN CASE WHEN p_op = 'is_null'
                    THEN jsonb_typeof(p_left) = 'null'
                    ELSE jsonb_typeof(p_left) <> 'null' END;
    END IF;

    IF p_left IS NULL THEN RETURN NULL; END IF;   -- unresolved left → indeterminate

    -- Age() — indeterminate unless the left parses as a timestamp AND the right as a number.
    IF p_op IN ('older_than', 'newer_than') THEN
        IF p_right IS NULL OR jsonb_typeof(p_left) = 'null' THEN RETURN NULL; END IF;
        BEGIN
            v_l_ts := (p_left #>> '{}')::timestamptz;
            v_secs := (p_right #>> '{}')::numeric;
        EXCEPTION WHEN others THEN RETURN NULL;
        END;
        RETURN CASE WHEN p_op = 'older_than'
                    THEN v_l_ts <  p_now - make_interval(secs => v_secs)
                    ELSE v_l_ts >= p_now - make_interval(secs => v_secs) END;
    END IF;

    -- Membership() — the right side MUST be a list; a scalar right is indeterminate, not false.
    IF p_op IN ('in', 'not_in') THEN
        IF p_right IS NULL OR jsonb_typeof(p_right) <> 'array' THEN RETURN NULL; END IF;
        RETURN CASE WHEN p_op = 'in'
                    THEN     EXISTS (SELECT 1 FROM jsonb_array_elements(p_right) e
                                     WHERE authz.values_equal(p_left, e))
                    ELSE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(p_right) e
                                     WHERE authz.values_equal(p_left, e)) END;
    END IF;

    -- Contains() / StartsWith() — note these return FALSE on a null operand, not indeterminate.
    -- That asymmetry with eq is in the C# and is preserved rather than tidied away.
    IF p_op IN ('contains', 'starts_with') THEN
        IF p_right IS NULL OR jsonb_typeof(p_left) = 'null' OR jsonb_typeof(p_right) = 'null' THEN
            RETURN false;
        END IF;

        -- A list on the left means element membership (subject.roles contains 'auditor');
        -- text on the left means substring. Same fork as Contains().
        IF p_op = 'contains' AND jsonb_typeof(p_left) = 'array' THEN
            RETURN EXISTS (SELECT 1 FROM jsonb_array_elements(p_left) e
                           WHERE authz.values_equal(e, p_right));
        END IF;

        v_l_txt := lower(p_left  #>> '{}');   -- OrdinalIgnoreCase in the C#
        v_r_txt := lower(p_right #>> '{}');
        RETURN CASE WHEN p_op = 'contains'
                    THEN position(v_r_txt IN v_l_txt) > 0
                    ELSE left(v_l_txt, length(v_r_txt)) = v_r_txt END;
    END IF;

    IF p_right IS NULL THEN RETURN NULL; END IF;

    IF p_op IN ('eq', 'neq') THEN
        RETURN CASE WHEN p_op = 'eq'
                    THEN      authz.values_equal(p_left, p_right)
                    ELSE NOT  authz.values_equal(p_left, p_right) END;
    END IF;

    -- CompareValues() — ordered comparison is defined for numbers and timestamps ONLY. Ordering
    -- text or uuids is indeterminate on purpose: "is this uuid less than that one" has no meaning a
    -- policy author could have intended, and answering it would silently satisfy a threshold rule
    -- written against the wrong attribute.
    IF p_op IN ('lt', 'lte', 'gt', 'gte') THEN
        IF jsonb_typeof(p_left) = 'null' OR jsonb_typeof(p_right) = 'null' THEN RETURN NULL; END IF;

        BEGIN
            v_l_num := (p_left  #>> '{}')::numeric;
            v_r_num := (p_right #>> '{}')::numeric;
            RETURN CASE p_op
                WHEN 'lt'  THEN v_l_num <  v_r_num
                WHEN 'lte' THEN v_l_num <= v_r_num
                WHEN 'gt'  THEN v_l_num >  v_r_num
                ELSE            v_l_num >= v_r_num
            END;
        EXCEPTION WHEN others THEN
            BEGIN
                v_l_ts := (p_left  #>> '{}')::timestamptz;
                v_r_ts := (p_right #>> '{}')::timestamptz;
                RETURN CASE p_op
                    WHEN 'lt'  THEN v_l_ts <  v_r_ts
                    WHEN 'lte' THEN v_l_ts <= v_r_ts
                    WHEN 'gt'  THEN v_l_ts >  v_r_ts
                    ELSE            v_l_ts >= v_r_ts
                END;
            EXCEPTION WHEN others THEN
                RETURN NULL;   -- neither numeric nor temporal → fail closed
            END;
        END;
    END IF;

    RETURN NULL;  -- unknown operator fails closed
END
$$;

-- ── 4. within_scope (A5.1) ────────────────────────────────────────────────────────────────────
-- The ancestor-or-self boundary as a row test. This is the rule the hand-written RLS layer never
-- expressed — no existing policy references app.current_franchise_id — which is why sub-brand
-- escalation had to be caught by 95 hand-placed IsWithinScope calls in C# and was missed in
-- CreateFranchise (A0.7). Expressed once here, it holds for every table that registers the columns.
CREATE OR REPLACE FUNCTION authz.within_scope(p_row jsonb)
RETURNS boolean
LANGUAGE plpgsql STABLE PARALLEL SAFE AS
$$
DECLARE
    v_nodes text[];
    v_node  text;
    v_type  text;
    v_id    text;
    v_col   text;
BEGIN
    v_nodes := kernel.current_scope_nodes();
    IF v_nodes IS NULL THEN RETURN NULL; END IF;      -- unresolved → indeterminate → deny
    IF cardinality(v_nodes) = 0 THEN RETURN false; END IF;  -- resolved, no memberships → deny

    FOREACH v_node IN ARRAY v_nodes LOOP
        IF v_node = 'platform' THEN RETURN true; END IF;

        v_type := split_part(v_node, ':', 1);
        v_id   := split_part(v_node, ':', 2);
        IF v_id = '' THEN CONTINUE; END IF;

        v_col := CASE v_type
            WHEN 'brand'     THEN 'brand_id'
            WHEN 'franchise' THEN 'franchise_id'
            WHEN 'store'     THEN 'store_id'
            WHEN 'warehouse' THEN 'warehouse_id'
            ELSE NULL END;
        IF v_col IS NULL THEN CONTINUE; END IF;

        IF (p_row -> v_col) IS NOT NULL
           AND jsonb_typeof(p_row -> v_col) <> 'null'
           AND (p_row ->> v_col) = v_id
        THEN
            RETURN true;
        END IF;
    END LOOP;

    RETURN false;
END
$$;

-- ── 5. Condition tree evaluation (A5.1) ───────────────────────────────────────────────────────
-- Mirrors ConditionEvaluator exactly, including the two asymmetries that matter: an empty AND is
-- true, an empty OR is false. Recursion depth is the tree depth, which authoring keeps small.
CREATE OR REPLACE FUNCTION authz.eval_node(p_node_id uuid, p_row jsonb, p_action text DEFAULT NULL)
RETURNS boolean
LANGUAGE plpgsql STABLE PARALLEL SAFE AS
$$
DECLARE
    n            authz.policy_condition%ROWTYPE;
    child        authz.policy_condition%ROWTYPE;
    v_child_res  boolean;
    v_saw_null   boolean := false;
    v_any        boolean := false;
    v_left       jsonb;
    v_right      jsonb;
BEGIN
    SELECT * INTO n FROM authz.policy_condition WHERE id = p_node_id;
    IF NOT FOUND THEN RETURN NULL; END IF;

    IF n.node_type = 'and' THEN
        FOR child IN SELECT * FROM authz.policy_condition
                     WHERE parent_id = n.id ORDER BY sort_order, id LOOP
            v_any := true;
            v_child_res := authz.eval_node(child.id, p_row, p_action);
            IF v_child_res IS FALSE THEN RETURN false; END IF;   -- one false settles an AND
            IF v_child_res IS NULL  THEN v_saw_null := true; END IF;
        END LOOP;
        IF NOT v_any THEN RETURN true; END IF;                   -- empty AND is vacuously true
        RETURN CASE WHEN v_saw_null THEN NULL ELSE true END;

    ELSIF n.node_type = 'or' THEN
        FOR child IN SELECT * FROM authz.policy_condition
                     WHERE parent_id = n.id ORDER BY sort_order, id LOOP
            v_any := true;
            v_child_res := authz.eval_node(child.id, p_row, p_action);
            IF v_child_res IS TRUE THEN RETURN true; END IF;     -- one true settles an OR
            IF v_child_res IS NULL THEN v_saw_null := true; END IF;
        END LOOP;
        IF NOT v_any THEN RETURN false; END IF;                  -- empty OR is vacuously false
        RETURN CASE WHEN v_saw_null THEN NULL ELSE false END;

    ELSIF n.node_type = 'not' THEN
        SELECT * INTO child FROM authz.policy_condition
        WHERE parent_id = n.id ORDER BY sort_order, id;
        -- A NOT with zero or two-plus children is malformed; indeterminate, not "true".
        IF NOT FOUND THEN RETURN NULL; END IF;
        IF (SELECT count(*) FROM authz.policy_condition WHERE parent_id = n.id) <> 1 THEN
            RETURN NULL;
        END IF;
        RETURN NOT authz.eval_node(child.id, p_row, p_action);   -- NOT NULL = NULL, as required

    ELSIF n.node_type = 'compare' THEN
        IF n.operator = 'within_scope' THEN
            RETURN authz.within_scope(p_row);
        END IF;

        v_left := authz.attr(n.left_attribute, p_row, p_action);

        IF n.right_kind = 'attribute' THEN
            v_right := authz.attr(n.right_value #>> '{}', p_row, p_action);
        ELSE
            v_right := n.right_value;
        END IF;

        RETURN authz.compare(n.operator, v_left, v_right);
    END IF;

    RETURN NULL;  -- unknown node type fails closed
END
$$;

-- ── 6. authz.permits (A5.1) ───────────────────────────────────────────────────────────────────
-- The combining algorithm, identical to PolicyDecisionPoint.Decide: deny-overrides (and an
-- UNEVALUABLE deny still denies), then the highest-priority permit that actually holds, then
-- default-deny.
--
-- SECURITY DEFINER because the policy tables are themselves RLS-protected: a caller evaluating a
-- policy must not need permission to read the policy. STABLE PARALLEL SAFE so the planner can push
-- it into a parallel sequential scan rather than serialising every filtered query.
--
-- search_path is pinned: a SECURITY DEFINER function that resolves unqualified names through the
-- caller's search_path is the classic privilege-escalation shape, and this one runs on every row.
CREATE OR REPLACE FUNCTION authz.permits(p_resource_type text, p_action text, p_row jsonb)
RETURNS boolean
LANGUAGE plpgsql STABLE SECURITY DEFINER PARALLEL SAFE
SET search_path = authz, kernel, pg_catalog
AS
$$
DECLARE
    pol            authz.policy%ROWTYPE;
    v_root         uuid;
    v_res          boolean;
    v_brand        uuid;
    v_has_permit   boolean := false;
BEGIN
    v_brand := kernel.current_brand_id();

    -- Pass 1 — denies, including denies we cannot evaluate.
    FOR pol IN
        SELECT * FROM authz.policy
        WHERE is_active
          AND effect = 'deny'
          AND lower(resource_type) = lower(p_resource_type)
          AND lower(action)        = lower(p_action)
          AND effective_from <= now()
          AND (effective_to IS NULL OR effective_to > now())
          AND (brand_id IS NULL OR brand_id = v_brand)
        ORDER BY priority
    LOOP
        SELECT id INTO v_root FROM authz.policy_condition
        WHERE policy_id = pol.id AND parent_id IS NULL ORDER BY sort_order, id LIMIT 1;

        IF v_root IS NULL THEN
            RETURN false;   -- unconditional deny
        END IF;

        v_res := authz.eval_node(v_root, p_row, p_action);
        IF v_res IS TRUE OR v_res IS NULL THEN
            RETURN false;   -- an unevaluable deny fails closed, exactly as in the PDP
        END IF;
    END LOOP;

    -- Pass 2 — the first permit whose condition holds.
    FOR pol IN
        SELECT * FROM authz.policy
        WHERE is_active
          AND effect = 'permit'
          AND lower(resource_type) = lower(p_resource_type)
          AND lower(action)        = lower(p_action)
          AND effective_from <= now()
          AND (effective_to IS NULL OR effective_to > now())
          AND (brand_id IS NULL OR brand_id = v_brand)
        ORDER BY priority
    LOOP
        v_has_permit := true;

        SELECT id INTO v_root FROM authz.policy_condition
        WHERE policy_id = pol.id AND parent_id IS NULL ORDER BY sort_order, id LIMIT 1;

        IF v_root IS NULL THEN
            RETURN true;    -- unconditional permit
        END IF;

        IF authz.eval_node(v_root, p_row, p_action) IS TRUE THEN
            RETURN true;
        END IF;
    END LOOP;

    RETURN false;           -- default-deny, whether or not any permit was applicable
END
$$;

REVOKE ALL ON FUNCTION authz.permits(text, text, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION authz.permits(text, text, jsonb) TO app_user, app_admin;

-- ── 7. RLS generator (A5.3) ───────────────────────────────────────────────────────────────────
-- Emits the policy body for a registered resource type. The row is projected to jsonb using only
-- the columns in attribute_map, so authz.permits never sees a column a policy cannot name — which
-- keeps the jsonb small and stops the per-row cost scaling with table width.
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

    -- Only the resource.* half of the map projects onto the row; subject and environment come from
    -- the GUCs. quote_ident on the column and quote_literal on the key: the map is operator-managed
    -- data, but it still reaches SQL as text, so it is escaped rather than trusted.
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

-- The full CREATE POLICY statement, so a reviewer can read the diff before anything is applied.
CREATE OR REPLACE FUNCTION authz.generated_policy_sql(p_resource_type text)
RETURNS TABLE (policy_name text, statement text)
LANGUAGE plpgsql STABLE AS
$$
DECLARE
    rt authz.resource_type%ROWTYPE;
BEGIN
    SELECT * INTO rt FROM authz.resource_type WHERE key = p_resource_type AND is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'authz.resource_type % is not registered', p_resource_type;
    END IF;

    RETURN QUERY
    SELECT
        'rls_abac_' || replace(p_resource_type, '.', '_'),
        format(
            'CREATE POLICY %I ON %I.%I FOR ALL TO app_user USING (%s) WITH CHECK (%s)',
            'rls_abac_' || replace(p_resource_type, '.', '_'),
            rt.schema_name, rt.table_name,
            authz.generated_policy_body(p_resource_type, 'read'),
            authz.generated_policy_body(p_resource_type, 'update'));
END
$$;

-- Applying it is a separate, explicit act. Never called by this migration: A9.1's benchmark decides
-- which tables are worth moving, and the plan's own go/no-go says the largest partitioned tables
-- may well keep their hand-written policies.
CREATE OR REPLACE FUNCTION authz.apply_generated_rls(p_resource_type text)
RETURNS text
LANGUAGE plpgsql AS
$$
DECLARE
    rt    authz.resource_type%ROWTYPE;
    gen   record;
BEGIN
    SELECT * INTO rt FROM authz.resource_type WHERE key = p_resource_type AND is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'authz.resource_type % is not registered', p_resource_type;
    END IF;

    SELECT * INTO gen FROM authz.generated_policy_sql(p_resource_type);

    EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', rt.schema_name, rt.table_name);
    EXECUTE format('DROP POLICY IF EXISTS %I ON %I.%I',
                   gen.policy_name, rt.schema_name, rt.table_name);
    EXECUTE gen.statement;

    RETURN gen.statement;
END
$$;

REVOKE ALL ON FUNCTION authz.apply_generated_rls(text) FROM PUBLIC;

-- ── 8. Drift detection (A5.4) ─────────────────────────────────────────────────────────────────
-- The load-bearing task of A5. A generated policy that someone has since hand-edited is worse than
-- a hand-written one, because the generator's existence implies nobody needs to read the policy —
-- so the edit is invisible. This view is what a CI test asserts is empty.
CREATE OR REPLACE VIEW authz.rls_drift AS
SELECT
    rt.key                       AS resource_type,
    rt.schema_name,
    rt.table_name,
    gen.policy_name,
    live.qual                    AS live_using,
    gen.statement                AS expected_statement,
    CASE
        WHEN live.policyname IS NULL THEN 'missing'
        ELSE 'modified'
    END                          AS drift_kind
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
        -- Compare the normalised predicate: pg rewrites whitespace and adds casts, so an exact
        -- string match would report drift on every policy. Stripping whitespace and case is enough
        -- to catch a real edit without drowning the signal in formatting noise.
        OR regexp_replace(lower(coalesce(live.qual, '')), '\s+', '', 'g')
           NOT LIKE '%authz.permits%'
      );

GRANT SELECT ON authz.rls_drift TO app_admin;

COMMENT ON FUNCTION authz.permits(text, text, jsonb) IS
    'ABAC decision for one row, from authz.policy. The SQL twin of PolicyDecisionPoint.Decide — '
    'deny-overrides, then priority, then default-deny, with an unevaluable deny failing closed.';
COMMENT ON VIEW authz.rls_drift IS
    'A5.4 — non-empty means a generated RLS policy is missing or has been hand-edited. CI asserts empty.';
