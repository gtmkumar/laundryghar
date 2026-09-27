-- 0023 — A0.1: revoke grants that were propagated from DENY rows.
--
-- Two scripts granted a new permission "to every role that already holds X":
--     db/migrations/0019_premium_feature_modules.up.sql:53-61   (domains.*, white_label.read)
--     db/patches/value_slab_pricing.sql:126-131                 (pricing.slab.manage)
-- Both tested membership with
--     EXISTS (SELECT 1 FROM role_permissions rp JOIN permissions bp ... WHERE bp.code = 'X')
-- and neither filtered `rp.effect`. identity_access.role_permissions holds ALLOW *and* DENY rows,
-- so a role explicitly DENIED X satisfied the test and was granted the derived permission as ALLOW.
--
-- Live effect on this database: `auditor` — the read-only role, carrying 97 deny rows including
-- `brands.update` = deny — holds `domains.manage` (high; rewrites a brand's custom-domain config
-- via AdminBrandDomains.cs:42-44) and `pricing.slab.manage`, both as ALLOW.
--
-- 0019 is already applied and checksummed, so it is not edited; the patch source is fixed for
-- fresh builds and this migration repairs the rows already created. Scope is deliberately limited
-- to the two MUTATING permissions: an auditor seeing `domains.read` / `white_label.read` is
-- defensible, an auditor rewriting a domain is not. Removing those reads is a product decision,
-- not a security fix, and is left alone.
--
-- Written as a rule ("any role denied the source may not hold the derived") rather than
-- `WHERE role.code = 'auditor'`, so it also repairs any brand-authored role in the same state.

-- ── 1. domains.manage — derived from brands.update ────────────────────────────────────────────
DELETE FROM identity_access.role_permissions rp
USING identity_access.permissions p
WHERE p.id = rp.permission_id
  AND p.code = 'domains.manage'
  AND rp.effect = 'allow'
  AND EXISTS (
      SELECT 1
      FROM   identity_access.role_permissions src
      JOIN   identity_access.permissions      sp ON sp.id = src.permission_id
      WHERE  src.role_id = rp.role_id
        AND  sp.code     = 'brands.update'
        AND  src.effect  = 'deny');

-- ── 2. pricing.slab.manage — derived from pricing.pricelist.update ────────────────────────────
DELETE FROM identity_access.role_permissions rp
USING identity_access.permissions p
WHERE p.id = rp.permission_id
  AND p.code = 'pricing.slab.manage'
  AND rp.effect = 'allow'
  AND EXISTS (
      SELECT 1
      FROM   identity_access.role_permissions src
      JOIN   identity_access.permissions      sp ON sp.id = src.permission_id
      WHERE  src.role_id = rp.role_id
        AND  sp.code     = 'pricing.pricelist.update'
        AND  src.effect  = 'deny');

-- ── 3. Force live tokens holding an affected role to refresh ──────────────────────────────────
-- Without this the revoked permission stays in already-issued JWTs until natural expiry.
-- Mirrors PermVersionBumper.BumpRoleHoldersAsync; ~15s bound via Auth:EnforceTokenVersion.
UPDATE identity_access.users u
SET    perm_version = perm_version + 1
WHERE  EXISTS (
    SELECT 1
    FROM   identity_access.user_scope_memberships m
    JOIN   identity_access.role_permissions       src ON src.role_id = m.role_id
    JOIN   identity_access.permissions            sp  ON sp.id = src.permission_id
    WHERE  m.user_id = u.id
      AND  m.revoked_at IS NULL
      AND  src.effect = 'deny'
      AND  sp.code IN ('brands.update', 'pricing.pricelist.update'));

-- ── 4. Guard: the defect must not survive this migration ──────────────────────────────────────
DO $$
DECLARE offenders int;
BEGIN
    SELECT count(*) INTO offenders
    FROM   identity_access.role_permissions rp
    JOIN   identity_access.permissions      p ON p.id = rp.permission_id
    WHERE  rp.effect = 'allow'
      AND  p.code IN ('domains.manage', 'pricing.slab.manage')
      AND  EXISTS (
           SELECT 1
           FROM   identity_access.role_permissions src
           JOIN   identity_access.permissions      sp ON sp.id = src.permission_id
           WHERE  src.role_id = rp.role_id
             AND  src.effect  = 'deny'
             AND  sp.code IN ('brands.update', 'pricing.pricelist.update'));

    IF offenders > 0 THEN
        RAISE EXCEPTION
            '0023 failed: % role(s) still hold a mutating permission propagated from a DENY row',
            offenders;
    END IF;
END $$;
