using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
namespace laundryghar.Logistics.Application.CapacityConfigs.Dtos;

public sealed record RiderCapacityConfigDto(
    Guid    Id,
    Guid    RiderId,
    Guid    BrandId,
    Guid?   StoreId,
    short?  DayOfWeek,
    TimeOnly? SlotStart,
    TimeOnly? SlotEnd,
    int     MaxPickupsPerSlot,
    int     MaxDeliveriesPerSlot,
    int     MaxConcurrentOrders,
    bool    IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string  Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateCapacityConfigRequest(
    Guid     RiderId,
    Guid?    StoreId,
    short?   DayOfWeek,
    TimeOnly? SlotStart,
    TimeOnly? SlotEnd,
    int      MaxPickupsPerSlot,
    int      MaxDeliveriesPerSlot,
    int      MaxConcurrentOrders,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record UpdateCapacityConfigRequest(
    short?   DayOfWeek,
    TimeOnly? SlotStart,
    TimeOnly? SlotEnd,
    int?     MaxPickupsPerSlot,
    int?     MaxDeliveriesPerSlot,
    int?     MaxConcurrentOrders,
    bool?    IsActive,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string?  Status);
