---
name: project-logistics-bc
description: BC-5 logistics schema + laundryghar.Logistics service — riders, rider_assignments, rider_capacity_config, rider_location_pings — key decisions, service architecture, and RiderOnly auth lane
metadata:
  type: project
---

BC-5 `logistics` schema added to `laundryghar.SharedDataModel` (4 tables, 6 enums, 4 configs, 4 DbSets).
`laundryghar.Logistics` service built on port 5004, added to laundryghar.slnx.

**Why:** Logistics bounded context maps the rider/delivery layer; keeps cross-BC consistency with prior BCs in the shared library.

**How to apply:** Use this as reference for any future logistics service work or schema changes.

## Interface application (verified against \d+)

- `Rider`: implements only `ISoftDeletable` (has `deleted_at`). Has `created_at, updated_at, created_by, updated_by` but NO `version` column — does NOT implement `IAuditableEntity`.
- `RiderAssignment`: plain entity. Has `created_at, updated_at, created_by` — NO `updated_by`, NO `version`, NO `deleted_at`.
- `RiderCapacityConfig`: plain entity. Has `created_at, updated_at, created_by, updated_by` — NO `version`, NO `deleted_at`.
- `RiderLocationPing`: plain entity. Has `created_at, created_by` only — NO `updated_at`, NO `version`, NO `deleted_at`.

## Partitioned table: rider_location_pings

- Partitioned RANGE on `pinged_at` (daily partitions).
- Composite PK: `(id, pinged_at)` — same pattern as `orders`/`process_logs`.
- EF config: `b.HasKey(e => new { e.Id, e.PingedAt })` with `ValueGeneratedOnAdd()` on Id only.

## Geography columns (GEOGRAPHY(Point,4326))

- `riders.last_known_location` — nullable `Point?`
- `rider_location_pings.location` — NOT NULL `Point` (required)
- Both use `HasColumnType("geography(Point,4326)")` — same pattern as Store/Territory/Warehouse/DeliveryAssignment.

## Cross-BC FKs

- `riders.user_id` → `identity_access.users` — ON DELETE CASCADE. Nav property `User` mapped (User entity is in this library).
- `riders.brand_id`, `franchise_id` → `tenancy_org` — ON DELETE RESTRICT. Full nav properties.
- `riders.primary_store_id` → `tenancy_org.stores` — NO explicit ON DELETE in DB (defaults to NO ACTION) — mapped as `DeleteBehavior.NoAction`.
- `rider_assignments.rider_id` → `logistics.riders` — ON DELETE CASCADE. Within-BC nav.
- `rider_location_pings.current_assignment_id` → `logistics.rider_assignments` — ON DELETE SET NULL. Nav mapped.

## Enums (static const string classes)

- `RiderStatus`: active, suspended, terminated, on_leave
- `RiderEmploymentType`: employee, contractor, gig, outsourced
- `RiderVehicleType`: two_wheeler, three_wheeler, four_wheeler, cycle, foot
- `RiderKycStatus`: pending, submitted, verified, rejected, expired
- `RiderAssignmentStatus`: scheduled, active, on_break, completed, cancelled, no_show
- `RiderCapacityConfigStatus`: active, inactive, archived

## Column surprises

- `rider_capacity_config.day_of_week` is `smallint` (not an enum/varchar) with CHECK 0–6; mapped as `short?`.
- `rider_capacity_config` has NO unique constraint — no index configuration needed beyond FKs.
- `rider_assignments` has no `updated_by` column (only `created_by`), unlike most other audit columns.
- `rider_location_pings.metadata` is nullable jsonb (unlike `riders.metadata` which is NOT NULL jsonb).
- `rider_location_pings` has no `activity_type` CHECK constraint — it's a free-form varchar(20).

## laundryghar.Logistics service decisions

**RiderOnly auth lane:** `RiderOnlyRequirement` + `RiderOnlyHandler` checks `token_use=user` AND `user_type=rider`. Added to the `PermissionPolicyProvider` alongside `permission:<code>` policies. Rider tokens use the standard system JWT (not a special token type) — differentiated purely by `user_type` claim, not `token_use`. Admin tokens fail RiderOnly; rider tokens fail permission: policies.

**Brand defense-in-depth (admin lane):** Services run as postgres superuser so RLS is bypassed. Every admin handler calls `_user.RequireBrandId()` and filters `WHERE brand_id = brandId`. Cross-brand GET by ID returns 404; list returns empty.

**Rider self-service brand resolution:** Rider self-service does NOT use `RequireBrandId()` (which handles X-Brand-Id for platform_admin). Instead, brand_id is read directly from the `brand_id` JWT claim — no header needed; the rider's brand is already embedded in their token.

**Batch location ping:** `POST /api/v1/rider/location/ping` accepts `List<LocationPingInput>`. Handler resolves rider ID from users.id (sub) to logistics.riders.id, then bulk-inserts pings. Also updates riders.last_known_location from the most recent ping. Uses `NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326)` to create `Point(longitude, latitude)`.

**Assignment self-filter 404:** `UpdateMyAssignmentStatusCommand` filters on `rider_id = riderId` — if the assignment exists but belongs to a different rider, the query returns null → endpoint returns 404. This prevents information leakage (attacker learns nothing about other riders' assignments).

**BC-5 permissions seeded:** rider.read, rider.manage, rider.assignment.read, rider.assignment.manage, rider.capacity.manage. Grants: platform_admin auto-all; brand_admin all 5; store_admin reader+assignment r/m; warehouse_supervisor reader+assignment.manage+capacity.manage; franchise_owner/auditor read-only (rider.read + rider.assignment.read).

**Cleanup note:** rider_location_pings is partitioned — DELETE works without needing to specify partition. Children (pings, capacity, assignments) must be deleted before the rider row itself (FK cascade on rider_id would handle assignments/pings anyway, but explicit ordering is cleaner).
