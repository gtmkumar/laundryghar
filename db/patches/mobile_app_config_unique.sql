-- ─────────────────────────────────────────────────────────────────────────────
-- Patch: mobile_app_config_unique.sql
-- Purpose:
--   1. Deduplicate any rows sharing (brand_id, app_type, platform, config_key),
--      keeping the row with the lowest id (earliest insert).
--   2. Ensure a named unique index exists on that natural key (guards future seeds).
--   3. Seed app_settings rows for app_type='rider' (ios + android) for every brand.
--
-- Idempotent: safe to run multiple times.
-- Run as superuser (postgres) so RLS does not block writes.
-- ─────────────────────────────────────────────────────────────────────────────

-- ── Step 1: Audit duplicates before deduplication ────────────────────────────
DO $$
DECLARE
    v_dup_count integer;
BEGIN
    SELECT COUNT(*)
    INTO   v_dup_count
    FROM (
        SELECT brand_id, app_type, platform, config_key, COUNT(*) AS cnt
        FROM   engagement_cms.mobile_app_config
        GROUP  BY brand_id, app_type, platform, config_key
        HAVING COUNT(*) > 1
    ) AS dupes;

    IF v_dup_count = 0 THEN
        RAISE NOTICE 'mobile_app_config: no duplicate (brand_id, app_type, platform, config_key) rows found — nothing to deduplicate.';
    ELSE
        RAISE NOTICE 'mobile_app_config: % duplicate group(s) found — deduplicating now.', v_dup_count;
    END IF;
END $$;

-- ── Step 2: Delete duplicate rows, keeping the earliest-inserted id per natural key ──
-- uuid has no ordering operators (no MIN/MAX), so we use DISTINCT ON with
-- created_at ASC (fallback: ctid for full tie-breaking) to identify the keeper.
DELETE FROM engagement_cms.mobile_app_config
WHERE id NOT IN (
    SELECT DISTINCT ON (brand_id, app_type, platform, config_key) id
    FROM   engagement_cms.mobile_app_config
    ORDER  BY brand_id, app_type, platform, config_key,
              created_at ASC, ctid ASC
);

-- ── Step 3: Create named unique index if not already present ─────────────────
-- Note: a UNIQUE CONSTRAINT (mobile_app_config_brand_id_app_type_platform_config_key_key)
-- already exists on these columns and backs an identically-scoped unique index.
-- The named index below is the canonical reference for this patch series.
-- IF NOT EXISTS prevents an error if the patch is re-run.
CREATE UNIQUE INDEX IF NOT EXISTS uix_mobile_app_config_brand_app_platform_key
    ON engagement_cms.mobile_app_config (brand_id, app_type, platform, config_key);

-- ── Step 4: Seed rider app_settings for every brand ──────────────────────────
-- Inserts ios + android rows for every brand in tenancy_org.brands.
-- ON CONFLICT DO NOTHING is safe because the unique constraint (or the index
-- above) prevents duplicate (brand_id, app_type, platform, config_key) rows.
INSERT INTO engagement_cms.mobile_app_config
    (brand_id, app_type, platform, config_key, config_value,
     description, is_active, status, created_at, updated_at)
SELECT
    b.id                                                        AS brand_id,
    'rider'                                                     AS app_type,
    p.platform                                                  AS platform,
    'app_settings'                                              AS config_key,
    '{"min_version":"1.0.0","force_update_version":"0.9.0","maintenance_mode":false}'::jsonb
                                                                AS config_value,
    'Rider app version gate and maintenance-mode flag.'         AS description,
    true                                                        AS is_active,
    'active'                                                    AS status,
    now()                                                       AS created_at,
    now()                                                       AS updated_at
FROM tenancy_org.brands          b
CROSS JOIN (VALUES ('ios'), ('android')) AS p(platform)
ON CONFLICT (brand_id, app_type, platform, config_key) DO NOTHING;

-- ── Verification ─────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_dup_count   integer;
    v_rider_count integer;
BEGIN
    SELECT COUNT(*)
    INTO   v_dup_count
    FROM (
        SELECT brand_id, app_type, platform, config_key, COUNT(*) AS cnt
        FROM   engagement_cms.mobile_app_config
        GROUP  BY brand_id, app_type, platform, config_key
        HAVING COUNT(*) > 1
    ) AS dupes;

    SELECT COUNT(*)
    INTO   v_rider_count
    FROM   engagement_cms.mobile_app_config
    WHERE  app_type = 'rider' AND config_key = 'app_settings';

    IF v_dup_count > 0 THEN
        RAISE EXCEPTION 'Deduplication failed — % duplicate group(s) still exist.', v_dup_count;
    END IF;

    RAISE NOTICE 'Patch complete. Duplicates remaining: %. Rider app_settings rows: %.', v_dup_count, v_rider_count;
END $$;
