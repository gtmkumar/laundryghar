-- =============================================================================
-- db/patches/subscriptions_module.sql
-- ADR-010: Recurring subscriptions — module A (customer) + module B (franchise SaaS)
-- Tables:  #93–98 in commerce schema + #99–102 in finance_royalty schema
--          + 2 materialized views (unique indexes for REFRESH CONCURRENTLY)
-- Permissions: subscription.manage / subscription.read (module A)
--              saas.manage / saas.read (module B)
--
-- Idempotent: safe to re-run.
-- Apply order: after 06_bc6_commerce.sql and 07_bc7_finance_royalty.sql
-- =============================================================================

-- ── 93. subscription_plans ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS commerce.subscription_plans (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                 UUID NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE RESTRICT,
    code                     VARCHAR(50) NOT NULL,
    name                     VARCHAR(100) NOT NULL,
    name_localized           JSONB NOT NULL DEFAULT '{}'::jsonb,
    description              TEXT,
    tier                     VARCHAR(30) NOT NULL DEFAULT 'standard'
                             CHECK (tier IN ('basic','standard','premium','custom')),
    billing_interval         VARCHAR(20) NOT NULL DEFAULT 'monthly'
                             CHECK (billing_interval IN ('weekly','monthly','quarterly','half_yearly','yearly')),
    interval_count           SMALLINT NOT NULL DEFAULT 1 CHECK (interval_count > 0),
    price                    NUMERIC(14,2) NOT NULL CHECK (price >= 0),
    setup_fee                NUMERIC(14,2) NOT NULL DEFAULT 0,
    currency_code            CHAR(3) NOT NULL DEFAULT 'INR',
    trial_days               SMALLINT NOT NULL DEFAULT 0,
    quota_type               VARCHAR(20) NOT NULL DEFAULT 'credit'
                             CHECK (quota_type IN ('credit','order_count','weight_kg','unlimited')),
    quota_value              NUMERIC(14,2),
    rollover_unused          BOOLEAN NOT NULL DEFAULT false,
    max_rollover             NUMERIC(14,2),
    overage_discount_percent NUMERIC(5,2) NOT NULL DEFAULT 0,
    applicable_services      UUID[] NOT NULL DEFAULT '{}',
    excluded_services        UUID[] NOT NULL DEFAULT '{}',
    pickup_included          BOOLEAN NOT NULL DEFAULT true,
    delivery_included        BOOLEAN NOT NULL DEFAULT true,
    express_included         BOOLEAN NOT NULL DEFAULT false,
    max_active_subscribers   INTEGER,
    current_subscriber_count INTEGER NOT NULL DEFAULT 0,
    gateway                  VARCHAR(30) CHECK (gateway IN ('razorpay','payu','cashfree','phonepe','none')),
    gateway_plan_id          VARCHAR(100),
    terms_and_conditions     TEXT,
    icon_url                 TEXT,
    color_hex                CHAR(7),
    display_order            SMALLINT NOT NULL DEFAULT 100,
    is_public                BOOLEAN NOT NULL DEFAULT true,
    is_featured              BOOLEAN NOT NULL DEFAULT false,
    status                   VARCHAR(20) NOT NULL DEFAULT 'draft'
                             CHECK (status IN ('draft','active','paused','retired')),
    available_from           TIMESTAMPTZ,
    available_to             TIMESTAMPTZ,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by               UUID,
    updated_by               UUID,
    version                  INTEGER NOT NULL DEFAULT 1,
    deleted_at               TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);

CREATE INDEX IF NOT EXISTS idx_subplan_brand_active   ON commerce.subscription_plans(brand_id, tier)
    WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_subplan_services_gin   ON commerce.subscription_plans USING GIN (applicable_services);

ALTER TABLE commerce.subscription_plans ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='commerce' AND tablename='subscription_plans' AND policyname='subplan_tenant'
  ) THEN
    CREATE POLICY subplan_tenant ON commerce.subscription_plans
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR brand_id = current_setting('app.current_brand_id', true)::uuid
    );
  END IF;
END$$;

-- ── 94. payment_mandates ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS commerce.payment_mandates (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id             UUID NOT NULL,
    customer_id          UUID NOT NULL REFERENCES customer_catalog.customers(id) ON DELETE CASCADE,
    mandate_type         VARCHAR(20) NOT NULL
                         CHECK (mandate_type IN ('upi_autopay','emandate','card','nach')),
    gateway              VARCHAR(30) NOT NULL DEFAULT 'razorpay',
    gateway_mandate_id   VARCHAR(100),
    gateway_token        VARCHAR(200),
    gateway_customer_id  VARCHAR(100),
    max_amount           NUMERIC(14,2) NOT NULL CHECK (max_amount > 0),
    debit_frequency      VARCHAR(20) NOT NULL DEFAULT 'as_presented'
                         CHECK (debit_frequency IN ('as_presented','weekly','monthly','quarterly','yearly')),
    upi_vpa              VARCHAR(100),
    card_last4           CHAR(4),
    card_network         VARCHAR(20),
    bank_name            VARCHAR(100),
    status               VARCHAR(20) NOT NULL DEFAULT 'created'
                         CHECK (status IN ('created','pending','active','paused','revoked','expired','failed')),
    start_at             TIMESTAMPTZ,
    end_at               TIMESTAMPTZ,
    authenticated_at     TIMESTAMPTZ,
    revoked_at           TIMESTAMPTZ,
    revoked_reason       TEXT,
    failure_code         VARCHAR(50),
    failure_message      TEXT,
    gateway_response     JSONB,
    metadata             JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_mandate_customer ON commerce.payment_mandates(customer_id) WHERE status = 'active';
CREATE INDEX IF NOT EXISTS idx_mandate_gateway  ON commerce.payment_mandates(gateway, gateway_mandate_id) WHERE gateway_mandate_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_mandate_status   ON commerce.payment_mandates(brand_id, status);

ALTER TABLE commerce.payment_mandates ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='commerce' AND tablename='payment_mandates' AND policyname='mandate_tenant'
  ) THEN
    CREATE POLICY mandate_tenant ON commerce.payment_mandates
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR brand_id = current_setting('app.current_brand_id', true)::uuid
    );
  END IF;
END$$;

-- ── 95. customer_subscriptions ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS commerce.customer_subscriptions (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                 UUID NOT NULL,
    customer_id              UUID NOT NULL REFERENCES customer_catalog.customers(id) ON DELETE RESTRICT,
    plan_id                  UUID NOT NULL REFERENCES commerce.subscription_plans(id) ON DELETE RESTRICT,
    mandate_id               UUID REFERENCES commerce.payment_mandates(id),
    subscription_number      VARCHAR(40) NOT NULL,
    price_snapshot           NUMERIC(14,2) NOT NULL,
    billing_interval         VARCHAR(20) NOT NULL,
    interval_count           SMALLINT NOT NULL DEFAULT 1,
    quota_type               VARCHAR(20) NOT NULL,
    quota_value              NUMERIC(14,2),
    currency_code            CHAR(3) NOT NULL DEFAULT 'INR',
    status                   VARCHAR(20) NOT NULL DEFAULT 'pending'
                             CHECK (status IN ('pending','trialing','active','past_due','paused','cancelled','expired')),
    auto_renew               BOOLEAN NOT NULL DEFAULT true,
    current_period_start     TIMESTAMPTZ,
    current_period_end       TIMESTAMPTZ,
    next_billing_at          TIMESTAMPTZ,
    trial_ends_at            TIMESTAMPTZ,
    credits_remaining        NUMERIC(14,2) NOT NULL DEFAULT 0,
    started_at               TIMESTAMPTZ,
    activated_at             TIMESTAMPTZ,
    cancel_at_period_end     BOOLEAN NOT NULL DEFAULT false,
    cancelled_at             TIMESTAMPTZ,
    cancel_reason            TEXT,
    paused_at                TIMESTAMPTZ,
    pause_resumes_at         TIMESTAMPTZ,
    ended_at                 TIMESTAMPTZ,
    past_due_since           TIMESTAMPTZ,
    dunning_attempts         SMALLINT NOT NULL DEFAULT 0,
    failed_payment_count     SMALLINT NOT NULL DEFAULT 0,
    total_cycles_billed      INTEGER NOT NULL DEFAULT 0,
    gateway_subscription_id  VARCHAR(100),
    metadata                 JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                  INTEGER NOT NULL DEFAULT 1,
    UNIQUE (brand_id, subscription_number)
);

CREATE INDEX IF NOT EXISTS idx_custsub_customer_active ON commerce.customer_subscriptions(customer_id)
    WHERE status IN ('trialing','active','past_due');
CREATE INDEX IF NOT EXISTS idx_custsub_plan            ON commerce.customer_subscriptions(plan_id);
CREATE INDEX IF NOT EXISTS idx_custsub_due             ON commerce.customer_subscriptions(next_billing_at)
    WHERE status IN ('active','trialing') AND auto_renew = true;
CREATE INDEX IF NOT EXISTS idx_custsub_pastdue         ON commerce.customer_subscriptions(brand_id, past_due_since)
    WHERE status = 'past_due';

ALTER TABLE commerce.customer_subscriptions ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='commerce' AND tablename='customer_subscriptions' AND policyname='custsub_tenant'
  ) THEN
    CREATE POLICY custsub_tenant ON commerce.customer_subscriptions
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR brand_id = current_setting('app.current_brand_id', true)::uuid
    );
  END IF;
END$$;

-- ── 96. subscription_invoices ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS commerce.subscription_invoices (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                 UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES commerce.customer_subscriptions(id) ON DELETE RESTRICT,
    customer_id              UUID NOT NULL,
    invoice_number           VARCHAR(40) NOT NULL UNIQUE,
    billing_period_start     TIMESTAMPTZ NOT NULL,
    billing_period_end       TIMESTAMPTZ NOT NULL,
    subtotal                 NUMERIC(14,2) NOT NULL DEFAULT 0,
    setup_fee                NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total           NUMERIC(14,2) NOT NULL DEFAULT 0,
    taxable_amount           NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                     NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                     NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                     NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total                NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total              NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid              NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due               NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code            CHAR(3) NOT NULL DEFAULT 'INR',
    payment_id               UUID REFERENCES commerce.payments(id),
    status                   VARCHAR(20) NOT NULL DEFAULT 'draft'
                             CHECK (status IN ('draft','issued','paid','past_due','failed','void','refunded')),
    attempt_count            SMALLINT NOT NULL DEFAULT 0,
    issued_at                TIMESTAMPTZ,
    due_at                   TIMESTAMPTZ,
    paid_at                  TIMESTAMPTZ,
    gateway_invoice_id       VARCHAR(100),
    invoice_s3_key           TEXT,
    metadata                 JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (customer_subscription_id, billing_period_start)
);

CREATE INDEX IF NOT EXISTS idx_subinv_subscription ON commerce.subscription_invoices(customer_subscription_id, billing_period_start DESC);
CREATE INDEX IF NOT EXISTS idx_subinv_customer     ON commerce.subscription_invoices(customer_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_subinv_status       ON commerce.subscription_invoices(brand_id, status, due_at);

ALTER TABLE commerce.subscription_invoices ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='commerce' AND tablename='subscription_invoices' AND policyname='subinv_tenant'
  ) THEN
    CREATE POLICY subinv_tenant ON commerce.subscription_invoices
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR brand_id = current_setting('app.current_brand_id', true)::uuid
    );
  END IF;
END$$;

-- ── 97. subscription_billing_attempts (append-only) ────────────────────────
CREATE TABLE IF NOT EXISTS commerce.subscription_billing_attempts (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                 UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES commerce.customer_subscriptions(id) ON DELETE CASCADE,
    subscription_invoice_id  UUID NOT NULL REFERENCES commerce.subscription_invoices(id) ON DELETE CASCADE,
    mandate_id               UUID REFERENCES commerce.payment_mandates(id),
    attempt_number           SMALLINT NOT NULL DEFAULT 1,
    amount                   NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    gateway                  VARCHAR(30),
    gateway_payment_id       VARCHAR(100),
    status                   VARCHAR(20) NOT NULL DEFAULT 'initiated'
                             CHECK (status IN ('initiated','success','failed','cancelled')),
    failure_code             VARCHAR(50),
    failure_message          TEXT,
    gateway_response         JSONB,
    attempted_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    next_retry_at            TIMESTAMPTZ,
    idempotency_key          VARCHAR(100) UNIQUE,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_subattempt_invoice      ON commerce.subscription_billing_attempts(subscription_invoice_id, attempt_number);
CREATE INDEX IF NOT EXISTS idx_subattempt_subscription ON commerce.subscription_billing_attempts(customer_subscription_id, attempted_at DESC);
CREATE INDEX IF NOT EXISTS idx_subattempt_retry        ON commerce.subscription_billing_attempts(next_retry_at) WHERE status = 'failed';

-- No RLS on billing_attempts — append-only, accessed by worker via bypass_rls.
-- No policy row needed; brand_id stored for audit queries by the worker.

-- ── 98. subscription_usage_ledger (append-only) ─────────────────────────────
CREATE TABLE IF NOT EXISTS commerce.subscription_usage_ledger (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                 UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES commerce.customer_subscriptions(id) ON DELETE RESTRICT,
    customer_id              UUID NOT NULL,
    billing_period_start     TIMESTAMPTZ NOT NULL,
    billing_period_end       TIMESTAMPTZ NOT NULL,
    transaction_type         VARCHAR(20) NOT NULL
                             CHECK (transaction_type IN ('allocate','consume','rollover','expire','adjustment','refund')),
    amount                   NUMERIC(14,2) NOT NULL,
    balance_before           NUMERIC(14,2) NOT NULL,
    balance_after            NUMERIC(14,2) NOT NULL,
    order_id                 UUID,
    order_created_at         TIMESTAMPTZ,
    reference_type           VARCHAR(30),
    reference_id             UUID,
    notes                    TEXT,
    performed_by_type        VARCHAR(20) DEFAULT 'system',
    performed_by_id          UUID,
    occurred_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_subusage_subscription ON commerce.subscription_usage_ledger(customer_subscription_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_subusage_period       ON commerce.subscription_usage_ledger(customer_subscription_id, billing_period_start);
CREATE INDEX IF NOT EXISTS idx_subusage_order        ON commerce.subscription_usage_ledger(order_id) WHERE order_id IS NOT NULL;

-- ── app_user grants — module A ───────────────────────────────────────────────
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.subscription_plans           TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.payment_mandates             TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.customer_subscriptions       TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.subscription_invoices        TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.subscription_billing_attempts TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.subscription_usage_ledger    TO app_user;

-- ── MV: customer subscription MRR (refresh hourly) ──────────────────────────
CREATE MATERIALIZED VIEW IF NOT EXISTS analytics.mv_subscription_mrr AS
SELECT
    cs.brand_id,
    sp.tier,
    DATE_TRUNC('month', now())::DATE                                     AS as_of_month,
    COUNT(*) FILTER (WHERE cs.status = 'active')                         AS active_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'trialing')                       AS trialing_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'past_due')                       AS past_due_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'cancelled')                      AS cancelled_subscriptions,
    SUM(
      CASE cs.billing_interval
        WHEN 'weekly'      THEN cs.price_snapshot * 52.0 / 12.0 / cs.interval_count
        WHEN 'monthly'     THEN cs.price_snapshot / cs.interval_count
        WHEN 'quarterly'   THEN cs.price_snapshot / (3 * cs.interval_count)
        WHEN 'half_yearly' THEN cs.price_snapshot / (6 * cs.interval_count)
        WHEN 'yearly'      THEN cs.price_snapshot / (12 * cs.interval_count)
        ELSE cs.price_snapshot
      END
    ) FILTER (WHERE cs.status IN ('active','past_due'))                  AS mrr
FROM commerce.customer_subscriptions cs
JOIN commerce.subscription_plans sp ON sp.id = cs.plan_id
GROUP BY cs.brand_id, sp.tier;

CREATE UNIQUE INDEX IF NOT EXISTS idx_mvsubmrr_unique ON analytics.mv_subscription_mrr(brand_id, tier, as_of_month);
GRANT SELECT ON analytics.mv_subscription_mrr TO app_user;

-- =============================================================================
-- MODULE B — Finance (finance_royalty schema)
-- =============================================================================

-- ── 99. platform_plans ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS finance_royalty.platform_plans (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    description             TEXT,
    tier                    VARCHAR(30) NOT NULL DEFAULT 'starter'
                            CHECK (tier IN ('starter','growth','pro','enterprise','custom')),
    billing_interval        VARCHAR(20) NOT NULL DEFAULT 'monthly'
                            CHECK (billing_interval IN ('monthly','quarterly','yearly')),
    interval_count          SMALLINT NOT NULL DEFAULT 1 CHECK (interval_count > 0),
    price                   NUMERIC(14,2) NOT NULL CHECK (price >= 0),
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    annual_discount_percent NUMERIC(5,2) NOT NULL DEFAULT 0,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    trial_days              SMALLINT NOT NULL DEFAULT 0,
    max_stores              INTEGER,
    max_warehouses          INTEGER,
    max_users               INTEGER,
    max_orders_per_month    INTEGER,
    max_riders              INTEGER,
    overage_per_order       NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_per_store       NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_per_user        NUMERIC(14,2) NOT NULL DEFAULT 0,
    features                JSONB NOT NULL DEFAULT '{}'::jsonb,
    support_level           VARCHAR(20) NOT NULL DEFAULT 'email'
                            CHECK (support_level IN ('community','email','priority','dedicated')),
    is_public               BOOLEAN NOT NULL DEFAULT true,
    is_featured             BOOLEAN NOT NULL DEFAULT false,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    gateway                 VARCHAR(30),
    gateway_plan_id         VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','active','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);

CREATE INDEX IF NOT EXISTS idx_platplan_active       ON finance_royalty.platform_plans(tier) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_platplan_brand        ON finance_royalty.platform_plans(brand_id) WHERE brand_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_platplan_features_gin ON finance_royalty.platform_plans USING GIN (features);

-- No RLS on platform_plans: global catalog, platform_admin managed.
-- app_user can read (platform_plans are not tenant-scoped).

-- ── 100. franchise_subscriptions ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS finance_royalty.franchise_subscriptions (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                 UUID NOT NULL,
    franchise_id             UUID NOT NULL REFERENCES tenancy_org.franchises(id) ON DELETE RESTRICT,
    platform_plan_id         UUID NOT NULL REFERENCES finance_royalty.platform_plans(id) ON DELETE RESTRICT,
    subscription_number      VARCHAR(40) NOT NULL,
    price_snapshot           NUMERIC(14,2) NOT NULL,
    billing_interval         VARCHAR(20) NOT NULL,
    interval_count           SMALLINT NOT NULL DEFAULT 1,
    currency_code            CHAR(3) NOT NULL DEFAULT 'INR',
    max_stores               INTEGER,
    max_warehouses           INTEGER,
    max_users                INTEGER,
    max_orders_per_month     INTEGER,
    max_riders               INTEGER,
    status                   VARCHAR(20) NOT NULL DEFAULT 'pending'
                             CHECK (status IN ('pending','trialing','active','past_due','suspended','cancelled','expired')),
    auto_renew               BOOLEAN NOT NULL DEFAULT true,
    payment_method           VARCHAR(20) NOT NULL DEFAULT 'invoice'
                             CHECK (payment_method IN ('invoice','auto_debit')),
    gateway_mandate_id       VARCHAR(100),
    gateway_subscription_id  VARCHAR(100),
    current_period_start     TIMESTAMPTZ,
    current_period_end       TIMESTAMPTZ,
    next_billing_at          TIMESTAMPTZ,
    trial_ends_at            TIMESTAMPTZ,
    current_period_orders    INTEGER NOT NULL DEFAULT 0,
    started_at               TIMESTAMPTZ,
    activated_at             TIMESTAMPTZ,
    cancel_at_period_end     BOOLEAN NOT NULL DEFAULT false,
    cancelled_at             TIMESTAMPTZ,
    cancel_reason            TEXT,
    past_due_since           TIMESTAMPTZ,
    dunning_attempts         SMALLINT NOT NULL DEFAULT 0,
    suspend_grace_until      TIMESTAMPTZ,
    suspended_at             TIMESTAMPTZ,
    suspended_reason         TEXT,
    reactivated_at           TIMESTAMPTZ,
    ended_at                 TIMESTAMPTZ,
    total_cycles_billed      INTEGER NOT NULL DEFAULT 0,
    metadata                 JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                  INTEGER NOT NULL DEFAULT 1,
    UNIQUE (brand_id, subscription_number)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_fransub_one_live ON finance_royalty.franchise_subscriptions(franchise_id)
    WHERE status IN ('pending','trialing','active','past_due','suspended');
CREATE INDEX IF NOT EXISTS idx_fransub_plan    ON finance_royalty.franchise_subscriptions(platform_plan_id);
CREATE INDEX IF NOT EXISTS idx_fransub_due     ON finance_royalty.franchise_subscriptions(next_billing_at)
    WHERE status IN ('active','trialing') AND auto_renew = true;
CREATE INDEX IF NOT EXISTS idx_fransub_suspend ON finance_royalty.franchise_subscriptions(brand_id, status)
    WHERE status IN ('past_due','suspended');

ALTER TABLE finance_royalty.franchise_subscriptions ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='finance_royalty' AND tablename='franchise_subscriptions' AND policyname='fransub_tenant'
  ) THEN
    CREATE POLICY fransub_tenant ON finance_royalty.franchise_subscriptions
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR brand_id = current_setting('app.current_brand_id', true)::uuid
    );
  END IF;
END$$;

-- ── 101. franchise_subscription_invoices ────────────────────────────────────
CREATE TABLE IF NOT EXISTS finance_royalty.franchise_subscription_invoices (
    id                         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                   UUID NOT NULL,
    franchise_id               UUID NOT NULL,
    franchise_subscription_id  UUID NOT NULL REFERENCES finance_royalty.franchise_subscriptions(id) ON DELETE RESTRICT,
    invoice_number             VARCHAR(40) NOT NULL UNIQUE,
    billing_period_start       TIMESTAMPTZ NOT NULL,
    billing_period_end         TIMESTAMPTZ NOT NULL,
    base_amount                NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_amount             NUMERIC(14,2) NOT NULL DEFAULT 0,
    setup_fee                  NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    subtotal                   NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                       NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                       NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                       NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total                  NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total                NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid                NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due                 NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code              CHAR(3) NOT NULL DEFAULT 'INR',
    usage_snapshot             JSONB NOT NULL DEFAULT '{}'::jsonb,
    payment_id                 UUID REFERENCES commerce.payments(id),
    status                     VARCHAR(20) NOT NULL DEFAULT 'draft'
                               CHECK (status IN ('draft','issued','sent','paid','past_due','failed','void')),
    attempt_count              SMALLINT NOT NULL DEFAULT 0,
    issued_at                  TIMESTAMPTZ,
    due_at                     TIMESTAMPTZ,
    paid_at                    TIMESTAMPTZ,
    invoice_s3_key             TEXT,
    invoice_pdf_url            TEXT,
    metadata                   JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by                 UUID,
    UNIQUE (franchise_subscription_id, billing_period_start)
);

CREATE INDEX IF NOT EXISTS idx_fransubinv_subscription ON finance_royalty.franchise_subscription_invoices(franchise_subscription_id, billing_period_start DESC);
CREATE INDEX IF NOT EXISTS idx_fransubinv_franchise    ON finance_royalty.franchise_subscription_invoices(franchise_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_fransubinv_status       ON finance_royalty.franchise_subscription_invoices(brand_id, status, due_at);
CREATE INDEX IF NOT EXISTS idx_fransubinv_overdue      ON finance_royalty.franchise_subscription_invoices(due_at)
    WHERE status IN ('issued','sent','past_due');

ALTER TABLE finance_royalty.franchise_subscription_invoices ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='finance_royalty' AND tablename='franchise_subscription_invoices' AND policyname='fransubinv_tenant'
  ) THEN
    CREATE POLICY fransubinv_tenant ON finance_royalty.franchise_subscription_invoices
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR brand_id = current_setting('app.current_brand_id', true)::uuid
    );
  END IF;
END$$;

-- ── 102. franchise_subscription_events (append-only) ────────────────────────
CREATE TABLE IF NOT EXISTS finance_royalty.franchise_subscription_events (
    id                         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                   UUID NOT NULL,
    franchise_subscription_id  UUID NOT NULL REFERENCES finance_royalty.franchise_subscriptions(id) ON DELETE CASCADE,
    franchise_id               UUID NOT NULL,
    event_type                 VARCHAR(30) NOT NULL
                               CHECK (event_type IN (
                                   'created','trial_started','activated','renewed','upgraded',
                                   'downgraded','payment_failed','past_due','suspended',
                                   'reactivated','cancelled','expired','plan_changed')),
    from_plan_id               UUID,
    to_plan_id                 UUID,
    from_status                VARCHAR(20),
    to_status                  VARCHAR(20),
    amount                     NUMERIC(14,2),
    reason                     VARCHAR(200),
    notes                      TEXT,
    actor_type                 VARCHAR(20) NOT NULL DEFAULT 'system'
                               CHECK (actor_type IN ('system','platform_admin','brand_admin','franchise_owner','job','webhook')),
    actor_id                   UUID,
    metadata                   JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at                TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_fransubevt_subscription ON finance_royalty.franchise_subscription_events(franchise_subscription_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_fransubevt_type         ON finance_royalty.franchise_subscription_events(brand_id, event_type, occurred_at DESC);

-- ── app_user grants — module B ───────────────────────────────────────────────
GRANT SELECT, INSERT, UPDATE, DELETE ON finance_royalty.platform_plans                   TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON finance_royalty.franchise_subscriptions          TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON finance_royalty.franchise_subscription_invoices  TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON finance_royalty.franchise_subscription_events    TO app_user;

-- ── MV: franchise SaaS MRR / ARR ────────────────────────────────────────────
CREATE MATERIALIZED VIEW IF NOT EXISTS analytics.mv_franchise_saas_mrr AS
SELECT
    fs.brand_id,
    pp.tier,
    DATE_TRUNC('month', now())::DATE                                      AS as_of_month,
    COUNT(*) FILTER (WHERE fs.status = 'active')                          AS active_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'trialing')                        AS trialing_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'past_due')                        AS past_due_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'suspended')                       AS suspended_subscriptions,
    SUM(
      CASE fs.billing_interval
        WHEN 'monthly'   THEN fs.price_snapshot / fs.interval_count
        WHEN 'quarterly' THEN fs.price_snapshot / (3 * fs.interval_count)
        WHEN 'yearly'    THEN fs.price_snapshot / (12 * fs.interval_count)
        ELSE fs.price_snapshot
      END
    ) FILTER (WHERE fs.status IN ('active','past_due'))                   AS mrr,
    SUM(
      CASE fs.billing_interval
        WHEN 'monthly'   THEN fs.price_snapshot * 12 / fs.interval_count
        WHEN 'quarterly' THEN fs.price_snapshot * 4  / fs.interval_count
        WHEN 'yearly'    THEN fs.price_snapshot / fs.interval_count
        ELSE fs.price_snapshot * 12
      END
    ) FILTER (WHERE fs.status IN ('active','past_due'))                   AS arr
FROM finance_royalty.franchise_subscriptions fs
JOIN finance_royalty.platform_plans pp ON pp.id = fs.platform_plan_id
GROUP BY fs.brand_id, pp.tier;

CREATE UNIQUE INDEX IF NOT EXISTS idx_mvfransaasmrr_unique ON analytics.mv_franchise_saas_mrr(brand_id, tier, as_of_month);
GRANT SELECT ON analytics.mv_franchise_saas_mrr TO app_user;

-- =============================================================================
-- Permissions seed — subscription.manage, subscription.read, saas.manage, saas.read
-- =============================================================================

-- subscription.manage — admin plan CRUD
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'subscription.manage', 'subscription', 'manage',
    'Manage subscription plans', 'Create, update, delete subscription plan definitions.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'subscription.manage');

-- subscription.read — subscription list/detail for admins
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'subscription.read', 'subscription', 'read',
    'Read subscription data', 'View customer subscriptions and invoices.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'subscription.read');

-- saas.manage — platform_plan CRUD + franchise subscription assign/cancel
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'saas.manage', 'saas', 'manage',
    'Manage SaaS plans', 'Create/update platform SaaS plans and manage franchise subscriptions.',
    true, true, 'high', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'saas.manage');

-- saas.read
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description, is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'saas.read', 'saas', 'read',
    'Read SaaS subscription data', 'View franchise SaaS subscription details and invoices.',
    true, true, 'normal', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'saas.read');

-- Grant subscription.manage to brand_admin, franchise_owner (not store_admin — billing is brand-level)
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'subscription.manage'
WHERE r.code IN ('platform_admin','brand_admin','franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- Grant subscription.read to brand_admin, franchise_owner
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'subscription.read'
WHERE r.code IN ('platform_admin','brand_admin','franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- Grant saas.manage and saas.read to platform_admin ONLY
INSERT INTO identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code IN ('saas.manage','saas.read')
WHERE r.code = 'platform_admin'
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

SELECT 'subscriptions_module.sql applied successfully.' AS result;
