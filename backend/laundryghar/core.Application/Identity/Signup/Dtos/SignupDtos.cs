namespace core.Application.Identity.Signup.Dtos;

/// <summary>A template a prospective provider can launch on — PLATFORM_STRATEGY.md §9's
/// "pick vertical template + plan".</summary>
public sealed record SignupTemplateDto(
    string Key,
    string VerticalKey,
    string Name,
    string? Description,
    string FulfillmentMode,
    string? DefaultBundleCode,
    IReadOnlyList<string> CatalogCategories);

/// <summary>Step 1: the business details, submitted with the phone that will own the account.</summary>
public sealed record SignupStartRequest(
    string BusinessName,
    string PhoneE164,
    string TemplateKey,
    string? Email = null,
    /// <summary>Optional per §9 ("GSTIN optional") — an Indian business may not have one yet.</summary>
    string? Gstin = null);

public sealed record SignupStartResponse(string Message, DateTimeOffset ExpiresAt);

/// <summary>Step 2: prove the phone, and the account is created.</summary>
public sealed record SignupCompleteRequest(
    string PhoneE164,
    string Code,
    string BusinessName,
    string TemplateKey,
    string? Email = null,
    string? Gstin = null);

/// <summary>What a brand-new provider gets back: their brand, and what it was provisioned with.</summary>
public sealed record SignupCompleteResponse(
    Guid BrandId,
    string BrandCode,
    string BrandName,
    string VerticalKey,
    string TemplateKey,
    string? BundleCode,
    string Status,
    DateTimeOffset? TrialEndsAt,
    int CatalogCategoriesCreated,
    int CatalogItemsCreated);
