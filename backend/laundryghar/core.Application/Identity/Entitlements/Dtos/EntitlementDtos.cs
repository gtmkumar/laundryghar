namespace core.Application.Identity.Entitlements.Dtos;

/// <summary>One module row in a brand's entitlement matrix.</summary>
public sealed record BrandModuleDto(
    string Key,
    string Label,
    string? Section,
    bool IsCore,
    bool Entitled,
    string? Source,         // 'bundle' | 'manual' | null (not licensed)
    DateOnly? ValidUntil);

/// <summary>A brand's full entitlement view: every active module + whether it's licensed.</summary>
public sealed record BrandEntitlementsDto(
    Guid BrandId,
    string BrandName,
    IReadOnlyList<BrandModuleDto> Modules);

public sealed record ModuleBundleItemDto(string Key, string Label);
public sealed record ModuleBundleDto(string Code, string Name, string? Description, IReadOnlyList<ModuleBundleItemDto> Items, string? VerticalKey = null);

/// <summary>Toggle a single module's licensing for a brand (a 'manual' override).</summary>
public sealed record SetBrandModuleRequest(string ModuleKey, bool Enabled, DateOnly? ValidUntil = null);

/// <summary>Apply a plan bundle to a brand: replace its 'bundle' rows with the bundle's items.</summary>
public sealed record ApplyBundleRequest(string BundleCode);
