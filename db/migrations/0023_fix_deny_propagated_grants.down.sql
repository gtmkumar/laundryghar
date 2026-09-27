-- 0023 rollback — restore the grants that 0019 / value_slab_pricing.sql propagated from DENY rows.
--
-- This deliberately reconstructs the DEFECTIVE state: it re-grants `domains.manage` and
-- `pricing.slab.manage` to every role that is explicitly DENIED the source permission, which is
-- precisely the set the up-migration removed. Rolling back re-opens the hole where a read-only
-- role can rewrite a brand's custom domain — that is what an exact inverse means here.

INSERT INTO identity_access.role_permissions (id, role_id, permission_id, effect, granted_at, created_at)
SELECT gen_random_uuid(), src.role_id, derived.id, 'allow', now(), now()
FROM   identity_access.role_permissions src
JOIN   identity_access.permissions      sp      ON sp.id = src.permission_id
JOIN   identity_access.permissions      derived ON derived.code =
           CASE sp.code
               WHEN 'brands.update'             THEN 'domains.manage'
               WHEN 'pricing.pricelist.update'  THEN 'pricing.slab.manage'
           END
WHERE  src.effect = 'deny'
  AND  sp.code IN ('brands.update', 'pricing.pricelist.update')
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- Re-bump so tokens pick the restored grants back up.
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
