using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Queries;

public sealed record GetModuleBundlesQuery : IQuery<IReadOnlyList<ModuleBundleDto>>;

public class GetModuleBundlesQueryHandler : IQueryHandler<GetModuleBundlesQuery, IReadOnlyList<ModuleBundleDto>>
{
    private readonly ICoreDbContext _db;
    public GetModuleBundlesQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<ModuleBundleDto>> HandleAsync(GetModuleBundlesQuery q, CancellationToken ct)
    {
        var bundles = await _db.ModuleBundles.AsNoTracking()
            .Select(b => new { b.Code, b.Name, b.Description, b.VerticalKey, b.Price, b.BillingInterval, b.CurrencyCode, b.IsPublic })
            .ToListAsync(ct);

        // A bundle packages FEATURES since migration 0005 — what the tier sells, not what it shows.
        var labels = await _db.Features.AsNoTracking()
            .ToDictionaryAsync(f => f.Key, f => f.Name, ct);

        var items = await _db.BundleFeatures.AsNoTracking()
            .Select(i => new { i.BundleCode, i.FeatureKey })
            .ToListAsync(ct);
        var byBundle = items.GroupBy(i => i.BundleCode)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return bundles.Select(b => new ModuleBundleDto(
            b.Code, b.Name, b.Description,
            (byBundle.TryGetValue(b.Code, out var its) ? its : [])
                .Select(i => new ModuleBundleItemDto(i.FeatureKey, labels.GetValueOrDefault(i.FeatureKey, i.FeatureKey)))
                .ToList(),
            b.VerticalKey, b.Price, b.BillingInterval, b.CurrencyCode, b.IsPublic)).ToList();
    }
}
