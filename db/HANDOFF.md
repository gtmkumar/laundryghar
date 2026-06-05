# Laundry Ghar — Database Schema Build & FK Completion · Handoff

**Status:** Per-bounded-context schema layout built and verified against ER diagram. Category A (7 in-file forward-reference FK bugs) fixed in source SQL + applied to live DB. Category B (138-FK completion patch set) generated, dry-run-validated, **not yet applied** — awaiting an apply policy decision.

This document is self-contained — a fresh agent or operator should be able to continue from here without any prior chat history.

---

## 1. Connection details

| Setting | Value |
|---|---|
| Database | `laundry_ghar_db` |
| Host / Port | `localhost` / `5432` |
| User / Password | `postgres` / `postgres` |
| PostgreSQL | 16.14 (Homebrew) |
| Extensions installed | `pgcrypto`, `citext`, `postgis`, `pg_trgm`, `btree_gin`, `pg_stat_statements`, `unaccent`, `pg_partman` 5.4.3 |

Quick verify connection:
```bash
PGPASSWORD=postgres psql -U postgres -p 5432 -h localhost -d laundry_ghar_db -tAc "SELECT current_database();"
```

---

## 2. Repository layout

```
laundryghar/
├── database_scripts/                          # source schema (split by bounded context)
│   ├── README.md                              # BC plan + apply-order WARNING
│   ├── apply_all.sh                           # legacy: builds into ONE schema (public)
│   ├── apply_schemas.sh                       # MAIN: builds per-BC schema layout
│   ├── _00_bootstrap.sql                      # extensions + 10 schema CREATEs
│   ├── 00_kernel.sql                          # BC-0  system_settings, feature_flags, file_attachments, outbox_events
│   ├── 01_bc1_tenancy_org.sql                 # BC-1  platforms, brands, territories, franchises, stores, warehouses
│   ├── 02_bc2_identity_access.sql             # BC-2  users, roles, permissions, audit_logs, …
│   ├── 03_bc3_customer_catalog.sql            # BC-3  customers, services, items, price_lists, …
│   ├── 04_bc4_order_lifecycle.sql             # BC-4  orders, garments, warehouse_batches, … (the largest)
│   ├── 05_bc5_logistics.sql                   # BC-5  riders, rider_assignments, rider_location_pings
│   ├── 06_bc6_commerce.sql                    # BC-6  packages, coupons, payments, wallets
│   ├── 07_bc7_finance_royalty.sql             # BC-7  cash_books, expenses, royalty_invoices
│   ├── 08_bc8_engagement_cms.sql              # BC-8  notifications, banners, mobile_app_config
│   ├── 09_bc9_analytics.sql                   # BC-9  5 materialized views (no tables)
│   ├── 99_cross_cutting.sql                   # ORIGINAL — references `public.*`; superseded by below
│   └── 99_cross_cutting_schema_qualified.sql  # patched: schema-qualified, error-tolerant
└── db/
    ├── HANDOFF.md                             # this file
    ├── tools/
    │   └── generate_fk_patches.py             # deterministic FK-gap parser/emitter (re-runnable)
    └── patches/
        ├── apply_patches.sh                   # idempotent orchestrator, BC dep order
        ├── fk_patch_00_kernel.sql             #   2 actionable FKs
        ├── fk_patch_01_tenancy_org.sql        #   0
        ├── fk_patch_02_identity_access.sql    #   8
        ├── fk_patch_03_customer_catalog.sql   #   5
        ├── fk_patch_04_order_lifecycle.sql    #  63   ← largest; many composite FKs to orders
        ├── fk_patch_05_logistics.sql          #   9
        ├── fk_patch_06_commerce.sql           #  24
        ├── fk_patch_07_finance_royalty.sql    #  20
        ├── fk_patch_08_engagement_cms.sql     #   7
        ├── fk_patch_09_analytics.sql          #   0
        └── fk_patch_review_polymorphic.sql    #  28 entries needing a human decision
```

---

## 3. The 10 PostgreSQL schemas (bounded-context layout)

| Schema | Source file | Logical tables | MVs | Partition children (partman) |
|---|---|---:|---:|---:|
| `kernel` | `00_kernel.sql` | 4 | 0 | 0 |
| `tenancy_org` | `01_bc1_tenancy_org.sql` | 10 | 0 | 0 |
| `identity_access` | `02_bc2_identity_access.sql` | 11 | 0 | 14 |
| `customer_catalog` | `03_bc3_customer_catalog.sql` | 14 | 0 | 0 |
| `order_lifecycle` | `04_bc4_order_lifecycle.sql` | 20 | 0 | 28 |
| `logistics` | `05_bc5_logistics.sql` | 4 | 0 | 16 |
| `commerce` | `06_bc6_commerce.sql` | 13 | 0 | 0 |
| `finance_royalty` | `07_bc7_finance_royalty.sql` | 8 | 0 | 0 |
| `engagement_cms` | `08_bc8_engagement_cms.sql` | 8 | 0 | 8 |
| `analytics` | `09_bc9_analytics.sql` | 0 | 5 | 0 |
| **Total** | | **92** | **5** | **66** |

`99_cross_cutting_schema_qualified.sql` is still applied during build but only calls `partman.create_parent()` for the 5 partitioned tables — it no longer targets a dedicated schema. The previously-empty `cross_cutting` schema was dropped on 2026-05-27.

Verify counts at any time:
```sql
SELECT n.nspname AS schema,
       count(*) FILTER (WHERE c.relkind IN ('r','p') AND c.relispartition=false) AS logical_tables,
       count(*) FILTER (WHERE c.relkind='r' AND c.relispartition=true)            AS partition_children,
       count(*) FILTER (WHERE c.relkind='m')                                       AS mat_views
FROM pg_namespace n LEFT JOIN pg_class c ON c.relnamespace=n.oid
WHERE n.nspname IN ('kernel','tenancy_org','identity_access','customer_catalog',
                    'order_lifecycle','logistics','commerce','finance_royalty',
                    'engagement_cms','analytics')
GROUP BY n.nspname ORDER BY n.nspname;
```

---

## 4. ⚠ Apply order — FK-dependency-correct, NOT lexical

The README in `database_scripts/` originally claimed lexical order (`00 → 01 → 02 → …`) would work. **It will not.** `00_kernel.sql` declares columns that FK `brands`/`franchises`/`stores`, which only exist after `01_bc1_tenancy_org.sql`. The corrected order, used by both `apply_schemas.sh` and `apply_patches.sh`:

```
01 → 02 → 00 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 99
```

`apply_all.sh` and the README have already been updated to reflect this order.

---

## 5. Five tables converted to native partitioned tables by `pg_partman`

`pg_partman` 5.4.3 successfully converted these from regular to partitioned tables during the initial apply (verified in `partman.part_config`):

| Table | Schema | Partition column | Interval | Retention |
|---|---|---|---|---|
| `orders` | `order_lifecycle` | `created_at` | 1 month | — |
| `audit_logs` | `identity_access` | `occurred_at` | 1 month | — |
| `process_logs` | `order_lifecycle` | `occurred_at` | 1 month | — |
| `notifications_log` | `engagement_cms` | `sent_at` | 1 month | — |
| `rider_location_pings` | `logistics` | `pinged_at` | 1 day | 14 days |

### Critical consequence: composite primary keys

PostgreSQL requires **every unique constraint on a partitioned table to include the partitioning column**. So:

- `order_lifecycle.orders` PK is `(id, created_at)` — not just `(id)`
- Same for `audit_logs`, `process_logs`, `notifications_log`, `rider_location_pings`

**Every FK that references one of these tables must therefore be composite.** The schema author anticipated this — every `order_id` column across the codebase has a companion `order_created_at` column (similarly for the other 4 partitioned parents). The generator at `db/tools/generate_fk_patches.py` auto-detects companion timestamp columns and emits composite FKs accordingly.

Example pattern:
```sql
ALTER TABLE commerce.payments
    ADD CONSTRAINT payments_order_id_fkey
    FOREIGN KEY (order_id, order_created_at)
    REFERENCES order_lifecycle.orders(id, created_at)
    ON DELETE RESTRICT;
```

When a FK is added to a partitioned parent table, PostgreSQL automatically propagates the constraint to all existing and future partition children. Verified for the 7 Category A FKs (29 propagated rows for `orders`'s 2 partition FKs across 15 partition children).

---

## 6. Current state of `laundry_ghar_db`

### What has been applied

| Step | What | When |
|---|---|---|
| Bootstrap | `_00_bootstrap.sql` — 8 extensions + 10 schemas | Initial build |
| Source SQL × 10 | `01_…` through `09_…` applied in FK-correct order with per-file `SET search_path` | Initial build |
| Cross-cutting | `99_cross_cutting_schema_qualified.sql` — partman.create_parent() for 5 tables | Initial build |
| **Category A fix** | `user_scope_memberships.role_id → roles(id)` + `idx_usm_role_fk` | After ER-diagram verification |
| **Category A fix** | 7 forward-reference FKs in source SQL files 04/06/07 + same in live DB | Bug fix pass |
| **Category B applied** | 138-FK completion patch set ran via `db/patches/apply_patches.sh` | 2026-05-27 |

All Category A and Category B changes are persisted in both the source SQL and the live DB. The source SQL files themselves are the canonical record — re-running `apply_schemas.sh` against a fresh DB produces an equivalent state (modulo pg_partman conversion happening at the end).

Verification 2026-05-27: 526 parent FKs across the 11 BC schemas. Sampled cross-aggregate FKs (`orders.coupon_id`, `payments.customer_id`, `wallet_transactions.order_id` composite, `expenses.category_id`, `riders.user_id`, etc.) all present. Re-running `apply_patches.sh` is a clean no-op (every `ALTER TABLE` hits `duplicate_object` and is swallowed by the `DO/EXCEPTION` block; every `CREATE INDEX IF NOT EXISTS` reports "relation already exists").

### What is still outstanding

- **3 unresolved entries** in `db/patches/fk_patch_review_polymorphic.sql` needing product input (see §13).
- **RLS coverage**: only 12 of ~140 tables have RLS enabled despite the convention in §11; most tenant-scoped tables (`orders*`, `payments`, `audit_logs*`, `expenses`, `riders`, …) are unprotected. Policies on the 12 enabled tables target `public` role and need a review of their USING/WITH CHECK clauses against the `app.current_brand_id` contract.

### What has been applied since (2026-05-27)

| Step | What | Source |
|---|---|---|
| `updated_at` triggers | `kernel.set_updated_at()` + BEFORE UPDATE trigger on all 61 parent tables with an `updated_at` column (auto-propagates to 14 partition children) | `db/patches/triggers_set_updated_at.sql` |
| partman config fix | Set `infinite_time_partitions=true` on all 5 partman-managed tables so `premake` actually pre-creates partitions; bumped `rider_location_pings` premake 7→14 days | live `partman.part_config` UPDATE |
| partman maintenance | Ran `partman.run_maintenance_proc()` — runway: orders/audit_logs/process_logs/notifications_log → 2026-12-01 (188 d), rider_location_pings → 2026-06-11 (15 d) | live |
| partman scheduler | Shell script + launchd plist template — **not auto-loaded**, opt-in via `launchctl load -w ~/Library/LaunchAgents/com.laundryghar.partman.plist` | `db/tools/run_partman_maintenance.sh`, `db/tools/com.laundryghar.partman.plist` |
| RLS proposal | Two NOLOGIN roles (`app_user`, `app_admin`), six `kernel.current_*()` helper functions, CRUD grants on all 11 BC schemas, **92 inert policies** across four buckets (`rls_brand`, `rls_brand_or_customer`, `rls_user_self`, `rls_admin_only`). **RLS NOT ENABLED on any table.** Pre-existing 12 `*_tenant` policies left intact. Functionally validated end-to-end against `commerce.coupons` (wrong-brand=0, right-brand=1, bypass=1, superuser=1). | `db/patches/rls_proposal.sql` |

Trigger behaviour: if a caller leaves `updated_at` untouched in an `UPDATE`, the trigger bumps it to `now()`; if the caller explicitly sets `updated_at` (e.g. migrations / backfills), the trigger keeps the supplied value. Verified.

partman scheduler is **not yet enabled** — the plist template exists but the user has to opt in. Until enabled, partitions will deplete daily and someone must call `db/tools/run_partman_maintenance.sh` (or run `CALL partman.run_maintenance_proc();` in psql) periodically.

RLS is **not yet enforcing** — policies exist but no table has `ENABLE ROW LEVEL SECURITY` set (except the 12 that already did). Activation is a per-table or wholesale step documented in `rls_proposal.sql` §5. Until the app switches its DB connection role from `postgres` (superuser) to a member of `app_user`, RLS would be inert even after activation. Session var contract is in `rls_proposal.sql` header.

---

## 7. Category B patch set — the 138-FK completion plan

### Bucket system (codified in the generator)

| Bucket | Meaning | `ON DELETE` | Count | Why |
|---:|---|---|---:|---|
| 1 | Domain cross-aggregate link (`orders.coupon_id → coupons`) | `RESTRICT` | 52 | Preserve referential integrity across aggregate roots |
| 2 | Tenant scoping on non-partitioned referencing table (`customers.brand_id → brands`) | `RESTRICT` | 71 | Tenant rows are long-lived; soft-delete via `status='archived'` |
| 3 | Tenant scoping on **partitioned** referencing table (`orders.brand_id → brands`) | `RESTRICT` | 11 | Same as bucket 2, but FK propagates to all partition children |
| C | Aggregate child (`order_items.order_id → orders`) — child table starts with parent's singular form | `CASCADE` | 6 | Children disappear when parent does |
| S | Soft pointer (`orders.pickup_slot_id → delivery_slots`) | `SET NULL` | 5 | Parent may be deleted/cancelled; child remains valid |
| A | Audit/actor column (`created_by`, `*_user_id` audit) | *skipped* | — | Convention: allow user hard-delete without orphaning audit trail |
| P | Polymorphic (paired with `*_type` column) | *skipped, listed in review file* | — | No single FK target by design |
| X | Unresolved | *listed in review file* | — | No matching target table (3 real cases) |

### Generator key features

- **Idempotent** every `ALTER TABLE` wrapped in `DO $$ … EXCEPTION WHEN duplicate_object THEN NULL; END $$;` — patches re-runnable
- **Already-FK-aware** parses `ALTER TABLE … ADD CONSTRAINT FOREIGN KEY` lines in source SQL to skip Category A FKs (verified: `user_scope_memberships.role_id` correctly absent from patches)
- **Composite-FK-aware** detects when target table is in `PARTITIONED_PARENTS` and looks up the child's matching `*_<partition_col>` companion column; emits 2-column FK
- **Bucket-3-aware** checks if the **referencing** table is partitioned (not the target) for the special-care bucket
- **Schema-qualified** every target is `<schema>.<table>(id)` — does not rely on `search_path`
- **Unconditional companion index** every FK gets `CREATE INDEX IF NOT EXISTS idx_<table>_<col>_fk ON …` because partial indexes (`WHERE deleted_at IS NULL` etc.) won't be used for FK enforcement scans (this was the `idx_usm_role` vs `idx_usm_role_fk` lesson from Category A)

### Patch validation already performed

```
✓ fk_patch_03_customer_catalog.sql — BEGIN/ROLLBACK dry-run: 5 DOs + 5 indexes
✓ fk_patch_02_identity_access.sql — BEGIN/ROLLBACK dry-run: 8 DOs (4 composite-FK on partitioned audit_logs)
✓ Idempotency: applied fk_patch_03 twice in same transaction; second apply silently no-op'd via WHEN duplicate_object
✓ Schema qualification: every FK target resolved by explicit <schema>.<table>; no search_path dependency
```

### The 28 review cases (`fk_patch_review_polymorphic.sql`)

**25 polymorphic** (paired with a `*_type` discriminator — keep as-is unless redesigning):
- `*.scope_id`, `*.owner_id`, `*.aggregate_id`, `*.recipient_id`, `*.reference_id`, `*.resource_id`, `*.correlation_id`, `*.causation_id`, `*.request_id`
- Five garment-tracking columns: `garments.current_location_id`, `garment_inspections.location_id`, `stock_reconciliation_items.{expected,found}_location_id`, `stock_reconciliation_items.last_known_holder_id` — likely want a `garment_locations` table or a `*_type` discriminator

**3 truly unresolved** (no target table exists anywhere in the 92 tables):
- `customer_packages.purchase_order_id` — no `purchase_orders` table
- `refresh_tokens.family_id` — no `token_families` table (token-rotation lineage)
- `pickup_requests.converted_order_id` — already handled by Category A; appears in review only because the generator's column→table map doesn't know about it (harmless)

---

## 8. How to apply the patch set

```bash
cd /Users/gtmkumar/Documents/source/laundryghar/db/patches
./apply_patches.sh
# overrides:  DB_NAME=… DB_USER=… DB_PASS=… DB_HOST=… DB_PORT=… ./apply_patches.sh
```

The orchestrator:
1. Connects to `laundry_ghar_db` (default)
2. Applies the 11 patches in BC-dependency order, each in its own transaction
3. Prints a per-schema FK count for verification

Re-running is safe (every constraint is idempotent via `DO $$ EXCEPTION` and every index via `IF NOT EXISTS`).

---

## 9. How to regenerate the patches (if source SQL changes)

The generator is deterministic and re-runnable:

```bash
cd /Users/gtmkumar/Documents/source/laundryghar
python3 db/tools/generate_fk_patches.py
```

It overwrites `db/patches/fk_patch_*.sql` from the current state of `database_scripts/*.sql`. If you've added new tables, update the maps at the top of `generate_fk_patches.py`:
- `TABLE_TO_SCHEMA` — every table → its BC schema
- `COLUMN_TO_TABLE` — column name → target table (singular→plural mapping + aliases like `pickup_rider_id → riders`)
- `PARTITIONED_PARENTS` — if you add a new pg_partman-managed table
- `AUDIT_COLS`, `POLYMORPHIC_COLS`, `SOFT_POINTER_COLS` — bucket overrides

---

## 10. ER-diagram verification (BC-1 + BC-2)

A diagram was verified against the live DB. All 10 boxes, all annotated columns, and all 8 explicit relationships matched the implementation. Two findings during verification:

1. **`user_scope_memberships.role_id`** was missing its FK to `roles(id)` — **fixed in Category A** (added with `ON DELETE RESTRICT` because `roles` has a `deleted_at` soft-delete column).
2. The companion partial index `idx_usm_role (role_id) WHERE revoked_at IS NULL` is not used for FK enforcement scans — **fixed in Category A** by adding the unconditional `idx_usm_role_fk`. Both indexes coexist: the partial one stays cheaper for the hot authz query path.

---

## 11. Architectural conventions to preserve

| Convention | Rule |
|---|---|
| Primary keys | `UUID` via `gen_random_uuid()` (sortable v7-ish); composite `(id, partition_col)` for partitioned parents |
| Money | `NUMERIC(14,2)`; never float |
| Timestamps | `TIMESTAMPTZ`; never plain `TIMESTAMP` |
| Soft delete | `deleted_at TIMESTAMPTZ NULL` + partial indexes `WHERE deleted_at IS NULL` |
| Audit columns | `created_at`, `updated_at`, `created_by`, `updated_by`, `version` |
| Enums | Lookup tables, not PG enum types (alter-friendly) |
| JSONB | For flexible/sparse data; always GIN-indexed |
| Partitions | Monthly for hot OLTP, daily for high-volume ephemera |
| Tenant scoping | `brand_id` everywhere; `franchise_id`/`store_id` where scoped |
| RLS | Enforced via session vars: `app.current_brand_id`, `app.current_franchise_id`, `app.current_store_id`, `app.current_user_id`, `app.bypass_rls` |
| `created_by`/`updated_by` | Never FK'd — convention allows user hard-delete without orphaning audit trail |
| FK enforcement indexes | Must be **unconditional** (`CREATE INDEX … ON tbl(col)`) — partial indexes break FK scans |
| Cross-BC FKs | Allowed (DB is one transactional unit); schema-qualified target names |
| Apply order | FK-dependency-correct, not lexical |

---

## 12. Common verification queries

```sql
-- 1. Per-schema object inventory
SELECT n.nspname AS schema,
       count(*) FILTER (WHERE c.relkind IN ('r','p') AND c.relispartition=false) AS logical_tables,
       count(*) FILTER (WHERE c.relkind='m')                                       AS mat_views,
       count(*) FILTER (WHERE c.relkind='r' AND c.relispartition=true)             AS partition_children
FROM pg_namespace n LEFT JOIN pg_class c ON c.relnamespace=n.oid
WHERE n.nspname IN ('kernel','tenancy_org','identity_access','customer_catalog',
                    'order_lifecycle','logistics','commerce','finance_royalty',
                    'engagement_cms','analytics')
GROUP BY n.nspname ORDER BY n.nspname;

-- 2. FKs per schema
SELECT n.nspname AS schema, count(*) AS fks
FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
WHERE c.contype='f' AND n.nspname NOT IN ('pg_catalog','information_schema','partman','public')
GROUP BY n.nspname ORDER BY n.nspname;

-- 3. partman state
SELECT parent_table, control, partition_interval, retention
FROM partman.part_config ORDER BY parent_table;

-- 4. Any user objects accidentally in public?
SELECT relname, relkind FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname='public' AND c.relkind IN ('r','p','m');
-- expected: only PostGIS (spatial_ref_sys, geometry_columns, geography_columns) and pg_stat_statements views

-- 5. Long-running / blocking sessions
SELECT pid, now()-query_start AS duration, wait_event_type, wait_event, left(query,80) AS snippet
FROM pg_stat_activity WHERE state != 'idle' ORDER BY duration DESC NULLS LAST;
```

---

## 13. Open decisions / next steps

| # | Item | Blocker | Owner |
|---|---|---|---|
| 1 | ~~Apply Category B patches (138 FKs)~~ | ✅ Applied 2026-05-27 — see §6 | — |
| 2 | ~~Decide on partial vs full apply of Category B~~ | ✅ N/A — full apply chosen and complete | — |
| 3 | ~~Resolve the 5 garment-location columns~~ | ✅ 2026-05-27 — discriminator pattern via `db/patches/polymorphic_location_discriminators.sql`. Added `<col>_type` varchar with vocabulary CHECK (`store`/`warehouse`/`rider`/`customer`/`transit`/`other`) + pair-consistency CHECK (`(id IS NULL) = (type IS NULL)`). App must validate that the row referenced by `(type, id)` actually exists in the right target table — DB cannot enforce that. | — |
| 4 | ~~Decide on `refresh_tokens.family_id`~~ | ✅ 2026-05-27 — self-FK to `refresh_tokens(id)` (NO ACTION on delete, matching `parent_token_id` pattern) via `db/patches/auth_token_lineage_and_package_purchase_fk.sql` | — |
| 5 | ~~Decide on `customer_packages.purchase_order_id`~~ | ✅ 2026-05-27 — was not an orphan: companion `purchase_order_created_at` column proves composite FK to partitioned `orders` was intended. Added `(purchase_order_id, purchase_order_created_at) → orders(id, created_at) ON DELETE RESTRICT` via same patch file | — |
| 6 | ~~Fresh-rebuild robustness~~ | ✅ 2026-05-27 — `db/build_from_scratch.sh` orchestrates all 7 stages (schemas → Cat-B FKs → triggers → discriminators → token/package FKs → RLS proposal → partman config+maintenance). Auto-detects existing DB and skips schema build; `FORCE_REBUILD=1` overrides. Idempotent end-to-end on built DBs | — |
| 7 | EF Core `DbContext` per BC | Schema layout supports `modelBuilder.HasDefaultSchema("identity_access")` per BC; not yet wired in `backend/services/*` | Backend team |

---

## 14. Glossary

| Term | Meaning |
|---|---|
| BC | Bounded Context — DDD concept; here one per source SQL file (e.g. BC-4 = order_lifecycle) |
| Forward-reference FK | A column declared before its target table is declared in the same file; needs a post-creation `ALTER TABLE` |
| Bucket 1/2/3/C/S/A/P/X | FK classification codes used by the generator (see §7) |
| Composite FK | FK with multiple columns; required when the target table is partitioned (composite PK) |
| Companion timestamp | A `*_created_at`/`*_occurred_at` column added to a referencing table specifically so a composite FK to a partitioned parent can be formed |
| pg_partman | PostgreSQL extension that automates partition maintenance; converted 5 tables in this DB |
| Tenant scoping column | `brand_id`/`franchise_id`/`store_id`/`warehouse_id` — used for multi-tenant isolation (often via RLS) |

---

**Last verified:** 2026-05-27 against `laundry_ghar_db` on `localhost:5432`. Schema count, FK count, and patch dry-runs all passed.
