-- Down for 0028.
DROP VIEW IF EXISTS authz.parity_summary;
DROP VIEW IF EXISTS authz.parity_disagreement;
ALTER TABLE authz.decision_log DROP COLUMN IF EXISTS rbac_allowed;
