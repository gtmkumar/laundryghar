-- 0009_brand_status_lookup — rollback: must exactly undo the .up.sql
--
-- Dropping this function makes the suspension gate blind again: BrandStatusStore falls back to
-- returning null, which fails OPEN, so suspended brands resume trading. Roll back only together
-- with the middleware that calls it.

DROP FUNCTION IF EXISTS kernel.brand_status(uuid);
