using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Queries;

public sealed record GetBrandEntitlementsQuery(Guid BrandId) : IQuery<BrandEntitlementsDto?>;

/// <summary>
/// A brand's entitlement matrix, keyed on FEATURES (the sellable catalogue) rather than modules
/// (the sidebar) since migration 0005. Each row carries the modules that feature unlocks, so the
/// console can show what buying it actually gives — including the sellable features that unlock no
/// menu at all (custom_domain, api_access, white_label_app).
/// </summary>
public class GetBrandEntitlementsQueryHandler : IQueryHandler<GetBrandEntitlementsQuery, BrandEntitlementsDto?>
{
    private readonly ICoreDbContext _db;
    public GetBrandEntitlementsQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<BrandEntitlementsDto?> HandleAsync(GetBrandEntitlementsQuery q, CancellationToken ct)
    {
        var brand = await _db.Brands.AsNoTracking()
            .Where(b => b.Id == q.BrandId)
            .Select(b => new { b.Id, b.Name, b.VerticalKey })
            .FirstOrDefaultAsync(ct);
        if (brand is null) return null;

        // Only features available to the brand's vertical appear in its matrix — a salon brand
        // never sees the laundry-only ones (e.g. fabrics). (Phase 2 slice 2C, now on features.)
        var features = (await _db.Features.AsNoTracking()
            .Where(f => f.Status == "active")
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Key)
            .Select(f => new { f.Key, f.Name, f.Description, f.IsCore, f.IsSellable, f.VerticalKey })
            .ToListAsync(ct))
            .Where(f => VerticalKey.IsAvailableTo(f.VerticalKey, brand.VerticalKey))
            .ToList();

        // The modules each feature unlocks, filtered by the same vertical rule so the console never
        // promises a module this brand could not see anyway.
        var moduleLabels = (await _db.Modules.AsNoTracking()
            .Where(m => m.Status == "active" && m.FeatureKey != null)
            .Select(m => new { m.FeatureKey, m.Key, m.Label, m.VerticalKey })
            .ToListAsync(ct))
            .Where(m => VerticalKey.IsAvailableTo(m.VerticalKey, brand.VerticalKey))
            .GroupBy(m => m.FeatureKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                          g => g.Select(m => new FeatureModuleDto(m.Key, m.Label)).ToList(),
                          StringComparer.OrdinalIgnoreCase);

        var rows = await _db.BrandFeatures.AsNoTracking()
            .Where(bf => bf.BrandId == q.BrandId)
            .Select(bf => new { bf.FeatureKey, bf.Enabled, bf.Source, bf.ValidUntil })
            .ToListAsync(ct);
        var byKey = rows.ToDictionary(r => r.FeatureKey, StringComparer.OrdinalIgnoreCase);

        var dto = features.Select(f =>
        {
            byKey.TryGetValue(f.Key, out var r);
            // Core features are always entitled; otherwise entitled iff an enabled row exists.
            var entitled = f.IsCore || (r is { Enabled: true });
            return new BrandFeatureDto(
                f.Key, f.Name, f.Description, f.IsCore, f.IsSellable, entitled,
                f.IsCore ? "core" : r?.Source, r?.ValidUntil,
                moduleLabels.GetValueOrDefault(f.Key, []));
        }).ToList();

        return new BrandEntitlementsDto(brand.Id, brand.Name, dto);
    }
}
