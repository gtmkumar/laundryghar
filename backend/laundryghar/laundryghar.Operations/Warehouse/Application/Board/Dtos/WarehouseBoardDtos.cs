using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
namespace laundryghar.Warehouse.Application.Board.Dtos;

/// <summary>One garment card on the warehouse kanban.</summary>
public sealed record GarmentCardDto(
    Guid Id,
    string TagCode,
    string ItemName,
    string FabricName,
    string CustomerName,
    string Stage,
    DateTimeOffset? LastScannedAt,
    bool IsFlagged);

/// <summary>A processing-stage column with its garment cards.</summary>
public sealed record StageColumnDto(
    string Stage,
    string Label,
    int Count,
    IReadOnlyList<GarmentCardDto> Cards);

/// <summary>Header metrics for the board.</summary>
public sealed record WarehouseBoardSummaryDto(
    Guid? WarehouseId,
    string WarehouseName,
    string WarehouseCode,
    int InFlightCount,
    int CapacityPct,
    int ThroughputTarget,
    int ThroughputToday);

public sealed record WarehouseBoardDto(
    WarehouseBoardSummaryDto Summary,
    IReadOnlyList<StageColumnDto> Columns);
