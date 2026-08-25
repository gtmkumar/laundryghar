-- 0006_roles_follow_features — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- PLATFORM_STRATEGY.md §5 / §6.3: "Roles appear with features. Buy Fleet → Rider appears. Buy
-- Processing → Facility Staff appears. Small business, small screen." A courier shop should see
-- four roles where a full laundry sees seven, without anyone hand-pruning a list.
--
-- Adds identity_access.roles.feature_key: the feature a brand must own for the role to be offered
-- AND grantable. NULL = always available (the vertical-neutral operating roles).
--
-- ─── Which roles get gated, and why only these ───────────────────────────────────────────────
-- Only the three the strategy names explicitly:
--   rider                                 -> fleet                (§6.1 "Rider (Fleet feature)")
--   warehouse_supervisor, warehouse_staff -> processing_facility  (§6.1 "Facility Staff (Processing feature)")
--   partner_admin, partner_operator       -> raas_partner         (§5 sells raas_partner; these roles
--                                                                  exist for nothing else)
--
-- Deliberately NOT gated: salon_manager / salon_staff / hub_supervisor / hub_operator. They are the
-- salon and logistics analogues of the warehouse pair, so mapping them to processing_facility is
-- tempting — but the strategy never says so, and they are ALREADY constrained by roles.vertical_key,
-- which is the mechanism that actually keeps them off a laundry brand's screen. Gating them on a
-- guess could hide a role a salon brand needs, and a role that silently disappears is a support
-- ticket that looks like data loss. Left NULL until someone decides (tracked with OQ-5).

ALTER TABLE identity_access.roles
    ADD COLUMN IF NOT EXISTS feature_key VARCHAR(64) REFERENCES identity_access.features(key);

UPDATE identity_access.roles r SET feature_key = v.feature
FROM (VALUES
    ('rider',                'fleet'),
    ('warehouse_supervisor', 'processing_facility'),
    ('warehouse_staff',      'processing_facility'),
    ('partner_admin',        'raas_partner'),
    ('partner_operator',     'raas_partner')
) AS v(role_code, feature)
WHERE r.code = v.role_code
  AND EXISTS (SELECT 1 FROM identity_access.features f WHERE f.key = v.feature);

COMMENT ON COLUMN identity_access.roles.feature_key IS
    'The feature a brand must have licensed for this role to be offered and grantable '
    '(PLATFORM_STRATEGY.md §5 "roles follow features"). NULL = always available.';

-- Verify the gating landed — but only where there was something to gate. A database with none of
-- these role codes is perfectly legitimate (a fresh bootstrap before the identity seeder runs, or a
-- test fixture that applies the DDL without the seed rows), and failing the migration there would
-- block every such environment.
--
-- The first version of this block asserted `gated > 0` unconditionally and did exactly that: it
-- aborted the migration in the integration-test fixture, which applies 02_bc2's DDL and seeds no
-- roles. Caught by RolesFollowFeaturesTests before it could reach a real bootstrap.
DO $$
DECLARE
    targets int;
    gated   int;
BEGIN
    SELECT count(*) INTO targets FROM identity_access.roles
     WHERE code IN ('rider','warehouse_supervisor','warehouse_staff','partner_admin','partner_operator');

    IF targets = 0 THEN
        RAISE NOTICE 'roles_follow_features: none of the gated role codes exist here — nothing to do';
        RETURN;
    END IF;

    SELECT count(*) INTO gated FROM identity_access.roles WHERE feature_key IS NOT NULL;
    IF gated = 0 THEN
        RAISE EXCEPTION
          'roles_follow_features: % gateable role(s) exist but none was gated — the features they '
          'reference (fleet / processing_facility / raas_partner) are missing from the catalogue', targets;
    END IF;

    RAISE NOTICE 'roles gated by a feature: % of % candidates', gated, targets;
END $$;
