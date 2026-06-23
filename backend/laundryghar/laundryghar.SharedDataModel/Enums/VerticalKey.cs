namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// The industry vertical a brand operates. A <c>Brand</c> operates exactly ONE vertical
/// (<c>tenancy_org.brands.vertical_key</c>), which is denormalized onto each order
/// (<c>order_lifecycle.orders.vertical_key</c>) and drives <c>IFulfillmentStrategy</c>
/// resolution, mobile feature-pack mounting, ModuleBundle defaults, and per-vertical i18n.
///
/// <para>Orthogonal to <see cref="JobType"/>: <c>VerticalKey</c> selects which strategy owns
/// stages/catalog/resources, while <c>JobType</c> describes the dispatch/vehicle-tier shape.</para>
/// </summary>
public static class VerticalKey
{
    public const string Laundry   = "laundry";
    public const string Salon     = "salon";
    public const string Logistics = "logistics";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Laundry, Salon, Logistics };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
