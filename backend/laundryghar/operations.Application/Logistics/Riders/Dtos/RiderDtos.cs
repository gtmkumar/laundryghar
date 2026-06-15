using laundryghar.SharedDataModel.Crypto;
using laundryghar.Utilities.Services;

namespace operations.Application.Logistics.Riders.Dtos;

/// <summary>Full rider profile DTO returned from admin endpoints.
/// Financial PII fields (PanNumber, BankAccountNumber, UpiId) are masked by default;
/// callers holding <c>users.read_financial</c> (or platform_admin) receive clear values.</summary>
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
    string? PrimaryStoreName,
    // ── Financial PII (masked by default) ─────────────────────────────────
    string? PanNumber       = null,
    string? BankAccountNumber = null,
    string? BankIfsc        = null,
    string? BankAccountName = null,
    string? UpiId           = null,
    // ── Vehicle verification gate (Wave 3) ────────────────────────────────
    string  VehicleVerificationStatus = "pending");

/// <summary>
/// Applies masking to financial PII fields on a <see cref="RiderDto"/>.
/// </summary>
internal static class RiderDtoFinancialMask
{
    internal const string ReadFinancialPermission = "users.read_financial";

    internal static RiderDto Apply(RiderDto dto, ICurrentUser actor) =>
        actor.IsPlatformAdmin || actor.HasPermission(ReadFinancialPermission)
            ? dto
            : dto with
            {
                PanNumber         = PiiMask.MaskPan(dto.PanNumber),
                BankAccountNumber = PiiMask.MaskBankAccount(dto.BankAccountNumber),
                UpiId             = PiiMask.MaskUpi(dto.UpiId),
                // IFSC returned clear (public branch code). BankAccountName is not secret.
            };
}

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
