namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// What kind of thing a catalog item represents — the vertical-neutral discriminator on
/// <c>customer_catalog.items.catalog_kind</c>. Lets catalog/pricing/clients branch on the item
/// shape without baking in laundry assumptions; kind-specific attributes live in the
/// <c>items.attributes</c> jsonb bag. Introduced in multi-vertical Phase 2 (slice 2A).
///
/// <para>Orthogonal to <see cref="VerticalKey"/>: a brand's vertical sets the DEFAULT kind for its
/// items (see <see cref="DefaultFor"/>), but a single catalog may legitimately mix kinds.</para>
/// </summary>
public static class CatalogKind
{
    /// <summary>Laundry: a launderable garment/article — the existing default.</summary>
    public const string LaundryGarment = "laundry_garment";

    /// <summary>Salon: a bookable service rendered against staff/resource time.</summary>
    public const string Service = "service";

    /// <summary>Logistics: a parcel/shipment line.</summary>
    public const string Parcel = "parcel";

    /// <summary>A generic sellable product with no fulfilment pipeline.</summary>
    public const string Product = "product";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { LaundryGarment, Service, Parcel, Product };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>The default catalog kind for a brand's vertical, used when none is specified.</summary>
    public static string DefaultFor(string? verticalKey) => verticalKey switch
    {
        VerticalKey.Salon     => Service,
        VerticalKey.Logistics => Parcel,
        _                     => LaundryGarment, // laundry + unknown → preserves existing behaviour
    };
}
