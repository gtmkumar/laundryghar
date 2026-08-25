-- 0010_vertical_terminology — rollback: must exactly undo the .up.sql
--
-- Clients fall back to their built-in neutral defaults, so the UI reverts to generic wording
-- ("item", "booking", "Location") rather than breaking.

DROP TABLE IF EXISTS identity_access.vertical_terms;
