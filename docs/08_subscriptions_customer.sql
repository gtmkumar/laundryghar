-- ============================================================================
-- LAUNDRY GHAR — Customer Subscriptions (module A, Commerce community)
-- File 08_subscriptions_customer.sql  (load order 8 of 9)
-- ============================================================================
-- CANONICAL: SQL files are AUTHORITATIVE over any .md spec. On conflict, SQL wins.
-- LOAD ORDER: run after 01–07 (depends on brands, customers, services, payments).
-- CONVENTIONS: UUID v7 PKs · NUMERIC(14,2) money · TIMESTAMPTZ · soft-delete
--              audit cols · lookup/CHECK enums · JSONB+GIN · RLS by brand
-- PURPOSE: Recurring, auto-renewing customer subscriptions (e.g., "₹999/mo,
--          15kg wash, auto-renew via UPI AutoPay"). Distinct from prepaid
--          `packages` (one-shot credit packs). Quota resets each billing cycle.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 93. subscription_plans — recurring plan catalog (brand-scoped)
-- ----------------------------------------------------------------------------
CREATE TABLE subscription_plans (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    tier                    VARCHAR(30) NOT NULL DEFAULT 'standard'
                            CHECK (tier IN ('basic','standard','premium','custom')),
    billing_interval        VARCHAR(20) NOT NULL DEFAULT 'monthly'
                            CHECK (billing_interval IN ('weekly','monthly','quarterly','half_yearly','yearly')),
    interval_count          SMALLINT NOT NULL DEFAULT 1 CHECK (interval_count > 0),
    price                   NUMERIC(14,2) NOT NULL CHECK (price >= 0),
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    trial_days              SMALLINT NOT NULL DEFAULT 0,
    -- quota model: what the plan includes per billing cycle
    quota_type              VARCHAR(20) NOT NULL DEFAULT 'credit'
                            CHECK (quota_type IN ('credit','order_count','weight_kg','unlimited')),
    quota_value             NUMERIC(14,2),                  -- credits / orders / kg per cycle; NULL if unlimited
    rollover_unused         BOOLEAN NOT NULL DEFAULT false, -- unused quota carries to next cycle?
    max_rollover            NUMERIC(14,2),
    overage_discount_percent NUMERIC(5,2) NOT NULL DEFAULT 0, -- discount on pay-as-you-go beyond quota
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    excluded_services       UUID[] NOT NULL DEFAULT '{}',
    pickup_included         BOOLEAN NOT NULL DEFAULT true,
    delivery_included       BOOLEAN NOT NULL DEFAULT true,
    express_included        BOOLEAN NOT NULL DEFAULT false,
    max_active_subscribers  INTEGER,                        -- cap on concurrent subscribers; NULL = unlimited
    current_subscriber_count INTEGER NOT NULL DEFAULT 0,
    gateway                 VARCHAR(30) CHECK (gateway IN ('razorpay','payu','cashfree','phonepe','none')),
    gateway_plan_id         VARCHAR(100),                   -- Razorpay plan_id
    terms_and_conditions    TEXT,
    icon_url                TEXT,
    color_hex               CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_public               BOOLEAN NOT NULL DEFAULT true,
    is_featured             BOOLEAN NOT NULL DEFAULT false,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','active','paused','retired')),
    available_from          TIMESTAMPTZ,
    available_to            TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    UNIQUE (brand_id, code)
);
CREATE INDEX idx_subplan_brand_active   ON subscription_plans(brand_id, tier)
    WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_subplan_services_gin   ON subscription_plans USING GIN (applicable_services);

ALTER TABLE subscription_plans ENABLE ROW LEVEL SECURITY;
CREATE POLICY subplan_tenant ON subscription_plans
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 94. payment_mandates — customer recurring-payment authorization (UPI AutoPay / e-mandate / NACH)
-- ----------------------------------------------------------------------------
CREATE TABLE payment_mandates (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    mandate_type            VARCHAR(20) NOT NULL
                            CHECK (mandate_type IN ('upi_autopay','emandate','card','nach')),
    gateway                 VARCHAR(30) NOT NULL DEFAULT 'razorpay',
    gateway_mandate_id      VARCHAR(100),
    gateway_token           VARCHAR(200),
    gateway_customer_id     VARCHAR(100),
    max_amount              NUMERIC(14,2) NOT NULL CHECK (max_amount > 0),   -- per-debit cap authorized
    debit_frequency         VARCHAR(20) NOT NULL DEFAULT 'as_presented'
                            CHECK (debit_frequency IN ('as_presented','weekly','monthly','quarterly','yearly')),
    upi_vpa                 VARCHAR(100),
    card_last4              CHAR(4),
    card_network            VARCHAR(20),
    bank_name               VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'created'
                            CHECK (status IN ('created','pending','active','paused','revoked','expired','failed')),
    start_at                TIMESTAMPTZ,
    end_at                  TIMESTAMPTZ,
    authenticated_at        TIMESTAMPTZ,
    revoked_at              TIMESTAMPTZ,
    revoked_reason          TEXT,
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    gateway_response        JSONB,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_mandate_customer       ON payment_mandates(customer_id) WHERE status = 'active';
CREATE INDEX idx_mandate_gateway        ON payment_mandates(gateway, gateway_mandate_id) WHERE gateway_mandate_id IS NOT NULL;
CREATE INDEX idx_mandate_status         ON payment_mandates(brand_id, status);

ALTER TABLE payment_mandates ENABLE ROW LEVEL SECURITY;
CREATE POLICY mandate_tenant ON payment_mandates
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 95. customer_subscriptions — an active recurring subscription instance
-- ----------------------------------------------------------------------------
CREATE TABLE customer_subscriptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    plan_id                 UUID NOT NULL REFERENCES subscription_plans(id) ON DELETE RESTRICT,
    mandate_id              UUID REFERENCES payment_mandates(id),
    subscription_number     VARCHAR(40) NOT NULL,
    -- snapshots (plan can change later; subscription keeps what it was sold)
    price_snapshot          NUMERIC(14,2) NOT NULL,
    billing_interval        VARCHAR(20) NOT NULL,
    interval_count          SMALLINT NOT NULL DEFAULT 1,
    quota_type              VARCHAR(20) NOT NULL,
    quota_value             NUMERIC(14,2),
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    -- lifecycle
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','trialing','active','past_due','paused','cancelled','expired')),
    auto_renew              BOOLEAN NOT NULL DEFAULT true,
    current_period_start    TIMESTAMPTZ,
    current_period_end      TIMESTAMPTZ,
    next_billing_at         TIMESTAMPTZ,
    trial_ends_at           TIMESTAMPTZ,
    credits_remaining       NUMERIC(14,2) NOT NULL DEFAULT 0,   -- quota left in current cycle
    started_at              TIMESTAMPTZ,
    activated_at            TIMESTAMPTZ,
    cancel_at_period_end    BOOLEAN NOT NULL DEFAULT false,
    cancelled_at            TIMESTAMPTZ,
    cancel_reason           TEXT,
    paused_at               TIMESTAMPTZ,
    pause_resumes_at        TIMESTAMPTZ,
    ended_at                TIMESTAMPTZ,
    -- dunning
    past_due_since          TIMESTAMPTZ,
    dunning_attempts        SMALLINT NOT NULL DEFAULT 0,
    failed_payment_count    SMALLINT NOT NULL DEFAULT 0,
    total_cycles_billed     INTEGER NOT NULL DEFAULT 0,
    gateway_subscription_id VARCHAR(100),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    version                 INTEGER NOT NULL DEFAULT 1,
    UNIQUE (brand_id, subscription_number)
);
CREATE INDEX idx_custsub_customer_active ON customer_subscriptions(customer_id)
    WHERE status IN ('trialing','active','past_due');
CREATE INDEX idx_custsub_plan            ON customer_subscriptions(plan_id);
CREATE INDEX idx_custsub_due             ON customer_subscriptions(next_billing_at)
    WHERE status IN ('active','trialing') AND auto_renew = true;
CREATE INDEX idx_custsub_pastdue         ON customer_subscriptions(brand_id, past_due_since)
    WHERE status = 'past_due';

ALTER TABLE customer_subscriptions ENABLE ROW LEVEL SECURITY;
CREATE POLICY custsub_tenant ON customer_subscriptions
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 96. subscription_invoices — one invoice per billing cycle
-- ----------------------------------------------------------------------------
CREATE TABLE subscription_invoices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES customer_subscriptions(id) ON DELETE RESTRICT,
    customer_id             UUID NOT NULL,
    invoice_number          VARCHAR(40) NOT NULL UNIQUE,
    billing_period_start    TIMESTAMPTZ NOT NULL,
    billing_period_end      TIMESTAMPTZ NOT NULL,
    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    setup_fee               NUMERIC(14,2) NOT NULL DEFAULT 0,
    discount_total          NUMERIC(14,2) NOT NULL DEFAULT 0,
    taxable_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    payment_id              UUID REFERENCES payments(id),
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','issued','paid','past_due','failed','void','refunded')),
    attempt_count           SMALLINT NOT NULL DEFAULT 0,
    issued_at               TIMESTAMPTZ,
    due_at                  TIMESTAMPTZ,
    paid_at                 TIMESTAMPTZ,
    gateway_invoice_id      VARCHAR(100),
    invoice_s3_key          TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (customer_subscription_id, billing_period_start)
);
CREATE INDEX idx_subinv_subscription    ON subscription_invoices(customer_subscription_id, billing_period_start DESC);
CREATE INDEX idx_subinv_customer        ON subscription_invoices(customer_id, created_at DESC);
CREATE INDEX idx_subinv_status          ON subscription_invoices(brand_id, status, due_at);

ALTER TABLE subscription_invoices ENABLE ROW LEVEL SECURITY;
CREATE POLICY subinv_tenant ON subscription_invoices
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 97. subscription_billing_attempts — each charge attempt against the mandate (dunning, append-only)
-- ----------------------------------------------------------------------------
CREATE TABLE subscription_billing_attempts (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES customer_subscriptions(id) ON DELETE CASCADE,
    subscription_invoice_id UUID NOT NULL REFERENCES subscription_invoices(id) ON DELETE CASCADE,
    mandate_id              UUID REFERENCES payment_mandates(id),
    attempt_number          SMALLINT NOT NULL DEFAULT 1,
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    gateway                 VARCHAR(30),
    gateway_payment_id      VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'initiated'
                            CHECK (status IN ('initiated','success','failed','cancelled')),
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    gateway_response        JSONB,
    attempted_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    next_retry_at           TIMESTAMPTZ,
    idempotency_key         VARCHAR(100) UNIQUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_subattempt_invoice     ON subscription_billing_attempts(subscription_invoice_id, attempt_number);
CREATE INDEX idx_subattempt_subscription ON subscription_billing_attempts(customer_subscription_id, attempted_at DESC);
CREATE INDEX idx_subattempt_retry       ON subscription_billing_attempts(next_retry_at) WHERE status = 'failed';

-- ----------------------------------------------------------------------------
-- 98. subscription_usage_ledger — per-cycle quota allocation & consumption (append-only)
-- ----------------------------------------------------------------------------
CREATE TABLE subscription_usage_ledger (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_subscription_id UUID NOT NULL REFERENCES customer_subscriptions(id) ON DELETE RESTRICT,
    customer_id             UUID NOT NULL,
    billing_period_start    TIMESTAMPTZ NOT NULL,
    billing_period_end      TIMESTAMPTZ NOT NULL,
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('allocate','consume','rollover','expire','adjustment','refund')),
    amount                  NUMERIC(14,2) NOT NULL,         -- in quota units (credit/order/kg)
    balance_before          NUMERIC(14,2) NOT NULL,
    balance_after           NUMERIC(14,2) NOT NULL,
    order_id                UUID,                            -- set when consumed by an order
    order_created_at        TIMESTAMPTZ,
    reference_type          VARCHAR(30),
    reference_id            UUID,
    notes                   TEXT,
    performed_by_type       VARCHAR(20) DEFAULT 'system',
    performed_by_id         UUID,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_subusage_subscription  ON subscription_usage_ledger(customer_subscription_id, occurred_at DESC);
CREATE INDEX idx_subusage_period        ON subscription_usage_ledger(customer_subscription_id, billing_period_start);
CREATE INDEX idx_subusage_order         ON subscription_usage_ledger(order_id) WHERE order_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- MV: customer subscription MRR (refresh hourly)
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW mv_subscription_mrr AS
SELECT
    cs.brand_id,
    sp.tier,
    DATE_TRUNC('month', now())::DATE                                    AS as_of_month,
    COUNT(*) FILTER (WHERE cs.status = 'active')                        AS active_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'trialing')                      AS trialing_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'past_due')                      AS past_due_subscriptions,
    COUNT(*) FILTER (WHERE cs.status = 'cancelled')                     AS cancelled_subscriptions,
    -- normalize every interval to a monthly figure
    SUM(
      CASE cs.billing_interval
        WHEN 'weekly'      THEN cs.price_snapshot * 52.0/12.0 / cs.interval_count
        WHEN 'monthly'     THEN cs.price_snapshot / cs.interval_count
        WHEN 'quarterly'   THEN cs.price_snapshot / (3 * cs.interval_count)
        WHEN 'half_yearly' THEN cs.price_snapshot / (6 * cs.interval_count)
        WHEN 'yearly'      THEN cs.price_snapshot / (12 * cs.interval_count)
        ELSE cs.price_snapshot
      END
    ) FILTER (WHERE cs.status IN ('active','past_due'))                 AS mrr
FROM customer_subscriptions cs
JOIN subscription_plans sp ON sp.id = cs.plan_id
GROUP BY cs.brand_id, sp.tier;

CREATE UNIQUE INDEX idx_mvsubmrr_unique ON mv_subscription_mrr(brand_id, tier, as_of_month);
