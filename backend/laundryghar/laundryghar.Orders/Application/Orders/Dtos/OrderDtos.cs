namespace laundryghar.Orders.Application.Orders.Dtos;

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
    string? CouponCode = null
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
    /// <summary>Total discount applied (coupon + loyalty + package). Populated from DiscountTotal on the order entity.</summary>
    decimal DiscountTotal,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal? AmountDue,
    string CurrencyCode,
    int TotalItems,
    string Status,
    string PaymentStatus,
    DateTimeOffset PlacedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>TAT-computed delivery promise (null for legacy orders placed before this feature).</summary>
    DateTimeOffset? PromisedDeliveryAt,
    IReadOnlyList<OrderItemDto>? Items,
    IReadOnlyList<OrderAddonDto>? Addons,
    IReadOnlyList<OrderStatusHistoryDto>? StatusHistory,
    /// <summary>Customer rating 1–5. Null until the customer rates the order.</summary>
    short? Rating = null,
    string? RatingComment = null,
    DateTimeOffset? RatedAt = null
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
