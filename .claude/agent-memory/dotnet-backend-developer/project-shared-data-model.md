---
name: project-shared-data-model
description: Context for the laundryghar.SharedDataModel library — DB-first EF Core 10 mapping of the live PostgreSQL schema, package versions, design decisions, and RLS interceptor approach.
metadata:
  type: project
---

The SharedDataModel library is a **database-first** EF Core mapping of the live PostgreSQL `laundry_ghar_db`. Migrations must NEVER be run against the live DB — the DB schema is canonical.

**Why:** The live DB has extra columns (status, created_by, created_at etc.) not in early markdown docs; introspecting via `\d+` was the only reliable source of truth.

**Package versions that worked (as of 2026-06-05):**
- Npgsql.EntityFrameworkCore.PostgreSQL: 10.0.2 (latest stable; 10.0.3 does not exist)
- Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite: 10.0.2
- Microsoft.EntityFrameworkCore: 10.0.4 (Npgsql 10.0.2 requires >= 10.0.4; original 10.0.0 caused NU1605 downgrade error)
- Microsoft.EntityFrameworkCore.Relational: 10.0.4
- Microsoft.EntityFrameworkCore.Design: 10.0.4

**How to apply:** Always check Npgsql's transitive EF Core minimum when targeting a new Npgsql version to avoid NU1605 downgrade errors.

**RLS interceptor pattern:**
`RlsConnectionInterceptor` (DbConnectionInterceptor) fires on ConnectionOpened(Async) and issues `SELECT set_config(...)` for app.current_brand_id, current_franchise_id, current_store_id, current_user_id, bypass_rls. Empty string = unset (RLS policies treat empty as no tenant). Registered as Transient so it picks up scoped ICurrentTenant per-request.

**Soft-delete query filters applied to:** Platform, Brand, Territory, FranchiseAgreement, Franchise, Store, Warehouse, User, Role, FileAttachment (tenancy/identity/kernel). customer_catalog adds: Customer, CustomerAddress, ServiceCategory, Service, FabricType, ItemGroup, Item, ItemVariant, PriceList, AddOn. All others have no deleted_at. Use IgnoreQueryFilters() to see soft-deleted rows.

**Composite PK on audit_logs:** `(Id, OccurredAt)` — required by PostgreSQL range partitioning. EF treats it as a normal table; inserts route automatically to monthly partitions.

**Cross-BC FK policy:** FK columns pointing outside mapped BCs are kept as scalar `Guid?` with no navigation property. Exception: when the target BC is already in scope (e.g., identity_access.users referenced from customer_catalog), navigation properties ARE added.

**Geography types:** Stores and Warehouses use `geography(Point,4326)` -> `NetTopologySuite.Geometries.Point`. Territories use `geography(MultiPolygon,4326)` -> `MultiPolygon`. customer_addresses also has `geography(Point,4326)`. Requires `.UseNetTopologySuite()` on the Npgsql options.

**uuid[] / text[] arrays in FeatureFlag and AddOn:** Mapped as `Guid[]?`/`Guid[]` and `string[]?`/`string[]` with `.HasColumnType("uuid[]")` / `.HasColumnType("text[]")`. Npgsql handles natively.

**customer_catalog BC surprises (2026-06-05):** dpdp_consents.geo_location is `varchar(100)` despite its name — NOT a geography type; mapped as plain string. items.search_tokens is `tsvector` (DB-managed); mapped as string with `.ValueGeneratedOnAddOrUpdate()`. customer_addresses, fabric_types, item_groups, item_variants all have deleted_at but no version column — do NOT implement IAuditableEntity. PriceList has both `version` (row-version/EF) and `version_number` (business integer) as separate columns.

**order_lifecycle BC — partitioned tables (2026-06-05):**
- `orders` partitioned by RANGE(created_at) — composite PK `(id, created_at)`. Child tables (order_items, order_addons, order_status_history, order_notes, garments, garment_inspections, quality_checks, delivery_slot_bookings, delivery_assignments) all carry `order_created_at` column for the composite FK. Configure with `HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt }).HasPrincipalKey(o => new { o.Id, o.CreatedAt })`.
- `process_logs` partitioned by RANGE(occurred_at) — composite PK `(id, occurred_at)`. No child composite FKs.
- `orders.amount_due` is GENERATED ALWAYS AS (grand_total - amount_paid) STORED — mapped with `.ValueGeneratedOnAddOrUpdate()`, never written by app.
- Composite FK on nullable columns (delivery_assignments, delivery_slot_bookings, garment_inspections, quality_checks, pickup_requests, order_notes FK to orders when nullable) → mapped as **scalar columns only, no EF navigation**, because EF Core doesn't model nullable composite FK navigations cleanly.
- geography columns confirmed real in: order_status_history.location, delivery_assignments.geo_location, garment_inspections.geo_location → all `geography(Point,4326)` → `NetTopologySuite.Geometries.Point`.
- Cross-BC FKs to logistics.riders (pickup_rider_id, delivery_rider_id on orders; rider_id on delivery_assignments) and commerce.* (coupon_id, package_id, customer_package_id on orders) → scalar Guid? only, no navigation.
