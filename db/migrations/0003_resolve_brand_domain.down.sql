-- 0003_resolve_brand_domain — rollback: must exactly undo the .up.sql
--
-- Dropping this function makes every custom domain stop resolving: the resolver falls back to
-- the X-Brand-Id header / ?brandCode= / default-brand chain, so requests to theirbrand.com are
-- served as the DEFAULT brand rather than the provider's. Roll back only together with, or
-- before, the application code that calls it.

DROP FUNCTION IF EXISTS kernel.resolve_brand_domain(text);
