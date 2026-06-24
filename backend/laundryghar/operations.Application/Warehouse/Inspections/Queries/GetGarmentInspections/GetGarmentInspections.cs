using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Commands.CreateInspection;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Queries.GetGarmentInspections;

public sealed record GetGarmentInspectionsQuery(Guid FulfillmentUnitId, int Page, int PageSize)
    : IQuery<PaginatedList<GarmentInspectionDto>>;

public sealed class GetGarmentInspectionsQueryHandler
    : IQueryHandler<GetGarmentInspectionsQuery, PaginatedList<GarmentInspectionDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentInspectionsQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<GarmentInspectionDto>> HandleAsync(GetGarmentInspectionsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<GarmentInspectionDto>.CreateAsync(
            _db.FulfillmentUnitInspections
                .Where(i => i.FulfillmentUnitId == query.FulfillmentUnitId && i.BrandId == brandId)
                .OrderByDescending(i => i.InspectedAt)
                .Select(i => CreateInspectionCommandHandler.ToDto(i, null)),
            query.Page, query.PageSize, cancellationToken);
    }
}
