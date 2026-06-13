using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using laundryghar.Warehouse.Application.Inspections.Commands;
using laundryghar.Warehouse.Application.Inspections.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Warehouse.Application.Inspections.Queries;

public sealed record GetGarmentInspectionsQuery(Guid GarmentId, int Page, int PageSize)
    : IRequest<PaginatedList<GarmentInspectionDto>>;

public sealed class GetGarmentInspectionsHandler
    : IRequestHandler<GetGarmentInspectionsQuery, PaginatedList<GarmentInspectionDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentInspectionsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<GarmentInspectionDto>> Handle(GetGarmentInspectionsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<GarmentInspectionDto>.CreateAsync(
            _db.GarmentInspections
                .Where(i => i.GarmentId == q.GarmentId && i.BrandId == brandId)
                .OrderByDescending(i => i.InspectedAt)
                .Select(i => CreateInspectionHandler.ToDto(i, null)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetGarmentConditionsQuery(int Page, int PageSize)
    : IRequest<PaginatedList<GarmentConditionDto>>;

public sealed class GetGarmentConditionsHandler
    : IRequestHandler<GetGarmentConditionsQuery, PaginatedList<GarmentConditionDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentConditionsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<GarmentConditionDto>> Handle(GetGarmentConditionsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<GarmentConditionDto>.CreateAsync(
            _db.GarmentConditions
                .Where(c => c.BrandId == brandId)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => CreateGarmentConditionHandler.ToDto(c)),
            q.Page, q.PageSize, ct);
    }
}
