using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Garments.Commands.CreateGarment;
using operations.Application.Warehouse.Garments.Dtos;

namespace operations.Application.Warehouse.Garments.Queries.GetGarments;

public sealed record GetGarmentsQuery(
    int Page, int PageSize,
    string? Stage, Guid? StoreId, Guid? BatchId
) : IQuery<PaginatedList<GarmentDto>>;

public class GetGarmentsQueryHandler : IQueryHandler<GetGarmentsQuery, PaginatedList<GarmentDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetGarmentsQueryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public Task<PaginatedList<GarmentDto>> HandleAsync(GetGarmentsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        // Defense-in-depth brand predicate (superuser bypasses RLS)
        var q = _db.Garments.Where(g => g.BrandId == brandId);

        if (!string.IsNullOrEmpty(query.Stage)) q = q.Where(g => g.CurrentStage == query.Stage);
        if (query.StoreId.HasValue)             q = q.Where(g => g.StoreId == query.StoreId.Value);
        if (query.BatchId.HasValue)             q = q.Where(g => g.CurrentBatchId == query.BatchId.Value);

        return PaginatedList<GarmentDto>.CreateAsync(
            q.OrderByDescending(g => g.CreatedAt).Select(g => CreateGarmentCommandHandler.ToDto(g)),
            query.Page, query.PageSize, cancellationToken);
    }
}
