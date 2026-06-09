-- =============================================================================
-- rider_cod_settlement.sql  (Rider Ops Phase 3)
-- Tracks COD cash a rider collects on delivery and the rider→store settlement
-- (cash handover). Logistics-only source of truth; posting to the finance cash
-- book is a deliberate follow-up. Settlements are admin-recorded (one step).
--
--   delivery_assignments.cod_amount / cod_collected_at  — cash collected per leg
--   delivery_assignments.settlement_id                  — which settlement cleared it
--   logistics.rider_settlements                         — a batch deposit by a rider
--   permission rider.settle                             — gate for recording settlements
--
-- Idempotent. Additive only. Run:
--   PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--     -f db/patches/rider_cod_settlement.sql
-- =============================================================================

BEGIN;

-- ── 1. COD collection columns on the delivery leg ───────────────────────────
ALTER TABLE order_lifecycle.delivery_assignments
    ADD COLUMN IF NOT EXISTS cod_amount       numeric(10,2) NULL,
    ADD COLUMN IF NOT EXISTS cod_collected_at timestamptz   NULL,
    ADD COLUMN IF NOT EXISTS settlement_id    uuid          NULL;  -- scalar link (cross-BC, no FK)

COMMENT ON COLUMN order_lifecycle.delivery_assignments.cod_amount IS
    'Cash the rider collected on this delivery leg (COD). Null = nothing to collect / prepaid.';
COMMENT ON COLUMN order_lifecycle.delivery_assignments.settlement_id IS
    'logistics.rider_settlements.id that cleared this collection (null = outstanding).';

-- Outstanding-cash lookups: collected but not yet settled.
CREATE INDEX IF NOT EXISTS idx_da_cod_outstanding
    ON order_lifecycle.delivery_assignments (brand_id, rider_id)
    WHERE cod_amount IS NOT NULL AND settlement_id IS NULL;

-- ── 2. rider_settlements (a rider's batched cash handover to the store) ──────
CREATE TABLE IF NOT EXISTS logistics.rider_settlements (
    id               uuid PRIMARY KEY,
    brand_id         uuid NOT NULL,
    franchise_id     uuid NOT NULL,
    rider_id         uuid NOT NULL,
    store_id         uuid NULL,                 -- where the cash was deposited (optional)
    total_amount     numeric(12,2) NOT NULL,
    collection_count integer NOT NULL DEFAULT 0,
    reference        text NULL,                 -- deposit slip / txn reference
    status           varchar(20) NOT NULL DEFAULT 'settled'
                       CHECK (status IN ('settled','disputed','reversed')),
    settled_at       timestamptz NOT NULL,
    settled_by       uuid NULL,
    notes            text NULL,
    metadata         jsonb NOT NULL DEFAULT '{}',
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid NULL,
    updated_by       uuid NULL
);

CREATE INDEX IF NOT EXISTS idx_rider_settlements_rider
    ON logistics.rider_settlements (brand_id, rider_id, settled_at DESC);

-- RLS — brand-scoped, mirroring logistics.riders.
ALTER TABLE logistics.rider_settlements ENABLE ROW LEVEL SECURITY;
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policy WHERE polrelid = 'logistics.rider_settlements'::regclass AND polname = 'rls_brand'
    ) THEN
        CREATE POLICY rls_brand ON logistics.rider_settlements
            USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
    END IF;
END $$;

GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.rider_settlements TO app_user;

-- ── 3. permission rider.settle + grants (mirrors rider.verify) ──────────────
INSERT INTO identity_access.permissions
    (id, code, module, action, name, description,
     is_system, requires_scope, risk_level, status, created_at, updated_at)
SELECT gen_random_uuid(), 'rider.settle', 'rider', 'settle',
       'Settle rider COD cash',
       'Record a rider''s COD cash handover (settlement) and clear outstanding collections.',
       true, true, 'high', 'active', now(), now()
WHERE NOT EXISTS (SELECT 1 FROM identity_access.permissions WHERE code = 'rider.settle');

INSERT INTO identity_access.role_permissions
    (id, role_id, permission_id, granted_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, now(), now()
FROM identity_access.roles r
JOIN identity_access.permissions p ON p.code = 'rider.settle'
WHERE r.code IN ('platform_admin','brand_admin','operations_manager','franchise_owner')
  AND r.deleted_at IS NULL AND p.status = 'active'
ON CONFLICT (role_id, permission_id) DO NOTHING;

COMMIT;

-- Quick check
SELECT 'cols' AS what, string_agg(column_name, ', ') AS detail
FROM information_schema.columns
WHERE table_schema='order_lifecycle' AND table_name='delivery_assignments'
  AND column_name IN ('cod_amount','cod_collected_at','settlement_id')
UNION ALL
SELECT 'table', to_regclass('logistics.rider_settlements')::text
UNION ALL
SELECT 'perm', code FROM identity_access.permissions WHERE code='rider.settle';
