-- 0007_align_plan_tiers — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- PLATFORM_STRATEGY.md §5 defines four tiers — Starter / Growth / Pro / Enterprise — and says what
-- each includes. The shipped bundles were `starter`, `pro`, `enterprise` (+ `salon-starter`), with
-- contents inherited from the old module list rather than §5's feature list, and no `growth` at all.
-- This aligns them.
--
-- ─── §5's plan table, verbatim ───────────────────────────────────────────────────────────────
--   Starter     bookings + scheduling, 1 location, our sub-domain
--   Growth      + fleet, online payments, multi-location, WhatsApp bot
--   Pro         + processing, item tracking, loyalty/coupons, analytics, API
--   Enterprise  + custom domain, white-label app, RaaS, dedicated support
-- Tiers are cumulative, so each row below is the previous one plus its own additions.
--
-- ─── Two judgement calls, stated rather than buried ─────────────────────────────────────────
--
-- 1. THE DERIVED FEATURES. 16 of the 33 features are `is_sellable = false` — auto-created from
--    existing modules by 0005 so entitlement could be total. They are not on the price list, so they
--    are not a tier's selling point; but a business cannot operate without customers, items, pricing,
--    POS, support, cash book. Those go in EVERY tier. The enterprise-flavoured ones (royalty,
--    franchises, audit) go in Enterprise only. `platform_plans` and `platform_billing` go in NO
--    tenant bundle at all — they are OUR operator console, not something a provider buys.
--
-- 2. TWO SELLABLE FEATURES §5 NEVER PLACES. `wallet` and `customer_subscriptions` appear in §5's
--    feature catalogue but in none of its four tier descriptions. They are placed in Pro (the
--    "everything operational" tier) because leaving them unsellable in every plan would make them
--    unbuyable except as add-ons. FLAGGED: confirm the tier.
--
-- ─── Prices are PLACEHOLDERS ────────────────────────────────────────────────────────────────
-- §5 gives no numbers, only "low monthly / mid / higher / custom". The existing 999 / 2999 / 7999
-- are pre-existing dev seed values, kept as-is. `growth` is set to 1999 purely to sit between
-- starter and pro. NONE of these are a commercial decision — they are placeholders so the tier
-- structure is testable, and must be confirmed before anyone is charged (tracked as OQ-4).

-- ─── 1. The Growth tier §5 names but the catalogue lacked ────────────────────────────────────
INSERT INTO identity_access.module_bundle (code, name, description, price, billing_interval, currency_code, is_public)
VALUES ('growth', 'Growth',
        'Starter plus fleet, online payments, multiple locations and the WhatsApp bot.',
        1999.00, 'monthly', 'INR', true)
ON CONFLICT (code) DO UPDATE
    SET name = EXCLUDED.name, description = EXCLUDED.description;

-- Make sure the three pre-existing tiers carry a price + interval so every public tier is billable.
UPDATE identity_access.module_bundle SET billing_interval = COALESCE(billing_interval, 'monthly'),
                                         currency_code    = COALESCE(currency_code, 'INR')
WHERE price IS NOT NULL;

-- ─── 2. Rebuild each tier's contents from §5 ─────────────────────────────────────────────────
-- Wholesale replace rather than merge: the old rows came from the module list, so merging would
-- leave whatever §5 does not mention silently included.
DELETE FROM identity_access.bundle_feature
 WHERE bundle_code IN ('starter', 'growth', 'pro', 'enterprise');

-- 2a. The operational spine — in every tenant tier (see judgement call 1).
INSERT INTO identity_access.bundle_feature (bundle_code, feature_key)
SELECT b.code, f.key
FROM   (VALUES ('starter'), ('growth'), ('pro'), ('enterprise')) AS b(code)
CROSS JOIN identity_access.features f
WHERE  f.status = 'active'
  AND  NOT f.is_sellable
  AND  f.key NOT IN ('royalty', 'franchises', 'audit', 'platform_plans', 'platform_billing')
ON CONFLICT DO NOTHING;

-- 2b. Enterprise also gets the enterprise-flavoured derived features.
INSERT INTO identity_access.bundle_feature (bundle_code, feature_key)
SELECT 'enterprise', k FROM unnest(ARRAY['royalty', 'franchises', 'audit']) AS k
WHERE EXISTS (SELECT 1 FROM identity_access.features f WHERE f.key = k)
ON CONFLICT DO NOTHING;

-- 2c. The sellable features, cumulatively, exactly as §5 lists them.
INSERT INTO identity_access.bundle_feature (bundle_code, feature_key)
SELECT v.bundle, v.feature
FROM (VALUES
    -- Starter: bookings + scheduling.
    ('starter',    'bookings'),
    ('starter',    'scheduling'),

    -- Growth: Starter + fleet, online payments, multi-location, WhatsApp bot.
    ('growth',     'bookings'),
    ('growth',     'scheduling'),
    ('growth',     'fleet'),
    ('growth',     'online_payments'),
    ('growth',     'multi_location'),
    ('growth',     'whatsapp_bot'),

    -- Pro: Growth + processing, item tracking, loyalty/coupons, analytics, API.
    ('pro',        'bookings'),
    ('pro',        'scheduling'),
    ('pro',        'fleet'),
    ('pro',        'online_payments'),
    ('pro',        'multi_location'),
    ('pro',        'whatsapp_bot'),
    ('pro',        'processing_facility'),
    ('pro',        'item_tracking'),
    ('pro',        'loyalty'),
    ('pro',        'coupons'),
    ('pro',        'advanced_analytics'),
    ('pro',        'api_access'),
    ('pro',        'wallet'),                  -- §5 never places this — see judgement call 2
    ('pro',        'customer_subscriptions'),  -- §5 never places this — see judgement call 2

    -- Enterprise: Pro + custom domain, white-label app, RaaS.
    ('enterprise', 'bookings'),
    ('enterprise', 'scheduling'),
    ('enterprise', 'fleet'),
    ('enterprise', 'online_payments'),
    ('enterprise', 'multi_location'),
    ('enterprise', 'whatsapp_bot'),
    ('enterprise', 'processing_facility'),
    ('enterprise', 'item_tracking'),
    ('enterprise', 'loyalty'),
    ('enterprise', 'coupons'),
    ('enterprise', 'advanced_analytics'),
    ('enterprise', 'api_access'),
    ('enterprise', 'wallet'),
    ('enterprise', 'customer_subscriptions'),
    ('enterprise', 'custom_domain'),
    ('enterprise', 'white_label_app'),
    ('enterprise', 'raas_partner')
) AS v(bundle, feature)
WHERE EXISTS (SELECT 1 FROM identity_access.features f WHERE f.key = v.feature)
ON CONFLICT DO NOTHING;

-- ─── 3. Assert the tiers are actually cumulative ─────────────────────────────────────────────
-- A tier that does not contain everything below it is a pricing bug: someone upgrades and LOSES a
-- capability. Cheaper to catch here than in a customer's console.
DO $$
DECLARE
    pair   record;
    missing text;
BEGIN
    FOR pair IN SELECT * FROM (VALUES
        ('starter', 'growth'), ('growth', 'pro'), ('pro', 'enterprise')
    ) AS t(lower_tier, higher_tier)
    LOOP
        SELECT string_agg(f.feature_key, ', ') INTO missing
        FROM   identity_access.bundle_feature f
        WHERE  f.bundle_code = pair.lower_tier
          AND  NOT EXISTS (SELECT 1 FROM identity_access.bundle_feature h
                            WHERE h.bundle_code = pair.higher_tier AND h.feature_key = f.feature_key);

        IF missing IS NOT NULL THEN
            RAISE EXCEPTION 'tier % is not a superset of % — upgrading would LOSE: %',
                            pair.higher_tier, pair.lower_tier, missing;
        END IF;
    END LOOP;

    RAISE NOTICE 'tiers verified cumulative: starter ⊆ growth ⊆ pro ⊆ enterprise';
END $$;

-- Every sellable feature must be buyable in at least one tier, or it can only ever be an add-on.
DO $$
DECLARE orphaned text;
BEGIN
    SELECT string_agg(f.key, ', ') INTO orphaned
    FROM   identity_access.features f
    WHERE  f.is_sellable AND f.status = 'active'
      AND  NOT EXISTS (SELECT 1 FROM identity_access.bundle_feature bf WHERE bf.feature_key = f.key);

    IF orphaned IS NOT NULL THEN
        RAISE EXCEPTION 'sellable features in no tier at all: %', orphaned;
    END IF;
END $$;
