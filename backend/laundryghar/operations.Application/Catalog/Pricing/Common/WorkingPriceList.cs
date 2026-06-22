using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Common;

/// <summary>
/// Resolves the brand "working price list" that the Items screen edits inline.
/// This is the brand-scoped default list (e.g. MAIN-BRAND-PL). Inline item-price
/// edits write base (Cotton/fabric-null) rows here directly — a deliberately
/// live-editable surface, distinct from the versioned draft→publish flow that the
/// Price-lists tab still offers for franchise/store overrides and new versions.
/// </summary>
public static class WorkingPriceList
{
    /// <summary>Read-only id of the brand working list, or null if none exists yet.</summary>
    public static async Task<Guid?> ResolveIdAsync(IOperationsDbContext db, Guid brandId, CancellationToken ct)
    {
        var candidates = await db.PriceLists.AsNoTracking()
            .Where(pl => pl.BrandId == brandId && pl.ScopeType == "brand" && pl.DeletedAt == null)
            .Select(pl => new { pl.Id, pl.IsDefault, pl.IsPublished, pl.PublishedAt, pl.CreatedAt })
            .ToListAsync(ct);

        return candidates
            .OrderByDescending(pl => pl.IsDefault)
            .ThenByDescending(pl => pl.IsPublished)
            .ThenByDescending(pl => pl.PublishedAt ?? pl.CreatedAt)
            .Select(pl => (Guid?)pl.Id)
            .FirstOrDefault();
    }

    /// <summary>Tracked working list, creating a live default brand list if none exists.</summary>
    public static async Task<PriceList> EnsureAsync(IOperationsDbContext db, Guid brandId, Guid? actorId, CancellationToken ct)
    {
        var existing = await ResolveIdAsync(db, brandId, ct);
        if (existing is { } id)
        {
            var tracked = await db.PriceLists.FirstAsync(pl => pl.Id == id, ct);
            return tracked;
        }

        var now = DateTimeOffset.UtcNow;
        var created = new PriceList
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            Code          = "MAIN-WORKING",
            Name          = "Working Price List",
            CurrencyCode  = "INR",
            ScopeType     = "brand",
            VersionNumber = 1,
            EffectiveFrom = now,
            IsDefault     = true,
            IsPublished   = true,
            PublishedAt   = now,
            PublishedBy   = actorId,
            Status        = "published",
            CreatedAt     = now,
            UpdatedAt     = now,
            CreatedBy     = actorId,
            UpdatedBy     = actorId,
            Version       = 1,
        };
        db.PriceLists.Add(created);
        return created;
    }
}
