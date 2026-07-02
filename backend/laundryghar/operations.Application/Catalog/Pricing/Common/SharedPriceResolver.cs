using System.Globalization;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Common;

/// <summary>
/// The single source of truth for effective-price resolution, shared by the admin
/// <c>ResolvePriceHandler</c> and the order-path <c>PriceResolver</c> so the two can never drift
/// (GH #24 resolver dedupe). Both call <see cref="ResolveAsync"/> and map the rich
/// <see cref="Resolution"/> onto their own public shapes.
///
/// <para>Resolution rule (most-specific wins, fallback chain):</para>
/// <list type="number">
///   <item>Store-scoped PUBLISHED price list (when a store is supplied).</item>
///   <item>Franchise-scoped PUBLISHED price list (franchise resolved from the store).</item>
///   <item>Brand-scoped PUBLISHED price list.</item>
/// </list>
/// Within a scope the most-recently-published list wins. Row match is brandId + serviceId + itemId,
/// preferring a variant-specific row over the null-variant row.
///
/// <para>Value-slab items (GH #22): when the item's <c>PricingMode == value_slab</c> the base price
/// comes from the brand's value slabs keyed on the declared value; price lists are bypassed. A
/// missing/non-positive declared value or an uncovered value throws a structured 422.</para>
/// </summary>
public static class SharedPriceResolver
{
    /// <summary>
    /// The resolved price plus everything either caller needs. <see cref="PriceListItemId"/> is the
    /// matched price_list_items row id (null for value-slab); <see cref="PriceListId"/> is the owning
    /// list (<see cref="Guid.Empty"/> for value-slab).
    /// </summary>
    public sealed record Resolution(
        Guid? PriceListItemId,
        Guid PriceListId,
        string PriceListCode,
        string ScopeType,
        decimal BasePrice,
        decimal? ExpressPrice,
        decimal TaxRatePercent,
        bool IsTaxable,
        string ItemName,
        string ServiceName,
        string? DisplayLabel,
        bool IsValueSlab);

    public static async Task<Resolution?> ResolveAsync(
        IOperationsDbContext db,
        Guid brandId,
        Guid? storeId,
        Guid itemId,
        Guid serviceId,
        Guid? variantId,
        decimal? declaredValue,
        CancellationToken ct)
    {
        // ── Value-slab items: price from declared value, not price lists (GH #22). ──
        var itemInfo = await db.Items.AsNoTracking()
            .Where(i => i.Id == itemId && i.BrandId == brandId)
            .Select(i => new { i.Name, i.PricingMode })
            .FirstOrDefaultAsync(ct);
        if (itemInfo is not null && itemInfo.PricingMode == laundryghar.SharedDataModel.Enums.PricingMode.ValueSlab)
        {
            ValueSlabResolver.RequireDeclaredValue(declaredValue, itemId, itemInfo.Name);
            var slabPrice = await ValueSlabResolver.ResolveSlabPriceAsync(
                db, brandId, serviceId, declaredValue!.Value, itemId, ct);
            var svcName = await db.Services.AsNoTracking()
                .Where(s => s.Id == serviceId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
            // Express handling: base stays the slab price; the express surcharge % from settings
            // applies downstream unchanged (so ExpressPrice is left null here).
            return new Resolution(
                PriceListItemId: null,
                PriceListId: Guid.Empty,
                PriceListCode: "value_slab",
                ScopeType: "value_slab",
                BasePrice: slabPrice,
                ExpressPrice: null,
                TaxRatePercent: 0m,
                IsTaxable: true,
                ItemName: itemInfo.Name,
                ServiceName: svcName,
                DisplayLabel: $"Declared value {declaredValue.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
                IsValueSlab: true);
        }

        // Resolve franchise from the store (only when a store is supplied).
        Guid? franchiseId = null;
        if (storeId.HasValue)
        {
            var store = await db.Stores
                .Where(s => s.Id == storeId.Value)
                .Select(s => new { s.FranchiseId })
                .FirstOrDefaultAsync(ct);
            franchiseId = store?.FranchiseId;
        }

        // Load all published lists for this brand (brand predicate = defense-in-depth over RLS).
        var publishedLists = await db.PriceLists
            .Where(pl => pl.BrandId == brandId
                      && pl.IsPublished
                      && pl.Status == "published"
                      && pl.DeletedAt == null)
            .Select(pl => new { pl.Id, pl.Code, pl.ScopeType, pl.FranchiseId, pl.StoreId, pl.PublishedAt })
            .ToListAsync(ct);

        // Priority: store(2) > franchise(1) > brand(0); most-recently-published within same scope.
        var scoped = publishedLists
            .Where(pl =>
                (storeId.HasValue    && pl.ScopeType == "store"     && pl.StoreId     == storeId)     ||
                (franchiseId.HasValue && pl.ScopeType == "franchise" && pl.FranchiseId == franchiseId) ||
                pl.ScopeType == "brand")
            .OrderByDescending(pl =>
                pl.ScopeType == "store"     ? 2 :
                pl.ScopeType == "franchise" ? 1 : 0)
            .ThenByDescending(pl => pl.PublishedAt)
            .Select(pl => new { pl.Id, pl.Code, pl.ScopeType })
            .ToList();

        foreach (var list in scoped)
        {
            var priceRow = await db.PriceListItems
                .Where(pi =>
                    pi.PriceListId == list.Id &&
                    pi.ItemId     == itemId &&
                    pi.ServiceId  == serviceId &&
                    pi.IsActive   && pi.Status == "active" &&
                    (variantId == null
                        ? pi.ItemVariantId == null
                        : (pi.ItemVariantId == variantId || pi.ItemVariantId == null)))
                .OrderByDescending(pi => pi.ItemVariantId != null ? 1 : 0) // variant-specific first
                .Join(db.Items.Where(i => i.Id == itemId),
                    pi => pi.ItemId, i => i.Id,
                    (pi, i) => new { pi, ItemName = i.Name })
                .Join(db.Services.Where(s => s.Id == serviceId),
                    x => x.pi.ServiceId, s => s.Id,
                    (x, s) => new
                    {
                        x.pi.Id, x.pi.BasePrice, x.pi.ExpressPrice,
                        x.pi.TaxRatePercent, x.pi.IsTaxable, x.pi.DisplayLabel,
                        x.ItemName, ServiceName = s.Name
                    })
                .FirstOrDefaultAsync(ct);

            if (priceRow is null) continue;

            return new Resolution(
                PriceListItemId: priceRow.Id,
                PriceListId: list.Id,
                PriceListCode: list.Code,
                ScopeType: list.ScopeType,
                BasePrice: priceRow.BasePrice,
                ExpressPrice: priceRow.ExpressPrice,
                TaxRatePercent: priceRow.TaxRatePercent,
                IsTaxable: priceRow.IsTaxable,
                ItemName: priceRow.ItemName,
                ServiceName: priceRow.ServiceName,
                DisplayLabel: priceRow.DisplayLabel,
                IsValueSlab: false);
        }

        return null; // no published price found at any scope
    }
}
