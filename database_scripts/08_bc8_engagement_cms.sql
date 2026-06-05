-- ============================================================================
-- LAUNDRY GHAR — 08 BC-8 Engagement & CMS
-- ============================================================================
-- Wave:           2
-- Bounded ctx:    BC-8 (Engagement & CMS)
-- Source §:       §12 notifications & CMS
-- Tables:         8  (#81–88)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
--   - 02_bc2_identity_access.sql
--   - 03_bc3_customer_catalog.sql
-- Owning agent:   agent/integrator
-- Purpose:        Versioned notification templates per channel, per-customer channel prefs, transactional outbox + send log (PARTITIONED monthly), full WhatsApp conversation log, mobile-app onboarding slides + home-screen banners + remote config. Reads from every other BC via outbox_events (BC-0).
-- ============================================================================

-- SECTION 12: NOTIFICATIONS & CMS (8 tables: #81–88)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 81. notification_templates — versioned templates per channel
-- ----------------------------------------------------------------------------
CREATE TABLE notification_templates (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(100) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    channel                 VARCHAR(20) NOT NULL
                            CHECK (channel IN ('sms','whatsapp','email','push','in_app','voice')),
    category                VARCHAR(50) NOT NULL,
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    subject_template        VARCHAR(500),
    body_template           TEXT NOT NULL,
    sms_sender_id           VARCHAR(20),
    whatsapp_template_name  VARCHAR(200),
    whatsapp_template_id    VARCHAR(200),
    whatsapp_lang_code      VARCHAR(20),
    whatsapp_namespace      VARCHAR(100),
    push_title_template     VARCHAR(200),
    push_action_deeplink    TEXT,
    push_icon_url           TEXT,
    push_sound              VARCHAR(50),
    variables               JSONB NOT NULL DEFAULT '[]'::jsonb,
    version_number          INTEGER NOT NULL DEFAULT 1,
    parent_template_id      UUID REFERENCES notification_templates(id),
    is_transactional        BOOLEAN NOT NULL DEFAULT true,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    approved_at             TIMESTAMPTZ,
    approved_by             UUID,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, code, channel, locale, version_number)

);
CREATE INDEX idx_notiftpl_lookup        ON notification_templates(brand_id, code, channel, locale) WHERE is_active = true;

-- ----------------------------------------------------------------------------
-- 82. notification_preferences — per-customer channel toggles
-- ----------------------------------------------------------------------------
CREATE TABLE notification_preferences (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id             UUID REFERENCES customers(id) ON DELETE CASCADE,
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    notification_category   VARCHAR(50) NOT NULL,
    sms_enabled             BOOLEAN NOT NULL DEFAULT true,
    whatsapp_enabled        BOOLEAN NOT NULL DEFAULT true,
    email_enabled           BOOLEAN NOT NULL DEFAULT true,
    push_enabled            BOOLEAN NOT NULL DEFAULT true,
    in_app_enabled          BOOLEAN NOT NULL DEFAULT true,
    voice_enabled           BOOLEAN NOT NULL DEFAULT false,
    quiet_hours_start       TIME,
    quiet_hours_end         TIME,
    timezone                VARCHAR(50),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    CHECK (customer_id IS NOT NULL OR user_id IS NOT NULL),
    UNIQUE (customer_id, notification_category),
    UNIQUE (user_id, notification_category)

);
CREATE INDEX idx_notifpref_customer     ON notification_preferences(customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX idx_notifpref_user         ON notification_preferences(user_id) WHERE user_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 83. notifications_outbox — transactional outbox for reliable send
-- ----------------------------------------------------------------------------
CREATE TABLE notifications_outbox (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    template_id             UUID REFERENCES notification_templates(id),
    template_code           VARCHAR(100) NOT NULL,
    channel                 VARCHAR(20) NOT NULL,
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    recipient_type          VARCHAR(20) NOT NULL CHECK (recipient_type IN ('customer','user','rider','franchisee','manual')),
    recipient_id            UUID,
    recipient_phone         VARCHAR(20),
    recipient_email         CITEXT,
    recipient_fcm_token     TEXT,
    recipient_apns_token    TEXT,
    subject                 VARCHAR(500),
    body                    TEXT NOT NULL,
    variables_resolved      JSONB,
    push_title              VARCHAR(200),
    push_deeplink           TEXT,
    push_payload            JSONB,
    reference_type          VARCHAR(50),
    reference_id            UUID,
    correlation_id          UUID,
    priority                SMALLINT NOT NULL DEFAULT 5 CHECK (priority BETWEEN 1 AND 10),
    scheduled_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at              TIMESTAMPTZ,
    attempts                SMALLINT NOT NULL DEFAULT 0,
    max_attempts            SMALLINT NOT NULL DEFAULT 5,
    next_attempt_at         TIMESTAMPTZ,
    last_attempt_at         TIMESTAMPTZ,
    last_error              TEXT,
    sent_at                 TIMESTAMPTZ,
    provider                VARCHAR(50),
    provider_message_id     VARCHAR(200),
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','queued','sending','sent','failed','expired','suppressed','cancelled')),
    suppression_reason      VARCHAR(100),
    idempotency_key         VARCHAR(100) UNIQUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_outbox_due             ON notifications_outbox(scheduled_at, priority)
    WHERE status IN ('pending','queued');
CREATE INDEX idx_outbox_retry           ON notifications_outbox(next_attempt_at, priority)
    WHERE status = 'failed' AND attempts < max_attempts;
CREATE INDEX idx_outbox_reference       ON notifications_outbox(reference_type, reference_id);
CREATE INDEX idx_outbox_recipient       ON notifications_outbox(recipient_type, recipient_id, created_at DESC);

-- ----------------------------------------------------------------------------
-- 84. notifications_log — successful send log (PARTITIONED monthly)
-- ----------------------------------------------------------------------------
CREATE TABLE notifications_log (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    sent_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    brand_id                UUID NOT NULL,
    outbox_id               UUID,
    channel                 VARCHAR(20) NOT NULL,
    template_code           VARCHAR(100),
    recipient_type          VARCHAR(20) NOT NULL,
    recipient_id            UUID,
    recipient_address       VARCHAR(255),
    provider                VARCHAR(50),
    provider_message_id     VARCHAR(200),
    status                  VARCHAR(20) NOT NULL
                            CHECK (status IN ('sent','delivered','read','clicked','failed','bounced','blocked')),
    delivered_at            TIMESTAMPTZ,
    read_at                 TIMESTAMPTZ,
    clicked_at              TIMESTAMPTZ,
    failure_code            VARCHAR(50),
    failure_message         TEXT,
    cost                    NUMERIC(10,4),
    reference_type          VARCHAR(50),
    reference_id            UUID,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    PRIMARY KEY (id, sent_at)

) PARTITION BY RANGE (sent_at);

CREATE INDEX idx_notiflog_brand_time    ON notifications_log(brand_id, sent_at DESC);
CREATE INDEX idx_notiflog_recipient     ON notifications_log(recipient_type, recipient_id, sent_at DESC);
CREATE INDEX idx_notiflog_reference     ON notifications_log(reference_type, reference_id);
CREATE INDEX idx_notiflog_provider      ON notifications_log(provider, provider_message_id);

-- ----------------------------------------------------------------------------
-- 85. whatsapp_message_log — full WhatsApp conversation log
-- ----------------------------------------------------------------------------
CREATE TABLE whatsapp_message_log (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    direction               VARCHAR(10) NOT NULL CHECK (direction IN ('inbound','outbound')),
    customer_id             UUID,
    user_id                 UUID,
    phone_e164              VARCHAR(20) NOT NULL,
    provider                VARCHAR(50) NOT NULL DEFAULT 'meta',
    wa_message_id           VARCHAR(200) UNIQUE,
    wa_conversation_id      VARCHAR(200),
    template_name           VARCHAR(200),
    message_type            VARCHAR(20)
                            CHECK (message_type IN ('text','template','image','document','audio','video','button','list','location','contact')),
    body_text               TEXT,
    media_s3_key            TEXT,
    media_mime_type         VARCHAR(100),
    button_payload          VARCHAR(500),
    reference_type          VARCHAR(50),
    reference_id            UUID,
    status                  VARCHAR(20)
                            CHECK (status IN ('sent','delivered','read','failed','received')),
    sent_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    delivered_at            TIMESTAMPTZ,
    read_at                 TIMESTAMPTZ,
    failed_at               TIMESTAMPTZ,
    error_code              VARCHAR(50),
    error_message           TEXT,
    cost_units              NUMERIC(10,4),
    raw_payload             JSONB,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_walog_phone_time       ON whatsapp_message_log(phone_e164, sent_at DESC);
CREATE INDEX idx_walog_customer         ON whatsapp_message_log(customer_id, sent_at DESC);
CREATE INDEX idx_walog_reference        ON whatsapp_message_log(reference_type, reference_id);

-- ----------------------------------------------------------------------------
-- 86. onboarding_slides — mobile app onboarding carousel content
-- ----------------------------------------------------------------------------
CREATE TABLE onboarding_slides (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    app_type                VARCHAR(20) NOT NULL DEFAULT 'customer'
                            CHECK (app_type IN ('customer','rider','staff','pos')),
    title                   VARCHAR(200) NOT NULL,
    title_localized         JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    description_localized   JSONB NOT NULL DEFAULT '{}'::jsonb,
    image_url               TEXT NOT NULL,
    image_dark_url          TEXT,
    animation_url           TEXT,
    cta_text                VARCHAR(50),
    cta_deeplink            TEXT,
    background_color        CHAR(7),
    text_color              CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    show_from               TIMESTAMPTZ,
    show_until              TIMESTAMPTZ,
    min_app_version         VARCHAR(20),
    max_app_version         VARCHAR(20),
    target_segments         TEXT[],
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_onbslide_active        ON onboarding_slides(brand_id, app_type, display_order)
    WHERE is_active = true;

-- ----------------------------------------------------------------------------
-- 87. app_banners — home screen banners / promotional cards
-- ----------------------------------------------------------------------------
CREATE TABLE app_banners (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    app_type                VARCHAR(20) NOT NULL DEFAULT 'customer',
    placement               VARCHAR(50) NOT NULL
                            CHECK (placement IN ('home_top','home_middle','home_bottom','services_top','cart_top','order_success','profile')),
    title                   VARCHAR(200),
    title_localized         JSONB NOT NULL DEFAULT '{}'::jsonb,
    subtitle                VARCHAR(300),
    subtitle_localized      JSONB NOT NULL DEFAULT '{}'::jsonb,
    image_url               TEXT NOT NULL,
    image_dark_url          TEXT,
    cta_text                VARCHAR(50),
    cta_deeplink            TEXT,
    external_url            TEXT,
    promotion_id            UUID REFERENCES promotions(id),
    coupon_id               UUID REFERENCES coupons(id),
    background_color        CHAR(7),
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    show_from               TIMESTAMPTZ,
    show_until              TIMESTAMPTZ,
    target_audience         VARCHAR(30) DEFAULT 'all',
    target_segments         TEXT[],
    target_cities           TEXT[],
    impressions_count       INTEGER NOT NULL DEFAULT 0,
    clicks_count            INTEGER NOT NULL DEFAULT 0,
    min_app_version         VARCHAR(20),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_banner_active          ON app_banners(brand_id, placement, display_order)
    WHERE is_active = true;
CREATE INDEX idx_banner_active_range    ON app_banners(brand_id, show_from, show_until) WHERE is_active = true;

-- ----------------------------------------------------------------------------
-- 88. mobile_app_config — remote config per app per platform
-- ----------------------------------------------------------------------------
CREATE TABLE mobile_app_config (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE CASCADE,
    app_type                VARCHAR(20) NOT NULL,
    platform                VARCHAR(10) NOT NULL CHECK (platform IN ('android','ios','web')),
    config_key              VARCHAR(100) NOT NULL,
    config_value            JSONB NOT NULL,
    description             TEXT,
    is_force_update         BOOLEAN NOT NULL DEFAULT false,
    min_app_version         VARCHAR(20),
    max_app_version         VARCHAR(20),
    target_segments         TEXT[],
    rollout_percent         SMALLINT DEFAULT 100 CHECK (rollout_percent BETWEEN 0 AND 100),
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by              UUID,
    created_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, app_type, platform, config_key)

);
CREATE INDEX idx_mobilecfg_lookup       ON mobile_app_config(brand_id, app_type, platform) WHERE is_active = true;


-- ============================================================================
