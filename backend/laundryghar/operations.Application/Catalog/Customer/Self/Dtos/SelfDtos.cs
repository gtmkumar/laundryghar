namespace operations.Application.Catalog.Customer.Self.Dtos;

// ── Profile ───────────────────────────────────────────────────────────────────

public sealed record CustomerProfileDto(
    Guid Id,
    Guid BrandId,
    string CustomerCode,
    string PhoneE164,
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    string Locale,
    string Timezone,
    bool MarketingOptIn,
    bool SmsOptIn,
    bool WhatsappOptIn,
    bool EmailOptIn,
    bool PushOptIn,
    int LoyaltyPointsBalance,
    decimal WalletBalance,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record PatchProfileRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Gender,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    string? Locale,
    string? Timezone,
    bool? MarketingOptIn,
    bool? SmsOptIn,
    bool? WhatsappOptIn,
    bool? EmailOptIn,
    bool? PushOptIn
);

// ── Address ───────────────────────────────────────────────────────────────────

public sealed record CustomerAddressDto(
    Guid Id,
    Guid CustomerId,
    string Label,
    string? CustomLabel,
    string? RecipientName,
    string? RecipientPhone,
    string AddressLine1,
    string? AddressLine2,
    string? Landmark,
    string? Floor,
    string? FlatNumber,
    string? BuildingName,
    string? Society,
    string? Area,
    string City,
    string State,
    string Pincode,
    string CountryCode,
    string? DeliveryInstructions,
    bool IsDefault,
    bool IsVerified,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record CreateAddressRequest(
    string Label,
    string? CustomLabel,
    string? RecipientName,
    string? RecipientPhone,
    string AddressLine1,
    string? AddressLine2,
    string? Landmark,
    string? Floor,
    string? FlatNumber,
    string? BuildingName,
    string? Society,
    string? Area,
    string City,
    string State,
    string Pincode,
    string CountryCode,
    string? DeliveryInstructions,
    bool IsDefault
);

public sealed record UpdateAddressRequest(
    string Label,
    string? CustomLabel,
    string? RecipientName,
    string? RecipientPhone,
    string AddressLine1,
    string? AddressLine2,
    string? Landmark,
    string? Floor,
    string? FlatNumber,
    string? BuildingName,
    string? Society,
    string? Area,
    string City,
    string State,
    string Pincode,
    string CountryCode,
    string? DeliveryInstructions,
    bool IsDefault
);

// ── Device ────────────────────────────────────────────────────────────────────

public sealed record CustomerDeviceDto(
    Guid Id,
    Guid CustomerId,
    string DeviceId,
    string Platform,
    string? OsVersion,
    string? DeviceModel,
    string? AppVersion,
    string? FcmToken,
    bool PushEnabled,
    bool IsActive,
    DateTimeOffset LastSeenAt,
    DateTimeOffset FirstSeenAt
);

public sealed record RegisterDeviceRequest(
    string DeviceId,
    string Platform,
    string? OsVersion,
    string? DeviceModel,
    string? DeviceName,
    string? AppVersion,
    string? AppBuild,
    string? FcmToken,
    string? ApnsToken,
    bool PushEnabled,
    string? Language,
    string? Timezone
);

// ── DPDP Consent ──────────────────────────────────────────────────────────────

public sealed record DpdpConsentDto(
    Guid Id,
    string Purpose,
    string PurposeDescription,
    string[] DataCategories,
    string ConsentStatus,
    string ConsentMethod,
    string PrivacyPolicyVersion,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset CreatedAt
);

public sealed record GrantConsentRequest(
    string Purpose,
    string PurposeDescription,
    string[] DataCategories,
    string ConsentMethod,
    string PrivacyPolicyVersion,
    string? TermsVersion,
    string? ConsentTextSnapshot
);

public sealed record WithdrawConsentRequest(
    string Purpose,
    string PrivacyPolicyVersion
);

// ── Serviceability ────────────────────────────────────────────────────────────

/// <summary>Returns whether the given pincode is covered by any active store or territory in the brand.</summary>
public sealed record ServiceabilityDto(bool Serviceable);

// ── Account Deletion ──────────────────────────────────────────────────────────

public sealed record AccountDeletionRequestDto(
    Guid Id,
    string Status,
    string RequestSource,
    string? Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset GracePeriodEndsAt,
    DateTimeOffset? CancelledAt
);

public sealed record CreateDeletionRequestRequest(
    string RequestSource,
    string? Reason,
    string? ReasonText
);
