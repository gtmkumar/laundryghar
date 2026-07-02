using operations.Application.Catalog.Pricing.Common;
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
///
/// <para>Value-slab items (GH #22): when the item's <c>PricingMode == value_slab</c>, the base price
/// comes from the brand's <see cref="ValueSlabResolver">value slabs</see> keyed on the caller's
/// declared value (price lists are bypassed). A missing/non-positive declared value or an
/// uncovered value throws a structured 422.</para>
/// </summary>
public static class PriceResolver
{
    public sealed record ResolvedPrice(
        Guid? PriceListItemId,
        decimal BasePrice,
        decimal? ExpressPrice,
        decimal TaxRatePercent,
        bool IsTaxable,
        string ServiceNameSnapshot,
        string ItemNameSnapshot,
        /// <summary>True when the base price came from a value slab (no price-list row). GH #22.</summary>
        bool IsValueSlab = false
    );

    public static async Task<ResolvedPrice?> ResolveAsync(
        IOperationsDbContext db,
        Guid brandId,
        Guid storeId,
        Guid serviceId,
        Guid itemId,
        Guid? variantId,
        CancellationToken ct,
        decimal? declaredValue = null)
    {
        // Delegates to the shared resolver (GH #24 dedupe) and maps to the order-path shape.
        var r = await SharedPriceResolver.ResolveAsync(
            db, brandId, storeId, itemId, serviceId, variantId, declaredValue, ct);
        if (r is null) return null;

        // PriceListItemId is the matched row id for a standard price, null for a value slab.
        return new ResolvedPrice(
            r.PriceListItemId,
            r.BasePrice,
            r.ExpressPrice,
            r.TaxRatePercent,
            r.IsTaxable,
            r.ServiceName,
            r.ItemName,
            r.IsValueSlab);
    }
}
