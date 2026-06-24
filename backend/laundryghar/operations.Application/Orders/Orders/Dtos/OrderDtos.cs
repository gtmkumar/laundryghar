namespace operations.Application.Orders.Orders.Dtos;

// ── Request records ─────────────────────────────────────────────────────────

public sealed record CreateOrderRequest(
    Guid CustomerId,
    Guid StoreId,
    string Channel,
    bool IsExpress,
    bool RequiresPickup,
    bool RequiresDelivery,
    Guid? PickupAddressId,
    Guid? DeliveryAddressId,
    CreateOrderItemRequest[] Items,
    CreateOrderAddonRequest[] Addons,
    string? NotesCustomer,
    /// <summary>Optional coupon code. Validated and applied server-side; 422 on invalid.</summary>
    string? CouponCode = null,
    /// <summary>
    /// Number of loyalty points the customer wishes to redeem as a discount.
    /// Server-side rules apply: MinBurnPoints, MaxBurnPerOrderPct, and actual balance cap.
    /// Pass 0 (default) to skip loyalty burn.
    /// </summary>
    int LoyaltyPointsToRedeem = 0,
    /// <summary>
    /// Optional explicit CustomerPackage to apply. When null the handler auto-resolves
    /// the earliest-expiring active package with remaining balance for this customer/brand.
    /// </summary>
    Guid? CustomerPackageId = null,
    /// <summary>
    /// Marketplace job kind — see <see cref="laundryghar.SharedDataModel.Enums.JobType"/>.
    /// Defaults to 'laundry' (current behaviour). 'parcel' is a point-to-point delivery:
    /// no catalog items, both pickup + delivery addresses required, value = pickup+delivery charge.
    /// </summary>
    string JobType = "laundry",
    /// <summary>
    /// Optional required vehicle tier — see <see cref="laundryghar.SharedDataModel.Enums.VehicleTier"/>.
    /// NULL = no constraint. Dispatch matches a rider whose vehicle ranks at least this high.
    /// </summary>
    string? RequestedVehicleTier = null,
    /// <summary>
    /// Signed fare quote token from POST /fare/quote. REQUIRED for parcel jobs; it locks in
    /// the pickup + delivery charge. Must match this request's addresses + tier and be unexpired.
    /// </summary>
    string? FareQuoteToken = null
);

/// <summary>Customer parcel (point-to-point) order request. Fare is locked by the quote token.</summary>
public sealed record CreateParcelOrderRequest(
    Guid PickupAddressId,
    Guid DeliveryAddressId,
    string? VehicleTier,
    string FareQuoteToken,
    string? NotesCustomer = null,
    string? PaymentPreference = null
);

public sealed record CreateOrderItemRequest(
    Guid ItemId,
    Guid? ItemVariantId,
    Guid ServiceId,
    decimal Quantity,
    string? Notes
);

public sealed record CreateOrderAddonRequest(
    Guid AddonId,
    int? OrderItemIndex,   // null = order-level addon, non-null = 0-based index into Items array
    decimal Quantity
);

public sealed record UpdateOrderStatusRequest(
    string ToStatus,
    string? Reason,
    string? Notes,
    bool CustomerNotified
);

public sealed record CreateOrderNoteRequest(
    string NoteType,
    string Visibility,
    string NoteText,
    bool IsPinned
);

/// <summary>Customer rating payload — score 1–5, optional comment.</summary>
public sealed record RateOrderRequest(int Score, string? Comment);

// ── Response DTOs ────────────────────────────────────────────────────────────

public sealed record OrderDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string OrderNumber,
    Guid BrandId,
    Guid StoreId,
    Guid CustomerId,
    string Channel,
    string OrderType,
    bool IsExpress,
    decimal Subtotal,
    decimal AddonTotal,
    decimal ExpressSurcharge,
    decimal TaxTotal,
    decimal Cgst,
    decimal Sgst,
    /// <summary>Total discount applied (coupon + loyalty + package + promotion). Populated from DiscountTotal on the order entity.</summary>
    decimal DiscountTotal,
    /// <summary>Promotion discount component within DiscountTotal. Zero when no promotion was applied.</summary>
    decimal PromotionDiscount,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal? AmountDue,
    string CurrencyCode,
    int TotalItems,
    string Status,
    /// <summary>Generic vertical-neutral lifecycle super-state — see <c>OrderLifecycleState</c>.
    /// Lets clients/reports group orders without knowing each vertical's detailed <see cref="Status"/>.</summary>
    string LifecycleState,
    string PaymentStatus,
    DateTimeOffset PlacedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>TAT-computed delivery promise (null for legacy orders placed before this feature).</summary>
    DateTimeOffset? PromisedDeliveryAt,
    IReadOnlyList<OrderItemDto>? Items,
    IReadOnlyList<OrderAddonDto>? Addons,
    IReadOnlyList<OrderStatusHistoryDto>? StatusHistory,
    /// <summary>
    /// Status codes this order may legally transition to next, sourced from the order's
    /// <c>IFulfillmentStrategy</c> (resolved by FulfillmentMode) for the current
    /// <see cref="Status"/>. Empty array for terminal states. Populated only on order-detail
    /// responses (null on list rows, where the projection runs in SQL and cannot evaluate it).
    /// </summary>
    IReadOnlyList<string>? AllowedTransitions = null,
    /// <summary>Customer rating 1–5. Null until the customer rates the order.</summary>
    short? Rating = null,
    string? RatingComment = null,
    DateTimeOffset? RatedAt = null,
    /// <summary>
    /// OTP the rider will read back to the customer to confirm the correct parcel.
    /// Exposed ONLY to the owning customer AND ONLY while status == out_for_delivery.
    /// Null in all other states and for all admin/staff callers.
    /// </summary>
    string? DeliveryOtp = null,
    /// <summary>Marketplace job kind — 'laundry' (default) or 'parcel' (point-to-point delivery).</summary>
    string JobType = "laundry"
);

public sealed record OrderItemDto(
    Guid Id,
    Guid ServiceId,
    Guid ItemId,
    Guid? ItemVariantId,
    string ItemNameSnapshot,
    string ServiceNameSnapshot,
    decimal UnitPrice,
    decimal Quantity,
    string UnitOfMeasure,
    decimal LineSubtotal,
    decimal LineTotal,
    string Status
);

public sealed record OrderAddonDto(
    Guid Id,
    Guid? OrderItemId,
    Guid AddonId,
    string AddonNameSnapshot,
    string PricingType,
    decimal UnitPrice,
    decimal Quantity,
    decimal TotalCharge
);

public sealed record OrderStatusHistoryDto(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    string ChangedByType,
    string? Reason,
    bool CustomerNotified
);

public sealed record OrderNoteDto(
    Guid Id,
    string NoteType,
    string Visibility,
    string AuthorType,
    string NoteText,
    bool IsPinned,
    DateTimeOffset CreatedAt
);
