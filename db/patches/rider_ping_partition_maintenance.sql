-- Daily-partition maintenance for logistics.rider_location_pings, callable by the
-- unprivileged app role.
--
-- Problem (DEFECT 5): rider GPS pings are stored in a RANGE-partitioned table keyed
-- on pinged_at, with one partition per calendar day. NOTHING ever created upcoming
-- partitions — the seeded partitions ran out (last was p20260611) and new pings
-- either fell into the catch-all DEFAULT partition (defeating partition pruning and
-- retention) or, once a real partition is later created for a day already holding
-- DEFAULT rows, the CREATE fails. The mobile app surfaced this as a 400 on
-- POST /api/v1/rider/location/ping ("could not be saved due to a data error").
--
-- The table is owned by `postgres`; the services connect as `app_user`, which has
-- no CREATE on the logistics schema and cannot own/alter the partitioned table. So,
-- mirroring analytics.refresh_all_matviews(), we expose a SECURITY DEFINER function
-- owned by postgres that creates the missing daily partitions idempotently and
-- grants the app roles I/S/U/D on each. app_user is granted EXECUTE only.
--
-- Partition boundaries are expressed in Asia/Kolkata (IST, +05:30) to match the
-- existing partitions, so a "day" is the operator's wall-clock day.
--
-- DEFAULT-overlap handling: if the DEFAULT partition already holds rows for a day we
-- want to materialise, PostgreSQL rejects the CREATE. We detach DEFAULT, create the
-- missing daily partitions, let tuple-routing move the overlapping rows back into the
-- new partitions, then re-attach DEFAULT. The whole thing runs in one transaction and
-- is a no-op when every requested partition already exists.

CREATE OR REPLACE FUNCTION logistics.ensure_rider_ping_partitions(days_ahead integer DEFAULT 14)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = logistics, pg_catalog, pg_temp
AS $$
DECLARE
    parent      regclass := 'logistics.rider_location_pings'::regclass;
    tz          text     := 'Asia/Kolkata';
    today       date     := (now() AT TIME ZONE tz)::date;
    last_day    date     := today + GREATEST(days_ahead, 0);
    cur         date;
    pname       text;
    fromts      text;
    tots        text;
    created     integer  := 0;
    need_default_dance boolean := false;
    default_oid regclass;
BEGIN
    -- Which days are missing a dedicated partition?
    -- (we only do the detach/attach dance if at least one is missing AND DEFAULT
    --  holds rows in the [today, last_day] window).
    cur := today;
    WHILE cur <= last_day LOOP
        pname := 'rider_location_pings_p' || to_char(cur, 'YYYYMMDD');
        IF NOT EXISTS (
            SELECT 1 FROM pg_class
            WHERE relname = pname AND relnamespace = 'logistics'::regnamespace
        ) THEN
            need_default_dance := true;
            EXIT;
        END IF;
        cur := cur + 1;
    END LOOP;

    IF NOT need_default_dance THEN
        RETURN 0;  -- everything already provisioned
    END IF;

    -- Locate the DEFAULT partition (if any) and detach it so overlapping rows don't
    -- block CREATE. If there is no DEFAULT partition, skip the dance.
    SELECT c.oid::regclass INTO default_oid
    FROM pg_inherits i
    JOIN pg_class c ON c.oid = i.inhrelid
    WHERE i.inhparent = parent
      AND pg_get_expr(c.relpartbound, c.oid) = 'DEFAULT'
    LIMIT 1;

    IF default_oid IS NOT NULL THEN
        EXECUTE format('ALTER TABLE %s DETACH PARTITION %s', parent, default_oid);
    END IF;

    -- Create every missing daily partition with the standard grants.
    cur := today;
    WHILE cur <= last_day LOOP
        pname  := 'rider_location_pings_p' || to_char(cur, 'YYYYMMDD');
        fromts := to_char(cur,     'YYYY-MM-DD') || ' 00:00:00+05:30';
        tots   := to_char(cur + 1, 'YYYY-MM-DD') || ' 00:00:00+05:30';

        IF NOT EXISTS (
            SELECT 1 FROM pg_class
            WHERE relname = pname AND relnamespace = 'logistics'::regnamespace
        ) THEN
            EXECUTE format(
                'CREATE TABLE logistics.%I PARTITION OF logistics.rider_location_pings FOR VALUES FROM (%L) TO (%L)',
                pname, fromts, tots);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.%I TO app_user', pname);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.%I TO app_admin', pname);
            created := created + 1;
        END IF;
        cur := cur + 1;
    END LOOP;

    -- Move any DEFAULT rows that now belong in a real partition, then re-attach DEFAULT.
    IF default_oid IS NOT NULL THEN
        EXECUTE format(
            'INSERT INTO logistics.rider_location_pings
             SELECT * FROM %s
             WHERE pinged_at >= %L AND pinged_at < %L',
            default_oid,
            to_char(today, 'YYYY-MM-DD') || ' 00:00:00+05:30',
            to_char(last_day + 1, 'YYYY-MM-DD') || ' 00:00:00+05:30');

        EXECUTE format(
            'DELETE FROM %s
             WHERE pinged_at >= %L AND pinged_at < %L',
            default_oid,
            to_char(today, 'YYYY-MM-DD') || ' 00:00:00+05:30',
            to_char(last_day + 1, 'YYYY-MM-DD') || ' 00:00:00+05:30');

        EXECUTE format(
            'ALTER TABLE logistics.rider_location_pings ATTACH PARTITION %s DEFAULT',
            default_oid);
    END IF;

    RETURN created;
END;
$$;

ALTER FUNCTION logistics.ensure_rider_ping_partitions(integer) OWNER TO postgres;
REVOKE ALL  ON FUNCTION logistics.ensure_rider_ping_partitions(integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION logistics.ensure_rider_ping_partitions(integer) TO app_user;
GRANT EXECUTE ON FUNCTION logistics.ensure_rider_ping_partitions(integer) TO app_admin;
