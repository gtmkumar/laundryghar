namespace laundryghar.Catalog.Application.Pricing.Dtos;

// ── PriceList ─────────────────────────────────────────────────────────────────

public sealed record PriceListDto(
    Guid Id,
    Guid BrandId,
    Guid? FranchiseId,
    Guid? StoreId,
    string Code,
    string Name,
    string? Description,
    string CurrencyCode,
    string ScopeType,
    int VersionNumber,
    Guid? ParentPriceListId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsDefault,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreatePriceListRequest(
    string Code,
    string Name,
    string? Description,
    string CurrencyCode,
    string ScopeType,
    Guid? FranchiseId,
    Guid? StoreId,
    Guid? ParentPriceListId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsDefault,
    string? Notes
);

public sealed record UpdatePriceListRequest(
    string Name,
    string? Description,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsDefault,
    string? Notes,
    string Status
);

// ── PriceListItem ─────────────────────────────────────────────────────────────

public sealed record PriceListItemDto(
    Guid Id,
    Guid PriceListId,
    Guid BrandId,
    Guid ServiceId,
    Guid ItemId,
    Guid? ItemVariantId,
    Guid? FabricTypeId,
    Guid? ItemGroupId,
    decimal BasePrice,
    decimal? ExpressPrice,
    int MinimumQuantity,
    decimal TaxRatePercent,
    bool IsTaxable,
    string? DisplayLabel,
    string? Notes,
    bool IsActive,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreatePriceListItemRequest(
    Guid ServiceId,
    Guid ItemId,
    Guid? ItemVariantId,
    Guid? FabricTypeId,
    Guid? ItemGroupId,
    decimal BasePrice,
    decimal? ExpressPrice,
    int MinimumQuantity,
    decimal TaxRatePercent,
    bool IsTaxable,
    string? DisplayLabel,
    string? Notes
);

public sealed record UpdatePriceListItemRequest(
    decimal BasePrice,
    decimal? ExpressPrice,
    int MinimumQuantity,
    decimal TaxRatePercent,
    bool IsTaxable,
    string? DisplayLabel,
    string? Notes,
    bool IsActive
);

// ── Price resolution ──────────────────────────────────────────────────────────

public sealed record PriceResolutionDto(
    Guid PriceListId,
    string PriceListCode,
    string ScopeType,
    decimal BasePrice,
    decimal? ExpressPrice,
    decimal TaxRatePercent,
    bool IsTaxable,
    string? DisplayLabel
);
