using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Analytics.Reporting.Queries;

public sealed record GetWarehouseThroughputQuery(
    Guid? WarehouseId,
    DateOnly? From,
    DateOnly? To) : IQuery<IReadOnlyList<WarehouseThroughput>>;

public sealed class GetWarehouseThroughputHandler
    : IQueryHandler<GetWarehouseThroughputQuery, IReadOnlyList<WarehouseThroughput>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetWarehouseThroughputHandler(ICommerceDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<IReadOnlyList<WarehouseThroughput>> HandleAsync(
        GetWarehouseThroughputQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.WarehouseThroughputs
            .AsNoTracking()
            .Where(x => x.BrandId == brandId);

        if (q.WarehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == q.WarehouseId.Value);
        if (q.From.HasValue)
            query = query.Where(x => x.ThroughputDate >= q.From.Value);
        if (q.To.HasValue)
            query = query.Where(x => x.ThroughputDate <= q.To.Value);

        return await query.OrderByDescending(x => x.ThroughputDate).ToListAsync(ct);
    }
}
