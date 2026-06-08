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
    DateTimeOffset UpdatedAt,
    // ── Enriched identity / org fields (populated via LEFT joins) ─────────
    string? RiderName,
    string? Email,
    string? Phone,
    string? UserStatus,
    string? FranchiseName,
    string? PrimaryStoreName);

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

/// <summary>
/// Request body for updating a rider profile (status / employment / vehicle / KYC
/// documents / payout / capacity). Every field is optional — only the non-null ones
/// are applied, so a partial form never clobbers the fields it didn't send.
/// KYC <em>status</em> is intentionally NOT settable here — it only transitions through
/// the dedicated verify/reject endpoints (gated by permission:rider.verify). The KYC
/// <em>document</em> fields (Aadhaar/PAN/DL) remain editable so operators can correct them.
/// </summary>
public sealed record UpdateRiderRequest(
    string? Status,
    string? EmploymentType,
    string? VehicleType,
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
    int?    DailyPickupCapacity,
    int?    DailyDeliveryCapacity,
    decimal? ServiceRadiusKm,
    Guid?   PrimaryStoreId);

/// <summary>Optional body for the KYC reject endpoint — carries the rejection reason.</summary>
public sealed record RejectRiderRequest(string? Reason);
