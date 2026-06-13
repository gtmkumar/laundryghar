using laundryghar.Utilities.Common;
using Microsoft.AspNetCore.Mvc;

namespace laundryghar.Analytics.Endpoints;

public static class AnalyticsEndpoints
{
    public static WebApplication MapAnalyticsEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/v1/admin/analytics")
                       .RequireAuthorization()
                       .WithTags("Admin - Analytics");

        // ── GET /daily-store-revenue ──────────────────────────────────────────
        admin.MapGet("/daily-store-revenue", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            CancellationToken ct,
            [FromQuery] Guid? storeId = null,
            [FromQuery] DateOnly? from = null,
            [FromQuery] DateOnly? to = null) =>
        {
            var brandId = user.RequireBrandId();

            var q = db.DailyStoreRevenues
                      .AsNoTracking()
                      .Where(x => x.BrandId == brandId);

            if (storeId.HasValue)
                q = q.Where(x => x.StoreId == storeId.Value);
            if (from.HasValue)
                q = q.Where(x => x.RevenueDate >= from.Value);
            if (to.HasValue)
                q = q.Where(x => x.RevenueDate <= to.Value);

            var data = await q.OrderByDescending(x => x.RevenueDate).ToListAsync(ct);
            return Results.Ok(new ListResponse<DailyStoreRevenue> { Status = true, Data = data });
        }).RequireAuthorization("permission:analytics.read");

        // ── GET /monthly-franchise-revenue ────────────────────────────────────
        admin.MapGet("/monthly-franchise-revenue", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            CancellationToken ct,
            [FromQuery] Guid? franchiseId = null,
            [FromQuery] int? year = null) =>
        {
            var brandId = user.RequireBrandId();

            var q = db.MonthlyFranchiseRevenues
                      .AsNoTracking()
                      .Where(x => x.BrandId == brandId);

            if (franchiseId.HasValue)
                q = q.Where(x => x.FranchiseId == franchiseId.Value);
            if (year.HasValue)
                q = q.Where(x => x.RevenueMonth.Year == year.Value);

            var data = await q.OrderByDescending(x => x.RevenueMonth).ToListAsync(ct);
            return Results.Ok(new ListResponse<MonthlyFranchiseRevenue> { Status = true, Data = data });
        }).RequireAuthorization("permission:analytics.read");

        // ── GET /warehouse-throughput ─────────────────────────────────────────
        admin.MapGet("/warehouse-throughput", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            CancellationToken ct,
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] DateOnly? from = null,
            [FromQuery] DateOnly? to = null) =>
        {
            var brandId = user.RequireBrandId();

            var q = db.WarehouseThroughputs
                      .AsNoTracking()
                      .Where(x => x.BrandId == brandId);

            if (warehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == warehouseId.Value);
            if (from.HasValue)
                q = q.Where(x => x.ThroughputDate >= from.Value);
            if (to.HasValue)
                q = q.Where(x => x.ThroughputDate <= to.Value);

            var data = await q.OrderByDescending(x => x.ThroughputDate).ToListAsync(ct);
            return Results.Ok(new ListResponse<WarehouseThroughput> { Status = true, Data = data });
        }).RequireAuthorization("permission:analytics.read");

        // ── GET /customer-ltv (paginated, top customers by LTV) ──────────────
        admin.MapGet("/customer-ltv", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var brandId = user.RequireBrandId();
            page     = page     < 1  ? 1  : page;
            pageSize = pageSize < 1  ? 20 : pageSize;
            pageSize = pageSize > 100 ? 100 : pageSize;

            var q = db.CustomerLtvs
                      .AsNoTracking()
                      .Where(x => x.BrandId == brandId)
                      .OrderByDescending(x => x.LifetimeRevenue);

            var paged = await PaginatedList<CustomerLtv>.CreateAsync(q, page, pageSize, ct);
            return Results.Ok(new PaginatedListResponse<CustomerLtv> { Status = true, Data = paged });
        }).RequireAuthorization("permission:analytics.read");

        // ── GET /rider-performance (paginated) ────────────────────────────────
        admin.MapGet("/rider-performance", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var brandId = user.RequireBrandId();
            page     = page     < 1  ? 1  : page;
            pageSize = pageSize < 1  ? 20 : pageSize;
            pageSize = pageSize > 100 ? 100 : pageSize;

            var q = db.RiderPerformances
                      .AsNoTracking()
                      .Where(x => x.BrandId == brandId)
                      .OrderByDescending(x => x.PerfDate)
                      .ThenByDescending(x => x.CompletionRate);

            var paged = await PaginatedList<RiderPerformance>.CreateAsync(q, page, pageSize, ct);
            return Results.Ok(new PaginatedListResponse<RiderPerformance> { Status = true, Data = paged });
        }).RequireAuthorization("permission:analytics.read");

        // ── GET /dashboard ────────────────────────────────────────────────────
        admin.MapGet("/dashboard", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var brandId = user.RequireBrandId();
            var today   = DateOnly.FromDateTime(DateTime.UtcNow);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            // NOTE: these three queries are awaited SEQUENTIALLY. They share one
            // scoped DbContext, which is not thread-safe — running them concurrently
            // (Task.WhenAll) throws "A second operation was started on this context".
            // Today's revenue totals from mv_daily_store_revenue
            var todayRev = await db.DailyStoreRevenues
                .AsNoTracking()
                .Where(x => x.BrandId == brandId && x.RevenueDate == today)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    OrdersCount    = g.Sum(x => x.OrdersCount),
                    GrossRevenue   = g.Sum(x => x.GrossRevenue),
                    CollectedAmount = g.Sum(x => x.CollectedAmount),
                    UniqueCustomers = g.Sum(x => x.UniqueCustomers),
                })
                .FirstOrDefaultAsync(ct);

            // This-month totals from mv_monthly_franchise_revenue
            var monthRev = await db.MonthlyFranchiseRevenues
                .AsNoTracking()
                .Where(x => x.BrandId == brandId && x.RevenueMonth == monthStart)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    OrdersCount  = g.Sum(x => x.OrdersCount),
                    GrossRevenue = g.Sum(x => x.GrossRevenue),
                    NetRevenue   = g.Sum(x => x.NetRevenue),
                })
                .FirstOrDefaultAsync(ct);

            // Top 5 customers by lifetime revenue
            var topCustomers = await db.CustomerLtvs
                .AsNoTracking()
                .Where(x => x.BrandId == brandId)
                .OrderByDescending(x => x.LifetimeRevenue)
                .Take(5)
                .Select(x => new { x.CustomerId, x.CustomerSegment, x.LifetimeRevenue, x.LifetimeOrders })
                .ToListAsync(ct);

            var dashboard = new
            {
                Today = new
                {
                    OrdersCount     = todayRev?.OrdersCount ?? 0,
                    GrossRevenue    = todayRev?.GrossRevenue ?? 0,
                    CollectedAmount = todayRev?.CollectedAmount ?? 0,
                    UniqueCustomers = todayRev?.UniqueCustomers ?? 0,
                },
                ThisMonth = new
                {
                    OrdersCount  = monthRev?.OrdersCount ?? 0,
                    GrossRevenue = monthRev?.GrossRevenue ?? 0,
                    NetRevenue   = monthRev?.NetRevenue ?? 0,
                },
                TopCustomersByLtv = topCustomers,
            };

            return Results.Ok(new { Status = true, Data = dashboard });
        }).RequireAuthorization("permission:analytics.read");

        // ── POST /refresh (analytics.refresh) ────────────────────────────────
        admin.MapPost("/refresh", async (
            LaundryGharDbContext db,
            ICurrentUser user,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            // Ensure caller has brand context (validates auth context is complete)
            _ = user.RequireBrandId();

            // Refresh via the SECURITY DEFINER function: the matviews are owned by
            // postgres and this service connects as app_user, so a direct REFRESH
            // is permission-denied. The function (owned by postgres, RLS-exempt)
            // refreshes all 7 matviews CONCURRENTLY across every brand. The same
            // function backs the periodic MatviewRefreshService.
            // See db/patches/analytics_refresh_function.sql.
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT analytics.refresh_all_matviews();", ct);
                logger.LogInformation("Analytics matviews refreshed via refresh_all_matviews().");
                return Results.Ok(new { Status = true, Data = new { refreshed = true } });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Analytics matview refresh failed.");
                return Results.Ok(new { Status = false, Data = new { refreshed = false, error = ex.Message } });
            }
        }).RequireAuthorization("permission:analytics.refresh");

        return app;
    }
}
