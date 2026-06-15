using commerce.Application.Analytics.Reporting.Commands;
using commerce.Application.Analytics.Reporting.Dtos;
using commerce.Application.Analytics.Reporting.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Endpoints;

namespace commerce.WebApi.Endpoints.Analytics;

/// <summary>
/// Admin — analytics reporting over the materialized views (mv_daily_store_revenue,
/// mv_monthly_franchise_revenue, mv_warehouse_throughput, mv_customer_ltv, mv_rider_performance).
/// Per-route permission policies; brand scoping enforced in handlers via ICurrentUser.RequireBrandId().
/// </summary>
public class AnalyticsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/analytics";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Analytics");
        group.RequireAuthorization();

        group.MapGet(GetDailyStoreRevenue, "/daily-store-revenue").RequireAuthorization("permission:analytics.read");
        group.MapGet(GetMonthlyFranchiseRevenue, "/monthly-franchise-revenue").RequireAuthorization("permission:analytics.read");
        group.MapGet(GetWarehouseThroughput, "/warehouse-throughput").RequireAuthorization("permission:analytics.read");
        group.MapGet(GetCustomerLtv, "/customer-ltv").RequireAuthorization("permission:analytics.read");
        group.MapGet(GetRiderPerformance, "/rider-performance").RequireAuthorization("permission:analytics.read");
        group.MapGet(GetDashboard, "/dashboard").RequireAuthorization("permission:analytics.read");
        group.MapPost(Refresh, "/refresh").RequireAuthorization("permission:analytics.refresh");
    }

    public static async Task<IResult> GetDailyStoreRevenue(
        IDispatcher dispatcher,
        CancellationToken ct,
        Guid? storeId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var data = await dispatcher.QueryAsync(new GetDailyStoreRevenueQuery(storeId, from, to), ct);
        return Results.Ok(new ListResponse<DailyStoreRevenue> { Status = true, Data = data });
    }

    public static async Task<IResult> GetMonthlyFranchiseRevenue(
        IDispatcher dispatcher,
        CancellationToken ct,
        Guid? franchiseId = null,
        int? year = null)
    {
        var data = await dispatcher.QueryAsync(new GetMonthlyFranchiseRevenueQuery(franchiseId, year), ct);
        return Results.Ok(new ListResponse<MonthlyFranchiseRevenue> { Status = true, Data = data });
    }

    public static async Task<IResult> GetWarehouseThroughput(
        IDispatcher dispatcher,
        CancellationToken ct,
        Guid? warehouseId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var data = await dispatcher.QueryAsync(new GetWarehouseThroughputQuery(warehouseId, from, to), ct);
        return Results.Ok(new ListResponse<WarehouseThroughput> { Status = true, Data = data });
    }

    public static async Task<IResult> GetCustomerLtv(
        IDispatcher dispatcher,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20)
    {
        page     = page     < 1   ? 1   : page;
        pageSize = pageSize < 1   ? 20  : pageSize;
        pageSize = pageSize > 100 ? 100 : pageSize;

        var paged = await dispatcher.QueryAsync(new GetCustomerLtvQuery(page, pageSize), ct);
        return Results.Ok(new PaginatedListResponse<CustomerLtv> { Status = true, Data = paged });
    }

    public static async Task<IResult> GetRiderPerformance(
        IDispatcher dispatcher,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20)
    {
        page     = page     < 1   ? 1   : page;
        pageSize = pageSize < 1   ? 20  : pageSize;
        pageSize = pageSize > 100 ? 100 : pageSize;

        var paged = await dispatcher.QueryAsync(new GetRiderPerformanceQuery(page, pageSize), ct);
        return Results.Ok(new PaginatedListResponse<RiderPerformanceResponse> { Status = true, Data = paged });
    }

    public static async Task<IResult> GetDashboard(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetDashboardQuery(), ct);
        return Results.Ok(new SingleResponse<DashboardDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Refresh(IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(new RefreshMatviewsCommand(), ct);
        return Results.Ok(new SingleResponse<RefreshResultDto> { Status = result.Refreshed, Data = result });
    }
}
