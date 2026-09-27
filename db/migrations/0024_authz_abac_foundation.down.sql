-- 0024 rollback — drop the ABAC foundation.
--
-- Safe to run: 0024 creates structure and vocabulary only, no policy rows, and nothing in the
-- application depends on these tables until A4.2 wires the PEP. Dropping the schema takes the
-- decision_log partitions with it, so partman's bookkeeping row must go first or its maintenance
-- job will fail on a missing parent.

DELETE FROM partman.part_config WHERE parent_table = 'authz.decision_log';

DROP SCHEMA IF EXISTS authz CASCADE;
