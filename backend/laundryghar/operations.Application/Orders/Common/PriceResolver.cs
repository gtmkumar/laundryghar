using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Orders.Common;

/// <summary>
/// Server-side price resolution — mirrors Catalog's ResolvePriceQuery logic.
/// Resolution rule (most-specific wins, fallback chain):
///   1. Store-scoped PUBLISHED price list (most recently published)
///   2. Franchise-scoped PUBLISHED price list
///   3. Brand-scoped PUBLISHED price list
/// Within a scope, the most-recently-published list wins.
/// Row match: brandId + serviceId + itemId. VariantId preference: variant-specific row > null-variant row.
/// Returns null if no published price found at any scope.
/// </summary>
public static class PriceResolver
{
    public sealed record ResolvedPrice(
        Guid PriceListItemId,
        decimal BasePrice,
        decimal? ExpressPrice,
        decimal TaxRatePercent,
        bool IsTaxable,
        string ServiceNameSnapshot,
        string ItemNameSnapshot
    );

    public static async Task<ResolvedPrice?> ResolveAsync(
        IOperationsDbContext db,
        Guid brandId,
        Guid storeId,
        Guid serviceId,
        Guid itemId,
        Guid? variantId,
        CancellationToken ct)
    {
        // Resolve franchise from store
        var store = await db.Stores
            .Where(s => s.Id == storeId)
            .Select(s => new { s.FranchiseId })
            .FirstOrDefaultAsync(ct);
        var franchiseId = store?.FranchiseId;

        // Load all published lists for this brand
        var publishedLists = await db.PriceLists
            .Where(pl => pl.BrandId == brandId
                      && pl.IsPublished
                      && pl.Status == "published"
                      && pl.DeletedAt == null)
            .Select(pl => new { pl.Id, pl.ScopeType, pl.FranchiseId, pl.StoreId, pl.PublishedAt })
            .ToListAsync(ct);

        // Priority: store(2) > franchise(1) > brand(0); most-recently-published within same scope
        var scopedIds = publishedLists
            .Where(pl =>
                (pl.ScopeType == "store"     && pl.StoreId     == storeId)     ||
                (pl.ScopeType == "franchise"  && franchiseId.HasValue && pl.FranchiseId == franchiseId) ||
                pl.ScopeType == "brand")
            .OrderByDescending(pl =>
                pl.ScopeType == "store"     ? 2 :
                pl.ScopeType == "franchise" ? 1 : 0)
            .ThenByDescending(pl => pl.PublishedAt)
            .Select(pl => pl.Id)
            .ToList();

        foreach (var listId in scopedIds)
        {
            // Fetch item + service snapshots alongside price row
            var priceRow = await db.PriceListItems
                .Where(pi =>
                    pi.PriceListId == listId &&
                    pi.ItemId     == itemId &&
                    pi.ServiceId  == serviceId &&
                    pi.IsActive   && pi.Status == "active" &&
                    (variantId == null
                        ? pi.ItemVariantId == null
                        : (pi.ItemVariantId == variantId || pi.ItemVariantId == null)))
                .OrderByDescending(pi => pi.ItemVariantId != null ? 1 : 0)
                .Join(db.Items.Where(i => i.Id == itemId),
                    pi => pi.ItemId, i => i.Id,
                    (pi, i) => new { pi, ItemName = i.Name })
                .Join(db.Services.Where(s => s.Id == serviceId),
                    x => x.pi.ServiceId, s => s.Id,
                    (x, s) => new
                    {
                        x.pi.Id, x.pi.BasePrice, x.pi.ExpressPrice,
                        x.pi.TaxRatePercent, x.pi.IsTaxable,
                        ItemName = x.ItemName,
                        ServiceName = s.Name
                    })
                .FirstOrDefaultAsync(ct);

            if (priceRow is null) continue;

            return new ResolvedPrice(
                priceRow.Id,
                priceRow.BasePrice,
                priceRow.ExpressPrice,
                priceRow.TaxRatePercent,
                priceRow.IsTaxable,
                priceRow.ServiceName,
                priceRow.ItemName);
        }

        return null;
    }
}
