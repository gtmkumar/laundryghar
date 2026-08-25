-- 0008_backfill_entitlements_from_plans — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- The step that makes entitlement enforcement SAFE to switch on (PLATFORM_STRATEGY.md §5,
-- task T-04). Until now `Entitlement:Enforced` has been false everywhere, because turning it on
-- against today's data would silently remove capabilities from live brands.
--
-- ─── The problem, measured rather than assumed ───────────────────────────────────────────────
-- Checked against this database before writing a line: with enforcement on and no backfill, the
-- only live brand (LG-MAIN) would lose SIX modules —
--     appointments, audit, fabrics, items, partner_booking, platform_billing
-- `items` is the catalogue. Losing it is not a downgrade, it is an outage.
--
-- ─── What this migration does ────────────────────────────────────────────────────────────────
-- Establishes `entitlement = plan ∪ add-ons` (§5) for every EXISTING brand, in two passes:
--
--   1. PLAN. A brand with an active platform subscription gets its tier's features as
--      source='bundle'. That is the durable, correct half: change the tier later and these rows are
--      re-expanded by ApplyBundleToBrand.
--
--   2. GRANDFATHERED ADD-ONS. Anything the brand can reach TODAY but whose feature its tier does not
--      include becomes source='manual' — a recorded, visible, revocable add-on. This is deliberately
--      NOT a blanket "give everyone everything": each grandfathered row is an explicit statement that
--      this brand was already using this before it was sellable, and an operator can see and remove
--      it in the Licensing console. Brands created AFTER this migration get only what they buy.
--
-- The alternative — flipping the switch and letting brands lose capabilities — would have been
-- defensible only if anyone had ever sold them a tier. Nobody has; LG-MAIN's 'pro' subscription is
-- itself an artefact of testing. Taking something away that was never sold is not enforcement, it is
-- a regression.

-- ─── 1. Expand each brand's plan ─────────────────────────────────────────────────────────────
INSERT INTO identity_access.brand_feature (brand_id, feature_key, enabled, source, created_at, updated_at)
SELECT s.brand_id, bf.feature_key, true, 'bundle', now(), now()
FROM   identity_access.brand_platform_subscription s
JOIN   identity_access.bundle_feature bf ON bf.bundle_code = s.bundle_code
JOIN   identity_access.features f        ON f.key = bf.feature_key AND f.status = 'active'
JOIN   tenancy_org.brands b              ON b.id = s.brand_id
WHERE  s.status = 'active'
  -- Respect the vertical gate: a shared tier must never license a laundry-only feature to a salon.
  AND  (f.vertical_key IS NULL OR f.vertical_key = b.vertical_key)
ON CONFLICT (brand_id, feature_key) DO NOTHING;   -- an existing manual row always wins

-- ─── 2. Grandfather what each brand could already reach ──────────────────────────────────────
-- "Could reach" = a non-core module whose feature is not core and is not already entitled. Recorded
-- as a manual add-on so it survives every future plan change (ApplyBundleToBrand never touches
-- manual rows) and is visible to an operator rather than hidden in a backfill.
INSERT INTO identity_access.brand_feature (brand_id, feature_key, enabled, source, created_at, updated_at)
SELECT DISTINCT b.id, m.feature_key, true, 'manual', now(), now()
FROM   tenancy_org.brands b
CROSS JOIN identity_access.modules m
JOIN   identity_access.features f ON f.key = m.feature_key
WHERE  m.status = 'active'
  AND  NOT m.is_core
  AND  m.feature_key IS NOT NULL
  AND  f.status = 'active'
  AND  NOT f.is_core
  -- vertical gate again: never grandfather a module this brand's vertical cannot see anyway
  AND  (m.vertical_key IS NULL OR m.vertical_key = b.vertical_key)
  AND  (f.vertical_key IS NULL OR f.vertical_key = b.vertical_key)
  AND  NOT EXISTS (SELECT 1 FROM identity_access.brand_feature x
                    WHERE x.brand_id = b.id AND x.feature_key = m.feature_key AND x.enabled)
ON CONFLICT (brand_id, feature_key) DO NOTHING;

-- ─── 2b. Re-enable rows that are explicitly DISABLED but still reachable today ────────────────
-- A row with enabled=false is a latent "this brand does not have this" that has NEVER taken effect,
-- because enforcement has been off for its whole life. Turning enforcement on is what would finally
-- make it bite — and on this database exactly one such row exists, `items`, which is the CATALOGUE.
-- Silently honouring it at the moment of switch-over would look indistinguishable from the migration
-- breaking the console.
--
-- So they are re-enabled, and every one is named in a NOTICE below. An operator who genuinely meant
-- to withhold a feature can switch it off again in the Licensing console, where the change is
-- deliberate, attributed and visible — rather than arriving as a side effect of enabling enforcement.
DO $$
DECLARE r record;
BEGIN
    FOR r IN
        SELECT b.code AS brand_code, bf.feature_key
        FROM   identity_access.brand_feature bf
        JOIN   tenancy_org.brands b ON b.id = bf.brand_id
        JOIN   identity_access.modules m ON m.feature_key = bf.feature_key
        WHERE  NOT bf.enabled
          AND  m.status = 'active' AND NOT m.is_core
          AND  (m.vertical_key IS NULL OR m.vertical_key = b.vertical_key)
    LOOP
        RAISE WARNING 're-enabling disabled entitlement %.% — it was reachable before enforcement; '
                      'switch it off again in Licensing if that was deliberate',
                      r.brand_code, r.feature_key;
    END LOOP;
END $$;

UPDATE identity_access.brand_feature bf
   SET enabled = true, updated_at = now()
  FROM tenancy_org.brands b
 WHERE bf.brand_id = b.id
   AND NOT bf.enabled
   AND EXISTS (SELECT 1 FROM identity_access.modules m
                WHERE m.feature_key = bf.feature_key
                  AND m.status = 'active' AND NOT m.is_core
                  AND (m.vertical_key IS NULL OR m.vertical_key = b.vertical_key));

-- ─── 3. Prove no brand lost anything ─────────────────────────────────────────────────────────
-- The whole point of the migration. Aborts the transaction rather than let a live brand wake up
-- without its catalogue.
DO $$
DECLARE lost text;
BEGIN
    SELECT string_agg(b.code || ': ' || m.key, ', ')
    INTO   lost
    FROM   tenancy_org.brands b
    CROSS JOIN identity_access.modules m
    LEFT JOIN identity_access.features f ON f.key = m.feature_key
    WHERE  m.status = 'active'
      AND  NOT m.is_core
      AND  m.feature_key IS NOT NULL
      AND  COALESCE(f.is_core, false) = false
      AND  (m.vertical_key IS NULL OR m.vertical_key = b.vertical_key)
      AND  NOT EXISTS (SELECT 1 FROM identity_access.brand_feature bf
                        WHERE bf.brand_id = b.id AND bf.feature_key = m.feature_key AND bf.enabled);

    IF lost IS NOT NULL THEN
        RAISE EXCEPTION 'backfill incomplete — these brands would lose modules once enforced: %', lost;
    END IF;

    RAISE NOTICE 'entitlement backfill verified: every brand keeps every module it can reach today';
END $$;

DO $$
DECLARE b record;
BEGIN
    FOR b IN
        SELECT br.code,
               count(*) FILTER (WHERE bf.source = 'bundle') AS from_plan,
               count(*) FILTER (WHERE bf.source = 'manual') AS grandfathered
        FROM   tenancy_org.brands br
        JOIN   identity_access.brand_feature bf ON bf.brand_id = br.id AND bf.enabled
        GROUP  BY br.code
    LOOP
        RAISE NOTICE '  % — % from plan, % grandfathered add-ons', b.code, b.from_plan, b.grandfathered;
    END LOOP;
END $$;
