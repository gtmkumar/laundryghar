---
name: soft-delete-status
description: Per-entity archived status value to set on soft-delete — driven by each table's status CHECK constraint, NOT a uniform 'archived'
metadata:
  type: project
---

Soft-delete (DELETE/Archive) handlers must set status off its live value (not just deleted_at) so status-keyed reports don't miscount archived rows as active. The archived value is NOT uniform — it is dictated by each table's status CHECK constraint (verified in database_scripts/06_bc6_commerce.sql, 03_bc3_customer_catalog.sql, docs/SCHEMA_FULL.sql).

**Why it matters:** the admin UI labels coupon/package delete as "Archive", but those tables have NO 'archived' value in their CHECK — picking 'archived' would throw at runtime. The correct terminal value differs by family:
- commerce coupons/packages, subscription_plans, platform_plans → **retired** (no 'archived' in CHECK)
- customer_catalog services/items/item_variants/fabric_types/item_groups/add_ons/service_categories → **disabled** (CHECK is active|disabled[|seasonal]; no archived/retired)
- customer_catalog price_lists → **archived** (CHECK is draft|published|archived)
- engagement_cms notification_templates → **archived** (uses CmsStatus: active|inactive|archived)

**How to apply:** before adding a soft-delete status, grep the table's CHECK in the schema SQL and pick its terminal value. Tenancy/identity entities (brands/franchises→archived, stores/warehouses→closed, customers→deleted) have the SAME defect class but were left OUT of scope of the 2026-06-13 fix because changing tenant status has cascade/onboarding side effects — flag to orchestrator if a report needs them too. promotions and payment_methods HARD-delete (no soft-delete path).
