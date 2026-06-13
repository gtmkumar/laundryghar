using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
namespace laundryghar.Warehouse.Application.Processes.Dtos;

public sealed record WarehouseProcessDto(
    Guid Id, Guid BrandId, string Code, string Name,
    string ProcessCategory, short SequenceOrder,
    int? ExpectedDurationMin, bool RequiresMachine,
    bool RequiresSupervisor, bool IsActive,
    DateTimeOffset CreatedAt
);

public sealed record CreateWarehouseProcessRequest(
    string Code, string Name, string NameLocalized,
    string ProcessCategory, short SequenceOrder,
    int? ExpectedDurationMin, bool RequiresMachine, bool RequiresSupervisor
);

public sealed record ProcessLogEntryDto(
    Guid Id, Guid BrandId, Guid WarehouseId, Guid GarmentId,
    string TagCode, string ProcessCode, string Action,
    string? FromStage, string? ToStage,
    DateTimeOffset OccurredAt, DateTimeOffset CreatedAt
);

public sealed record CreateProcessLogRequest(
    Guid GarmentId,
    Guid WarehouseId,
    Guid? BatchId,
    Guid? ProcessId,
    string ProcessCode,
    string Action,
    string? FromStage,
    string? ToStage,
    string? PerformedByName
);
