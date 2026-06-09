-- =============================================================================
-- seed_rider_ops_demo.sql
-- Demo data for the admin "Rider Ops" live board (Phase 1): puts a few seed
-- riders on the map with current locations, an active leg each, and a GPS trail,
-- so the live map / statuses / breadcrumb can be seen working end-to-end.
-- Idempotent: fixed UUIDs + DELETE-then-INSERT for the demo rows; rider UPDATEs
-- are naturally repeatable. Safe to re-run. Coordinates are around Gurugram NCR.
-- Run:  PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--         -f db/patches/seed_rider_ops_demo.sql
-- =============================================================================

BEGIN;

-- Anchors
-- brand  = 5b375161-9b8b-4177-ab58-54848606aa2f
-- store  = 09707413-c18a-43eb-9eab-15e4f012199d (Sector 45 Flagship)
-- riders : R-GGN-S45-002 d4000001 (on the way) · R-GGN-S14-001 d4000002 (arrived)
--          R-GGN-DLF-001 d4000004 (idle)        · R-GGN-SL-001  d4000006 (offline)

-- ── 1. Put riders on the map ────────────────────────────────────────────────
-- On the way — fresh ping, carrying a load, en route.
UPDATE logistics.riders SET
    last_known_location = ST_SetSRID(ST_MakePoint(77.0685, 28.4495), 4326)::geography,
    last_ping_at = now() - interval '20 seconds',
    is_online = true, is_on_duty = true, on_duty_since = now() - interval '3 hours',
    current_load = 2, updated_at = now()
WHERE id = 'd4000001-0000-0000-0000-000000000001';

-- Arrived — at a customer doorstep.
UPDATE logistics.riders SET
    last_known_location = ST_SetSRID(ST_MakePoint(77.0810, 28.4321), 4326)::geography,
    last_ping_at = now() - interval '40 seconds',
    is_online = true, is_on_duty = true, on_duty_since = now() - interval '2 hours',
    current_load = 1, updated_at = now()
WHERE id = 'd4000002-0000-0000-0000-000000000001';

-- Idle — on duty, parked at the store, no active leg.
UPDATE logistics.riders SET
    last_known_location = ST_SetSRID(ST_MakePoint(77.0640, 28.4419), 4326)::geography,
    last_ping_at = now() - interval '1 minute',
    is_online = true, is_on_duty = true, on_duty_since = now() - interval '1 hour',
    current_load = 0, updated_at = now()
WHERE id = 'd4000004-0000-0000-0000-000000000001';

-- Offline — has a last location but went off duty.
UPDATE logistics.riders SET
    last_known_location = ST_SetSRID(ST_MakePoint(77.0510, 28.4600), 4326)::geography,
    last_ping_at = now() - interval '4 hours',
    is_online = false, is_on_duty = false, on_duty_since = NULL,
    current_load = 0, updated_at = now()
WHERE id = 'd4000006-0000-0000-0000-000000000001';

-- ── 2. Active legs today (drive OpsStatus + today's counts) ─────────────────
DELETE FROM order_lifecycle.delivery_assignments
WHERE id IN (
    md5('seed_ops_da_ontheway')::uuid,
    md5('seed_ops_da_arrived')::uuid,
    md5('seed_ops_da_done1')::uuid,
    md5('seed_ops_da_done2')::uuid
);

INSERT INTO order_lifecycle.delivery_assignments
    (id, brand_id, store_id, rider_id, leg_type, sequence_number,
     assigned_at, started_at, arrived_at, completed_at,
     geo_location, distance_km, address_snapshot, otp_verified, status, metadata,
     created_at, updated_at)
VALUES
    -- in-progress delivery, rider en route
    (md5('seed_ops_da_ontheway')::uuid, '5b375161-9b8b-4177-ab58-54848606aa2f',
     '09707413-c18a-43eb-9eab-15e4f012199d', 'd4000001-0000-0000-0000-000000000001',
     'delivery', 1, now() - interval '25 minutes', now() - interval '20 minutes', NULL, NULL,
     ST_SetSRID(ST_MakePoint(77.0720, 28.4470), 4326)::geography, 3.4, '{}', false, 'started', '{}',
     now() - interval '25 minutes', now()),
    -- in-progress pickup, rider on site
    (md5('seed_ops_da_arrived')::uuid, '5b375161-9b8b-4177-ab58-54848606aa2f',
     '09707413-c18a-43eb-9eab-15e4f012199d', 'd4000002-0000-0000-0000-000000000001',
     'pickup', 1, now() - interval '35 minutes', now() - interval '30 minutes', now() - interval '3 minutes', NULL,
     ST_SetSRID(ST_MakePoint(77.0810, 28.4321), 4326)::geography, 2.1, '{}', false, 'arrived', '{}',
     now() - interval '35 minutes', now()),
    -- two already-completed legs earlier today (so throughput counts aren't all zero).
    -- Kept within the current IST calendar day (which the board counts) — small
    -- offsets so they land after IST-midnight regardless of the hour.
    (md5('seed_ops_da_done1')::uuid, '5b375161-9b8b-4177-ab58-54848606aa2f',
     '09707413-c18a-43eb-9eab-15e4f012199d', 'd4000001-0000-0000-0000-000000000001',
     'pickup', 0, now() - interval '50 minutes', now() - interval '48 minutes', now() - interval '44 minutes', now() - interval '40 minutes',
     NULL, 4.0, '{}', true, 'completed', '{}', now() - interval '50 minutes', now() - interval '40 minutes'),
    (md5('seed_ops_da_done2')::uuid, '5b375161-9b8b-4177-ab58-54848606aa2f',
     '09707413-c18a-43eb-9eab-15e4f012199d', 'd4000002-0000-0000-0000-000000000001',
     'delivery', 0, now() - interval '40 minutes', now() - interval '38 minutes', now() - interval '34 minutes', now() - interval '30 minutes',
     NULL, 2.6, '{}', true, 'completed', '{}', now() - interval '40 minutes', now() - interval '30 minutes');

-- ── 3. GPS breadcrumb for the on-the-way rider (last ~15 min) ───────────────
DELETE FROM logistics.rider_location_pings
WHERE rider_id = 'd4000001-0000-0000-0000-000000000001'
  AND metadata ->> 'seed' = 'rider_ops_demo';

INSERT INTO logistics.rider_location_pings
    (id, pinged_at, rider_id, brand_id, location, speed_kmph, heading_degrees, is_moving, metadata, created_at)
SELECT
    md5('seed_ops_ping_' || g)::uuid,
    now() - ((15 - g) * interval '90 seconds'),
    'd4000001-0000-0000-0000-000000000001',
    '5b375161-9b8b-4177-ab58-54848606aa2f',
    ST_SetSRID(ST_MakePoint(77.0640 + g * 0.0003, 28.4419 + g * 0.0005), 4326)::geography,
    18 + (g % 4) * 3, 45, true, '{"seed":"rider_ops_demo"}', now()
FROM generate_series(1, 15) AS g;

-- ── 4. Demo COD cash (Phase 3): a completed delivery leg holds uncleared cash ─
-- Re-creating the assignment above wiped these, so set them after the insert.
UPDATE order_lifecycle.delivery_assignments
SET cod_amount = 350.00, cod_collected_at = now() - interval '35 minutes', settlement_id = NULL
WHERE id = md5('seed_ops_da_done2')::uuid;

COMMIT;

-- Quick check
SELECT rider_code,
       ST_Y(last_known_location::geometry) AS lat,
       ST_X(last_known_location::geometry) AS lng,
       is_on_duty
FROM logistics.riders
WHERE id IN ('d4000001-0000-0000-0000-000000000001','d4000002-0000-0000-0000-000000000001',
             'd4000004-0000-0000-0000-000000000001','d4000006-0000-0000-0000-000000000001')
ORDER BY rider_code;
