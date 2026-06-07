-- ============================================================================
-- seed_warehouse_garments.sql  (idempotent, deterministic)
-- Populates order_lifecycle.garments with ~154 garments "in flight" across the
-- six active processing stages (received → sorting → washing → drying → ironing
-- → qc) so the Warehouse kanban board renders real data through real APIs.
--
-- Every garment references a REAL seeded order / order_item / customer; item and
-- fabric come from REAL catalog rows (a few extra garment types are added here).
-- Re-runnable: deterministic UUIDs via md5(seed) + ON CONFLICT DO NOTHING.
--
-- Run as a privileged (postgres) connection — bypasses RLS for cross-brand seed.
--   PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/seed_warehouse_garments.sql
-- ============================================================================

BEGIN;

-- Brand / warehouse anchors (the single active demo brand + Mumbai warehouse).
DO $$
DECLARE
  v_brand uuid := '5b375161-9b8b-4177-ab58-54848606aa2f';
BEGIN
  -- ── Extra garment types (real catalog rows) so the board shows variety ─────
  INSERT INTO customer_catalog.items (id, brand_id, code, name, display_order)
  VALUES
    (md5('wh-item:Coat')::uuid,    v_brand, 'COAT',    'Coat',    20),
    (md5('wh-item:Kurta')::uuid,   v_brand, 'KURTA',   'Kurta',   21),
    (md5('wh-item:Suit')::uuid,    v_brand, 'SUIT',    'Suit',    22),
    (md5('wh-item:Blazer')::uuid,  v_brand, 'BLAZER',  'Blazer',  23),
    (md5('wh-item:Tie')::uuid,     v_brand, 'TIE',     'Tie',     24),
    (md5('wh-item:Lehenga')::uuid, v_brand, 'LEHENGA', 'Lehenga', 25),
    (md5('wh-item:Scarf')::uuid,   v_brand, 'SCARF',   'Scarf',   26)
  ON CONFLICT (id) DO NOTHING;

  -- ── Linen fabric (Cotton / Silk / Woolen already exist) ───────────────────
  INSERT INTO customer_catalog.fabric_types (id, brand_id, code, name)
  VALUES (md5('wh-fabric:Linen')::uuid, v_brand, 'LINEN', 'Linen')
  ON CONFLICT (id) DO NOTHING;

  -- ── Realistic single-site WIP/throughput target (drives Capacity %) ───────
  --    154 in flight / 240 target ≈ 64% capacity on the board header.
  UPDATE tenancy_org.warehouses
  SET daily_throughput_target = 240
  WHERE id = 'a6c735c1-51df-47a3-aee8-5972edfa3e5b';
END $$;

-- ── Garments in flight ──────────────────────────────────────────────────────
WITH anchor AS (
  SELECT '5b375161-9b8b-4177-ab58-54848606aa2f'::uuid AS brand_id,
         'a6c735c1-51df-47a3-aee8-5972edfa3e5b'::uuid AS warehouse_id
),
item_pool AS (
  SELECT array_agg(id ORDER BY display_order, name) AS ids
  FROM customer_catalog.items
  WHERE brand_id = (SELECT brand_id FROM anchor) AND status = 'active'
),
fabric_pool AS (
  SELECT array_agg(id ORDER BY name) AS ids
  FROM customer_catalog.fabric_types
  WHERE brand_id = (SELECT brand_id FROM anchor) AND status = 'active'
),
src AS (
  SELECT oi.id              AS order_item_id,
         o.id               AS order_id,
         o.created_at       AS order_created_at,
         o.customer_id,
         o.store_id,
         o.brand_id,
         o.franchise_id,
         row_number() OVER (ORDER BY o.created_at, oi.id) AS rn
  FROM order_lifecycle.order_items oi
  JOIN order_lifecycle.orders o
    ON o.id = oi.order_id AND o.created_at = oi.order_created_at
  JOIN customer_catalog.customers c
    ON c.id = o.customer_id
  WHERE coalesce(c.first_name, '') <> ''
    AND o.brand_id = (SELECT brand_id FROM anchor)
  LIMIT 154
),
staged AS (
  SELECT s.*,
         CASE
           WHEN rn <= 28  THEN 'received'   -- 28
           WHEN rn <= 54  THEN 'sorting'    -- 26
           WHEN rn <= 96  THEN 'washing'    -- 42
           WHEN rn <= 116 THEN 'drying'     -- 20
           WHEN rn <= 134 THEN 'ironing'    -- 18
           ELSE                'qc'         -- 20
         END AS stage
  FROM src s
)
INSERT INTO order_lifecycle.garments
  (id, brand_id, franchise_id, store_id, warehouse_id,
   order_id, order_created_at, order_item_id, customer_id,
   tag_code, item_id, fabric_type_id, color, brand_name, size,
   current_stage, current_location_type, current_location_id,
   last_scanned_at, has_ornaments, has_lining, is_designer_wear, rewash_count,
   metadata, created_at, updated_at, version, status)
SELECT
  md5('whseed:' || st.rn)::uuid,
  st.brand_id, st.franchise_id, st.store_id, (SELECT warehouse_id FROM anchor),
  st.order_id, st.order_created_at, st.order_item_id, st.customer_id,
  'LG-' || lpad((28400 + ((st.rn - 1) / 3))::text, 5, '0')
        || '-' || lpad(((((st.rn - 1) % 3) + 1))::text, 2, '0'),
  (SELECT ids[1 + (st.rn % array_length(ids, 1))] FROM item_pool),
  (SELECT ids[1 + (st.rn % array_length(ids, 1))] FROM fabric_pool),
  (ARRAY['White','Blue','Black','Beige','Grey','Maroon'])[1 + (st.rn % 6)],
  (ARRAY['Raymond','Allen Solly','Fabindia','Zara','Manyavar','Peter England'])[1 + (st.rn % 6)],
  (ARRAY['S','M','L','XL','38','40'])[1 + (st.rn % 6)],
  st.stage, 'warehouse', (SELECT warehouse_id FROM anchor),
  now() - (((st.rn * 7) % 175 + 2) || ' minutes')::interval,
  (st.rn % 11 = 0), (st.rn % 5 = 0), (st.rn % 17 = 0), 0,
  '{}'::jsonb,
  now() - (((st.rn * 13) % 600 + 60) || ' minutes')::interval,
  now() - (((st.rn * 7)  % 175 + 2)  || ' minutes')::interval,
  1, 'active'
FROM staged st
ON CONFLICT (id) DO NOTHING;

COMMIT;

-- Report
SELECT current_stage, count(*)
FROM order_lifecycle.garments
GROUP BY current_stage
ORDER BY count(*) DESC;
