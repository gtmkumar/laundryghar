namespace core.Application.Identity.Onboarding.Dtos;

// ── Read model ──────────────────────────────────────────────────────────────
public sealed record OnboardingStepDto(string Key, string Title, string Description, bool Done, string? Summary);

public sealed record OnboardingAddress(string Line1, string City, string State, string Pincode);

public sealed record OnboardingOwnerDto(Guid? UserId, string? Name, string? Email, string? Status);

public sealed record OnboardingStateDto(
    Guid Id,
    string Code,
    string LegalName,
    string? DisplayName,
    string? Gstin,
    string? Pan,
    string ContactPhone,
    string? ContactEmail,
    OnboardingAddress? BillingAddress,
    OnboardingAddress? OperationalAddress,
    decimal RoyaltyPercent,
    decimal MarketingFeePercent,
    decimal InitialFranchiseFee,
    int TermYears,
    bool AgreementCreated,
    string? AgreementNumber,
    OnboardingOwnerDto Owner,
    int StoreCount,
    string OnboardingStatus,
    bool IsActive,
    int ProgressPct,
    bool CanActivate,
    IReadOnlyList<OnboardingStepDto> Steps);

// ── Write models ────────────────────────────────────────────────────────────
public sealed record StartOnboardingRequest(string LegalName, string? DisplayName, string ContactPhone, string? ContactEmail);

public sealed record SaveDetailsRequest(
    string LegalName, string? DisplayName, string? Gstin, string? Pan,
    string ContactPhone, string? ContactEmail,
    OnboardingAddress? BillingAddress, OnboardingAddress? OperationalAddress);

public sealed record SaveCommercialsRequest(
    decimal RoyaltyPercent, decimal MarketingFeePercent, decimal InitialFranchiseFee, int TermYears);

public sealed record InviteOwnerRequest(string Email, string? FirstName, string? LastName, string? Phone);

public sealed record AddStoreRequest(string Name, string? Code, string AddressLine1, string City, string State, string Pincode);
