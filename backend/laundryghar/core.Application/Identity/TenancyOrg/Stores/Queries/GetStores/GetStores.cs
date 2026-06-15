using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Stores.Queries.GetStores;

public sealed record GetStoresQuery(Guid? BrandId, Guid? FranchiseId, int Page = 1, int PageSize = 20) : IQuery<PaginatedList<StoreDto>>;

public class GetStoresQueryHandler : IQueryHandler<GetStoresQuery, PaginatedList<StoreDto>>
{
    private readonly ICoreDbContext _db;

    public GetStoresQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PaginatedList<StoreDto>> HandleAsync(GetStoresQuery query, CancellationToken cancellationToken)
    {
        var q = _db.Stores.AsNoTracking().AsQueryable();
        if (query.BrandId.HasValue)     q = q.Where(s => s.BrandId     == query.BrandId.Value);
        if (query.FranchiseId.HasValue) q = q.Where(s => s.FranchiseId == query.FranchiseId.Value);
        return PaginatedList<StoreDto>.CreateAsync(
            q.OrderBy(s => s.Name).Select(s => new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt)),
            query.Page, query.PageSize, cancellationToken);
    }
}
