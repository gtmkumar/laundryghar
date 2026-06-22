using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Queries.Matrix;

/// <summary>
/// Builds the price-matrix view: rows are the resolved price list's items (base/Cotton rate),
/// columns are fabric types (each row's fabric price = base × multiplier, computed client-side).
/// Resolves the effective list store → franchise → brand (store filter optional), like ResolvePrice.
/// </summary>
public sealed record GetPricingMatrixQuery(Guid? StoreId) : IQuery<PricingMatrixDto>;

public sealed class GetPricingMatrixHandler : IQueryHandler<GetPricingMatrixQuery, PricingMatrixDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetPricingMatrixHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PricingMatrixDto> HandleAsync(GetPricingMatrixQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var fabrics = await _db.FabricTypes.AsNoTracking()
            .Where(f => f.BrandId == brandId && f.DeletedAt == null && f.Status == "active")
            .OrderBy(f => f.DisplayOrder).ThenBy(f => f.Name)
            .Select(f => new PricingMatrixFabricDto(f.Code, f.Name, f.PriceMultiplier))
            .ToListAsync(ct);

        var stores = await _db.Stores.AsNoTracking()
            .Where(s => s.BrandId == brandId && s.DeletedAt == null)
            .OrderBy(s => s.Name)
            .Select(s => new PricingMatrixStoreDto(s.Id, s.Name))
            .ToListAsync(ct);

        // Resolve the effective published list: store → franchise → brand (default wins within a tie).
        var published = await _db.PriceLists.AsNoTracking()
            .Where(pl => pl.IsPublished && pl.DeletedAt == null && pl.Status == "published" && pl.BrandId == brandId)
            .Select(pl => new { pl.Id, pl.Name, pl.ScopeType, pl.FranchiseId, pl.StoreId, pl.PublishedAt, pl.IsDefault })
            .ToListAsync(ct);

        Guid? franchiseId = null;
        if (q.StoreId is { } sid)
            franchiseId = (await _db.Stores.AsNoTracking().Where(s => s.Id == sid).Select(s => new { s.FranchiseId }).FirstOrDefaultAsync(ct))?.FranchiseId;

        var chosen = published
            .Where(pl =>
                (q.StoreId.HasValue && pl.ScopeType == "store" && pl.StoreId == q.StoreId) ||
                (franchiseId.HasValue && pl.ScopeType == "franchise" && pl.FranchiseId == franchiseId) ||
                pl.ScopeType == "brand")
            .OrderByDescending(pl => pl.ScopeType == "store" ? 2 : pl.ScopeType == "franchise" ? 1 : 0)
            .ThenByDescending(pl => pl.IsDefault)
            .ThenByDescending(pl => pl.PublishedAt)
            .FirstOrDefault();

        if (chosen is null)
            return new PricingMatrixDto(null, null, fabrics, [], stores);

        var raw = await _db.PriceListItems.AsNoTracking()
            .Where(pi => pi.PriceListId == chosen.Id && pi.IsActive && pi.Status == "active")
            .OrderBy(pi => pi.CreatedAt)
            .Select(pi => new { pi.BasePrice, pi.DisplayLabel, ItemName = pi.Item.Name, ServiceName = pi.Service.Name })
            .ToListAsync(ct);

        var rows = raw.Select(r => new PricingMatrixRowDto(
            string.IsNullOrWhiteSpace(r.DisplayLabel)
                ? string.Join(" · ", new[] { r.ItemName, r.ServiceName }.Where(s => !string.IsNullOrWhiteSpace(s)))
                : r.DisplayLabel!,
            r.BasePrice)).ToList();

        return new PricingMatrixDto(chosen.Name, chosen.ScopeType, fabrics, rows, stores);
    }
}
