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
    string? PaymentPreference,
    /// <summary>
    /// Optional body-field idempotency key. When provided (and not already sent
    /// via the Idempotency-Key header), duplicate submissions from the same
    /// customer return the existing request. Max 150 chars.
    /// </summary>
    string? IdempotencyKey = null,
    /// <summary>
    /// Source channel for this booking: app | web | mcp | whatsapp | pos | call.
    /// Defaults to "app". Can also be supplied via the X-Channel request header;
    /// the header takes precedence over the body field.
    /// </summary>
    string? Channel = null,
    /// <summary>
    /// Optional coupon code the customer wants to apply on the eventual order.
    /// Validated server-side at submit time (active, in-window, within per-customer usage limits).
    /// Stored on the pickup request and threaded into the order on admin conversion.
    /// </summary>
    string? CouponCode = null
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
    string PaymentPreference,
    /// <summary>
    /// Source channel that originated the booking: app | web | mcp | whatsapp | pos | call.
    /// </summary>
    string Source = "app",
    /// <summary>
    /// Idempotency key supplied at booking time. Null when no key was provided.
    /// </summary>
    string? IdempotencyKey = null,
    /// <summary>
    /// Coupon code the customer intends to apply, validated at booking time.
    /// Null when no coupon was submitted.
    /// </summary>
    string? CouponCode = null
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

/// <summary>Request body for POST /pickup-requests/{id}/reschedule.</summary>
public sealed record ReschedulePickupRequest(
    /// <summary>New pickup date. Must be today or in the future.</summary>
    DateOnly NewDate,
    /// <summary>New slot id. When provided the old slot capacity is released and the new slot is booked atomically.</summary>
    Guid? NewSlotId
);

/// <summary>Request body for POST /customer/coupons/validate (Orders service preview — no redemption written).</summary>
public sealed record ValidateCouponRequest(
    string CouponCode,
    /// <summary>Estimated cart subtotal for minimum-order-value and discount calculation.</summary>
    decimal? EstimatedSubtotal
);
