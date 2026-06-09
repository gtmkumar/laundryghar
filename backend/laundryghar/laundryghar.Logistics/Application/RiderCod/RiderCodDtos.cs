namespace laundryghar.Logistics.Application.RiderCod;

/// <summary>Per-rider outstanding COD cash — collected on delivery but not yet settled.</summary>
public sealed record RiderCodSummaryDto(
    Guid     RiderId,
    string   RiderCode,
    string?  RiderName,
    string?  FranchiseName,
    decimal  OutstandingAmount,
    int      OutstandingCount,
    DateTimeOffset? OldestCollectedAt);

/// <summary>One outstanding COD collection (a completed COD delivery leg).</summary>
public sealed record CodCollectionDto(
    Guid     AssignmentId,
    Guid?    OrderId,
    string?  OrderNumber,
    decimal  Amount,
    DateTimeOffset CollectedAt);

/// <summary>A rider's outstanding cash with the individual collections that make it up.</summary>
public sealed record RiderCodDetailDto(
    Guid     RiderId,
    string   RiderCode,
    string?  RiderName,
    decimal  OutstandingAmount,
    int      OutstandingCount,
    IReadOnlyList<CodCollectionDto> Collections);

/// <summary>A recorded settlement (cash handover).</summary>
public sealed record RiderSettlementDto(
    Guid     Id,
    Guid     RiderId,
    Guid?    StoreId,
    string?  StoreName,
    decimal  TotalAmount,
    int      CollectionCount,
    string?  Reference,
    string   Status,
    DateTimeOffset SettledAt,
    Guid?    SettledBy,
    string?  Notes);

/// <summary>Record a settlement that clears ALL of a rider's outstanding COD cash.</summary>
public sealed record SettleRiderCodRequest(Guid? StoreId, string? Reference, string? Notes);
