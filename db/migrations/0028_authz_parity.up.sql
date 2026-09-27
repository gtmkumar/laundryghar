-- 0028 — A6.2: the parity harness.
--
-- Shadow mode is only worth running if the disagreements it finds are readable. A decision_log row
-- on its own says what ABAC decided; it does not say whether that MATCHES what the RBAC gate
-- decided for the same request, which is the only question the cutover turns on.
--
-- So the PEP records BOTH verdicts on the same row. It can: an ASP.NET Core authorization handler
-- sees every requirement in the policy, so AbacAuthorizationHandler evaluates the permission claim
-- itself at the moment it evaluates the policy rows — same request, same principal, same instant,
-- no sampling and no replay infrastructure.
--
-- The exit gate for A7.1 is then a single query: zero UNEXPLAINED rows in authz.parity_disagreement.

ALTER TABLE authz.decision_log
    ADD COLUMN IF NOT EXISTS rbac_allowed boolean;

COMMENT ON COLUMN authz.decision_log.rbac_allowed IS
    'What the existing permission-claim gate decided for this same request. NULL when it could not '
    'be determined (no PermissionRequirement on the endpoint). Compared against `decision` to '
    'produce authz.parity_disagreement.';

-- ── The parity report ─────────────────────────────────────────────────────────────────────────
-- Two disagreement kinds, and they are not equally urgent:
--
--   abac_stricter  ABAC denies what RBAC allows. Cutting over TIGHTENS access. Every row is a real
--                  behaviour change that has to be reviewed — it may be the point of the policy, or
--                  it may be an unresolved attribute failing closed on a path nobody considered.
--                  This is the common case at the start of a shadow window and the one that blocks.
--
--   abac_looser    ABAC permits what RBAC denies. Cutting over WIDENS access. There should be zero
--                  of these ever: the A6.3 backfill is a projection of role_permissions, so a
--                  permit ABAC grants that RBAC does not is a defect in the projection, not a
--                  policy decision. One row here is worse than a thousand of the other kind.
CREATE OR REPLACE VIEW authz.parity_disagreement AS
SELECT
    d.occurred_at,
    d.brand_id,
    d.user_id,
    d.token_use,
    d.resource_type,
    d.action,
    d.decision,
    d.rbac_allowed,
    d.matched_policy,
    d.reason,
    d.attributes,
    CASE
        WHEN d.rbac_allowed AND d.decision <> 'permit' THEN 'abac_stricter'
        ELSE 'abac_looser'
    END AS kind
FROM authz.decision_log d
WHERE d.rbac_allowed IS NOT NULL
  AND (d.rbac_allowed <> (d.decision = 'permit'));

COMMENT ON VIEW authz.parity_disagreement IS
    'A6.2 — every request where ABAC and the RBAC gate disagreed. The A7.1 exit gate per module is '
    'zero abac_looser rows and zero UNEXPLAINED abac_stricter rows.';

-- A per-(resource_type, action) rollup: what a cutover review actually reads. A module whose rows
-- are all one policy key and all explained is ready; one with a scatter of policies is not.
CREATE OR REPLACE VIEW authz.parity_summary AS
SELECT
    d.resource_type,
    d.action,
    count(*)                                                        AS decisions,
    count(*) FILTER (WHERE d.decision = 'permit')                   AS abac_permits,
    count(*) FILTER (WHERE d.decision <> 'permit')                  AS abac_denies,
    count(*) FILTER (WHERE d.rbac_allowed)                          AS rbac_permits,
    count(*) FILTER (WHERE d.rbac_allowed IS NOT NULL
                       AND d.rbac_allowed AND d.decision <> 'permit') AS stricter,
    count(*) FILTER (WHERE d.rbac_allowed IS NOT NULL
                       AND NOT d.rbac_allowed AND d.decision = 'permit') AS looser,
    min(d.occurred_at)                                              AS window_start,
    max(d.occurred_at)                                              AS window_end,
    round(avg(d.latency_us))                                        AS avg_latency_us,
    max(d.latency_us)                                               AS max_latency_us
FROM authz.decision_log d
GROUP BY d.resource_type, d.action;

COMMENT ON VIEW authz.parity_summary IS
    'A6.2 — per-target rollup of a shadow window, including the latency A9.1 gates the cutover on.';

GRANT SELECT ON authz.parity_disagreement, authz.parity_summary TO app_admin;
