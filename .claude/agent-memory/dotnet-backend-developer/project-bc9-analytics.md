---
name: project-bc9-analytics
description: BC-9 analytics microservice — design decisions for materialized view entity mapping and REFRESH MATERIALIZED VIEW CONCURRENTLY fallback pattern
metadata:
  type: project
---

BC-9 analytics service (laundryghar.Analytics, port 5008) is live. It is an admin-only read-only reporting microservice over 5 PostgreSQL materialized views in the analytics schema.

**Key decision: RLS does NOT apply to materialized views.** Every query against the analytics MVs must explicitly filter `.Where(x => x.BrandId == _user.RequireBrandId())`. There is no automatic row-level security filter on these views.

**Why:** MVs are pre-computed snapshots, not live tables, so Postgres RLS policies defined on the source tables do not carry over. Forgetting this would allow a brand_admin to read another brand's data.

**How to apply:** Whenever adding a new analytics endpoint that queries these MVs, always add the `BrandId ==` guard before any other filter. Never rely on EF global query filters for the analytics entities — they have `HasNoKey()` and no filters configured.

**REFRESH CONCURRENTLY vs plain REFRESH decision:** All 5 MVs have UNIQUE indexes (idx_mv*_unique), so REFRESH MATERIALIZED VIEW CONCURRENTLY succeeds. The refresh endpoint tries CONCURRENTLY first and falls back to plain REFRESH if the error message contains "does not have a unique index" or "CONCURRENTLY". This fallback exists as a safety net for operational scenarios (e.g., index rebuild in progress).

**Permission model:** analytics.read and analytics.refresh were added as BC-9 permissions. Grants: brand_admin → both; franchise_owner → analytics.read; auditor → analytics.read; platform_admin → all (bypass).

**MV column type mappings decided:**
- date → DateOnly
- timestamptz → DateTimeOffset
- numeric (unscaled) → decimal with HasColumnType("numeric")
- numeric(14,2), numeric(3,2), numeric(5,2) → decimal with explicit HasColumnType
- bigint → long
- integer → int
- uuid → Guid
- varchar(N) → string with HasMaxLength(N)
