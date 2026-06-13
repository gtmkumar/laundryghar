namespace laundryghar.Catalog.Application.Catalog.Dtos;

// ── ServiceCategory ──────────────────────────────────────────────────────────

public sealed record ServiceCategoryDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string? IconUrl,
    string? ImageUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsVisibleMobile,
    bool IsVisiblePos,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateServiceCategoryRequest(
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string? IconUrl,
    string? ImageUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsVisibleMobile,
    bool IsVisiblePos,
    string[] RequiresWarehouseCap
);

public sealed record UpdateServiceCategoryRequest(
    string Name,
    string NameLocalized,
    string? Description,
    string? IconUrl,
    string? ImageUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsVisibleMobile,
    bool IsVisiblePos,
    string Status
);

// ── Service ──────────────────────────────────────────────────────────────────

public sealed record ServiceDto(
    Guid Id,
    Guid BrandId,
    Guid CategoryId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string PricingModel,
    int BaseTatHours,
    int ExpressTatHours,
    decimal ExpressMultiplier,
    bool IsExpressAvailable,
    bool RequiresInspection,
    bool RequiresQc,
    string? IconUrl,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateServiceRequest(
    Guid CategoryId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string PricingModel,
    int BaseTatHours,
    int ExpressTatHours,
    decimal ExpressMultiplier,
    bool IsExpressAvailable,
    bool RequiresInspection,
    bool RequiresQc,
    string? IconUrl,
    short DisplayOrder
);

public sealed record UpdateServiceRequest(
    string Name,
    string NameLocalized,
    string? Description,
    string PricingModel,
    int BaseTatHours,
    int ExpressTatHours,
    decimal ExpressMultiplier,
    bool IsExpressAvailable,
    bool RequiresInspection,
    bool RequiresQc,
    string? IconUrl,
    short DisplayOrder,
    string Status
);

// ── FabricType ────────────────────────────────────────────────────────────────

public sealed record FabricTypeDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string? CareInstructions,
    decimal PriceMultiplier,
    bool RequiresSpecialCare,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateFabricTypeRequest(
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string? CareInstructions,
    decimal PriceMultiplier,
    bool RequiresSpecialCare,
    short DisplayOrder
);

public sealed record UpdateFabricTypeRequest(
    string Name,
    string NameLocalized,
    string? Description,
    string? CareInstructions,
    decimal PriceMultiplier,
    bool RequiresSpecialCare,
    short DisplayOrder,
    string Status
);

// ── ItemGroup ─────────────────────────────────────────────────────────────────

public sealed record ItemGroupDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string? IconUrl,
    short DisplayOrder,
    bool IsVisibleMobile,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateItemGroupRequest(
    string Code,
    string Name,
    string NameLocalized,
    string? IconUrl,
    short DisplayOrder,
    bool IsVisibleMobile
);

public sealed record UpdateItemGroupRequest(
    string Name,
    string NameLocalized,
    string? IconUrl,
    short DisplayOrder,
    bool IsVisibleMobile,
    string Status
);

// ── Item ──────────────────────────────────────────────────────────────────────

public sealed record ItemDto(
    Guid Id,
    Guid BrandId,
    Guid? ItemGroupId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string? IconUrl,
    string? ImageUrl,
    int? TypicalWeightGrams,
    bool RequiresPerSidePrice,
    string[] Aliases,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateItemRequest(
    Guid? ItemGroupId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string? IconUrl,
    string? ImageUrl,
    int? TypicalWeightGrams,
    bool RequiresPerSidePrice,
    string[]? Aliases,
    short DisplayOrder
);

public sealed record UpdateItemRequest(
    Guid? ItemGroupId,
    string Name,
    string NameLocalized,
    string? Description,
    string? IconUrl,
    string? ImageUrl,
    int? TypicalWeightGrams,
    bool RequiresPerSidePrice,
    string[]? Aliases,
    short DisplayOrder,
    string Status
);

// ── ItemVariant ───────────────────────────────────────────────────────────────

public sealed record ItemVariantDto(
    Guid Id,
    Guid BrandId,
    Guid ItemId,
    Guid? FabricTypeId,
    string Code,
    string VariantName,
    string? Side,
    string? Size,
    string? Color,
    string? Sku,
    string? Barcode,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateItemVariantRequest(
    Guid ItemId,
    Guid? FabricTypeId,
    string Code,
    string VariantName,
    string? Side,
    string? Size,
    string? Color,
    string? Sku,
    string? Barcode,
    short DisplayOrder
);

public sealed record UpdateItemVariantRequest(
    Guid? FabricTypeId,
    string VariantName,
    string? Side,
    string? Size,
    string? Color,
    string? Sku,
    string? Barcode,
    short DisplayOrder,
    string Status
);

// ── AddOn ─────────────────────────────────────────────────────────────────────

public sealed record AddOnDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string PricingType,
    decimal PriceValue,
    decimal? MinCharge,
    decimal? MaxCharge,
    Guid[] ApplicableServices,
    Guid[] ApplicableCategories,
    bool IsTaxable,
    decimal TaxRatePercent,
    bool RequiresApproval,
    string? IconUrl,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateAddOnRequest(
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string PricingType,
    decimal PriceValue,
    decimal? MinCharge,
    decimal? MaxCharge,
    Guid[]? ApplicableServices,
    Guid[]? ApplicableCategories,
    bool IsTaxable,
    decimal TaxRatePercent,
    bool RequiresApproval,
    string? IconUrl,
    short DisplayOrder
);

public sealed record UpdateAddOnRequest(
    string Name,
    string NameLocalized,
    string? Description,
    string PricingType,
    decimal PriceValue,
    decimal? MinCharge,
    decimal? MaxCharge,
    Guid[]? ApplicableServices,
    Guid[]? ApplicableCategories,
    bool IsTaxable,
    decimal TaxRatePercent,
    bool RequiresApproval,
    string? IconUrl,
    short DisplayOrder,
    string Status
);
