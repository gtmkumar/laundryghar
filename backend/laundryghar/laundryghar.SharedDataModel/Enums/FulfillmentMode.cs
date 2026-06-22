namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// How an order is fulfilled — the leg topology / resource model the owning
/// <c>IFulfillmentStrategy</c> implements. Denormalized onto
/// <c>order_lifecycle.orders.fulfillment_mode</c>.
/// </summary>
public static class FulfillmentMode
{
    /// <summary>Laundry: collect → process → deliver (has a store-drop + processing pipeline).</summary>
    public const string ProcessDeliver = "process_deliver";

    /// <summary>Salon: an on-site booked time slot against staff/resource capacity (no pickup leg).</summary>
    public const string Appointment = "appointment";

    /// <summary>Logistics: a single origin → destination trip (no store drop, no processing).</summary>
    public const string PointToPoint = "point_to_point";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { ProcessDeliver, Appointment, PointToPoint };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>The default fulfilment mode for a vertical, used to denormalize onto new orders.</summary>
    public static string DefaultFor(string? verticalKey) => verticalKey switch
    {
        VerticalKey.Salon     => Appointment,
        VerticalKey.Logistics => PointToPoint,
        _                     => ProcessDeliver, // laundry + unknown → preserves existing behaviour
    };
}
