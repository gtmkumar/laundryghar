-- =============================================================================
-- db/patches/order_number_sequence.sql
--
-- Race-free order_number generation. The app previously derived the running
-- number with COUNT(*) of this year's orders for the store + 1 — two concurrent
-- CreateOrder requests read the same count and mint the SAME order_number,
-- causing duplicates / unique violations under load.
--
-- This replaces that with an atomic per-(brand, store, year) counter:
-- INSERT ... ON CONFLICT DO UPDATE ... RETURNING serialises concurrent callers
-- on the row lock, so each gets a distinct value. The human-readable format
-- (LG-<year>-<storeCode>-<NNNNNN>, six-digit zero-padded, resets per store/year)
-- is preserved.
--
-- Idempotent. RUN as postgres.
-- =============================================================================

-- 1. Per-(brand, store, year) counter table -----------------------------------
CREATE TABLE IF NOT EXISTS order_lifecycle.order_number_sequences (
    brand_id   uuid   NOT NULL,
    store_id   uuid   NOT NULL,
    year       int    NOT NULL,
    last_value bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (brand_id, store_id, year)
);

-- RLS: brand-scoped like every other order_lifecycle table. The counter is
-- always touched inside a request whose tenant context (app.current_brand_id)
-- matches the row's brand_id, so INSERT/UPDATE pass; cross-brand rows are hidden.
ALTER TABLE order_lifecycle.order_number_sequences ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS rls_brand ON order_lifecycle.order_number_sequences;
CREATE POLICY rls_brand ON order_lifecycle.order_number_sequences
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()))
    WITH CHECK (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));

GRANT SELECT, INSERT, UPDATE, DELETE ON order_lifecycle.order_number_sequences TO app_user;

-- 2. Atomic allocator ----------------------------------------------------------
CREATE OR REPLACE FUNCTION order_lifecycle.next_order_number(
    p_brand      uuid,
    p_store      uuid,
    p_store_code text,
    p_year       int
)
RETURNS text
LANGUAGE plpgsql
AS $function$
DECLARE
    v_seq bigint;
BEGIN
    INSERT INTO order_lifecycle.order_number_sequences AS s (brand_id, store_id, year, last_value)
    VALUES (p_brand, p_store, p_year, 1)
    ON CONFLICT (brand_id, store_id, year)
    DO UPDATE SET last_value = s.last_value + 1
    RETURNING s.last_value INTO v_seq;

    RETURN format('LG-%s-%s-%s', p_year, p_store_code, lpad(v_seq::text, 6, '0'));
END
$function$;

GRANT EXECUTE ON FUNCTION order_lifecycle.next_order_number(uuid, uuid, text, int) TO app_user;

SELECT 'order_number_sequence.sql applied successfully.' AS result;
