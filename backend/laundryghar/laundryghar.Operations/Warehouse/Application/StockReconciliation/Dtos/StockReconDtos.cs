using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
namespace laundryghar.Warehouse.Application.StockReconciliation.Dtos;

public sealed record StockReconciliationDto(
    Guid Id, Guid BrandId, Guid? WarehouseId, Guid? StoreId,
    DateOnly ReconDate, string ReconType,
    DateTimeOffset StartedAt, Guid StartedBy,
    DateTimeOffset? CompletedAt,
    int ExpectedCount, int ScannedCount, int MatchedCount,
    int MissingCount, int UnexpectedCount,
    string Status, DateTimeOffset CreatedAt
);

public sealed record CreateStockReconciliationRequest(
    Guid? WarehouseId,
    Guid? StoreId,
    DateOnly ReconDate,
    string ReconType
);

public sealed record StockReconciliationItemDto(
    Guid Id, Guid ReconciliationId, Guid BrandId,
    Guid? GarmentId, string TagCode,
    string? ExpectedStage, string? FoundStage,
    string Status, DateTimeOffset FlaggedAt
);

public sealed record AddReconItemRequest(
    Guid? GarmentId,
    string TagCode,
    string? ExpectedStage,
    string? ExpectedLocationType,
    string? FoundStage,
    string? FoundLocationType,
    string Status
);

public sealed record CloseReconRequest(string? Notes);
