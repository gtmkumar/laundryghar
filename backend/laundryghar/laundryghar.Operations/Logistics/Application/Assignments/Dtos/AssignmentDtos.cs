using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
namespace laundryghar.Logistics.Application.Assignments.Dtos;

public sealed record RiderAssignmentDto(
    Guid    Id,
    Guid    RiderId,
    Guid    BrandId,
    Guid    StoreId,
    DateOnly ShiftDate,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    int     MaxPickups,
    int     MaxDeliveries,
    int     CompletedPickups,
    int     CompletedDeliveries,
    int     FailedAttempts,
    decimal? TotalDistanceKm,
    decimal? Earnings,
    string  Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateRiderAssignmentRequest(
    Guid     RiderId,
    Guid     StoreId,
    DateOnly ShiftDate,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    int      MaxPickups,
    int      MaxDeliveries,
    string?  Notes);

public sealed record UpdateRiderAssignmentRequest(
    string?  Status,
    int?     MaxPickups,
    int?     MaxDeliveries,
    string?  Notes,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt);
