using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Analytics.Reporting.Queries;

public sealed record GetMonthlyFranchiseRevenueQuery(
    Guid? FranchiseId,
    int? Year) : IQuery<IReadOnlyList<MonthlyFranchiseRevenue>>;

public sealed class GetMonthlyFranchiseRevenueHandler
    : IQueryHandler<GetMonthlyFranchiseRevenueQuery, IReadOnlyList<MonthlyFranchiseRevenue>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetMonthlyFranchiseRevenueHandler(ICommerceDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<IReadOnlyList<MonthlyFranchiseRevenue>> HandleAsync(
        GetMonthlyFranchiseRevenueQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.MonthlyFranchiseRevenues
            .AsNoTracking()
            .Where(x => x.BrandId == brandId);

        if (q.FranchiseId.HasValue)
            query = query.Where(x => x.FranchiseId == q.FranchiseId.Value);
        if (q.Year.HasValue)
            query = query.Where(x => x.RevenueMonth.Year == q.Year.Value);

        return await query.OrderByDescending(x => x.RevenueMonth).ToListAsync(ct);
    }
}
