---
name: project-commerce-analytics-migration
description: First Commerce endpoint migration (Analytics) into commerce.WebApi:5242 — matview seam choice, host already had permission/CustomerOnly auth, deferred refresh worker
metadata:
  type: project
---

Migrated the Analytics sub-domain (~830 lines, all matview-backed reporting) from legacy MediatR into the commerce split host (commerce.WebApi:5242, ICommerceDbContext over LaundryGharDbContext). First Commerce endpoint; mirrored the Operations migrations ([[project-operations-warehouse-migration]], [[project-operations-logistics-migration]]). Non-derivable points:

**1. Raw-SQL seam for matview refresh is a NON-QUERY, so `ExecuteSqlRawAsync(string, ct)` — not the Operations scalar/interpolated seams.**
**Why:** `analytics.refresh_all_matviews()` (SECURITY DEFINER; matviews owned by postgres, app_user can't REFRESH directly) returns no rows. Operations' `SqlQueryScalarAsync`/`ExecuteSqlInterpolatedAsync` don't fit. The SQL is a constant string (no interpolation) — no injection surface, so a plain `string` param is correct here (don't force FormattableString).
**How to apply:** added `Task<int> ExecuteSqlRawAsync(string sql, CancellationToken)` to ICommerceDbContext; impl forwards to `_db.Database.ExecuteSqlRawAsync`. Reuse for any future constant-SQL non-query.

**2. commerce.WebApi host ALREADY supports `permission:*` and `CustomerOnly` — no Program.cs auth changes needed for admin/customer lanes.**
**Why:** the host registers PermissionHandler + AnyPermissionHandler + CustomerOnlyHandler + PermissionPolicyProvider out of the box (no rider lane). Unlike the Operations RiderOnly gap, Analytics is all `permission:analytics.read` / `permission:analytics.refresh` — already resolvable.
**How to apply:** Commerce admin/customer endpoints need ZERO host auth wiring. Only a NEW policy family (e.g. a rider lane, or a brand-new requirement) would require touching Program.cs.

**3. Matview read models are keyless DbSets already on LaundryGharDbContext (`Set<T>()`-backed) — just surface them on ICommerceDbContext/CommerceDbContext.** No EF config, no new entities. The 5: DailyStoreRevenues, MonthlyFranchiseRevenues, WarehouseThroughputs, CustomerLtvs, RiderPerformances. RiderPerformance's 4 aggregate columns are `decimal?` (MV emits NULL → InvalidCastException if non-nullable); coalesce null→0 in the DTO via `PaginatedList.Map(RiderPerformanceResponse.From)`.

**4. Legacy anonymous `{ Status, Data }` responses → typed `SingleResponse<T>` (same JSON).** Source dashboard/refresh returned raw anonymous objects; SingleResponse<T> serializes identically (Message omitted when null). `/refresh` preserves legacy semantics: failure returns HTTP 200 with envelope Status=false + error in Data — the handler catches and returns RefreshResultDto, never throws.

**5. Deferred (NOT migrated): the `MatviewRefreshService` BackgroundService** (legacy Analytics/Infrastructure/Services). It's a hosted worker, not an endpoint — belongs with the CommerceHub worker host ([[project-commercehub-consolidation]]). On-demand POST /refresh IS migrated; the periodic auto-refresh is lost until the worker lane lands.
