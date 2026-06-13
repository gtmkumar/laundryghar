-- ─────────────────────────────────────────────────────────────────────────────
-- seed_dispatch_defaults.sql  (Laundry + Logistics marketplace — Wave 1.4)
-- Seeds the PLATFORM-level dispatch mode default into kernel.system_settings
-- (category 'dispatch', key 'mode'). Default is 'push' (current behaviour).
--
-- Resolution precedence (DispatchConfig.ResolveMode): franchise > brand > platform.
-- Only this platform row may be set to 'offer_accept' (gated by the dispatch.mode.manage
-- permission); brand/franchise rows may only narrow to 'push'.
--
-- Shape (camelCase): { mode, offerTtlSeconds, maxOfferRounds, offersPerRound }
--
-- Idempotent: ON CONFLICT leaves any operator-entered value untouched.
-- Run as superuser (postgres) so RLS does not block the seed.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/seed_dispatch_defaults.sql
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO kernel.system_settings
    (scope_type, brand_id, franchise_id, store_id, category, setting_key, setting_value,
     data_type, description, is_encrypted, status, created_at, updated_at)
VALUES
    ('platform', NULL, NULL, NULL, 'dispatch', 'mode',
     '{"mode":"push","offerTtlSeconds":60,"maxOfferRounds":3,"offersPerRound":1}'::jsonb,
     'object', 'Dispatch mode: push (auto-assign) or offer_accept (gig offer loop).',
     false, 'active', now(), now())
ON CONFLICT (scope_type, brand_id, franchise_id, store_id, category, setting_key) DO NOTHING;
