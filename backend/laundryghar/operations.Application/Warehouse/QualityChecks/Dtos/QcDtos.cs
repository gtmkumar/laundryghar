namespace operations.Application.Warehouse.QualityChecks.Dtos;

public sealed record QualityCheckDto(
    Guid Id, Guid BrandId, Guid WarehouseId, Guid FulfillmentUnitId,
    Guid? BatchId, short QcRound, Guid InspectorUserId,
    DateTimeOffset InspectedAt, string Result, bool RequiresRewash,
    string? RewashPriority, string? Notes, string Status,
    DateTimeOffset CreatedAt
);

public sealed record CreateQualityCheckRequest(
    Guid FulfillmentUnitId,
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
