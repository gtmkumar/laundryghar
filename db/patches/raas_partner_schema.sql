-- =============================================================================
-- db/patches/raas_partner_schema.sql
--
-- PURPOSE (RaaS partner MVP, issue #14): create the three logistics tables that
-- back the Rider-as-a-Service partner data layer —
--   • logistics.partners         — the tenant isolation ROOT (partner_id = partners.id)
--   • logistics.partner_users     — partner login principals (id = JWT sub)
--   • logistics.partner_bookings  — bookings a partner raises against a brand's fleet
--
-- MVP ONLY: no wallet / invoice / dispatch. Mirrors the riders / rider_assignments
-- column conventions (uuid PKs, timestamptz audit cols, snake_case, CHECK'd status).
--
-- RLS (kernel.current_partner_id() + rls_partner policies + ENABLE RLS) lives in the
-- companion patch db/patches/rls_partner.sql — run it AFTER this one.
--
-- Idempotent. Safe to re-run.
--
-- RUN (manual, as postgres — AFTER harden_app_user_and_rls_bypass.sql):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/raas_partner_schema.sql
-- =============================================================================

SET client_min_messages = WARNING;

-- Harmless: schema already exists in every deployed environment.
CREATE SCHEMA IF NOT EXISTS logistics;

-- ---------------------------------------------------------------------------
-- 1. logistics.partners — RaaS partner org (isolation root)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS logistics.partners (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code            VARCHAR(30)  NOT NULL,
    legal_name      VARCHAR(200) NOT NULL,
    status          VARCHAR(20)  NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active','suspended','terminated')),
    contact_email   VARCHAR(255),
    contact_phone   VARCHAR(20),
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by      UUID,
    updated_by      UUID,
    deleted_at      TIMESTAMPTZ,
    CONSTRAINT partners_code_key UNIQUE (code)
);

-- ---------------------------------------------------------------------------
-- 2. logistics.partner_users — partner login principals (id = JWT sub)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS logistics.partner_users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    partner_id      UUID NOT NULL,
    phone_e164      VARCHAR(20),
    email           VARCHAR(255),
    partner_role    VARCHAR(20) NOT NULL DEFAULT 'partner_operator'
                    CHECK (partner_role IN ('partner_admin','partner_operator')),
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active','suspended','invited')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by      UUID,
    updated_by      UUID,
    CONSTRAINT partner_users_partner_id_fkey
        FOREIGN KEY (partner_id) REFERENCES logistics.partners(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_partner_users_partner ON logistics.partner_users(partner_id);

-- ---------------------------------------------------------------------------
-- 3. logistics.partner_bookings — booking raised by a partner (partner_id = rls key)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS logistics.partner_bookings (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    partner_id                  UUID NOT NULL,
    brand_id                    UUID,          -- serving brand's fleet; no FK (brands are admin-only / cross-schema)
    created_by_partner_user_id  UUID NOT NULL,
    pickup_snapshot             JSONB NOT NULL DEFAULT '{}'::jsonb,
    drop_snapshot               JSONB NOT NULL DEFAULT '{}'::jsonb,
    quoted_fare                 NUMERIC(14,2),
    status                      VARCHAR(20) NOT NULL DEFAULT 'requested'
                                CHECK (status IN ('requested','assigned','in_progress','completed','cancelled')),
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by                  UUID,
    updated_by                  UUID,
    CONSTRAINT partner_bookings_partner_id_fkey
        FOREIGN KEY (partner_id) REFERENCES logistics.partners(id) ON DELETE CASCADE,
    CONSTRAINT partner_bookings_created_by_partner_user_id_fkey
        FOREIGN KEY (created_by_partner_user_id) REFERENCES logistics.partner_users(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS idx_partner_bookings_partner        ON logistics.partner_bookings(partner_id);
CREATE INDEX IF NOT EXISTS idx_partner_bookings_partner_status ON logistics.partner_bookings(partner_id, status);

-- ---------------------------------------------------------------------------
-- 4. GRANTs — explicit CRUD for the runtime roles.
-- ALTER DEFAULT PRIVILEGES in harden_app_user_and_rls_bypass.sql already covers
-- future logistics tables, but grant explicitly so this patch is self-contained
-- regardless of apply order. app_admin only exists once rls_proposal.sql has run.
-- ---------------------------------------------------------------------------
DO $grants$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['app_user','app_admin'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA logistics TO %I', r);
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE, DELETE ON '
                'logistics.partners, logistics.partner_users, logistics.partner_bookings TO %I', r);
        END IF;
    END LOOP;
END
$grants$;

SELECT 'raas_partner_schema.sql applied successfully.' AS result;
