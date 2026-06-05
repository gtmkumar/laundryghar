namespace laundryghar.Orders.Application.Pickup.Dtos;

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
    string Status,
    DateTimeOffset CreatedAt
);

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
    string? CustomerNotes
);

public sealed record AssignPickupRequest(Guid RiderId);

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
