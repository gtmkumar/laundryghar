using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Analytics.Reporting.Queries;

public sealed record GetDailyStoreRevenueQuery(
    Guid? StoreId,
    DateOnly? From,
    DateOnly? To) : IQuery<IReadOnlyList<DailyStoreRevenue>>;

public sealed class GetDailyStoreRevenueHandler
    : IQueryHandler<GetDailyStoreRevenueQuery, IReadOnlyList<DailyStoreRevenue>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetDailyStoreRevenueHandler(ICommerceDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<IReadOnlyList<DailyStoreRevenue>> HandleAsync(
        GetDailyStoreRevenueQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.DailyStoreRevenues
            .AsNoTracking()
            .Where(x => x.BrandId == brandId);

        if (q.StoreId.HasValue)
            query = query.Where(x => x.StoreId == q.StoreId.Value);
        if (q.From.HasValue)
            query = query.Where(x => x.RevenueDate >= q.From.Value);
        if (q.To.HasValue)
            query = query.Where(x => x.RevenueDate <= q.To.Value);

        return await query.OrderByDescending(x => x.RevenueDate).ToListAsync(ct);
    }
}
