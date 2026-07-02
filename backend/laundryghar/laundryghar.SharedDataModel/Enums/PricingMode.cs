namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// How an item's price is determined — the discriminator on
/// <c>customer_catalog.items.pricing_mode</c> (GH #22).
///
/// <para><see cref="Standard"/> items are priced from published price-list rows (item × service).
/// <see cref="ValueSlab"/> items (branded/luxury garments) are priced from per-brand value slabs:
/// the customer declares the garment's value and the matching slab's price becomes the line's base
/// price. See <c>ValueSlabResolver</c>.</para>
/// </summary>
public static class PricingMode
{
    /// <summary>Priced from published price-list rows — the existing default.</summary>
    public const string Standard = "standard";

    /// <summary>Priced by declared-value slabs (branded/luxury garments).</summary>
    public const string ValueSlab = "value_slab";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Standard, ValueSlab };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
