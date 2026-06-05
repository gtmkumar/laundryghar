-- ============================================================================
-- LAUNDRY GHAR — Franchise SaaS Subscriptions (module B, Finance community)
-- File 09_subscriptions_franchise.sql  (load order 9 of 9)
-- ============================================================================
-- CANONICAL: SQL files are AUTHORITATIVE over any .md spec. On conflict, SQL wins.
-- LOAD ORDER: run after 01–08 (depends on brands, franchises, payments).
-- CONVENTIONS: UUID v7 PKs · NUMERIC(14,2) money · TIMESTAMPTZ · soft-delete
--              audit cols · lookup/CHECK enums · JSONB+GIN · RLS by brand
-- PURPOSE: A real SaaS plan engine for the platform→franchise relationship:
--          tiered plans, auto-renew, usage/overage, dunning, suspend-on-nonpay.
--          Replaces the flat `franchise_agreements.technology_fee_monthly` with
--          a proper subscription lifecycle. (Royalty stays separate — that's a
--          revenue share, this is the SaaS access fee.)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 99. platform_plans — SaaS tiers the platform offers to franchises (global catalog)
-- ----------------------------------------------------------------------------
CREATE TABLE platform_plans (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES brands(id) ON DELETE CASCADE,  -- NULL = platform-global; set = white-label brand's own tiers
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
    -- quotas (NULL = unlimited)
    max_stores              INTEGER,
    max_warehouses          INTEGER,
    max_users               INTEGER,
    max_orders_per_month    INTEGER,
    max_riders              INTEGER,
    -- overage rates (charged when a quota is exceeded)
    overage_per_order       NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_per_store       NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_per_user        NUMERIC(14,2) NOT NULL DEFAULT 0,
    -- what's included
    features                JSONB NOT NULL DEFAULT '{}'::jsonb,    -- feature_flag keys enabled by this plan
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
CREATE INDEX idx_platplan_active        ON platform_plans(tier) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_platplan_brand         ON platform_plans(brand_id) WHERE brand_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_platplan_features_gin  ON platform_plans USING GIN (features);

-- ----------------------------------------------------------------------------
-- 100. franchise_subscriptions — a franchise's SaaS subscription instance
-- ----------------------------------------------------------------------------
CREATE TABLE franchise_subscriptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL REFERENCES franchises(id) ON DELETE RESTRICT,
    platform_plan_id        UUID NOT NULL REFERENCES platform_plans(id) ON DELETE RESTRICT,
    subscription_number     VARCHAR(40) NOT NULL,
    -- snapshots
    price_snapshot          NUMERIC(14,2) NOT NULL,
    billing_interval        VARCHAR(20) NOT NULL,
    interval_count          SMALLINT NOT NULL DEFAULT 1,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    -- quota snapshots (so a mid-term plan change doesn't retroactively shift limits)
    max_stores              INTEGER,
    max_warehouses          INTEGER,
    max_users               INTEGER,
    max_orders_per_month    INTEGER,
    max_riders              INTEGER,
    -- lifecycle
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','trialing','active','past_due','suspended','cancelled','expired')),
    auto_renew              BOOLEAN NOT NULL DEFAULT true,
    payment_method          VARCHAR(20) NOT NULL DEFAULT 'invoice'
                            CHECK (payment_method IN ('invoice','auto_debit')),
    gateway_mandate_id      VARCHAR(100),
    gateway_subscription_id VARCHAR(100),
    current_period_start    TIMESTAMPTZ,
    current_period_end      TIMESTAMPTZ,
    next_billing_at         TIMESTAMPTZ,
    trial_ends_at           TIMESTAMPTZ,
    current_period_orders   INTEGER NOT NULL DEFAULT 0,   -- usage counter for overage
    started_at              TIMESTAMPTZ,
    activated_at            TIMESTAMPTZ,
    cancel_at_period_end    BOOLEAN NOT NULL DEFAULT false,
    cancelled_at            TIMESTAMPTZ,
    cancel_reason           TEXT,
    -- dunning / suspension
    past_due_since          TIMESTAMPTZ,
    dunning_attempts        SMALLINT NOT NULL DEFAULT 0,
    suspend_grace_until     TIMESTAMPTZ,
    suspended_at            TIMESTAMPTZ,
    suspended_reason        TEXT,
    reactivated_at          TIMESTAMPTZ,
    ended_at                TIMESTAMPTZ,
    total_cycles_billed     INTEGER NOT NULL DEFAULT 0,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                 INTEGER NOT NULL DEFAULT 1,
    UNIQUE (brand_id, subscription_number)
);
-- one live SaaS subscription per franchise
CREATE UNIQUE INDEX idx_fransub_one_live ON franchise_subscriptions(franchise_id)
    WHERE status IN ('pending','trialing','active','past_due','suspended');
CREATE INDEX idx_fransub_plan            ON franchise_subscriptions(platform_plan_id);
CREATE INDEX idx_fransub_due             ON franchise_subscriptions(next_billing_at)
    WHERE status IN ('active','trialing') AND auto_renew = true;
CREATE INDEX idx_fransub_suspend         ON franchise_subscriptions(brand_id, status)
    WHERE status IN ('past_due','suspended');

ALTER TABLE franchise_subscriptions ENABLE ROW LEVEL SECURITY;
CREATE POLICY fransub_tenant ON franchise_subscriptions
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 101. franchise_subscription_invoices — monthly SaaS invoice (base + overage)
-- ----------------------------------------------------------------------------
CREATE TABLE franchise_subscription_invoices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    franchise_subscription_id UUID NOT NULL REFERENCES franchise_subscriptions(id) ON DELETE RESTRICT,
    invoice_number          VARCHAR(40) NOT NULL UNIQUE,
    billing_period_start    TIMESTAMPTZ NOT NULL,
    billing_period_end      TIMESTAMPTZ NOT NULL,
    base_amount             NUMERIC(14,2) NOT NULL DEFAULT 0,
    overage_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total          NUMERIC(14,2) NOT NULL DEFAULT 0,
    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    usage_snapshot          JSONB NOT NULL DEFAULT '{}'::jsonb,  -- {orders, stores, users, warehouses, riders}
    payment_id              UUID REFERENCES payments(id),
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','issued','sent','paid','past_due','failed','void')),
    attempt_count           SMALLINT NOT NULL DEFAULT 0,
    issued_at               TIMESTAMPTZ,
    due_at                  TIMESTAMPTZ,
    paid_at                 TIMESTAMPTZ,
    invoice_s3_key          TEXT,
    invoice_pdf_url         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (franchise_subscription_id, billing_period_start)
);
CREATE INDEX idx_fransubinv_subscription ON franchise_subscription_invoices(franchise_subscription_id, billing_period_start DESC);
CREATE INDEX idx_fransubinv_franchise    ON franchise_subscription_invoices(franchise_id, created_at DESC);
CREATE INDEX idx_fransubinv_status       ON franchise_subscription_invoices(brand_id, status, due_at);
CREATE INDEX idx_fransubinv_overdue      ON franchise_subscription_invoices(due_at)
    WHERE status IN ('issued','sent','past_due');

ALTER TABLE franchise_subscription_invoices ENABLE ROW LEVEL SECURITY;
CREATE POLICY fransubinv_tenant ON franchise_subscription_invoices
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 102. franchise_subscription_events — lifecycle audit (append-only)
-- ----------------------------------------------------------------------------
CREATE TABLE franchise_subscription_events (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_subscription_id UUID NOT NULL REFERENCES franchise_subscriptions(id) ON DELETE CASCADE,
    franchise_id            UUID NOT NULL,
    event_type              VARCHAR(30) NOT NULL
                            CHECK (event_type IN ('created','trial_started','activated','renewed','upgraded',
                                                  'downgraded','payment_failed','past_due','suspended',
                                                  'reactivated','cancelled','expired','plan_changed')),
    from_plan_id            UUID,
    to_plan_id              UUID,
    from_status             VARCHAR(20),
    to_status               VARCHAR(20),
    amount                  NUMERIC(14,2),
    reason                  VARCHAR(200),
    notes                   TEXT,
    actor_type              VARCHAR(20) NOT NULL DEFAULT 'system'
                            CHECK (actor_type IN ('system','platform_admin','brand_admin','franchise_owner','job','webhook')),
    actor_id                UUID,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_fransubevt_subscription ON franchise_subscription_events(franchise_subscription_id, occurred_at DESC);
CREATE INDEX idx_fransubevt_type         ON franchise_subscription_events(brand_id, event_type, occurred_at DESC);

-- ----------------------------------------------------------------------------
-- MV: franchise SaaS MRR / ARR (platform revenue from franchises; refresh hourly)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_franchise_saas_mrr AS
SELECT
    fs.brand_id,
    pp.tier,
    DATE_TRUNC('month', now())::DATE                                    AS as_of_month,
    COUNT(*) FILTER (WHERE fs.status = 'active')                        AS active_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'trialing')                      AS trialing_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'past_due')                      AS past_due_subscriptions,
    COUNT(*) FILTER (WHERE fs.status = 'suspended')                     AS suspended_subscriptions,
    SUM(
      CASE fs.billing_interval
        WHEN 'monthly'   THEN fs.price_snapshot / fs.interval_count
        WHEN 'quarterly' THEN fs.price_snapshot / (3 * fs.interval_count)
        WHEN 'yearly'    THEN fs.price_snapshot / (12 * fs.interval_count)
        ELSE fs.price_snapshot
      END
    ) FILTER (WHERE fs.status IN ('active','past_due'))                 AS mrr,
    SUM(
      CASE fs.billing_interval
        WHEN 'monthly'   THEN fs.price_snapshot * 12 / fs.interval_count
        WHEN 'quarterly' THEN fs.price_snapshot * 4 / fs.interval_count
        WHEN 'yearly'    THEN fs.price_snapshot / fs.interval_count
        ELSE fs.price_snapshot * 12
      END
    ) FILTER (WHERE fs.status IN ('active','past_due'))                 AS arr
FROM franchise_subscriptions fs
JOIN platform_plans pp ON pp.id = fs.platform_plan_id
GROUP BY fs.brand_id, pp.tier;

CREATE UNIQUE INDEX idx_mvfransaasmrr_unique ON mv_franchise_saas_mrr(brand_id, tier, as_of_month);
