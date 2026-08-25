-- 0011_vertical_templates — forward migration
--
-- PLATFORM_STRATEGY.md §3: "A vertical template = mode + terminology pack + preset catalog structure
-- + default feature set. Launch templates: Laundry, Courier/Parcel, Tiffin."
--
-- Until now a vertical was a hand-written SQL patch: `salon` arrived as phase4_salon_pack.sql, which
-- inserted a module, a bundle and a quota type by hand. That is why §7's promise — "create provider →
-- pick vertical template → pick plan → live on sub-domain in minutes" — could not be kept: there was
-- nothing to pick from.
--
-- ─── The four parts §3 names, and where each already lives ───────────────────────────────────
--   mode              -> fulfillment_mode        (orders.fulfillment_mode; strategies exist)
--   terminology pack  -> identity_access.vertical_terms   (migration 0010)
--   default features  -> identity_access.module_bundle    (migration 0007, §5 tiers)
--   catalog preset    -> catalog_seed jsonb, HERE — the only part with no home yet
--
-- So a template is mostly a JOIN. It is a table rather than a view because the catalog seed and the
-- choice of default bundle are editorial decisions per vertical, not derivable from anything else.

CREATE TABLE IF NOT EXISTS identity_access.vertical_templates (
    key                 VARCHAR(48) PRIMARY KEY,
    vertical_key        VARCHAR(20) NOT NULL
                        CHECK (vertical_key::text = ANY (ARRAY['laundry','salon','logistics','tiffin']::text[])),
    name                VARCHAR(128) NOT NULL,
    description         TEXT,

    -- The order state machine a brand on this template runs. Must be one the resolver knows about,
    -- or orders on this template cannot transition at all.
    fulfillment_mode    VARCHAR(20) NOT NULL
                        CHECK (fulfillment_mode::text = ANY (
                            ARRAY['process_deliver','appointment','point_to_point','recurring']::text[])),

    -- The tier a brand starts on. Nullable: a template may exist before its tier is priced.
    default_bundle_code VARCHAR NULL REFERENCES identity_access.module_bundle(code) ON DELETE SET NULL,

    -- Preset catalogue: [{ "category": "...", "items": ["...", ...] }, ...]. Deliberately jsonb and
    -- deliberately SMALL — enough that a new provider's first screen is not empty, not an attempt to
    -- author their price list for them. §8.1: we do not set a company's customer prices.
    catalog_seed        JSONB NOT NULL DEFAULT '[]'::jsonb,

    is_public           BOOLEAN NOT NULL DEFAULT true,
    sort_order          INTEGER NOT NULL DEFAULT 100,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

GRANT SELECT ON identity_access.vertical_templates TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.vertical_templates TO app_admin;

COMMENT ON TABLE identity_access.vertical_templates IS
    'A launchable vertical (PLATFORM_STRATEGY.md §3): mode + terminology + catalog preset + default '
    'tier. What a provider picks at signup so they are operating in minutes rather than configuring '
    'from an empty console.';

INSERT INTO identity_access.vertical_templates
    (key, vertical_key, name, description, fulfillment_mode, default_bundle_code, catalog_seed, sort_order)
VALUES
    ('laundry', 'laundry', 'Laundry & dry clean',
     'Pickup, process, deliver. Garment-level tracking, warehouse QC, delivery slots.',
     'process_deliver', 'pro',
     '[{"category":"Wash & Fold","items":["Shirt","Trouser","Bedsheet"]},
       {"category":"Dry Clean","items":["Suit","Saree","Jacket"]},
       {"category":"Ironing","items":["Shirt","Trouser"]}]'::jsonb, 10),

    ('courier', 'logistics', 'Courier & parcel',
     'Point-to-point pickup and delivery. No processing step.',
     'point_to_point', 'growth',
     '[{"category":"Documents","items":["Envelope","Legal folder"]},
       {"category":"Parcels","items":["Small parcel","Medium parcel","Large parcel"]}]'::jsonb, 20),

    ('salon', 'salon', 'Salon & studio',
     'Appointment-based services delivered at a studio.',
     'appointment', 'salon-starter',
     '[{"category":"Hair","items":["Haircut","Colour","Styling"]},
       {"category":"Skin","items":["Facial","Clean-up"]}]'::jsonb, 30),

    -- Tiffin is registered but NOT operable: `recurring` has no fulfilment strategy yet (T-14),
    -- so this template is is_public = false until it does. Listing an unrunnable template to a
    -- provider at signup would be worse than not offering it.
    ('tiffin', 'tiffin', 'Tiffin & meal subscription',
     'Recurring scheduled meal delivery. Requires the recurring fulfilment mode.',
     'recurring', 'growth',
     '[{"category":"Meal plans","items":["Veg thali","Non-veg thali","Lunch box"]}]'::jsonb, 40)
ON CONFLICT (key) DO UPDATE
    SET vertical_key = EXCLUDED.vertical_key, name = EXCLUDED.name,
        description = EXCLUDED.description, fulfillment_mode = EXCLUDED.fulfillment_mode,
        catalog_seed = EXCLUDED.catalog_seed, updated_at = now();

-- Tiffin waits for its fulfilment strategy (see above).
UPDATE identity_access.vertical_templates SET is_public = false WHERE key = 'tiffin';

-- A template whose vertical has no terminology would put laundry words on its screens (§12).
DO $$
DECLARE gaps text;
BEGIN
    SELECT string_agg(t.key, ', ') INTO gaps
    FROM   identity_access.vertical_templates t
    WHERE  NOT EXISTS (SELECT 1 FROM identity_access.vertical_terms vt
                        WHERE vt.vertical_key = t.vertical_key);

    IF gaps IS NOT NULL THEN
        RAISE EXCEPTION 'templates with no terminology pack: %', gaps;
    END IF;

    RAISE NOTICE 'vertical templates seeded: % (% public)',
        (SELECT count(*) FROM identity_access.vertical_templates),
        (SELECT count(*) FROM identity_access.vertical_templates WHERE is_public);
END $$;
