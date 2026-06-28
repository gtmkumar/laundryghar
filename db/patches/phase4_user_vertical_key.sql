-- =============================================================================
-- db/patches/phase4_user_vertical_key.sql
--
-- Multi-vertical · denormalise a user's "home" vertical onto identity_access.users.
--
--   * Adds identity_access.users.vertical_key (nullable = platform / cross-vertical, no single home).
--   * Backfills it from each user's PRIMARY membership's brand vertical
--     (brand / franchise / store / warehouse scope → brand → tenancy_org.brands.vertical_key).
--
-- The column is a convenience hint kept in sync by GrantMembership whenever the primary membership
-- changes; the authoritative vertical for any action remains the membership's brand. A platform-scoped
-- primary (or no primary) leaves vertical_key NULL.
--
-- Non-destructive + idempotent. RUN as postgres:
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/phase4_user_vertical_key.sql
-- =============================================================================

BEGIN;

-- 1. vertical_key column on users (null = no single home vertical) ------------
ALTER TABLE identity_access.users
    ADD COLUMN IF NOT EXISTS vertical_key VARCHAR(20);

DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'users_vertical_key_check') THEN
        ALTER TABLE identity_access.users
            ADD CONSTRAINT users_vertical_key_check
            CHECK (vertical_key IS NULL OR vertical_key IN ('laundry','salon','logistics'));
    END IF;
END
$chk$;

-- 2. Backfill from the primary membership's brand vertical --------------------
--    DISTINCT ON (user_id): one row per user even if stale duplicate primaries exist.
UPDATE identity_access.users u
   SET vertical_key = sub.vertical_key,
       updated_at   = now()
FROM (
    SELECT DISTINCT ON (m.user_id) m.user_id, b.vertical_key
    FROM identity_access.user_scope_memberships m
    LEFT JOIN tenancy_org.franchises f ON m.scope_type = 'franchise' AND f.id = m.scope_id
    LEFT JOIN tenancy_org.stores     s ON m.scope_type = 'store'     AND s.id = m.scope_id
    LEFT JOIN tenancy_org.warehouses w ON m.scope_type = 'warehouse' AND w.id = m.scope_id
    JOIN tenancy_org.brands b ON b.id = CASE m.scope_type
            WHEN 'brand'     THEN m.scope_id
            WHEN 'franchise' THEN f.brand_id
            WHEN 'store'     THEN s.brand_id
            WHEN 'warehouse' THEN w.brand_id
            ELSE NULL
        END
    WHERE m.is_primary AND m.revoked_at IS NULL
    ORDER BY m.user_id, m.granted_at DESC
) sub
WHERE u.id = sub.user_id
  AND u.vertical_key IS DISTINCT FROM sub.vertical_key;

-- 3. Verification gate -------------------------------------------------------
DO $verify$
DECLARE has_col int; bad int;
BEGIN
    SELECT count(*) INTO has_col FROM information_schema.columns
     WHERE table_schema='identity_access' AND table_name='users' AND column_name='vertical_key';
    IF has_col <> 1 THEN RAISE EXCEPTION 'user_vertical_key: users.vertical_key column missing'; END IF;

    SELECT count(*) INTO bad FROM identity_access.users
     WHERE vertical_key IS NOT NULL AND vertical_key NOT IN ('laundry','salon','logistics');
    IF bad <> 0 THEN RAISE EXCEPTION 'user_vertical_key: % users have an invalid vertical_key', bad; END IF;

    RAISE NOTICE 'user_vertical_key verification passed: users.vertical_key added + backfilled from primary membership brand.';
END
$verify$;

COMMIT;

SELECT 'phase4_user_vertical_key.sql applied successfully.' AS result;
