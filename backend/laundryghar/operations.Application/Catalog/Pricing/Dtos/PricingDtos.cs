namespace operations.Application.Catalog.Pricing.Dtos;

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
    DateTimeOffset UpdatedAt,
    // ── DEFECT 1 (additive) — denormalised catalog names for client display ──────
    // Populated by the customer-facing published price-list query so the mobile app
    // can render a real label instead of "FulfillmentUnit · Standard". Null on admin paths
    // that don't project the joins (existing positional callers are unaffected:
    // both default to null).
    string? ItemName = null,
    string? ServiceName = null
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

// ── Price matrix (items × fabric columns) ─────────────────────────────────────
public sealed record PricingMatrixFabricDto(string Code, string Name, decimal Multiplier);
public sealed record PricingMatrixRowDto(string Label, decimal BasePrice);
public sealed record PricingMatrixStoreDto(Guid Id, string Name);

public sealed record PricingMatrixDto(
    string? PriceListName,
    string? ScopeType,
    IReadOnlyList<PricingMatrixFabricDto> Fabrics,
    IReadOnlyList<PricingMatrixRowDto> Rows,
    IReadOnlyList<PricingMatrixStoreDto> Stores
);

// ── Change history ────────────────────────────────────────────────────────────
public sealed record PricingHistoryEntryDto(
    Guid Id,
    string TargetKind,
    Guid TargetId,
    string Summary,
    string? ActorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevertedAt
);
