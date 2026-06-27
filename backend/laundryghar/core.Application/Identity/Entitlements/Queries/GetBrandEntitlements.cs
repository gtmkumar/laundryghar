using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Queries;

public sealed record GetBrandEntitlementsQuery(Guid BrandId) : IQuery<BrandEntitlementsDto?>;

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

        // Only modules available to the brand's vertical appear in its entitlement matrix —
        // a salon brand never sees the laundry-only modules (e.g. fabrics). (Phase 2 slice 2C.)
        var mods = (await _db.Modules.AsNoTracking()
            .Where(m => m.Status == "active")
            .OrderBy(m => m.NavOrder)
            .Select(m => new { m.Key, m.Label, m.Section, m.IsCore, m.VerticalKey })
            .ToListAsync(ct))
            .Where(m => VerticalKey.IsAvailableTo(m.VerticalKey, brand.VerticalKey))
            .ToList();

        var rows = await _db.BrandModules.AsNoTracking()
            .Where(bm => bm.BrandId == q.BrandId)
            .Select(bm => new { bm.ModuleKey, bm.Enabled, bm.Source, bm.ValidUntil })
            .ToListAsync(ct);
        var byKey = rows.ToDictionary(r => r.ModuleKey, StringComparer.OrdinalIgnoreCase);

        var dto = mods.Select(m =>
        {
            byKey.TryGetValue(m.Key, out var r);
            // Core modules are always entitled; otherwise entitled iff an enabled row exists.
            var entitled = m.IsCore || (r is { Enabled: true });
            return new BrandModuleDto(m.Key, m.Label, m.Section, m.IsCore, entitled,
                m.IsCore ? "core" : r?.Source, r?.ValidUntil);
        }).ToList();

        return new BrandEntitlementsDto(brand.Id, brand.Name, dto);
    }
}
