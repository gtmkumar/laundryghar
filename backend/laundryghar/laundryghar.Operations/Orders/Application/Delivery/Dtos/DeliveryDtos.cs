namespace laundryghar.Orders.Application.Delivery.Dtos;

public sealed record DeliverySlotDto(
    Guid Id,
    Guid BrandId,
    Guid StoreId,
    DateOnly SlotDate,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    string SlotType,
    int Capacity,
    int BookedCount,
    int Available,
    bool IsExpress,
    bool IsActive,
    string Status
);

public sealed record CreateDeliverySlotRequest(
    Guid StoreId,
    DateOnly SlotDate,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    string SlotType,
    int Capacity,
    bool IsExpress
);

public sealed record UpdateDeliverySlotRequest(
    int? Capacity,
    bool? IsActive,
    string? Status
);
