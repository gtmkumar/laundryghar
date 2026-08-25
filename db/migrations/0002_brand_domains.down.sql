-- 0002_brand_domains — rollback: must exactly undo the .up.sql
--
-- DESTRUCTIVE: dropping the table discards every custom domain a provider has registered and
-- verified. Any host still pointing a CNAME at us stops resolving to its brand the moment this
-- runs. Check before rolling back:
--   SELECT b.code, d.domain, d.verified_at FROM tenancy_org.brand_domains d
--     JOIN tenancy_org.brands b ON b.id = d.brand_id ORDER BY b.code;
--
-- The trigger, policy, grants and indexes are all owned by the table and disappear with it;
-- they are dropped explicitly first so the rollback is readable and order-independent.

DROP TRIGGER IF EXISTS trg_brand_domains_set_updated_at ON tenancy_org.brand_domains;
DROP POLICY  IF EXISTS rls_brand ON tenancy_org.brand_domains;

DROP INDEX IF EXISTS tenancy_org.idx_brand_domains_brand;
DROP INDEX IF EXISTS tenancy_org.idx_brand_domains_verified;
DROP INDEX IF EXISTS tenancy_org.idx_brand_domains_one_primary;

DROP TABLE IF EXISTS tenancy_org.brand_domains;
