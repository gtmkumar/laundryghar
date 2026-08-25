-- 0010_vertical_terminology — forward migration
--
-- PLATFORM_STRATEGY.md §3: "Terminology is config, not code: 'garment' ⇄ 'parcel' ⇄ 'meal box' —
-- same tables underneath." §12 names terminology leakage as a top risk: "laundry words in a courier
-- UI kills credibility".
--
-- A table, not a C# map, because §3 says config — a new vertical (or a provider who calls them
-- "pieces" rather than "garments") must be a row, not a deploy.
--
-- ─── Where the vocabulary comes from ─────────────────────────────────────────────────────────
-- Grounded in what the repo already states, NOT invented:
--   * item nouns garment / service / parcel  — docs/MULTI_VERTICAL_BLUEPRINT.md §5 names exactly
--     these as the `itemSummary` unit label per vertical.
--   * on-site location Warehouse / Studio / Hub / Kitchen — already in
--     admin-web/src/lib/verticalTerms.ts.
--   * booking nouns order / appointment / shipment — `appointment` and `point_to_point` are the
--     shipped fulfillment_mode names; `order` is the existing spine.
-- EXTENDED BY THIS MIGRATION (say so plainly, so it can be corrected):
--   * every tiffin term (meal / Kitchen / delivery) — tiffin was added yesterday and nobody has
--     written its vocabulary down.
--   * the plural forms, which are mechanical.
-- Anything a vertical does not override falls back to the neutral default, so a missing row is a
-- slightly generic word, never a crash or a blank.

CREATE TABLE IF NOT EXISTS identity_access.vertical_terms (
    vertical_key VARCHAR(20) NOT NULL
                 CHECK (vertical_key::text = ANY (ARRAY['laundry','salon','logistics','tiffin']::text[])),
    term_key     VARCHAR(48) NOT NULL,
    singular     VARCHAR(64) NOT NULL,
    plural       VARCHAR(64),
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (vertical_key, term_key)
);

-- Global catalogue like `modules`/`features`: readable by all, writable by admin. No RLS —
-- terminology is per VERTICAL, not per tenant, so there is nothing to isolate.
GRANT SELECT ON identity_access.vertical_terms TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.vertical_terms TO app_admin;

COMMENT ON TABLE identity_access.vertical_terms IS
    'Per-vertical user-facing vocabulary (PLATFORM_STRATEGY.md §3 "terminology is config, not code"). '
    'Served to all four clients; a term with no row falls back to the neutral default.';

INSERT INTO identity_access.vertical_terms (vertical_key, term_key, singular, plural) VALUES
    -- what the customer hands over / receives
    ('laundry',   'item',            'garment',     'garments'),
    ('salon',     'item',            'service',     'services'),
    ('logistics', 'item',            'parcel',      'parcels'),
    ('tiffin',    'item',            'meal',        'meals'),

    -- the thing a customer creates
    ('laundry',   'booking',         'order',       'orders'),
    ('salon',     'booking',         'appointment', 'appointments'),
    ('logistics', 'booking',         'shipment',    'shipments'),
    ('tiffin',    'booking',         'delivery',    'deliveries'),

    -- the on-site processing/service location
    ('laundry',   'onsite_location', 'Warehouse',   'Warehouses'),
    ('salon',     'onsite_location', 'Studio',      'Studios'),
    ('logistics', 'onsite_location', 'Hub',         'Hubs'),
    ('tiffin',    'onsite_location', 'Kitchen',     'Kitchens')
ON CONFLICT (vertical_key, term_key) DO UPDATE
    SET singular = EXCLUDED.singular, plural = EXCLUDED.plural, updated_at = now();

-- Every shipped vertical must define every term key, or a client silently renders a laundry word in
-- a courier UI — the exact §12 risk this table exists to remove.
DO $$
DECLARE gaps text;
BEGIN
    SELECT string_agg(v.vertical_key || '.' || t.term_key, ', ')
    INTO   gaps
    FROM   (SELECT unnest(ARRAY['laundry','salon','logistics','tiffin']) AS vertical_key) v
    CROSS JOIN (SELECT DISTINCT term_key FROM identity_access.vertical_terms) t
    WHERE  NOT EXISTS (SELECT 1 FROM identity_access.vertical_terms x
                        WHERE x.vertical_key = v.vertical_key AND x.term_key = t.term_key);

    IF gaps IS NOT NULL THEN
        RAISE EXCEPTION 'vertical_terms is incomplete — every vertical must define every term: %', gaps;
    END IF;

    RAISE NOTICE 'terminology complete: % verticals x % terms',
        4, (SELECT count(DISTINCT term_key) FROM identity_access.vertical_terms);
END $$;
