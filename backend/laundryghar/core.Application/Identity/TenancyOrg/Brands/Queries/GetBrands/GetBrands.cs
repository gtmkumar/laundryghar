using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Brands.Queries.GetBrands;

// Platform-level list (paged). Brands are NOT brand-scoped — no brand filter.
public sealed record GetBrandsQuery(BrandListParams Params) : IQuery<PaginatedList<BrandDto>>;

public class GetBrandsQueryHandler : IQueryHandler<GetBrandsQuery, PaginatedList<BrandDto>>
{
    private readonly ICoreDbContext _db;

    public GetBrandsQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PaginatedList<BrandDto>> HandleAsync(GetBrandsQuery query, CancellationToken cancellationToken)
    {
        var q = _db.Brands.AsNoTracking();

        if (!string.IsNullOrEmpty(query.Params.Status))
            q = q.Where(b => b.Status == query.Params.Status);
        if (!string.IsNullOrEmpty(query.Params.Search))
            q = q.Where(b => b.Name.Contains(query.Params.Search)
                           || b.Code.Contains(query.Params.Search));

        var projected = q.OrderBy(b => b.Name).Select(b => new BrandDto(
            b.Id, b.PlatformId, b.Code, b.Name, b.LegalName, b.Tagline,
            b.CurrencyCode, b.Timezone, b.Status, b.CreatedAt, b.UpdatedAt));

        return PaginatedList<BrandDto>.CreateAsync(projected, query.Params.Page, query.Params.PageSize, cancellationToken);
    }
}
