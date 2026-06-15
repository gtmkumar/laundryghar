using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Warehouses.Queries.GetWarehouses;

public sealed record GetWarehousesQuery(Guid? BrandId, Guid? FranchiseId, int Page = 1, int PageSize = 20) : IQuery<PaginatedList<WarehouseDto>>;

public class GetWarehousesQueryHandler : IQueryHandler<GetWarehousesQuery, PaginatedList<WarehouseDto>>
{
    private readonly ICoreDbContext _db;

    public GetWarehousesQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PaginatedList<WarehouseDto>> HandleAsync(GetWarehousesQuery query, CancellationToken cancellationToken)
    {
        var q = _db.Warehouses.AsNoTracking().AsQueryable();
        if (query.BrandId.HasValue)     q = q.Where(w => w.BrandId     == query.BrandId.Value);
        if (query.FranchiseId.HasValue) q = q.Where(w => w.FranchiseId == query.FranchiseId.Value);
        return PaginatedList<WarehouseDto>.CreateAsync(
            q.OrderBy(w => w.Name).Select(w => new WarehouseDto(w.Id, w.BrandId, w.FranchiseId, w.Code, w.Name, w.City, w.Status, w.CreatedAt)),
            query.Page, query.PageSize, cancellationToken);
    }
}
