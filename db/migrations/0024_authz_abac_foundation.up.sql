-- 0024 — A1: the ABAC foundation. Schema `authz`.
--
-- Adds the attribute catalogue, resource registry, policy store and decision log described in
-- docs/ABAC_IMPLEMENTATION_PLAN.md §4. This migration is STRUCTURE ONLY — it creates no policy
-- rows, so nothing changes about who can do what. The engine ships in shadow mode (A6.1) and the
-- seed backfill from the existing 771 role_permissions rows is A6.3.
--
-- Why these tables and not a jsonb blob per policy: the condition tree has to be evaluated by BOTH
-- the application and PostgreSQL (A5.1 authz.permits), and a normalised tree is what lets the RLS
-- generator emit a predicate. A blob would force one of the two layers to reimplement a parser,
-- which is exactly the drift this plan exists to remove.

CREATE SCHEMA IF NOT EXISTS authz;
GRANT USAGE ON SCHEMA authz TO app_user, app_admin;

-- ── 1. Attribute catalogue (A1.1) ─────────────────────────────────────────────────────────────
-- The PIP contract. A policy may only reference a key that exists here, so a typo is a foreign-key
-- violation at authoring time rather than a silent false at evaluation time.
CREATE TABLE authz.attribute (
    key           varchar(120) PRIMARY KEY,
    category      varchar(20)  NOT NULL
                  CHECK (category IN ('subject', 'resource', 'action', 'environment')),
    data_type     varchar(20)  NOT NULL
                  CHECK (data_type IN ('uuid', 'text', 'numeric', 'boolean', 'timestamptz',
                                       'text[]', 'uuid[]')),
    -- Which IAttributeResolver supplies it. NULL = carried on the resource row itself and read
    -- through resource_type.attribute_map.
    resolver_key  varchar(80),
    description   text,
    is_active     boolean NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

-- ── 2. Resource type registry (A1.2) ──────────────────────────────────────────────────────────
-- Maps a logical resource to its physical table plus the attribute -> column mapping. This is the
-- table that makes generated RLS possible (A5.3): the generator reads attribute_map to know which
-- column backs `resource.brand_id` on each table.
CREATE TABLE authz.resource_type (
    key            varchar(120) PRIMARY KEY,
    schema_name    varchar(63)  NOT NULL,
    table_name     varchar(63)  NOT NULL,
    attribute_map  jsonb        NOT NULL DEFAULT '{}'::jsonb,
    description    text,
    is_active      boolean NOT NULL DEFAULT true,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT resource_type_attribute_map_is_object CHECK (jsonb_typeof(attribute_map) = 'object')
);

-- ── 3. Action vocabulary (A1.3) ───────────────────────────────────────────────────────────────
CREATE TABLE authz.action (
    key          varchar(60) PRIMARY KEY,
    description  text,
    created_at   timestamptz NOT NULL DEFAULT now()
);

-- ── 4. Policy header (A1.4) ───────────────────────────────────────────────────────────────────
-- brand_id NULL = platform-authored and applies to every tenant; non-null = that brand's own rule.
CREATE TABLE authz.policy (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    key              varchar(160) NOT NULL,
    version          int          NOT NULL DEFAULT 1,
    brand_id         uuid REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    description      text,
    effect           varchar(10)  NOT NULL CHECK (effect IN ('permit', 'deny')),
    resource_type    varchar(120) NOT NULL REFERENCES authz.resource_type(key) ON DELETE RESTRICT,
    action           varchar(60)  NOT NULL REFERENCES authz.action(key)        ON DELETE RESTRICT,
    -- The coarse RBAC gate this policy refines, if any. Keeping the permission code means
    -- "what can this person do?" stays enumerable for the admin console — see the plan's
    -- Recommendation section on why pure ABAC is not the target.
    permission_code  varchar(120),
    priority         int          NOT NULL DEFAULT 100,
    effective_from   timestamptz  NOT NULL DEFAULT now(),
    effective_to     timestamptz,
    is_active        boolean      NOT NULL DEFAULT true,
    created_at       timestamptz  NOT NULL DEFAULT now(),
    updated_at       timestamptz  NOT NULL DEFAULT now(),
    created_by       uuid,
    updated_by       uuid,
    CONSTRAINT policy_key_version_unique UNIQUE (key, version),
    CONSTRAINT policy_effective_range CHECK (effective_to IS NULL OR effective_to > effective_from)
);

CREATE INDEX ix_policy_lookup ON authz.policy (resource_type, action, is_active)
    WHERE is_active;
CREATE INDEX ix_policy_brand ON authz.policy (brand_id) WHERE brand_id IS NOT NULL;

-- ── 5. Condition tree (A1.4) ──────────────────────────────────────────────────────────────────
-- AND/OR/NOT internal nodes and `compare` leaves. A policy with no conditions is unconditional —
-- which is exactly how the 771 existing role_permissions rows project onto it (A6.3).
CREATE TABLE authz.policy_condition (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_id       uuid NOT NULL REFERENCES authz.policy(id) ON DELETE CASCADE,
    parent_id       uuid REFERENCES authz.policy_condition(id) ON DELETE CASCADE,
    node_type       varchar(12) NOT NULL CHECK (node_type IN ('and', 'or', 'not', 'compare')),
    left_attribute  varchar(120) REFERENCES authz.attribute(key) ON DELETE RESTRICT,
    operator        varchar(20)
                    CHECK (operator IN ('eq', 'neq', 'lt', 'lte', 'gt', 'gte', 'in', 'not_in',
                                        'contains', 'starts_with', 'is_null', 'is_not_null',
                                        'within_scope', 'older_than', 'newer_than')),
    right_kind      varchar(12) CHECK (right_kind IN ('literal', 'attribute', 'function')),
    right_value     jsonb,
    sort_order      int NOT NULL DEFAULT 0,
    -- A compare leaf needs an attribute and an operator; a boolean node must not carry them.
    CONSTRAINT policy_condition_shape CHECK (
        (node_type = 'compare' AND left_attribute IS NOT NULL AND operator IS NOT NULL)
     OR (node_type <> 'compare' AND left_attribute IS NULL AND operator IS NULL)
    )
);

CREATE INDEX ix_policy_condition_policy ON authz.policy_condition (policy_id);
CREATE INDEX ix_policy_condition_parent ON authz.policy_condition (parent_id)
    WHERE parent_id IS NOT NULL;

-- ── 6. Decision log (A1.5) ────────────────────────────────────────────────────────────────────
-- Partitioned monthly like identity_access.audit_logs, same (id, occurred_at) PK shape.
-- Unlike AuditSaveChangesInterceptor — which fires only on Added/Modified/Deleted and so records
-- no reads at all — this logs every DECISION, permit and deny, read and write.
CREATE TABLE authz.decision_log (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    occurred_at     timestamptz NOT NULL DEFAULT now(),
    brand_id        uuid,
    user_id         uuid,
    customer_id     uuid,
    token_use       varchar(20),
    resource_type   varchar(120),
    resource_id     uuid,
    action          varchar(60),
    decision        varchar(20) NOT NULL CHECK (decision IN ('permit', 'deny', 'not_applicable')),
    -- shadow: evaluated and logged but NOT enforced (A6.1). enforce: the decision was applied.
    mode            varchar(10) NOT NULL CHECK (mode IN ('shadow', 'enforce')),
    matched_policy  varchar(160),
    reason          text,
    attributes      jsonb,
    latency_us      int,
    PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX ix_decision_log_brand_time ON authz.decision_log (brand_id, occurred_at DESC);
CREATE INDEX ix_decision_log_denies ON authz.decision_log (occurred_at DESC)
    WHERE decision = 'deny';

SELECT partman.create_parent(
    p_parent_table => 'authz.decision_log',
    p_control      => 'occurred_at',
    p_interval     => '1 month',
    p_premake      => 6
);

-- ── 7. Row-level security ─────────────────────────────────────────────────────────────────────
-- The catalogue tables are global configuration: every tenant reads the same rows, so no RLS.
GRANT SELECT ON authz.attribute, authz.resource_type, authz.action TO app_user, app_admin;
GRANT INSERT, UPDATE, DELETE ON authz.attribute, authz.resource_type, authz.action TO app_admin;

-- Policies ARE tenant data. Note `brand_id IS NULL OR …`: platform-authored policies must stay
-- visible to every tenant. identity_access.roles omits exactly this clause, which is why a brand
-- admin cannot SELECT a single one of the 17 system roles (AUTHORITY_MODEL.md §10 #8). Repeating
-- that bug here would make every platform policy invisible to the tenants it governs.
ALTER TABLE authz.policy ENABLE ROW LEVEL SECURITY;
CREATE POLICY rls_brand_or_platform ON authz.policy FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id IS NULL OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

ALTER TABLE authz.policy_condition ENABLE ROW LEVEL SECURITY;
CREATE POLICY rls_via_policy ON authz.policy_condition FOR ALL TO app_user
    USING (kernel.rls_bypass() OR EXISTS (
        SELECT 1 FROM authz.policy p
        WHERE p.id = policy_condition.policy_id
          AND (p.brand_id IS NULL OR p.brand_id = kernel.current_brand_id())));

ALTER TABLE authz.decision_log ENABLE ROW LEVEL SECURITY;
CREATE POLICY rls_brand ON authz.decision_log FOR ALL TO app_user
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT ON authz.policy, authz.policy_condition TO app_user, app_admin;
GRANT INSERT, UPDATE, DELETE ON authz.policy, authz.policy_condition TO app_admin;

-- Append-only: no UPDATE, no DELETE, for anyone. The audit trail's own integrity is not a
-- permission any role should hold.
GRANT SELECT, INSERT ON authz.decision_log TO app_user, app_admin;

CREATE TRIGGER trg_set_updated_at BEFORE UPDATE ON authz.attribute
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();
CREATE TRIGGER trg_set_updated_at BEFORE UPDATE ON authz.resource_type
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();
CREATE TRIGGER trg_set_updated_at BEFORE UPDATE ON authz.policy
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ── 8. Seed the vocabulary ────────────────────────────────────────────────────────────────────
-- Attributes only — no policies. These are the keys the resolvers in A2 will supply.
INSERT INTO authz.attribute (key, category, data_type, resolver_key, description) VALUES
    ('subject.user_id',        'subject',     'uuid',        'subject', 'Staff user id (sub on a token_use=user JWT)'),
    ('subject.customer_id',    'subject',     'uuid',        'subject', 'Customer id (sub on a token_use=customer JWT)'),
    ('subject.user_type',      'subject',     'text',        'subject', 'users.user_type — NOT the role; the two have already drifted'),
    ('subject.token_use',      'subject',     'text',        'subject', 'user | customer | customer_mcp | partner | api_key'),
    ('subject.brand_id',       'subject',     'uuid',        'subject', 'Active brand from the JWT or the X-Brand-Id override'),
    ('subject.franchise_id',   'subject',     'uuid',        'subject', 'Active franchise'),
    ('subject.store_id',       'subject',     'uuid',        'subject', 'Active store'),
    ('subject.partner_id',     'subject',     'uuid',        'subject', 'RaaS logistics partner id'),
    ('subject.roles',          'subject',     'text[]',      'subject', 'Role codes held via live memberships'),
    ('subject.permissions',    'subject',     'text[]',      'subject', 'Effective permission codes from the JWT'),
    ('subject.scope_nodes',    'subject',     'text[]',      'subject', 'Ancestor-or-self membership nodes, e.g. brand:<uuid>'),
    ('subject.stepup_at',      'subject',     'timestamptz', 'subject', 'Last step-up re-verification; freshness window is 5 minutes'),
    ('subject.entitlements',   'subject',     'text[]',      'subject', 'Feature keys the brand has licensed'),

    ('resource.id',            'resource',    'uuid',        NULL, 'Primary key of the row being acted on'),
    ('resource.brand_id',      'resource',    'uuid',        NULL, 'Owning brand'),
    ('resource.franchise_id',  'resource',    'uuid',        NULL, 'Owning franchise'),
    ('resource.store_id',      'resource',    'uuid',        NULL, 'Owning store'),
    ('resource.warehouse_id',  'resource',    'uuid',        NULL, 'Owning warehouse'),
    ('resource.customer_id',   'resource',    'uuid',        NULL, 'Owning customer'),
    ('resource.owner_user_id', 'resource',    'uuid',        NULL, 'Assignee or creator, for ownership rules'),
    ('resource.status',        'resource',    'text',        NULL, 'Lifecycle status of the row'),
    ('resource.amount',        'resource',    'numeric',     NULL, 'Monetary amount, for threshold rules'),
    ('resource.created_at',    'resource',    'timestamptz', NULL, 'Row creation time, for time-window rules'),

    ('action.key',             'action',      'text',        NULL, 'The action being attempted'),

    ('env.now',                'environment', 'timestamptz', 'environment', 'Request timestamp'),
    ('env.ip',                 'environment', 'text',        'environment', 'Caller IP'),
    ('env.channel',            'environment', 'text',        'environment', 'admin | pos | customer_app | rider_app | api');

INSERT INTO authz.action (key, description) VALUES
    ('read',     'Read one row or list rows'),
    ('create',   'Create a row'),
    ('update',   'Modify a row'),
    ('delete',   'Delete a row'),
    ('approve',  'Approve a pending item'),
    ('assign',   'Assign work to someone'),
    ('cancel',   'Cancel an in-flight item'),
    ('refund',   'Issue a refund'),
    ('publish',  'Make a draft live'),
    ('export',   'Bulk-export data');
