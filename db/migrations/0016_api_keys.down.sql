-- 0016_api_keys — rollback

DELETE FROM identity_access.role_permissions rp
 USING identity_access.permissions p
 WHERE p.id = rp.permission_id AND p.code = 'api_keys.manage';
DELETE FROM identity_access.permissions WHERE code = 'api_keys.manage';

DROP FUNCTION IF EXISTS kernel.record_api_key_use(uuid, uuid, boolean);
DROP FUNCTION IF EXISTS kernel.resolve_api_key(text);

DROP TABLE IF EXISTS identity_access.api_key_usage;
DROP TABLE IF EXISTS identity_access.api_keys;
