using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Garments.Dtos;

namespace operations.Application.Warehouse.Garments.Queries.GetTags;

// ── Tags ──────────────────────────────────────────────────────────────────────

public sealed record GetTagsQuery(int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<GarmentTagDto>>;

public class GetTagsQueryHandler : IQueryHandler<GetTagsQuery, PaginatedList<GarmentTagDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetTagsQueryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public Task<PaginatedList<GarmentTagDto>> HandleAsync(GetTagsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.FulfillmentUnitTags.Where(t => t.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Status)) q = q.Where(t => t.Status == query.Status);

        return PaginatedList<GarmentTagDto>.CreateAsync(
            q.OrderByDescending(t => t.CreatedAt)
                .Select(t => new GarmentTagDto(
                    t.Id, t.BrandId, t.TagCode, t.TagFormat,
                    t.BatchNumber, t.AssignedToFulfillmentUnitId,
                    t.AssignedAt, t.IsDamaged, t.Status, t.CreatedAt)),
            query.Page, query.PageSize, cancellationToken);
    }
}
