-- ============================================================================
-- LAUNDRY GHAR — 02 BC-2 Identity & Access
-- ============================================================================
-- Wave:           0
-- Bounded ctx:    BC-2 (Identity & Access)
-- Source §:       §2
-- Tables:         11  (#11–21)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
-- Owning agent:   agent/foundation
-- Purpose:        Staff/admin users (customers live in BC-3), DB-driven RBAC (roles × permissions × scope), OTP, refresh tokens, login history, audit_logs (PARTITIONED monthly), password resets.
-- ============================================================================

-- SECTION 2: IDENTITY & ACCESS (11 tables: #11–21)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 11. users — system users (staff, admins, riders; customers are separate)
-- ----------------------------------------------------------------------------
CREATE TABLE users (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    phone_e164              VARCHAR(20) UNIQUE,
    email                   CITEXT UNIQUE,
    password_hash           TEXT,
    password_changed_at     TIMESTAMPTZ,
    must_change_password    BOOLEAN NOT NULL DEFAULT false,
    mfa_enabled             BOOLEAN NOT NULL DEFAULT false,
    mfa_secret              TEXT,
    mfa_backup_codes        TEXT[],
    user_type               VARCHAR(30) NOT NULL DEFAULT 'staff'
                            CHECK (user_type IN ('platform_admin','brand_admin','franchise_owner',
                                                 'store_admin','staff','warehouse_staff','rider','auditor','support')),
    locale                  VARCHAR(10) NOT NULL DEFAULT 'en-IN',
    timezone                VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata',
    status                  VARCHAR(20) NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','invited','locked','suspended','deleted')),
    last_login_at           TIMESTAMPTZ,
    last_login_ip           INET,
    last_active_at          TIMESTAMPTZ,
    failed_attempts         SMALLINT NOT NULL DEFAULT 0,
    locked_until            TIMESTAMPTZ,
    email_verified_at       TIMESTAMPTZ,
    phone_verified_at       TIMESTAMPTZ,
    invitation_token        TEXT,
    invitation_sent_at      TIMESTAMPTZ,
    invitation_accepted_at  TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    deleted_at              TIMESTAMPTZ,
    CHECK (phone_e164 IS NOT NULL OR email IS NOT NULL)
);
CREATE INDEX idx_users_status           ON users(status) WHERE deleted_at IS NULL;
CREATE INDEX idx_users_type             ON users(user_type) WHERE deleted_at IS NULL;
CREATE INDEX idx_users_last_active      ON users(last_active_at DESC);

-- ----------------------------------------------------------------------------
-- 12. user_profiles — extended user info, FCM tokens, settings
-- ----------------------------------------------------------------------------
CREATE TABLE user_profiles (
    user_id                 UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    first_name              VARCHAR(100),
    last_name               VARCHAR(100),
    display_name            VARCHAR(200),
    avatar_url              TEXT,
    date_of_birth           DATE,
    gender                  VARCHAR(20) CHECK (gender IN ('male','female','other','prefer_not_to_say')),
    designation             VARCHAR(100),
    department              VARCHAR(100),
    employee_id             VARCHAR(50),
    joined_at               DATE,
    emergency_contact_name  VARCHAR(200),
    emergency_contact_phone VARCHAR(20),
    address                 JSONB,
    fcm_token               TEXT,
    fcm_token_updated_at    TIMESTAMPTZ,
    apns_token              TEXT,
    apns_token_updated_at   TIMESTAMPTZ,
    preferences             JSONB NOT NULL DEFAULT '{}'::jsonb,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_userprof_employee      ON user_profiles(employee_id) WHERE employee_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 13. user_scope_memberships — user × (brand|franchise|store|warehouse) × role
-- ----------------------------------------------------------------------------
CREATE TABLE user_scope_memberships (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    scope_type              VARCHAR(20) NOT NULL
                            CHECK (scope_type IN ('platform','brand','franchise','store','warehouse','territory')),
    scope_id                UUID,
    role_id                 UUID NOT NULL,
    is_primary              BOOLEAN NOT NULL DEFAULT false,
    granted_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    granted_by              UUID,
    revoked_at              TIMESTAMPTZ,
    revoked_by              UUID,
    revoked_reason          TEXT,
    expires_at              TIMESTAMPTZ,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (user_id, scope_type, scope_id, role_id)

);
CREATE INDEX idx_usm_user_active        ON user_scope_memberships(user_id, expires_at)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_usm_scope              ON user_scope_memberships(scope_type, scope_id)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_usm_role               ON user_scope_memberships(role_id) WHERE revoked_at IS NULL;

-- ----------------------------------------------------------------------------
-- 14. roles — system + custom roles
-- ----------------------------------------------------------------------------
CREATE TABLE roles (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID REFERENCES brands(id) ON DELETE CASCADE,
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    description             TEXT,
    scope_type              VARCHAR(20) NOT NULL
                            CHECK (scope_type IN ('platform','brand','franchise','store','warehouse')),
    is_system               BOOLEAN NOT NULL DEFAULT false,
    is_assignable           BOOLEAN NOT NULL DEFAULT true,
    priority                SMALLINT NOT NULL DEFAULT 100,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    deleted_at              TIMESTAMPTZ,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_roles_brand            ON roles(brand_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_roles_scope            ON roles(scope_type) WHERE deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 15. permissions — granular permission codes (e.g., order.refund)
-- ----------------------------------------------------------------------------
CREATE TABLE permissions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code                    VARCHAR(100) NOT NULL UNIQUE,
    module                  VARCHAR(50) NOT NULL,
    action                  VARCHAR(50) NOT NULL,
    name                    VARCHAR(200) NOT NULL,
    description             TEXT,
    is_system               BOOLEAN NOT NULL DEFAULT true,
    requires_scope          BOOLEAN NOT NULL DEFAULT true,
    risk_level              VARCHAR(20) NOT NULL DEFAULT 'normal'
                            CHECK (risk_level IN ('low','normal','high','critical')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived'))
);
CREATE INDEX idx_permissions_module     ON permissions(module);

-- ----------------------------------------------------------------------------
-- 16. role_permissions — N:M role × permission
-- ----------------------------------------------------------------------------
CREATE TABLE role_permissions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id                 UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id           UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    granted_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    granted_by              UUID,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    UNIQUE (role_id, permission_id)

);
CREATE INDEX idx_roleperm_role          ON role_permissions(role_id);
CREATE INDEX idx_roleperm_permission    ON role_permissions(permission_id);

-- ----------------------------------------------------------------------------
-- Forward-reference FK: user_scope_memberships.role_id → roles(id)
-- Added here (not inline at table 13) because `roles` is defined AFTER
-- `user_scope_memberships` in this file. Matches the "assigned in" edge
-- in the BC-2 ER diagram.
-- (Not related to PostgreSQL's DEFERRABLE INITIALLY DEFERRED — this is just
--  a post-creation constraint to work around source-file declaration order.)
--
-- Companion index for FK enforcement (covers revoked rows too):
-- the existing `idx_usm_role` is partial (WHERE revoked_at IS NULL) and won't
-- be used when PostgreSQL validates ON DELETE / UPDATE on roles. We keep both:
-- the partial index stays cheaper for the hot authz read path.
-- ----------------------------------------------------------------------------
ALTER TABLE user_scope_memberships
    ADD CONSTRAINT user_scope_memberships_role_id_fkey
    FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE RESTRICT;

CREATE INDEX idx_usm_role_fk ON user_scope_memberships(role_id);

-- ----------------------------------------------------------------------------
-- 17. otp_codes — phone/email OTP with attempt counter
-- ----------------------------------------------------------------------------
CREATE TABLE otp_codes (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    purpose                 VARCHAR(30) NOT NULL
                            CHECK (purpose IN ('login','signup','verify_phone','verify_email',
                                               'reset_password','transaction','delivery_otp','sensitive_action')),
    identifier              VARCHAR(255) NOT NULL,
    identifier_type         VARCHAR(10) NOT NULL CHECK (identifier_type IN ('phone','email')),
    code_hash               TEXT NOT NULL,
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    customer_id             UUID,
    reference_id            UUID,
    reference_type          VARCHAR(50),
    attempts                SMALLINT NOT NULL DEFAULT 0,
    max_attempts            SMALLINT NOT NULL DEFAULT 3,
    verified_at             TIMESTAMPTZ,
    expires_at              TIMESTAMPTZ NOT NULL,
    ip_address              INET,
    user_agent              TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_otp_identifier_active  ON otp_codes(identifier, purpose, expires_at)
    WHERE verified_at IS NULL;
CREATE INDEX idx_otp_cleanup            ON otp_codes(expires_at);

-- ----------------------------------------------------------------------------
-- 18. refresh_tokens — JWT refresh tokens (hashed, revocable)
-- ----------------------------------------------------------------------------
CREATE TABLE refresh_tokens (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    customer_id             UUID,
    token_hash              TEXT NOT NULL UNIQUE,
    family_id               UUID NOT NULL,
    parent_token_id         UUID REFERENCES refresh_tokens(id),
    device_id               VARCHAR(255),
    device_name             VARCHAR(200),
    device_os               VARCHAR(50),
    ip_address              INET,
    user_agent              TEXT,
    issued_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at              TIMESTAMPTZ NOT NULL,
    last_used_at            TIMESTAMPTZ,
    revoked_at              TIMESTAMPTZ,
    revoked_reason          VARCHAR(50),
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CHECK (user_id IS NOT NULL OR customer_id IS NOT NULL)

);
CREATE INDEX idx_refresh_user_active    ON refresh_tokens(user_id, expires_at)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_refresh_customer_active ON refresh_tokens(customer_id, expires_at)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_refresh_family         ON refresh_tokens(family_id);

-- ----------------------------------------------------------------------------
-- 19. login_history — successful + failed login attempts
-- ----------------------------------------------------------------------------
CREATE TABLE login_history (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID REFERENCES users(id) ON DELETE SET NULL,
    customer_id             UUID,
    identifier              VARCHAR(255) NOT NULL,
    auth_method             VARCHAR(20) NOT NULL
                            CHECK (auth_method IN ('password','otp','oauth','mfa','refresh','impersonation')),
    success                 BOOLEAN NOT NULL,
    failure_reason          VARCHAR(100),
    ip_address              INET,
    user_agent              TEXT,
    device_id               VARCHAR(255),
    country_code            CHAR(2),
    city                    VARCHAR(100),
    is_suspicious           BOOLEAN NOT NULL DEFAULT false,
    risk_score              SMALLINT,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_loginhist_user         ON login_history(user_id, occurred_at DESC);
CREATE INDEX idx_loginhist_customer     ON login_history(customer_id, occurred_at DESC);
CREATE INDEX idx_loginhist_identifier   ON login_history(identifier, occurred_at DESC);
CREATE INDEX idx_loginhist_suspicious   ON login_history(occurred_at DESC) WHERE is_suspicious = true;

-- ----------------------------------------------------------------------------
-- 20. audit_logs — every state-changing action (PARTITIONED monthly)
-- ----------------------------------------------------------------------------
CREATE TABLE audit_logs (
    id                      UUID NOT NULL DEFAULT gen_random_uuid(),
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    brand_id                UUID,
    franchise_id            UUID,
    store_id                UUID,
    warehouse_id            UUID,
    actor_user_id           UUID,
    actor_customer_id       UUID,
    actor_type              VARCHAR(20) NOT NULL DEFAULT 'user'
                            CHECK (actor_type IN ('user','customer','system','api','webhook','job')),
    actor_display           VARCHAR(200),
    action                  VARCHAR(100) NOT NULL,
    resource_type           VARCHAR(50) NOT NULL,
    resource_id             UUID,
    resource_display        VARCHAR(200),
    old_values              JSONB,
    new_values              JSONB,
    changed_fields          TEXT[],
    ip_address              INET,
    user_agent              TEXT,
    request_id              UUID,
    correlation_id          UUID,
    success                 BOOLEAN NOT NULL DEFAULT true,
    error_message           TEXT,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    PRIMARY KEY (id, occurred_at)

) PARTITION BY RANGE (occurred_at);

CREATE INDEX idx_audit_resource         ON audit_logs(resource_type, resource_id, occurred_at DESC);
CREATE INDEX idx_audit_actor_user       ON audit_logs(actor_user_id, occurred_at DESC) WHERE actor_user_id IS NOT NULL;
CREATE INDEX idx_audit_brand_action     ON audit_logs(brand_id, action, occurred_at DESC);
CREATE INDEX idx_audit_correlation      ON audit_logs(correlation_id) WHERE correlation_id IS NOT NULL;

-- ----------------------------------------------------------------------------
-- 21. password_resets — password reset tokens with TTL
-- ----------------------------------------------------------------------------
CREATE TABLE password_resets (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 UUID REFERENCES users(id) ON DELETE CASCADE,
    customer_id             UUID,
    token_hash              TEXT NOT NULL UNIQUE,
    requested_ip            INET,
    requested_user_agent    TEXT,
    used_at                 TIMESTAMPTZ,
    used_ip                 INET,
    expires_at              TIMESTAMPTZ NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    CHECK (user_id IS NOT NULL OR customer_id IS NOT NULL)

);
CREATE INDEX idx_pwreset_active         ON password_resets(token_hash) WHERE used_at IS NULL;
CREATE INDEX idx_pwreset_cleanup        ON password_resets(expires_at);


-- ============================================================================
