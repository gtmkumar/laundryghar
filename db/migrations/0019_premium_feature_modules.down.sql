-- 0019_premium_feature_modules — rollback
--
-- The grandfathered brand_feature rows are deliberately LEFT. They record that a brand was using
-- custom domains before enforcement existed; deleting them on rollback would lose that fact and,
-- on a re-apply, the brand would be re-grandfathered anyway.

DROP FUNCTION IF EXISTS kernel.brand_app_identity(uuid);

DELETE FROM identity_access.role_permissions rp
 USING identity_access.permissions p
 WHERE p.id = rp.permission_id
   AND p.code IN ('domains.read', 'domains.manage', 'white_label.read');

DELETE FROM identity_access.permissions
 WHERE code IN ('domains.read', 'domains.manage', 'white_label.read');

DELETE FROM identity_access.modules WHERE key IN ('custom_domains', 'white_label');
