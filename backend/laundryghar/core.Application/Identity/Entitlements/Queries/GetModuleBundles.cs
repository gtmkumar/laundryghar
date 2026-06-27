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
            .Select(b => new { b.Code, b.Name, b.Description, b.VerticalKey })
            .ToListAsync(ct);

        var labels = await _db.Modules.AsNoTracking()
            .ToDictionaryAsync(m => m.Key, m => m.Label, ct);

        var items = await _db.ModuleBundleItems.AsNoTracking()
            .Select(i => new { i.BundleCode, i.ModuleKey })
            .ToListAsync(ct);
        var byBundle = items.GroupBy(i => i.BundleCode)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return bundles.Select(b => new ModuleBundleDto(
            b.Code, b.Name, b.Description,
            (byBundle.TryGetValue(b.Code, out var its) ? its : [])
                .Select(i => new ModuleBundleItemDto(i.ModuleKey, labels.GetValueOrDefault(i.ModuleKey, i.ModuleKey)))
                .ToList(),
            b.VerticalKey)).ToList();
    }
}
