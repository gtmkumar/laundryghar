namespace laundryghar.Logistics.Application.Riders.Dtos;

/// <summary>Full rider profile DTO returned from admin and rider-self endpoints.</summary>
public sealed record RiderDto(
    Guid    Id,
    Guid    UserId,
    Guid    BrandId,
    Guid    FranchiseId,
    Guid?   PrimaryStoreId,
    string  RiderCode,
    string  EmploymentType,
    string  VehicleType,
    string? VehicleNumber,
    string? VehicleModel,
    string? DrivingLicenseNumber,
    DateOnly? DlExpiryDate,
    DateOnly? InsuranceExpiryDate,
    int     DailyPickupCapacity,
    int     DailyDeliveryCapacity,
    decimal ServiceRadiusKm,
    decimal? RatingAverage,
    int     RatingCount,
    decimal? CompletionRate,
    int     LifetimeDeliveries,
    bool    IsOnline,
    bool    IsOnDuty,
    int     CurrentLoad,
    string  KycStatus,
    string  Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Request body for creating a rider profile (links an existing users row of user_type='rider').</summary>
public sealed record CreateRiderRequest(
    Guid    UserId,
    Guid    FranchiseId,
    Guid?   PrimaryStoreId,
    string  EmploymentType,
    string  VehicleType,
    string? VehicleNumber,
    string? VehicleModel,
    string? DrivingLicenseNumber,
    DateOnly? DlExpiryDate,
    string? AadhaarNumberMasked,
    string? PanNumber,
    DateOnly? InsuranceExpiryDate,
    string? BankAccountNumber,
    string? BankIfsc,
    string? BankAccountName,
    string? UpiId,
    int     DailyPickupCapacity,
    int     DailyDeliveryCapacity,
    decimal ServiceRadiusKm);

/// <summary>Request body for updating a rider profile (status / capacity / vehicle info).</summary>
public sealed record UpdateRiderRequest(
    string? Status,
    string? VehicleNumber,
    string? VehicleModel,
    string? DrivingLicenseNumber,
    DateOnly? DlExpiryDate,
    DateOnly? InsuranceExpiryDate,
    int?    DailyPickupCapacity,
    int?    DailyDeliveryCapacity,
    decimal? ServiceRadiusKm,
    string? KycStatus,
    Guid?   PrimaryStoreId);
