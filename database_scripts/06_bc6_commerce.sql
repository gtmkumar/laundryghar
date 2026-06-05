-- ============================================================================
-- LAUNDRY GHAR — 06 BC-6 Commerce
-- ============================================================================
-- Wave:           1
-- Bounded ctx:    BC-6 (Commerce)
-- Source §:       §9 packages/loyalty/coupons + §10 payments/wallet
-- Tables:         13  (#60–72)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
--   - 02_bc2_identity_access.sql
--   - 03_bc3_customer_catalog.sql
--   - 04_bc4_order_lifecycle.sql
-- Owning agent:   agent/commerce
-- Purpose:        Prepaid packages (Diamond/Gold/Silver), customer package subscriptions + usage ledger (append-only), loyalty programs + points ledger, coupons + redemptions, promotions, payment methods, payments + refunds, wallet accounts + transactions (append-only). Ledger semantics throughout.
-- ============================================================================

-- SECTION 9: PACKAGES, LOYALTY, COUPONS (8 tables: #60–67)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 60. packages — Diamond/Gold/Silver prepaid packages
-- ----------------------------------------------------------------------------
CREATE TABLE packages (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    tier                    VARCHAR(30) NOT NULL
                            CHECK (tier IN ('silver','gold','diamond','platinum','custom')),
    description             TEXT,
    price                   NUMERIC(14,2) NOT NULL CHECK (price > 0),
    credit_value            NUMERIC(14,2) NOT NULL CHECK (credit_value > 0),
    discount_percent        NUMERIC(5,2) NOT NULL DEFAULT 0
                            CHECK (discount_percent BETWEEN 0 AND 100),
    credit_multiplier       NUMERIC(4,2) NOT NULL DEFAULT 1.00,
    validity_days           INTEGER,
    is_unlimited_validity   BOOLEAN NOT NULL DEFAULT false,
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    excluded_services       UUID[] NOT NULL DEFAULT '{}',
    minimum_order_value     NUMERIC(14,2),
    max_usage_per_order     NUMERIC(14,2),
    max_purchases_per_cust  INTEGER,
    icon_url                TEXT,
    color_hex               CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_featured             BOOLEAN NOT NULL DEFAULT false,
    terms_and_conditions    TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
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
CREATE INDEX idx_packages_brand_active  ON packages(brand_id, tier) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_packages_services_gin  ON packages USING GIN (applicable_services);

ALTER TABLE packages ENABLE ROW LEVEL SECURITY;
CREATE POLICY packages_tenant ON packages
USING (
    current_setting('app.bypass_rls', true) = 'true'
    OR brand_id = current_setting('app.current_brand_id', true)::uuid
);

-- ----------------------------------------------------------------------------
-- 61. customer_packages — purchased subscription instances
-- ----------------------------------------------------------------------------
CREATE TABLE customer_packages (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id),
    package_id              UUID NOT NULL REFERENCES packages(id),
    purchase_order_id       UUID,
    purchase_order_created_at TIMESTAMPTZ,
    payment_id              UUID,
    purchase_amount         NUMERIC(14,2) NOT NULL,
    credit_value_total      NUMERIC(14,2) NOT NULL,
    credit_value_used       NUMERIC(14,2) NOT NULL DEFAULT 0,
    credit_value_remaining  NUMERIC(14,2) GENERATED ALWAYS AS (credit_value_total - credit_value_used) STORED,
    activated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at              TIMESTAMPTZ,
    is_unlimited_validity   BOOLEAN NOT NULL DEFAULT false,
    last_used_at            TIMESTAMPTZ,
    usage_count             INTEGER NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','exhausted','expired','suspended','refunded','cancelled')),
    suspended_at            TIMESTAMPTZ,
    suspended_reason        TEXT,
    refunded_at             TIMESTAMPTZ,
    refunded_amount         NUMERIC(14,2),
    refund_reason           TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_custpkg_customer_active ON customer_packages(customer_id, expires_at) WHERE status = 'active';
CREATE INDEX idx_custpkg_package        ON customer_packages(package_id);
CREATE INDEX idx_custpkg_expiring       ON customer_packages(expires_at) WHERE status = 'active' AND expires_at IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 62. package_usage_ledger — credit debits per order (append-only)
-- ----------------------------------------------------------------------------
CREATE TABLE package_usage_ledger (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_package_id     UUID NOT NULL REFERENCES customer_packages(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('debit','credit','refund','adjustment','expiry','bonus')),
    amount                  NUMERIC(14,2) NOT NULL,
    balance_before          NUMERIC(14,2) NOT NULL,
    balance_after           NUMERIC(14,2) NOT NULL,
    notes                   TEXT,
    reference_type          VARCHAR(30),
    reference_id            UUID,
    performed_by            UUID,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_pkguse_custpkg         ON package_usage_ledger(customer_package_id, occurred_at DESC);
CREATE INDEX idx_pkguse_customer        ON package_usage_ledger(customer_id, occurred_at DESC);
CREATE INDEX idx_pkguse_order           ON package_usage_ledger(order_id) WHERE order_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 63. loyalty_programs — earn/burn config per brand
-- ----------------------------------------------------------------------------
CREATE TABLE loyalty_programs (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL UNIQUE REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    earn_rate               NUMERIC(8,4) NOT NULL DEFAULT 1.0,
    earn_basis              VARCHAR(20) NOT NULL DEFAULT 'spend'
                            CHECK (earn_basis IN ('spend','order_count','garment_count')),
    burn_rate               NUMERIC(8,4) NOT NULL DEFAULT 0.10,
    min_burn_points         INTEGER NOT NULL DEFAULT 100,
    max_burn_per_order_pct  NUMERIC(5,2) NOT NULL DEFAULT 30.00,
    min_order_for_earn      NUMERIC(14,2) NOT NULL DEFAULT 0,
    excluded_services       UUID[] NOT NULL DEFAULT '{}',
    point_expiry_months     SMALLINT,
    welcome_bonus           INTEGER NOT NULL DEFAULT 0,
    referral_bonus_referrer INTEGER NOT NULL DEFAULT 0,
    referral_bonus_referee  INTEGER NOT NULL DEFAULT 0,
    birthday_bonus          INTEGER NOT NULL DEFAULT 0,
    tier_config             JSONB NOT NULL DEFAULT '{}'::jsonb,
    terms                   TEXT,
    launched_at             TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);

-- ----------------------------------------------------------------------------
-- 64. loyalty_points_ledger — append-only points journal
-- ----------------------------------------------------------------------------
CREATE TABLE loyalty_points_ledger (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL REFERENCES customers(id),
    loyalty_program_id      UUID NOT NULL REFERENCES loyalty_programs(id),
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('earn','burn','expire','adjust','refund','bonus','referral','welcome','birthday')),
    direction               SMALLINT NOT NULL CHECK (direction IN (-1, 1)),
    points                  INTEGER NOT NULL CHECK (points > 0),
    balance_before          INTEGER NOT NULL,
    balance_after           INTEGER NOT NULL,
    monetary_equivalent     NUMERIC(14,2),
    reference_type          VARCHAR(30),
    reference_id            UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    expires_at              TIMESTAMPTZ,
    notes                   TEXT,
    performed_by            UUID,
    performed_by_type       VARCHAR(20) DEFAULT 'system',
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_loyledg_customer       ON loyalty_points_ledger(customer_id, occurred_at DESC);
CREATE INDEX idx_loyledg_order          ON loyalty_points_ledger(order_id) WHERE order_id IS NOT NULL;
CREATE INDEX idx_loyledg_expiring       ON loyalty_points_ledger(expires_at)
    WHERE transaction_type = 'earn' AND expires_at IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 65. coupons — promo codes with usage limits
-- ----------------------------------------------------------------------------
CREATE TABLE coupons (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    coupon_type             VARCHAR(20) NOT NULL DEFAULT 'percent'
                            CHECK (coupon_type IN ('percent','flat','free_pickup','free_delivery','bogo','tiered')),
    discount_value          NUMERIC(14,2) NOT NULL,
    max_discount_amount     NUMERIC(14,2),
    min_order_value         NUMERIC(14,2) NOT NULL DEFAULT 0,
    applicable_services     UUID[] NOT NULL DEFAULT '{}',
    applicable_stores       UUID[] NOT NULL DEFAULT '{}',
    applicable_franchises   UUID[] NOT NULL DEFAULT '{}',
    customer_eligibility    VARCHAR(30) NOT NULL DEFAULT 'all'
                            CHECK (customer_eligibility IN ('all','new','returning','vip','segment','specific')),
    eligible_customer_ids   UUID[],
    eligible_segments       TEXT[],
    is_first_order_only     BOOLEAN NOT NULL DEFAULT false,
    is_single_use_per_cust  BOOLEAN NOT NULL DEFAULT false,
    max_total_uses          INTEGER,
    max_uses_per_customer   SMALLINT NOT NULL DEFAULT 1,
    current_usage_count     INTEGER NOT NULL DEFAULT 0,
    is_stackable            BOOLEAN NOT NULL DEFAULT false,
    is_public               BOOLEAN NOT NULL DEFAULT true,
    is_auto_apply           BOOLEAN NOT NULL DEFAULT false,
    valid_from              TIMESTAMPTZ NOT NULL,
    valid_until             TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('draft','active','paused','exhausted','expired','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    deleted_at              TIMESTAMPTZ,
    updated_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_coupons_brand_active   ON coupons(brand_id, code)
    WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX idx_coupons_valid          ON coupons(valid_from, valid_until)
    WHERE status = 'active' AND deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 66. coupon_redemptions — coupons applied to orders
-- ----------------------------------------------------------------------------
CREATE TABLE coupon_redemptions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    coupon_id               UUID NOT NULL REFERENCES coupons(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL,
    order_id                UUID NOT NULL,
    order_created_at        TIMESTAMPTZ NOT NULL,
    coupon_code             VARCHAR(50) NOT NULL,
    discount_amount         NUMERIC(14,2) NOT NULL,
    order_subtotal_snapshot NUMERIC(14,2) NOT NULL,
    redeemed_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    reverted_at             TIMESTAMPTZ,
    reverted_reason         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_couponred_coupon       ON coupon_redemptions(coupon_id, redeemed_at DESC);
CREATE INDEX idx_couponred_customer     ON coupon_redemptions(customer_id, redeemed_at DESC);
CREATE INDEX idx_couponred_order        ON coupon_redemptions(order_id);

-- ----------------------------------------------------------------------------
-- 67. promotions — first-order, cashback, banner campaigns
-- ----------------------------------------------------------------------------
CREATE TABLE promotions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    promotion_type          VARCHAR(30) NOT NULL
                            CHECK (promotion_type IN ('first_order_discount','cashback','referral','bundle',
                                                      'flat_discount','percent_discount','free_service','seasonal')),
    target_audience         VARCHAR(30) NOT NULL DEFAULT 'all'
                            CHECK (target_audience IN ('all','new_users','returning','dormant','vip','segment')),
    eligible_segments       TEXT[],
    rules                   JSONB NOT NULL,
    reward_config           JSONB NOT NULL,
    coupon_id               UUID REFERENCES coupons(id),
    banner_image_url        TEXT,
    deeplink_url            TEXT,
    valid_from              TIMESTAMPTZ NOT NULL,
    valid_until             TIMESTAMPTZ,
    total_budget            NUMERIC(14,2),
    spent_budget            NUMERIC(14,2) NOT NULL DEFAULT 0,
    impressions_count       INTEGER NOT NULL DEFAULT 0,
    redemptions_count       INTEGER NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','scheduled','active','paused','completed','retired')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_promo_brand_active     ON promotions(brand_id, valid_from, valid_until)
    WHERE status IN ('scheduled','active');


-- ============================================================================
-- SECTION 10: PAYMENTS & WALLET (5 tables: #68–72)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 68. payment_methods — lookup (UPI, card, wallet, COD, prepaid, etc.)
-- ----------------------------------------------------------------------------
CREATE TABLE payment_methods (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    method_type             VARCHAR(30) NOT NULL
                            CHECK (method_type IN ('upi','card','netbanking','wallet','cod','prepaid_package',
                                                   'loyalty_points','store_credit','bank_transfer')),
    gateway                 VARCHAR(30) CHECK (gateway IN ('razorpay','payu','cashfree','phonepe','paytm','none')),
    icon_url                TEXT,
    minimum_amount          NUMERIC(14,2),
    maximum_amount          NUMERIC(14,2),
    convenience_fee_type    VARCHAR(20) CHECK (convenience_fee_type IN ('flat','percent')),
    convenience_fee_value   NUMERIC(14,2),
    is_online               BOOLEAN NOT NULL DEFAULT true,
    is_refundable           BOOLEAN NOT NULL DEFAULT true,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    config                  JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_paymethod_brand        ON payment_methods(brand_id) WHERE is_active = true;

-- ----------------------------------------------------------------------------
-- 69. payments — every transaction with gateway ref
-- ----------------------------------------------------------------------------
CREATE TABLE payments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID,
    store_id                UUID,
    customer_id             UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    payment_method_id       UUID REFERENCES payment_methods(id),
    payment_purpose         VARCHAR(30) NOT NULL DEFAULT 'order'
                            CHECK (payment_purpose IN ('order','package','wallet_topup','tip','adjustment','refund','royalty')),
    payment_number          VARCHAR(40) UNIQUE NOT NULL,
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    convenience_fee         NUMERIC(14,2) NOT NULL DEFAULT 0,
    gateway_charge          NUMERIC(14,2) NOT NULL DEFAULT 0,
    net_amount              NUMERIC(14,2) NOT NULL,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    direction               SMALLINT NOT NULL DEFAULT 1 CHECK (direction IN (-1, 1)),
    gateway                 VARCHAR(30),
    gateway_order_id        VARCHAR(100),
    gateway_payment_id      VARCHAR(100),
    gateway_signature       TEXT,
    gateway_response        JSONB,
    upi_vpa                 VARCHAR(100),
    card_last4              CHAR(4),
    card_network            VARCHAR(20),
    bank_name               VARCHAR(100),
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','initiated','authorized','captured','succeeded',
                                              'failed','cancelled','refunded','partially_refunded','disputed')),
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    initiated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at            TIMESTAMPTZ,
    failed_at               TIMESTAMPTZ,
    reconciled_at           TIMESTAMPTZ,
    settlement_id           VARCHAR(100),
    settled_at              TIMESTAMPTZ,
    settled_amount          NUMERIC(14,2),
    idempotency_key         VARCHAR(100) UNIQUE,
    ip_address              INET,
    user_agent              TEXT,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_payments_order         ON payments(order_id, status);
CREATE INDEX idx_payments_customer      ON payments(customer_id, created_at DESC);
CREATE INDEX idx_payments_gateway       ON payments(gateway, gateway_payment_id) WHERE gateway_payment_id IS NOT NULL;
CREATE INDEX idx_payments_status        ON payments(brand_id, status, created_at DESC);
CREATE INDEX idx_payments_settlement    ON payments(settlement_id) WHERE settlement_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 70. payment_refunds — refund tracking
-- ----------------------------------------------------------------------------
CREATE TABLE payment_refunds (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    original_payment_id     UUID NOT NULL REFERENCES payments(id),
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    customer_id             UUID,
    refund_number           VARCHAR(40) UNIQUE NOT NULL,
    refund_type             VARCHAR(20) NOT NULL DEFAULT 'full'
                            CHECK (refund_type IN ('full','partial','goodwill','dispute_loss')),
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    reason                  VARCHAR(100) NOT NULL,
    reason_text             TEXT,
    refund_method           VARCHAR(30) CHECK (refund_method IN ('original','wallet','bank_transfer','store_credit')),
    gateway_refund_id       VARCHAR(100),
    gateway_response        JSONB,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','approved','processing','succeeded','failed','rejected')),
    requested_by            UUID,
    requested_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    processed_at            TIMESTAMPTZ,
    completed_at            TIMESTAMPTZ,
    failure_reason          TEXT,
    customer_notified_at    TIMESTAMPTZ,
    notes                   TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_refunds_original       ON payment_refunds(original_payment_id);
CREATE INDEX idx_refunds_order          ON payment_refunds(order_id);
CREATE INDEX idx_refunds_status         ON payment_refunds(brand_id, status, requested_at DESC);

-- ----------------------------------------------------------------------------
-- 71. wallet_accounts — customer wallet header
-- ----------------------------------------------------------------------------
CREATE TABLE wallet_accounts (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL UNIQUE REFERENCES customers(id) ON DELETE CASCADE,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    balance                 NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (balance >= 0),
    locked_balance          NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (locked_balance >= 0),
    available_balance       NUMERIC(14,2) GENERATED ALWAYS AS (balance - locked_balance) STORED,
    lifetime_credit         NUMERIC(14,2) NOT NULL DEFAULT 0,
    lifetime_debit          NUMERIC(14,2) NOT NULL DEFAULT 0,
    last_transaction_at     TIMESTAMPTZ,
    is_frozen               BOOLEAN NOT NULL DEFAULT false,
    frozen_at               TIMESTAMPTZ,
    frozen_reason           TEXT,
    version                 INTEGER NOT NULL DEFAULT 1,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_wallet_customer        ON wallet_accounts(customer_id);

-- ----------------------------------------------------------------------------
-- 72. wallet_transactions — append-only wallet ledger
-- ----------------------------------------------------------------------------
CREATE TABLE wallet_transactions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    wallet_account_id       UUID NOT NULL REFERENCES wallet_accounts(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    customer_id             UUID NOT NULL,
    transaction_type        VARCHAR(20) NOT NULL
                            CHECK (transaction_type IN ('topup','debit','refund','cashback','bonus','adjustment','reversal','lock','unlock')),
    direction               SMALLINT NOT NULL CHECK (direction IN (-1, 1)),
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    balance_before          NUMERIC(14,2) NOT NULL,
    balance_after           NUMERIC(14,2) NOT NULL,
    reference_type          VARCHAR(30),
    reference_id            UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    payment_id              UUID,
    refund_id               UUID,
    description             VARCHAR(255),
    notes                   TEXT,
    performed_by_type       VARCHAR(20) DEFAULT 'system',
    performed_by_id         UUID,
    idempotency_key         VARCHAR(100) UNIQUE,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_wallettxn_wallet       ON wallet_transactions(wallet_account_id, occurred_at DESC);
CREATE INDEX idx_wallettxn_customer     ON wallet_transactions(customer_id, occurred_at DESC);
CREATE INDEX idx_wallettxn_order        ON wallet_transactions(order_id) WHERE order_id IS NOT NULL;

-- ============================================================================
-- Forward-reference FK (post-creation): customer_packages.payment_id → payments
-- payments is defined later in this file. ON DELETE SET NULL preserves the
-- customer-package row when the payment record is purged.
-- ============================================================================
ALTER TABLE customer_packages
    ADD CONSTRAINT customer_packages_payment_id_fkey
    FOREIGN KEY (payment_id) REFERENCES payments(id) ON DELETE SET NULL;

CREATE INDEX idx_custpkg_payment_fk ON customer_packages(payment_id);


-- ============================================================================
