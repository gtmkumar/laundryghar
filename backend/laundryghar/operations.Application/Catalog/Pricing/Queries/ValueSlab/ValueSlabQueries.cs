using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Queries.ValueSlab;

/// <summary>Lists a brand's value-price slabs, newest-priced first (min value asc within lane).
/// <paramref name="ServiceId"/> filters to one service's lane when supplied (brand-wide/null-service
/// slabs are still returned so the admin sees everything that applies). <paramref name="IncludeArchived"/>
/// includes soft-deleted rows.</summary>
public sealed record GetValueSlabsQuery(Guid? ServiceId, bool IncludeArchived)
    : IQuery<IReadOnlyList<ValueSlabDto>>;

public sealed class GetValueSlabsHandler : IQueryHandler<GetValueSlabsQuery, IReadOnlyList<ValueSlabDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetValueSlabsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<ValueSlabDto>> HandleAsync(GetValueSlabsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.ValuePriceSlabs.AsNoTracking().Where(s => s.BrandId == brandId);
        if (!q.IncludeArchived) query = query.Where(s => s.Status != "archived");
        // A service filter surfaces that service's lane plus the brand-wide (null) lane that also applies.
        if (q.ServiceId is { } sid) query = query.Where(s => s.ServiceId == sid || s.ServiceId == null);

        var rows = await query
            .OrderBy(s => s.ServiceId == null ? 1 : 0)   // service-specific first
            .ThenBy(s => s.MinValue)
            .Select(s => new
            {
                s.Id, s.BrandId, s.ServiceId, s.MinValue, s.MaxValue, s.Price, s.Status,
                s.CreatedAt, s.UpdatedAt,
                ServiceName = s.Service != null ? s.Service.Name : null,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ValueSlabDto(
            r.Id, r.BrandId, r.ServiceId, r.ServiceName, r.MinValue, r.MaxValue, r.Price,
            r.Status, r.CreatedAt, r.UpdatedAt)).ToList();
    }
}
