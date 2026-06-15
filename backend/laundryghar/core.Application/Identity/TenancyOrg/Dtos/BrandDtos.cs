namespace core.Application.Identity.TenancyOrg.Dtos;

// ─── Query Params ──────────────────────────────────────────────────────────

public sealed record BrandListParams(int Page = 1, int PageSize = 20, string? Status = null, string? Search = null);

// ─── Request DTOs ──────────────────────────────────────────────────────────

public sealed record CreateBrandRequest(
    Guid PlatformId,
    string Code,
    string Name,
    string? LegalName = null,
    string? Tagline = null,
    string CurrencyCode = "INR",
    string CountryCode = "IN",
    string Timezone = "Asia/Kolkata",
    string LocaleDefault = "en-IN");

public sealed record UpdateBrandRequest(
    string? Name = null,
    string? LegalName = null,
    string? Tagline = null,
    string? Status = null,
    string? SupportEmail = null,
    string? SupportPhone = null,
    string? LogoUrl = null);

// ─── Response DTOs ─────────────────────────────────────────────────────────

public sealed record BrandDto(
    Guid Id,
    Guid PlatformId,
    string Code,
    string Name,
    string? LegalName,
    string? Tagline,
    string CurrencyCode,
    string Timezone,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
