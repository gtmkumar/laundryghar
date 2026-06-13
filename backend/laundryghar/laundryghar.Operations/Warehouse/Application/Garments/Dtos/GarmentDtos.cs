using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
namespace laundryghar.Warehouse.Application.Garments.Dtos;

public sealed record GarmentDto(
    Guid Id, Guid BrandId, Guid StoreId, Guid? WarehouseId,
    Guid OrderId, Guid OrderItemId, Guid CustomerId,
    string TagCode, string? SecondaryTagCode,
    Guid? ItemId, Guid? ItemVariantId, Guid? FabricTypeId,
    string? Color, string? Size, int? WeightGrams,
    bool HasOrnaments, bool HasLining, bool IsDesignerWear,
    string CurrentStage, Guid? CurrentBatchId,
    DateTimeOffset? LastScannedAt, short RewashCount,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt
);

public sealed record CreateGarmentRequest(
    Guid OrderItemId,
    string TagCode,
    string? Color, string? Size, int? WeightGrams,
    bool HasOrnaments, bool HasLining, bool IsDesignerWear,
    Guid? WarehouseId
);

public sealed record UpdateGarmentRequest(
    string? CurrentStage,
    Guid? CurrentBatchId,
    Guid? WarehouseId,
    string? Notes,
    string? CareInstructions
);

public sealed record GarmentTagDto(
    Guid Id, Guid BrandId, string TagCode, string TagFormat,
    string? BatchNumber, Guid? AssignedToGarmentId,
    DateTimeOffset? AssignedAt, bool IsDamaged, string Status,
    DateTimeOffset CreatedAt
);

public sealed record GenerateTagsRequest(int Count, string TagFormat, string? BatchNumber);

public sealed record GarmentJourneyDto(
    GarmentDto Garment,
    IReadOnlyList<InspectionSummaryDto> Inspections,
    IReadOnlyList<ProcessLogDto> ProcessLogs,
    IReadOnlyList<QcSummaryDto> QualityChecks
);

public sealed record InspectionSummaryDto(
    Guid Id, string InspectionType, string? OverallCondition,
    short IssuesCount, bool RequiresSpecialCare, DateTimeOffset InspectedAt
);

public sealed record ProcessLogDto(
    Guid Id, string ProcessCode, string Action,
    string? FromStage, string? ToStage, DateTimeOffset OccurredAt
);

public sealed record QcSummaryDto(
    Guid Id, string Result, bool RequiresRewash,
    short QcRound, DateTimeOffset InspectedAt
);
