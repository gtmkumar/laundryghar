-- 0011_vertical_templates — rollback: must exactly undo the .up.sql
--
-- Removes the launchable-template catalogue. Adding a vertical reverts to hand-writing a SQL patch,
-- the way `salon` originally arrived. No brand data is touched: a template is only ever read at
-- signup, and what it produced (features, catalogue rows) belongs to the brand thereafter.

DROP TABLE IF EXISTS identity_access.vertical_templates;
