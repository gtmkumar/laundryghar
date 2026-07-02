namespace operations.Application.Catalog.Catalog.Dtos;

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
    DateTimeOffset UpdatedAt,
    int? TatHours = null,
    bool ExpressEligible = false,
    decimal? ExpressSurcharge = null,
    string CatalogKind = "laundry_garment",
    // How the item is priced — 'standard' (price-list rows) or 'value_slab' (declared-value slabs). GH #22.
    string PricingMode = "standard"
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
    short DisplayOrder,
    int? TatHours = null,
    bool ExpressEligible = false,
    decimal? ExpressSurcharge = null,
    // Vertical-neutral item-shape discriminator; null → defaults to laundry_garment.
    string? CatalogKind = null,
    // Pricing mode; null → defaults to 'standard'. GH #22.
    string? PricingMode = null
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
    string Status,
    int? TatHours = null,
    bool ExpressEligible = false,
    decimal? ExpressSurcharge = null,
    // Pricing mode; null → leaves the item's current mode unchanged. GH #22.
    string? PricingMode = null
);

// ── Managed item (Items page aggregate: item + per-service base prices + fabrics) ─
public sealed record ItemServicePriceDto(Guid ServiceId, decimal BasePrice);

public sealed record ManagedItemDto(
    Guid Id,
    Guid? ItemGroupId,
    string? ItemGroupName,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    int? TypicalWeightGrams,
    int? TatHours,
    bool ExpressEligible,
    decimal? ExpressSurcharge,
    string[] Aliases,
    short DisplayOrder,
    string Status,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<Guid> FabricTypeIds,
    IReadOnlyList<ItemServicePriceDto> ServicePrices,
    // Pricing mode — 'standard' or 'value_slab' (GH #22). Lets the Items page flag slab items.
    string PricingMode = "standard"
);

public sealed record ItemStatsDto(
    int TotalItems,
    int CategoryCount,
    int ActiveItems,
    int DraftItems,
    int AvgTatHours
);

// Inline pricing + fabric save from the Items drawer/table. A null price for a
// service removes that service's row; fabric ids replace the item's fabric set.
public sealed record SaveItemServicePrice(Guid ServiceId, decimal? BasePrice);
public sealed record SaveItemPricingRequest(
    IReadOnlyList<SaveItemServicePrice> ServicePrices,
    IReadOnlyList<Guid> FabricTypeIds
);

// ── CSV / XLSX import (round-trips the Items Export format) ─────────────────────
// Services are matched by name; category by item-group name or code; fabrics by
// name. Existing codes are updated, new codes created. Prices upsert into the
// working list — or the Options.TargetPriceListId draft list when supplied.

/// <summary>A per-fabric price override for one service on one item (e.g. "Silk" under "Dry Clean").
/// Resolves the fabric by name; upserts a price_list_items row keyed by that fabric.</summary>
public sealed record ImportItemFabricPrice(string FabricName, decimal Price);

public sealed record ImportItemServicePrice(
    string ServiceName,
    decimal? BasePrice,
    // Optional fabric-variant prices for this service. Backward compatible: absent → base price only.
    IReadOnlyList<ImportItemFabricPrice>? FabricPrices = null
);

public sealed record ImportItemRow(
    string Code,
    string Name,
    string? Category,
    string? Status,
    int? TatHours,
    IReadOnlyList<ImportItemServicePrice> ServicePrices,
    // Optional row-level tax rate. Absent → price rows keep their current tax settings.
    decimal? TaxRatePercent = null
);

/// <summary>Extra knobs the import wizard passes on commit.</summary>
public sealed record ImportOptions(
    // Create missing item-groups (categories) instead of leaving unmatched rows ungrouped.
    bool? AutoCreateCategories = null,
    // Write prices into this (unpublished) list instead of the brand working list.
    Guid? TargetPriceListId = null,
    // Storage key of the original uploaded file (from the parse step), for the audit log.
    string? FileRef = null
);

public sealed record ImportItemsRequest(
    IReadOnlyList<ImportItemRow> Rows,
    ImportOptions? Options = null
);

public sealed record ImportItemsResult(
    int Created,
    int Updated,
    int PricesSet,
    IReadOnlyList<string> Errors,
    int CategoriesCreated = 0
);

// ── Server-side parse (POST /items/import/parse) — dry-run + diff report ────────

/// <summary>A parse/validation problem tied to a source line (and sheet, for xlsx workbooks).</summary>
public sealed record ImportRowError(int Line, string Message, string? Sheet = null);

/// <summary>One projected price change: current working-list price vs the value the import would set.</summary>
public sealed record ImportPriceChange(
    string Code,
    string ItemName,
    string ServiceName,
    string? FabricName,
    decimal? OldPrice,
    decimal NewPrice
);

/// <summary>Diff summary the wizard shows before the user confirms the import.</summary>
public sealed record ImportReport(
    int TotalRows,
    int ToCreate,
    int ToUpdate,
    IReadOnlyList<ImportPriceChange> PriceChanges,
    bool PriceChangesTruncated,
    IReadOnlyList<string> UnknownServices,
    IReadOnlyList<string> UnknownCategories,
    IReadOnlyList<ImportRowError> RowErrors
);

/// <summary>Response of the parse endpoint: normalized rows + the diff report + the stored file key.
/// <paramref name="SourceUrl"/> is set only for the Google Sheet flow (the sheet the CSV was fetched from).</summary>
public sealed record ParseImportResult(
    string? FileRef,
    string Layout,
    IReadOnlyList<ImportItemRow> Rows,
    ImportReport Report,
    string? SourceUrl = null
);

/// <summary>A generated import template (CSV or XLSX bytes) for download.</summary>
public sealed record ImportTemplateFile(byte[] Content, string ContentType, string FileName);

/// <summary>Request for the Google Sheet parse endpoint. <paramref name="Gid"/> (the sheet-tab id) is
/// optional and overrides any gid embedded in <paramref name="Url"/>.</summary>
public sealed record ParseGoogleSheetRequest(string Url, string? Gid = null);

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
