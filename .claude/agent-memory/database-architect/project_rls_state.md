---
name: project-rls-state
description: RLS activation status across all BC schemas — which tables have RLS enabled, which are deferred and why
metadata:
  type: project
---

As of 2026-06-05, RLS state for BC-1 (tenancy_org) and BC-2 (identity_access):

**tenancy_org — all 10 tables have RLS enabled:**
- Previously enabled (from original DDL): territories, franchise_agreements, franchises, stores, warehouses
- Enabled 2026-06-05: brands, platforms, holidays, operating_hours, store_warehouse_mappings
- All use `rls_brand` (B1) or `rls_admin_only` (B4) policy from rls_proposal.sql

**identity_access — 2 of 11 tables have RLS enabled:**
- Enabled 2026-06-05: audit_logs (B1, has brand_id), roles (B1, has brand_id)
- Deferred (not yet enabled):
  - users, permissions, role_permissions: rls_admin_only — enabling now would silently block all app_user reads before app switches away from superuser connection
  - login_history, otp_codes, refresh_tokens, password_resets, user_profiles, user_scope_memberships: rls_user_self (B3) — requires app.current_user_id session var not yet set by app middleware

**customer_catalog (BC-3) — all 14 tables have RLS enabled (2026-06-05):**
- Every table has a brand_id column; all carry a single uniform `rls_brand` policy (kernel.current_brand_id(), NULLIF-safe) targeting app_user. Files: rls_enable_customer_catalog.sql, fix_legacy_customer_catalog_rls_policies.sql.
- DECISION: rls_proposal.sql's `rls_brand_or_customer` policies (account_deletion_requests, customer_addresses, customer_devices, customers, dpdp_consents) were DROPPED in favor of plain brand-only `rls_brand`. Rationale: per BC-3 directive, customer-self filtering belongs in the app layer, not RLS. Result: all 14 tables uniform.
- Legacy raw-cast `*_tenant` policies dropped: customers_tenant, items_tenant, pricelist_tenant, svccat_tenant, services_tenant (DEF-002 class — threw on empty brand_id).

**order_lifecycle (BC-4) — 19 of 20 logical tables have RLS enabled (2026-06-05):**
- 19 brand_id-bearing tables carry a single uniform `rls_brand` policy (kernel-helper, NULLIF-safe), incl. the 2 partitioned parents `orders` and `process_logs` (RLS on parent propagates to all partition children — never enabled per-child). Files: rls_enable_order_lifecycle.sql, fix_legacy_order_lifecycle_rls_policies.sql.
- DECISION: `order_addons` left RLS OFF. It has NO brand_id column (brand scoping is transitive via order_id -> orders.brand_id; it is a CASCADE aggregate child of orders). A brand-only rls_brand policy is impossible. rls_proposal.sql had placed an rls_admin_only policy on it (deny app_user unless bypass) but we did NOT enable RLS, since doing so would block all app_user access to order add-ons (no bypass wired) and break the orders read path. Proper fix = denormalize brand_id onto order_addons OR an EXISTS-subquery policy against the parent order — a schema-design decision, out of scope for RLS activation.
- Legacy raw-cast `garments_tenant` policy dropped (DEF-002 class). It was the only legacy *_tenant policy in order_lifecycle.

**logistics (BC-5) — all 4 tables have RLS enabled (2026-06-05):**
- riders, rider_assignments, rider_capacity_config: regular tables; rowsecurity=t with rls_brand kernel-helper policy.
- rider_location_pings: PARTITIONED PARENT (daily by pinged_at, 16 date partitions + default); RLS enabled on parent only — partition children show relrowsecurity=f in pg_class which is correct PostgreSQL behavior; enforcement occurs at the parent.
- No legacy *_tenant policies existed on logistics tables (none to drop).
- No fix_legacy_logistics_rls_policies.sql needed.
- updated_at triggers: riders, rider_assignments, rider_capacity_config all have trg_*_set_updated_at triggers. rider_location_pings has NO updated_at column (GPS append-only table) — correct.
- File: db/patches/rls_enable_logistics.sql

**commerce (BC-6) — all 13 tables have RLS enabled (2026-06-05):**
- All 13 tables carry rls_brand (B1) or rls_brand_or_customer (B2) kernel-helper policies, all targeting app_user. Files: rls_enable_commerce.sql.
- Legacy raw-cast packages_tenant policy dropped (DEF-002 class). Only 1 legacy *_tenant policy existed.
- No fix_legacy_commerce_rls_policies.sql needed beyond the inline drop in rls_enable_commerce.sql.

**finance_royalty (BC-7) — all 8 tables have RLS enabled (2026-06-05):**
- All 8 tables carry a single uniform `rls_brand` policy (kernel-helper, NULLIF-safe) targeting app_user.
- Policy pre-existed from rls_proposal.sql on all 8 tables — only `ALTER TABLE … ENABLE ROW LEVEL SECURITY` was needed.
- No legacy *_tenant policies existed (DEF-002 audit confirmed clean).
- Updated_at triggers: 4 tables have updated_at (cash_books, expense_categories, expenses, royalty_invoices) — all 4 have triggers. Other 4 (cash_book_entries, expense_attachments, shift_handovers, royalty_calculations) have no updated_at column — no trigger needed (append-only / immutable).
- Files: rls_enable_finance_royalty.sql, fix_legacy_finance_royalty_rls_policies.sql.

**engagement_cms (BC-8) — all 8 tables have RLS enabled (2026-06-05):**
- All 8 logical tables have brand_id; all carry a kernel-helper policy from rls_proposal.sql.
- 6 tables: rls_brand (B1): app_banners, mobile_app_config, notification_templates, notifications_log (PARTITIONED), notifications_outbox, onboarding_slides.
- 2 tables: rls_brand_or_customer (B2): notification_preferences, whatsapp_message_log (both have customer_id column).
- notifications_log: PARTITIONED parent (monthly by sent_at); RLS enabled on parent — propagates to all 11 partition children + default.
- No legacy *_tenant policies existed (DEF-002 audit clean, 0 rows found).
- Updated_at triggers: 6 tables have the column and trigger (app_banners, mobile_app_config, notification_preferences, notification_templates, onboarding_slides, whatsapp_message_log). notifications_log and notifications_outbox have no updated_at column — append-only logs, no trigger needed.
- Files: rls_enable_engagement_cms.sql, fix_legacy_engagement_cms_rls_policies.sql (placeholder no-op).

**BC-9 analytics:** Materialized views only — no tables, no RLS applicable.

**Why:** App still connects as `postgres` (superuser bypasses RLS), so activation is safe but inert until app switches to `app_user` role.

**How to apply:** When app is ready, activate per-table with `ALTER TABLE <schema>.<table> ENABLE ROW LEVEL SECURITY` or use wholesale snippet in rls_proposal.sql §5. File `/db/patches/_applied_rls_bc1_bc2.sql` documents the 2026-06-05 activation for BC-1/BC-2.

Related: [[project-db-patch-history]]
