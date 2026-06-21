-- =============================================================================
-- permission_canonical_module.sql  (canonical registry — gap #6)
--   Adds identity_access.permissions.module_key: the SINGLE navigator module that
--   owns each permission. Removes the tag-overlap ambiguity that the matrix's
--   permission_modules[] reverse-mapping created (a tag like 'orders' lived in both
--   the orders and pos modules), and fixes orphans (pos.* permissions were owned by
--   no module). Entitlement (token + nav) keys off module_key; the matrix UI keeps
--   using permission_modules unchanged.
--   Additive + idempotent. Run as superuser (postgres):
--     PGPASSWORD=postgres psql -h localhost -U postgres -d laundry_ghar_db \
--       -f db/patches/permission_canonical_module.sql
-- =============================================================================

BEGIN;

ALTER TABLE identity_access.permissions
    ADD COLUMN IF NOT EXISTS module_key varchar;

-- (a) Exact key match wins: a permission whose module tag equals a module key is owned
--     by that module. Resolves every overlap toward the dedicated module (orders→orders,
--     analytics→analytics, stores/cms/royalty→themselves, promotions→promotions) and
--     fixes pos.* (tag 'pos' → the pos module, previously orphaned).
UPDATE identity_access.permissions p
SET module_key = p.module
WHERE p.module_key IS NULL
  AND EXISTS (SELECT 1 FROM identity_access.modules m WHERE m.key = p.module);

-- (b) Otherwise own it via the module that lists the tag in permission_modules,
--     preferring a dedicated (non-aggregator) module, then lowest nav_order.
UPDATE identity_access.permissions p
SET module_key = sub.key
FROM (
    SELECT DISTINCT ON (tag) tag, key
    FROM (
        SELECT m.key, m.nav_order,
               unnest(m.permission_modules) AS tag,
               (m.key IN ('settings','dashboard')) AS is_aggregator
        FROM identity_access.modules m
    ) x
    ORDER BY tag, is_aggregator ASC, nav_order ASC
) sub
WHERE p.module_key IS NULL AND sub.tag = p.module;

COMMIT;

-- Report: coverage + any remaining orphans (kept = always allowed by the entitlement filter).
SELECT 'mapped'  AS bucket, count(*) FROM identity_access.permissions WHERE module_key IS NOT NULL
UNION ALL
SELECT 'orphans', count(*) FROM identity_access.permissions WHERE module_key IS NULL;

SELECT module AS orphan_tag, count(*) AS perms
FROM identity_access.permissions WHERE module_key IS NULL GROUP BY 1 ORDER BY 1;
