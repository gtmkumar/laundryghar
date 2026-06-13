namespace laundryghar.Warehouse.Application.Batches.Dtos;

public sealed record WarehouseBatchDto(
    Guid Id, Guid BrandId, Guid WarehouseId,
    string BatchNumber, string BatchType,
    Guid? ServiceId, string? MachineId,
    int ExpectedGarmentCount, int ActualGarmentCount,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    string Status, string? FailureReason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt
);

public sealed record CreateWarehouseBatchRequest(
    Guid WarehouseId,
    string BatchType,
    Guid? ServiceId,
    string? MachineId,
    string? CycleProgram,
    int ExpectedGarmentCount
);

public sealed record UpdateWarehouseBatchRequest(
    string Status,
    string? MachineId,
    string? Notes,
    string? FailureReason
);
