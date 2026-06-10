namespace laundryghar.Orders.Application.Pickup.Dtos;

// ── Estimated cart item submitted by customer at booking time ────────────────
// These lines are ESTIMATES. The real order is created after weighing at store.

/// <summary>
/// A single estimated cart line from the customer booking flow.
/// All fields except <see cref="DisplayLabel"/> and <see cref="Quantity"/> are optional —
/// the customer may not have selected a specific catalog item.
/// </summary>
public sealed record RequestedCartItemDto(
    /// <summary>Catalog service id (nullable — customer may not select a specific service).</summary>
    Guid? ServiceId,
    /// <summary>Catalog item id (nullable).</summary>
    Guid? ItemId,
    /// <summary>Human-readable label shown to the customer on the booking confirmation, e.g. "Shirt – Wash &amp; Iron".</summary>
    string DisplayLabel,
    /// <summary>Quantity >= 1.</summary>
    int Quantity,
    /// <summary>Estimated unit price from the price list; null when no price was available.</summary>
    decimal? EstimatedUnitPrice
);

// ── Request / response records ────────────────────────────────────────────────

public sealed record CreatePickupRequestRequest(
    Guid AddressId,
    Guid? SlotId,
    DateOnly PickupDate,
    TimeOnly PickupWindowStart,
    TimeOnly PickupWindowEnd,
    bool IsExpress,
    int? EstimatedItems,
    decimal? EstimatedAmount,
    Guid[] ServicesRequested,
    string? CustomerNotes,
    /// <summary>
    /// Estimated cart lines submitted by the customer during the booking flow.
    /// Required for customer self-schedule; may be empty for admin-created requests.
    /// </summary>
    RequestedCartItemDto[]? CartItems,
    /// <summary>
    /// Customer payment intent: "wallet" | "cod" | "upi-deferred".
    /// UPI/card selections are normalised to "upi-deferred" at the handler layer.
    /// </summary>
    string? PaymentPreference
);

/// <summary>Full pickup request response — returned to both customer and admin endpoints.</summary>
public sealed record PickupRequestDto(
    Guid Id,
    string RequestNumber,
    Guid BrandId,
    Guid? StoreId,
    Guid CustomerId,
    Guid AddressId,
    Guid? PickupSlotId,
    DateOnly PickupDate,
    TimeOnly PickupWindowStart,
    TimeOnly PickupWindowEnd,
    bool IsExpress,
    int? EstimatedItems,
    decimal? EstimatedAmount,
    string Status,
    DateTimeOffset CreatedAt,
    /// <summary>Estimated cart lines; empty array when no items were supplied.</summary>
    IReadOnlyList<RequestedCartItemDto> CartItems,
    /// <summary>Customer payment intent recorded at booking time.</summary>
    string PaymentPreference
);

public sealed record AssignPickupRequest(Guid RiderId);

/// <summary>Request body for POST /pickup-requests/{id}/reject.</summary>
public sealed record RejectPickupRequest(
    /// <summary>Mandatory human-readable reason, max 300 chars.</summary>
    string Reason
);

public sealed record DeliveryAssignmentDto(
    Guid Id,
    Guid BrandId,
    Guid StoreId,
    Guid RiderId,
    Guid? OrderId,
    Guid? PickupRequestId,
    string LegType,
    DateTimeOffset AssignedAt,
    string Status
);

public sealed record CreateDeliveryAssignmentRequest(
    Guid RiderId,
    Guid? OrderId,
    DateTimeOffset? OrderCreatedAt,
    Guid? PickupRequestId,
    string LegType
);

public sealed record UpdateDeliveryAssignmentRequest(string Status);
