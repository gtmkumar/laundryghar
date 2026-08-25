-- 0017_onboarding_progress — rollback

DROP FUNCTION IF EXISTS kernel.ensure_brand_subdomain(uuid, text);
DROP FUNCTION IF EXISTS kernel.brand_onboarding_facts(uuid);
DROP TABLE IF EXISTS tenancy_org.onboarding_progress;
