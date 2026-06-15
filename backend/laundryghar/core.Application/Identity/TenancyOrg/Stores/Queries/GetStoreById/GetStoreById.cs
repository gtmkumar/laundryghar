using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Stores.Queries.GetStoreById;

public sealed record GetStoreByIdQuery(Guid Id) : IQuery<StoreDto?>;

public class GetStoreByIdQueryHandler : IQueryHandler<GetStoreByIdQuery, StoreDto?>
{
    private readonly ICoreDbContext _db;

    public GetStoreByIdQueryHandler(ICoreDbContext db) => _db = db;

    public Task<StoreDto?> HandleAsync(GetStoreByIdQuery query, CancellationToken cancellationToken) =>
        _db.Stores.AsNoTracking().Where(s => s.Id == query.Id)
            .Select(s => new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
