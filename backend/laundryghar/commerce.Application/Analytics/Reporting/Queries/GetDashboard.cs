using commerce.Application.Analytics.Reporting.Dtos;
using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Analytics.Reporting.Queries;

public sealed record GetDashboardQuery : IQuery<DashboardDto>;

public sealed class GetDashboardHandler : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetDashboardHandler(ICommerceDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DashboardDto> HandleAsync(GetDashboardQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var today   = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // NOTE: these three queries are awaited SEQUENTIALLY. They share one scoped DbContext,
        // which is not thread-safe — running them concurrently (Task.WhenAll) throws
        // "A second operation was started on this context".

        // Today's revenue totals from mv_daily_store_revenue
        var todayRev = await _db.DailyStoreRevenues
            .AsNoTracking()
            .Where(x => x.BrandId == brandId && x.RevenueDate == today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                OrdersCount     = g.Sum(x => x.OrdersCount),
                GrossRevenue    = g.Sum(x => x.GrossRevenue),
                CollectedAmount = g.Sum(x => x.CollectedAmount),
                UniqueCustomers = g.Sum(x => x.UniqueCustomers),
            })
            .FirstOrDefaultAsync(ct);

        // This-month totals from mv_monthly_franchise_revenue
        var monthRev = await _db.MonthlyFranchiseRevenues
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
        var topCustomers = await _db.CustomerLtvs
            .AsNoTracking()
            .Where(x => x.BrandId == brandId)
            .OrderByDescending(x => x.LifetimeRevenue)
            .Take(5)
            .Select(x => new DashboardTopCustomerDto(
                x.CustomerId, x.CustomerSegment, x.LifetimeRevenue, x.LifetimeOrders))
            .ToListAsync(ct);

        return new DashboardDto(
            new DashboardTodayDto(
                todayRev?.OrdersCount     ?? 0,
                todayRev?.GrossRevenue    ?? 0,
                todayRev?.CollectedAmount ?? 0,
                todayRev?.UniqueCustomers ?? 0),
            new DashboardThisMonthDto(
                monthRev?.OrdersCount  ?? 0,
                monthRev?.GrossRevenue ?? 0,
                monthRev?.NetRevenue   ?? 0),
            topCustomers);
    }
}
