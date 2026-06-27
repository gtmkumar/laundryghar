-- =============================================================================
-- db/patches/phase4_salon_pack.sql
--
-- Multi-vertical Phase 4 — the salon vertical PACK at the entitlement/commerce layer (blueprint
-- §7.4: salon ModuleBundle + permission pack appointment.*, salon quota units service_minutes).
-- This exercises the Phase-2 vertical seams (modules.vertical_key 2B, module_bundle.vertical_key +
-- per-vertical filtering 2C, quota_type 2E) for a real second vertical — salon becomes a fully
-- entitleable vertical with ZERO change to the shared spine.
--
-- Idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_salon_pack.sql
-- =============================================================================

BEGIN;

-- 1. Salon `appointments` nav module (vertical_key='salon') -------------------
INSERT INTO identity_access.modules
    (key, label, icon, route, section, nav_order, matrix_order,
     show_in_nav, show_in_matrix, required_permission, permission_modules, vertical_key, status)
VALUES
    ('appointments', 'Appointments', 'CalendarClock', '/appointments', 'Operations', 13, 25,
     true, true, 'appointment.manage', '{appointment}', 'salon', 'active')
ON CONFLICT (key) DO UPDATE
    SET vertical_key = EXCLUDED.vertical_key, required_permission = EXCLUDED.required_permission,
        route = EXCLUDED.route, updated_at = now();

-- 2. Salon module bundle (vertical_key='salon') -------------------------------
INSERT INTO identity_access.module_bundle (code, name, description, vertical_key)
VALUES ('salon-starter', 'Salon Starter', 'Salon core: appointments, customers, catalogue, POS.', 'salon')
ON CONFLICT (code) DO UPDATE SET vertical_key = EXCLUDED.vertical_key, name = EXCLUDED.name;

-- Bundle items: the salon-only module + the vertical-neutral shared ones. (ApplyBundleToBrand's
-- per-module vertical gate, slice 2C, would in any case drop laundry-only modules for a salon brand.)
INSERT INTO identity_access.module_bundle_item (bundle_code, module_key)
SELECT 'salon-starter', k FROM unnest(ARRAY['appointments','orders','customers','pricing','pos']) AS k
WHERE EXISTS (SELECT 1 FROM identity_access.modules m WHERE m.key = k)
ON CONFLICT DO NOTHING;

-- 3. Salon quota unit: widen commerce.subscription_plans quota_type to service_minutes ----
DO $relax$
DECLARE c record;
BEGIN
    FOR c IN
        SELECT con.conname FROM pg_constraint con
        JOIN pg_class rel     ON rel.oid = con.conrelid
        JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
        WHERE nsp.nspname='commerce' AND rel.relname='subscription_plans' AND con.contype='c'
          AND pg_get_constraintdef(con.oid) ILIKE '%quota_type%'
    LOOP
        EXECUTE format('ALTER TABLE commerce.subscription_plans DROP CONSTRAINT %I;', c.conname);
    END LOOP;
    ALTER TABLE commerce.subscription_plans
        ADD CONSTRAINT subscription_plans_quota_type_check
        CHECK (quota_type IN ('credit','order_count','job_count','weight_kg','service_minutes','unlimited'));
END
$relax$;

-- 4. Verification gate --------------------------------------------------------
DO $verify$
DECLARE m_vertical text; b_vertical text; def text;
BEGIN
    SELECT vertical_key INTO m_vertical FROM identity_access.modules WHERE key='appointments';
    IF m_vertical IS DISTINCT FROM 'salon' THEN RAISE EXCEPTION 'Phase 4 pack: appointments module not salon-tagged'; END IF;

    SELECT vertical_key INTO b_vertical FROM identity_access.module_bundle WHERE code='salon-starter';
    IF b_vertical IS DISTINCT FROM 'salon' THEN RAISE EXCEPTION 'Phase 4 pack: salon-starter bundle not salon-tagged'; END IF;

    SELECT pg_get_constraintdef(con.oid) INTO def FROM pg_constraint con
    JOIN pg_class rel     ON rel.oid = con.conrelid
    JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
    WHERE nsp.nspname='commerce' AND rel.relname='subscription_plans' AND con.conname='subscription_plans_quota_type_check';
    IF def IS NULL OR def NOT ILIKE '%service_minutes%' THEN RAISE EXCEPTION 'Phase 4 pack: quota_type missing service_minutes'; END IF;

    RAISE NOTICE 'Phase 4 pack verification passed: salon module + bundle + service_minutes quota added.';
END
$verify$;

COMMIT;

SELECT 'phase4_salon_pack.sql applied successfully.' AS result;
