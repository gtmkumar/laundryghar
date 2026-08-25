-- 0019_premium_feature_modules — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- ─── A gap found while wiring the white-label app config (T-22) ──────────────────────────────
-- `custom_domain` and `white_label_app` are both SELLABLE features in §5 — Enterprise-only, and two
-- of the four things that tier is sold on. Neither has a MODULE.
--
-- Entitlement is enforced along permission → module → feature (ScopeResolver). A feature with no
-- module sits at the end of a chain nothing walks: it can be bought, it appears on the price list,
-- and it gates absolutely nothing. Custom domains and the white-label app config were therefore
-- reachable by any brand with the ordinary `brands.update` permission, on any tier.
--
-- This is not a new capability. It is the enforcement that §5 already assumes exists, and without it
-- two of Enterprise's four selling points were free.
--
-- ─── Why new permissions rather than re-pointing the old ones ────────────────────────────────
-- `brands.read` / `brands.update` gate the whole brand-settings surface — name, logo, support
-- details, everything. Re-pointing THOSE at `custom_domain` would make a brand's own name uneditable
-- unless they bought a domain feature. So the domain endpoints get their own permissions, mapped to
-- the module that carries the feature, and the general brand permissions stay general.

-- ─── 1. The two missing modules ──────────────────────────────────────────────────────────────
INSERT INTO identity_access.modules (key, label, feature_key, is_core, status)
VALUES
    ('custom_domains', 'Custom domains',  'custom_domain',   false, 'active'),
    ('white_label',    'White-label app', 'white_label_app', false, 'active')
ON CONFLICT (key) DO UPDATE
    SET feature_key = EXCLUDED.feature_key,
        label       = EXCLUDED.label,
        is_core     = false,
        status      = 'active';

-- ─── 2. Permissions that hang off them ───────────────────────────────────────────────────────
INSERT INTO identity_access.permissions (id, code, module, action, name, description, is_system, requires_scope, risk_level, module_key, status, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'domains.read', 'domains', 'read',
     'View custom domains', 'See the custom domains attached to this brand.',
     true, true, 'low', 'custom_domains', 'active', now(), now()),
    (gen_random_uuid(), 'domains.manage', 'domains', 'manage',
     'Manage custom domains', 'Add, verify and remove custom domains. Owner-only (§6 Law 1: branding).',
     true, true, 'high', 'custom_domains', 'active', now(), now()),
    (gen_random_uuid(), 'white_label.read', 'white_label', 'read',
     'View white-label app configuration',
     'See the naming, identifiers and colours a branded build uses.',
     true, true, 'low', 'white_label', 'active', now(), now())
ON CONFLICT (code) DO UPDATE
    SET name = EXCLUDED.name, description = EXCLUDED.description,
        risk_level = EXCLUDED.risk_level, module_key = EXCLUDED.module_key, updated_at = now();

-- §6 Law 1 puts branding with the owner. Granted to the same roles that already hold
-- `brands.update`, so nobody GAINS reach — the endpoints simply stop being reachable without the
-- feature.
INSERT INTO identity_access.role_permissions (role_id, permission_id, created_at)
SELECT r.id, p.id, now()
FROM   identity_access.roles r
CROSS JOIN identity_access.permissions p
WHERE  p.code IN ('domains.read', 'domains.manage', 'white_label.read')
  AND  EXISTS (SELECT 1 FROM identity_access.role_permissions rp
               JOIN identity_access.permissions bp ON bp.id = rp.permission_id
               WHERE rp.role_id = r.id AND bp.code = 'brands.update')
ON CONFLICT DO NOTHING;

-- ─── 3. Grandfather what is already in use ───────────────────────────────────────────────────
-- Any brand that has ALREADY set up a custom domain was allowed to, under the rules as they stood.
-- Turning enforcement on must not take it away — that is a regression dressed as a feature gate,
-- and it is the same reasoning migration 0008 used when entitlement enforcement was first switched
-- on. Recorded as a visible, revocable manual add-on rather than a silent exception.
INSERT INTO identity_access.brand_feature (brand_id, feature_key, enabled, source, created_at, updated_at)
SELECT DISTINCT d.brand_id, 'custom_domain', true, 'manual', now(), now()
FROM   tenancy_org.brand_domains d
WHERE  NOT EXISTS (SELECT 1 FROM identity_access.brand_feature bf
                   WHERE bf.brand_id = d.brand_id AND bf.feature_key = 'custom_domain')
ON CONFLICT (brand_id, feature_key) DO NOTHING;

-- ─── 3b. The brand facts a branded build needs ───────────────────────────────────────────────
-- THE BRANDS-RLS TRAP, FOR THE FIFTH TIME. `tenancy_org.brands` carries
-- `rls_admin_only USING (kernel.rls_bypass())`, so an owner reading their OWN brand's name and
-- colour gets zero rows — the white-label endpoint answered 404 to the person whose app it is.
-- Caught live, again, and fixed the same way as the other four: one capability, not a bypass.
--
-- Note how little it exposes — the public face of a business (its name, its colour, its logo, its
-- support contact) plus the domain it already serves from. Nothing about billing, status or config.
CREATE OR REPLACE FUNCTION kernel.brand_app_identity(p_brand_id uuid)
RETURNS TABLE (
    code text, name text, primary_color text, logo_url text,
    support_email text, support_phone text, primary_domain text)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = tenancy_org, kernel, public, pg_catalog
AS $$
    SELECT b.code::text, b.name::text, b.primary_color::text, b.logo_url::text,
           b.support_email::text, b.support_phone::text,
           (SELECT d.domain::text FROM tenancy_org.brand_domains d
             WHERE d.brand_id = b.id AND d.is_primary AND d.verified_at IS NOT NULL
             LIMIT 1)
    FROM   tenancy_org.brands b
    WHERE  b.id = p_brand_id;
$$;

REVOKE ALL ON FUNCTION kernel.brand_app_identity(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.brand_app_identity(uuid) TO app_user;

-- ─── 4. Assert the chain is now complete ─────────────────────────────────────────────────────
-- A sellable feature with no module gates nothing. Now that two have been fixed, check there are no
-- others hiding — this is the assertion that would have caught the original gap.
DO $$
DECLARE orphaned text;
BEGIN
    SELECT string_agg(f.key, ', ')
    INTO   orphaned
    FROM   identity_access.features f
    WHERE  f.is_sellable AND f.status = 'active'
      AND  NOT EXISTS (SELECT 1 FROM identity_access.modules m
                       WHERE m.feature_key = f.key AND m.status = 'active');

    IF orphaned IS NOT NULL THEN
        RAISE WARNING 'sellable feature(s) with no module — these are on the price list but gate '
                      'NOTHING: %', orphaned;
    ELSE
        RAISE NOTICE 'every sellable feature is now reachable through a module — entitlement bites';
    END IF;
END $$;
