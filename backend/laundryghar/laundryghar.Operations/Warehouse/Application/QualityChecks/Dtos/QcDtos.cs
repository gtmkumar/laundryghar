namespace laundryghar.Warehouse.Application.QualityChecks.Dtos;

public sealed record QualityCheckDto(
    Guid Id, Guid BrandId, Guid WarehouseId, Guid GarmentId,
    Guid? BatchId, short QcRound, Guid InspectorUserId,
    DateTimeOffset InspectedAt, string Result, bool RequiresRewash,
    string? RewashPriority, string? Notes, string Status,
    DateTimeOffset CreatedAt
);

public sealed record CreateQualityCheckRequest(
    Guid GarmentId,
    Guid WarehouseId,
    Guid? BatchId,
    Guid InspectorUserId,
    string Result,
    string Issues,          // JSON array — passed as string (jsonb)
    Guid? PreWashInspectionId,
    Guid? PostWashInspectionId,
    bool RequiresRewash,
    string? RewashPriority,
    string? Notes
);
