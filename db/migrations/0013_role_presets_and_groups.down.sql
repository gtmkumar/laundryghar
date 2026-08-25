-- 0013_role_presets_and_groups — rollback: must exactly undo the .up.sql
--
-- Removes only the presentation layer. Because 0013 was purely additive — no role renamed, no grant
-- touched, no membership altered — this cannot affect anyone's access. The console reverts to the
-- full 17-role, per-module matrix it showed before.

DROP TABLE IF EXISTS identity_access.permission_groups;
DROP TABLE IF EXISTS identity_access.role_presets;
