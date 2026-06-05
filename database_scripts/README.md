# Laundry Ghar — schema split by bounded context

`SCHEMA_FULL.sql` (92 tables + 5 MVs, 3 763 lines) split into **11 files** matching
the wave-based bounded-context plan for the multi-agent Claude Code build.

## File layout

| # | File | Wave | BC | Tables | MVs | Owning agent |
|---|------|------|----|--------|-----|--------------|
| 00 | `00_kernel.sql`               | 0 | BC-0 | 4  |   | `agent/foundation` |
| 01 | `01_bc1_tenancy_org.sql`      | 0 | BC-1 | 10 |   | `agent/foundation` |
| 02 | `02_bc2_identity_access.sql`  | 0 | BC-2 | 11 |   | `agent/foundation` |
| 03 | `03_bc3_customer_catalog.sql` | 1 | BC-3 | 14 |   | `agent/customer-catalog` |
| 04 | `04_bc4_order_lifecycle.sql`  | 1 | BC-4 | 20 |   | `agent/order-lifecycle` |
| 05 | `05_bc5_logistics.sql`        | 1 | BC-5 |  4 |   | `agent/logistics` |
| 06 | `06_bc6_commerce.sql`         | 1 | BC-6 | 13 |   | `agent/commerce` |
| 07 | `07_bc7_finance_royalty.sql`  | 1 | BC-7 |  8 |   | `agent/finance-royalty` |
| 08 | `08_bc8_engagement_cms.sql`   | 2 | BC-8 |  8 |   | `agent/integrator` |
| 09 | `09_bc9_analytics.sql`        | 2 | BC-9 |  0 | 5 | `agent/integrator` |
| 99 | `99_cross_cutting.sql`        | 2 | —    |  0 |   | `agent/integrator` |
|   | **Total**                     |   |      | **92** | **5** |  |

Each file's header lists its exact dependency chain (`-- Apply after: …`).

## Apply order

⚠️ **Apply order is FK-dependency-correct, NOT lexical.**
`00_kernel.sql` declares columns referencing `brands`, `franchises`, `stores`
(all defined in `01_bc1_tenancy_org.sql`), so **01 must run before 00**.
`02_bc2_identity_access.sql` likewise references `brands` from 01.

Correct order:

```
01 → 02 → 00 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 99
```

Use `apply_all.sh` (already encodes this order) for a single-schema build,
or `apply_schemas.sh` for the per-bounded-context schema layout that puts
each file's tables in its own PostgreSQL schema (`tenancy_org`,
`identity_access`, `kernel`, `customer_catalog`, …).

## Parallel build plan (Claude Code orchestrator)

```
Wave 0   (sequential, 1 agent)         00 → 01 → 02
            │
            ▼
Wave 1   (5 parallel agents, isolated branches)
            ├─ agent/customer-catalog   → 03
            ├─ agent/order-lifecycle    → 04
            ├─ agent/logistics          → 05
            ├─ agent/commerce           → 06
            └─ agent/finance-royalty    → 07
            │
            ▼
Wave 2   (sequential, 1 integrator agent)   08 → 09 → 99
```

Wave-1 branches only reference tables defined in Wave-0 files. Zero
cross-branch FK creation — clean merges by construction.

## Why this split, not the original 13 sections

1. **Wave-0 closure on heavy FK targets.** `brands` (31 inbound FKs),
   `users` (12), `customers` (10) all land in Wave 0/early-Wave-1 — every
   downstream BC sees its FK targets already created.
2. **Order spine kept intact.** §5 + §6 + §7 share `garments.order_id`,
   `garments.warehouse_batch_id`, `process_logs.garment_id`. Splitting
   them across agents would create the worst kind of merge conflict
   (FK + business invariant). One BC, one branch, one transaction
   boundary.
3. **Catalog ↔ Customer cohesion.** §3 + §4 don't FK each other but are
   both Wave-1 prerequisites for orders and share zero coupling with §5+.
   Merging gives one agent ownership of "everything the mobile app reads
   before checkout."
4. **CMS depends on every domain.** §12 outbox + notifications log
   events from every BC, so it moves to Wave 2.

## Known issues carried from source (not fixed in split)

- **`pg_partman` extension created twice** in `00_kernel.sql` (was lines 32
  + 38 of source). Line 32 (`CREATE EXTENSION pg_partman` in `public`) is a
  legacy leftover — pg_partman should only live in the `partman` schema.
  Recommended fix: delete the line 32 `CREATE EXTENSION` and keep only
  the `partman`-schema version. Not changed here to keep the split a
  faithful 1:1 of `SCHEMA_FULL.sql`.

## Verification

```
$ grep -c "^CREATE TABLE "             *.sql | grep -v ':0$'
01_bc1_tenancy_org.sql:10
02_bc2_identity_access.sql:11
03_bc3_customer_catalog.sql:14
04_bc4_order_lifecycle.sql:20
05_bc5_logistics.sql:4
06_bc6_commerce.sql:13
07_bc7_finance_royalty.sql:8
08_bc8_engagement_cms.sql:8
00_kernel.sql:4
# total = 92 ✓

$ grep -c "^CREATE MATERIALIZED VIEW " 09_bc9_analytics.sql
5  ✓
```
