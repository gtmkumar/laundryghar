---
name: project-db-patch-history
description: Chronological record of patch waves applied to laundry_ghar_db — what was applied, when, and current FK/trigger/RLS counts
metadata:
  type: project
---

**Database:** laundry_ghar_db, PostgreSQL 18.4, localhost:5432

**2026-05-27 (Wave 1 — Category B completion):**
- All 138 FKs across BC patches 00-09 applied via apply_patches.sh
- 526 parent FKs total across 11 BC schemas
- polymorphic_location_discriminators.sql: garment location *_type discriminator columns added
- auth_token_lineage_and_package_purchase_fk.sql: refresh_tokens.family_id self-FK, customer_packages composite FK to orders
- triggers_set_updated_at.sql: kernel.set_updated_at() + 61 BEFORE UPDATE triggers (auto-propagates to 14 partition children)
- rls_proposal.sql: 92 inert policies + app_user/app_admin roles + 6 kernel helper functions + grants. RLS NOT enabled on any table at this point.
- partman config: infinite_time_partitions=true on all 5 managed tables; maintenance ran to 2026-12-01 runway

**2026-06-05 (Wave 2 — BC-1 + BC-2 gap-fix patches):**
- fk_patch_01_tenancy_org.sql: confirmed no-op (0 actionable FKs)
- fk_patch_02_identity_access.sql: confirmed idempotent (8 FKs already present from Wave 1); 8 companion indexes already existed
- triggers_set_updated_at.sql: re-ran idempotently; 76 triggers total across all BC schemas (15 on tenancy_org + identity_access combined)
- rls_proposal.sql: re-ran idempotently; 92 policies refreshed
- _applied_rls_bc1_bc2.sql: ENABLED RLS on 7 additional tables — tenancy_org: brands, platforms, holidays, operating_hours, store_warehouse_mappings; identity_access: audit_logs, roles. Total RLS-enabled tables in scope: tenancy_org=10, identity_access=2.

**2026-06-05 (Wave 3 — RLS/app_user QA defect fixes for BC-1/BC-2):**
- DEF-001: app_user_role.sql now has an unconditional `ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user' NOSUPERUSER...` after the IF-NOT-EXISTS create guard. Root cause: rls_proposal.sql creates app_user as NOLOGIN first, so app_user_role.sql's IF NOT EXISTS guard skipped it, leaving NOLOGIN + no password. app_user flags now (rolcanlogin,rolsuper,rolbypassrls) = (t,f,f) and authenticates with password 'app_user'.
- DEF-002: new file fix_legacy_tenancy_rls_policies.sql drops the 5 legacy `*_tenant` policies (franchises_tenant, territories_tenant, franagree_tenant, stores_tenant, warehouses_tenant) on tenancy_org. Root cause: they target role `public` (incl. app_user) and use raw `(current_setting('app.current_brand_id',true))::uuid` which throws on empty string; OR-combined with rls_brand it still aborts the query. The rls_brand policies (kernel.current_brand_id(), NULLIF-safe) fully cover app_user isolation. Script guards: asserts rls_brand exists per table before dropping. Both scripts idempotent.
- Note: legacy policies used `app.bypass_rls='true'` vs new policies' `kernel.rls_bypass()` (checks ='on') — a separate latent inconsistency, left as-is (out of scope).

**2026-06-05 (Wave 4 — BC-3 customer_catalog gap-fix + RLS closure):**
- fk_patch_03_customer_catalog.sql: confirmed idempotent no-op (5 FKs + 5 indexes already present).
- rls_enable_customer_catalog.sql (new): ENABLED RLS on the 9 previously-disabled tables + added rls_brand to customers. All 14 customer_catalog tables now rowsecurity=t with a single uniform rls_brand policy.
- fix_legacy_customer_catalog_rls_policies.sql (new): dropped 5 legacy raw-cast *_tenant policies (DEF-002 class: customers_tenant, items_tenant, pricelist_tenant, svccat_tenant, services_tenant) + 5 redundant rls_brand_or_customer policies. Each drop guarded by confirming rls_brand exists first.
- DECISION: dropped rls_brand_or_customer in favor of brand-only rls_brand because customer-self filtering is an app-layer concern per BC-3 directive.
- updated_at triggers: all 11 tables with updated_at already covered by global script; 3 tables lack the column (no trigger needed).
- Verified: brand A/B isolation, empty-brand 0 rows no error, all scripts idempotent.

**2026-06-05 (Wave 5 — BC-4 order_lifecycle gap-fix + RLS closure):**
- fk_patch_04_order_lifecycle.sql (largest, 63 FKs): confirmed idempotent no-op — FK count 529 unchanged (0 new, all 63 duplicates from 2026-05-27 Cat-B wave), all 63 indexes already present, 0 errors.
- rls_enable_order_lifecycle.sql (new): ENABLED RLS on 19 brand_id-bearing tables (incl. partitioned parents orders + process_logs; propagates to children). Uniform rls_brand kernel-helper policy on each.
- fix_legacy_order_lifecycle_rls_policies.sql (new): dropped 1 legacy raw-cast policy garments_tenant (DEF-002 class), guarded by confirming rls_brand exists.
- DECISION: order_addons left RLS OFF (no brand_id column; brand scoping transitive via order_id). See project-rls-state for full rationale.
- updated_at triggers: all 11 tables with updated_at already covered (incl. orders parent); process_logs has no updated_at.
- Verified: brand A/B isolation on orders (partitioned) + order_items, empty-brand 0 rows no error, all scripts idempotent.

**2026-06-05 (Wave 6 — BC-5 logistics gap-fix + RLS closure):**
- fk_patch_05_logistics.sql: confirmed idempotent no-op (9 FKs + 9 indexes all already present from 2026-05-27 Cat-B wave). 0 new constraints, 0 errors.
- rls_enable_logistics.sql (new): ENABLED RLS on all 4 logistics tables — riders, rider_assignments, rider_capacity_config (regular), rider_location_pings (partitioned parent, daily by pinged_at). Policies: single uniform rls_brand kernel-helper on each. Partition children show relrowsecurity=f in pg_class — correct behavior; enforcement is at the parent.
- Legacy *_tenant policies: NONE existed on logistics tables. No fix_legacy_logistics_rls_policies.sql needed.
- updated_at triggers: riders, rider_assignments, rider_capacity_config already have triggers from global script. rider_location_pings has no updated_at column (GPS append-only) — no trigger needed.
- Verified: brand A/B isolation on riders, empty-brand 0 rows no error (NULLIF-safe), partitioned table query error-free.

**2026-06-05 (Wave 7 — BC-6 commerce gap-fix + RLS closure):**
- fk_patch_06_commerce.sql: confirmed idempotent no-op (24 FKs + 24 indexes all already present). 0 new constraints, 0 errors.
- rls_enable_commerce.sql: ENABLED RLS on all 13 commerce tables — 8 with rls_brand (B1) + 5 with rls_brand_or_customer (B2). Inline legacy drop: packages_tenant (DEF-002 class, only 1 in BC-6). Note: packages had been the only table with RLS pre-enabled (from original DDL), so it was re-enabled idempotently.
- Verified: brand A/B isolation on coupons + payments, empty-brand 0 rows no error, all scripts idempotent.

**2026-06-05 (Wave 8 — BC-7 finance_royalty gap-fix + RLS closure):**
- fk_patch_07_finance_royalty.sql: confirmed idempotent no-op (20 FKs + 20 indexes all already present). Spurious `*_fkey_1..15` names on cash_book_entries and royalty_calculations are partition propagations to order_lifecycle.orders children — not duplicates of our constraints. 0 errors.
- rls_enable_finance_royalty.sql (new): ENABLED RLS on all 8 tables. All 8 already had rls_brand policy from rls_proposal.sql. Safety guard (refuse if no policy) passed for all.
- fix_legacy_finance_royalty_rls_policies.sql (new): DEF-002 audit clean — 0 *_tenant policies found. Assertion DO block passed without exception.
- updated_at: 4 tables have column + trigger (cash_books, expense_categories, expenses, royalty_invoices). 4 tables have neither (append-only: cash_book_entries, expense_attachments, shift_handovers, royalty_calculations).
- Verified: brand A/B isolation on cash_books + royalty_invoices, empty-brand 0 rows no error, cleanup complete.

**2026-06-05 (Wave 9 — BC-8 engagement_cms gap-fix + RLS closure):**
- fk_patch_08_engagement_cms.sql: confirmed idempotent no-op — all 7 FKs already present (notification_preferences_brand_id_fkey, notifications_log_brand_id_fkey + outbox_id_fkey, notifications_outbox_brand_id_fkey, whatsapp_message_log_brand_id_fkey + customer_id_fkey + user_id_fkey) and all 7 companion indexes already existed. FKs propagated to all 11 notifications_log partition children (2 each = 22 partition FK rows). 0 errors.
- rls_enable_engagement_cms.sql (new): ENABLED RLS on all 8 logical tables. Safety guard (never enable naked table) passed — all 8 had pre-existing kernel-helper policies. 6 tables rls_brand (B1), 2 tables rls_brand_or_customer (B2: notification_preferences, whatsapp_message_log). notifications_log is PARTITIONED — enabled on parent only.
- fix_legacy_engagement_cms_rls_policies.sql (new, placeholder): DEF-002 audit clean — 0 legacy *_tenant policies in engagement_cms. No drops needed.
- Updated_at triggers: 6 tables with updated_at column all already have triggers from global script. notifications_log and notifications_outbox have NO updated_at — append-only logs, correct.
- Verified: brand A/B isolation on app_banners + onboarding_slides, empty-brand→0 rows NO error (NULLIF-safe), cleanup complete.

**2026-06-07 (Wave 10 — Demo seed data):**
- db/patches/seed_demo_data.sql applied (idempotent, ON CONFLICT DO NOTHING)
- 6 active stores: Mumbai store renamed → "Laundry Ghar Sector 45" (code LGG-S45-001), 5 new Gurgaon stores added (LGG-S14-002, LGG-DLF-003, LGG-SL-004, LGG-S56-005, LGG-PV-006)
- 25 customers (18 new, 7 pre-existing) with realistic Indian names/phones (+91 E.164)
- 14 riders total (13 new: identity_access.users + user_profiles + logistics.riders; 1 pre-existed)
- 327 orders (285 past: 2026-05-25..2026-06-06; 42 today: mixed in-flight statuses), each with 1 order_item
- 5 analytics MVs refreshed CONCURRENTLY
- Verified: dashboard today.ordersCount=42, grossRevenue=24805.49; daily-store-revenue=84 rows (14 days x 6 stores); out_for_delivery=8; placed=8; in_process=4; stores=6; riders=14
- All UUIDs use valid hex format (b2, c3, d4 prefixes); seed uses deterministic md5() for order IDs → fully idempotent

**Still outstanding:**
- order_lifecycle.order_addons: needs brand_id denormalization OR EXISTS-subquery RLS policy to be lockable (schema-design decision)
- BC-9 analytics (materialized views — no RLS applicable)
- identity_access: users, permissions, role_permissions, and B3 user-self tables RLS activation deferred
- EF Core DbContext per-BC wiring (backend team)
- fk_patch_review_polymorphic.sql: 3 truly unresolved cases need product decision

Related: [[project-rls-state]]
