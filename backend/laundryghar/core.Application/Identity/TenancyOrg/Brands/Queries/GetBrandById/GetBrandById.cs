using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Brands.Queries.GetBrandById;

public sealed record GetBrandByIdQuery(Guid Id) : IQuery<BrandDto?>;

public class GetBrandByIdQueryHandler : IQueryHandler<GetBrandByIdQuery, BrandDto?>
{
    private readonly ICoreDbContext _db;

    public GetBrandByIdQueryHandler(ICoreDbContext db) => _db = db;

    public Task<BrandDto?> HandleAsync(GetBrandByIdQuery query, CancellationToken cancellationToken) =>
        _db.Brands.AsNoTracking()
            .Where(b => b.Id == query.Id)
            .Select(b => new BrandDto(b.Id, b.PlatformId, b.Code, b.Name, b.LegalName, b.Tagline,
                b.CurrencyCode, b.Timezone, b.VerticalKey, b.Status, b.CreatedAt, b.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
