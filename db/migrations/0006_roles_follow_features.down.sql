-- 0006_roles_follow_features — rollback: must exactly undo the .up.sql
--
-- Dropping the column makes every role unconditionally visible and grantable again — the pre-0006
-- behaviour. Nothing is lost: feature_key is derived from the fixed mapping in the .up.sql, so
-- re-applying restores it exactly.

ALTER TABLE identity_access.roles DROP COLUMN IF EXISTS feature_key;
