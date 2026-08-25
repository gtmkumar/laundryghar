-- 0007_align_plan_tiers — rollback: must exactly undo the .up.sql
--
-- Removes the Growth tier and restores the pre-0007 bundle contents, which were derived from the
-- module list by 0005 rather than from §5.
--
-- FAILS BY DESIGN if any brand is on the Growth tier: dropping the bundle would cascade its
-- bundle_feature rows and leave that brand's subscription pointing at a tier that no longer exists.
-- Move them to another tier first:
--   SELECT b.code, s.bundle_code FROM identity_access.brand_platform_subscription s
--     JOIN tenancy_org.brands b ON b.id = s.brand_id WHERE s.bundle_code = 'growth';

DO $$
DECLARE on_growth int;
BEGIN
    SELECT count(*) INTO on_growth
    FROM identity_access.brand_platform_subscription WHERE bundle_code = 'growth';

    IF on_growth > 0 THEN
        RAISE EXCEPTION 'cannot remove the growth tier: % brand subscription(s) still point at it', on_growth;
    END IF;
END $$;

DELETE FROM identity_access.bundle_feature
 WHERE bundle_code IN ('starter', 'growth', 'pro', 'enterprise');

DELETE FROM identity_access.module_bundle WHERE code = 'growth';

-- Restore the 0005-era contents: every bundle's features are the ones behind the modules that
-- bundle used to list. That mapping is what 0005 itself derived, so this reproduces it.
INSERT INTO identity_access.bundle_feature (bundle_code, feature_key)
SELECT DISTINCT v.bundle_code, m.feature_key
FROM (VALUES
    ('starter','orders'),('starter','customers'),('starter','pricing'),('starter','packages'),
    ('starter','cms'),('starter','pos'),('starter','support'),
    ('pro','orders'),('pro','customers'),('pro','pricing'),('pro','packages'),('pro','cms'),
    ('pro','pos'),('pro','support'),('pro','warehouse'),('pro','riders'),('pro','coupons'),
    ('pro','promotions'),('pro','subscriptions'),('pro','analytics'),('pro','cashbook'),('pro','expenses'),
    ('enterprise','orders'),('enterprise','customers'),('enterprise','pricing'),('enterprise','packages'),
    ('enterprise','cms'),('enterprise','pos'),('enterprise','support'),('enterprise','warehouse'),
    ('enterprise','riders'),('enterprise','coupons'),('enterprise','promotions'),('enterprise','subscriptions'),
    ('enterprise','analytics'),('enterprise','cashbook'),('enterprise','expenses'),
    ('enterprise','royalty'),('enterprise','franchises'),('enterprise','platform_plans')
) AS v(bundle_code, module_key)
JOIN identity_access.modules m ON m.key = v.module_key AND m.feature_key IS NOT NULL
ON CONFLICT DO NOTHING;
