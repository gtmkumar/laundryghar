-- Down for 0025. Drops only what 0025 created.
--
-- Safe to run at any time: 0025 changed no existing RLS policy, so removing these functions cannot
-- widen access on any table that was protected before it ran. A table that has since been cut over
-- with authz.apply_generated_rls() is the exception — its generated policy references
-- authz.permits and must be reverted to a hand-written policy BEFORE this migration is rolled back,
-- or the drop will fail on the dependency.

DROP VIEW IF EXISTS authz.rls_drift;

DROP FUNCTION IF EXISTS authz.apply_generated_rls(text);
DROP FUNCTION IF EXISTS authz.generated_policy_sql(text);
DROP FUNCTION IF EXISTS authz.generated_policy_body(text, text);
DROP FUNCTION IF EXISTS authz.permits(text, text, jsonb);
DROP FUNCTION IF EXISTS authz.eval_node(uuid, jsonb, text);
DROP FUNCTION IF EXISTS authz.within_scope(jsonb);
DROP FUNCTION IF EXISTS authz.compare(text, jsonb, jsonb, timestamptz);
DROP FUNCTION IF EXISTS authz.values_equal(jsonb, jsonb);
DROP FUNCTION IF EXISTS authz.attr(text, jsonb, text);

DROP FUNCTION IF EXISTS kernel.current_permissions();
DROP FUNCTION IF EXISTS kernel.current_roles();
DROP FUNCTION IF EXISTS kernel.current_scope_nodes();
DROP FUNCTION IF EXISTS kernel.split_setting(text);
DROP FUNCTION IF EXISTS kernel.current_token_use();
DROP FUNCTION IF EXISTS kernel.current_user_type();
