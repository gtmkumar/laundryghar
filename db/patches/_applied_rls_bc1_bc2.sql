-- ============================================================================
-- _applied_rls_bc1_bc2.sql
-- ----------------------------------------------------------------------------
-- Scoped RLS ENABLE for BC-1 (tenancy_org) and BC-2 (identity_access) only.
-- Generated from rls_proposal.sql §5, applied 2026-06-05.
--
-- SCOPE RATIONALE
-- ---------------
-- All policies from rls_proposal.sql §3 are already present on both schemas
-- (verified pre-flight). This file only issues ALTER TABLE ... ENABLE ROW
-- LEVEL SECURITY for the subset of tables that are safe to activate now.
--
-- tenancy_org — 10 tables
--   • Already enabled: territories, franchise_agreements, franchises, stores,
--     warehouses (5 tables — rls_brand policy, brand_id present)
--   • Enabled by this file: brands, platforms, holidays, operating_hours,
--     store_warehouse_mappings (5 tables — policies present, all have brand_id
--     or are admin-only; safe to activate)
--
-- identity_access — 11 tables
--   • Enabled by this file: audit_logs, roles (2 tables)
--       - Both have brand_id → rls_brand policy is semantically correct.
--   • NOT enabled (deferred):
--       - users, permissions, role_permissions (rls_admin_only — enabling now
--         would silently block all app_user reads of the users table before
--         the app switches connection role away from postgres/superuser; the
--         app is not yet wired to set app.bypass_rls for auth flows)
--       - login_history, otp_codes, refresh_tokens, password_resets,
--         user_profiles, user_scope_memberships (rls_user_self — these require
--         app.current_user_id session var; not yet set by the app middleware)
--
-- Idempotent: ALTER TABLE ... ENABLE ROW LEVEL SECURITY is safe to re-run
-- (PostgreSQL treats it as a no-op if already enabled).
-- ============================================================================

SET client_min_messages = WARNING;

-- ---------------------------------------------------------------------------
-- BC-1: tenancy_org — enable on the 5 tables that have policies but not yet
--       ENABLE ROW LEVEL SECURITY (the other 5 were already enabled).
-- ---------------------------------------------------------------------------
ALTER TABLE tenancy_org.brands             ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy_org.platforms          ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy_org.holidays           ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy_org.operating_hours    ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy_org.store_warehouse_mappings ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------
-- BC-2: identity_access — enable only on tables with brand_id (B1 bucket).
--       Deferred: admin-only and user-self tables (see rationale above).
-- ---------------------------------------------------------------------------
ALTER TABLE identity_access.audit_logs     ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.roles          ENABLE ROW LEVEL SECURITY;
