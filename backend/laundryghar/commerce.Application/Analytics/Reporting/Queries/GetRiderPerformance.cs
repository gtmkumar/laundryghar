using commerce.Application.Analytics.Reporting.Dtos;
using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Analytics.Reporting.Queries;

public sealed record GetRiderPerformanceQuery(int Page, int PageSize)
    : IQuery<PaginatedList<RiderPerformanceResponse>>;

public sealed class GetRiderPerformanceHandler
    : IQueryHandler<GetRiderPerformanceQuery, PaginatedList<RiderPerformanceResponse>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetRiderPerformanceHandler(ICommerceDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<RiderPerformanceResponse>> HandleAsync(
        GetRiderPerformanceQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.RiderPerformances
            .AsNoTracking()
            .Where(x => x.BrandId == brandId)
            .OrderByDescending(x => x.PerfDate)
            .ThenByDescending(x => x.CompletionRate);

        // Paginate on the raw matview entity in SQL, then coalesce null aggregates → 0 in memory.
        // mv_rider_performance emits NULL for the four aggregate columns when a rider has no
        // qualifying rows for the day; the entity is nullable to avoid an InvalidCastException on
        // materialization, and the response projection keeps the JSON shape numeric.
        var paged = await PaginatedList<laundryghar.SharedDataModel.Entities.Analytics.RiderPerformance>
            .CreateAsync(query, q.Page, q.PageSize, ct);

        return paged.Map(RiderPerformanceResponse.From);
    }
}
