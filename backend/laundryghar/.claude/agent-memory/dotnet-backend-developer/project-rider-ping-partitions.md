---
name: project-rider-ping-partitions
description: logistics.rider_location_pings is daily RANGE-partitioned; partition maintenance pattern (SECURITY DEFINER fn) because app_user can't do DDL
metadata:
  type: project
---

`logistics.rider_location_pings` is RANGE-partitioned by `pinged_at`, one partition per IST calendar day (boundaries are `+05:30`). There was NO partition-maintenance job — partitions ran out and pings fell into the catch-all DEFAULT partition (or, once a real partition is later created for a day DEFAULT already holds rows for, the CREATE fails).

**Why this is non-obvious:** the table is owned by `postgres`; services connect as `app_user`, which has NO CREATE on the `logistics` schema and cannot ALTER the partitioned parent. So partition DDL cannot run on the normal request/worker connection.

**How to apply (the chosen pattern, mirrors analytics.refresh_all_matviews):**
- DDL lives in a SECURITY DEFINER function owned by postgres: `logistics.ensure_rider_ping_partitions(days_ahead int)` (db/patches/rider_ping_partition_maintenance.sql). `app_user`/`app_admin` get EXECUTE only. The fn is idempotent, returns count created, and handles the DEFAULT-overlap case by DETACH DEFAULT → create dailies → move rows back → ATTACH DEFAULT.
- A Worker hosted service (`PartitionMaintenanceService`, on by default, `Worker:PartitionMaintenanceEnabled`) calls it on boot + daily, provisioning today..today+14.
- RLS is enforced at the partitioned PARENT (`relrowsecurity=t`); individual partitions have `relrowsecurity=f` but RLS still applies through the parent. New partitions need GRANTs (I/S/U/D to app_user/app_admin) but NOT their own policy.
- The RLS session key is `app.current_brand_id` (NOT `kernel.brand_id`); `kernel.current_brand_id()` reads it. RLS-bypass key is `app.bypass_rls`.
- A 400 "could not be saved due to a data error" on POST /rider/location/ping = an UNKNOWN-SQLSTATE DbUpdateException in ExceptionHandler.cs fallback. A stale/wrong `currentAssignmentId` (FK 23503) also triggers it.
