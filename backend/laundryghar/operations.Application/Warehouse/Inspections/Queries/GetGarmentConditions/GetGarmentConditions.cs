using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Commands.CreateGarmentCondition;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Queries.GetGarmentConditions;

public sealed record GetGarmentConditionsQuery(int Page, int PageSize)
    : IQuery<PaginatedList<GarmentConditionDto>>;

public sealed class GetGarmentConditionsQueryHandler
    : IQueryHandler<GetGarmentConditionsQuery, PaginatedList<GarmentConditionDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentConditionsQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<GarmentConditionDto>> HandleAsync(GetGarmentConditionsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<GarmentConditionDto>.CreateAsync(
            _db.GarmentConditions
                .Where(c => c.BrandId == brandId)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => CreateGarmentConditionCommandHandler.ToDto(c)),
            query.Page, query.PageSize, cancellationToken);
    }
}
