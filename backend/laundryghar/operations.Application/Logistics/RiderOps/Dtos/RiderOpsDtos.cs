namespace operations.Application.Logistics.RiderOps.Dtos;

/// <summary>
/// A rider's current operational snapshot for the admin "Rider Ops" live board:
/// where they are, what they're doing right now, and today's throughput.
/// Money fields (COD collected / earnings) are surfaced in later phases.
/// </summary>
public sealed record RiderLiveDto(
    Guid     Id,
    string   RiderCode,
    string?  RiderName,
    string?  Phone,
    string   Status,            // rider lifecycle: active|suspended|terminated
    bool     IsOnDuty,
    int      CurrentLoad,
    double?  Lat,               // WGS-84 last-known location (null if never pinged)
    double?  Lng,
    DateTimeOffset? LastPingAt,
    bool     IsStale,           // last ping older than the freshness window
    string   OpsStatus,         // offline|idle|on_the_way|arrived (derived)
    string?  ActiveLegType,     // pickup|delivery|return of the in-progress leg
    Guid?    ActiveOrderId,
    string?  ActiveOrderNumber,
    int      PickupsToday,      // completed pickup legs today (IST)
    int      DeliveriesToday);  // completed delivery legs today (IST)

/// <summary>A single GPS breadcrumb for plotting a rider's trail on the map.</summary>
public sealed record RiderTrackPointDto(
    double          Lat,
    double          Lng,
    DateTimeOffset  PingedAt,
    decimal?        SpeedKmph,
    bool?           IsMoving);

/// <summary>
/// Per-rider productivity over a date range (defaults to today, IST). COD / earnings
/// are placeholders here (0) until the cash-settlement and payout phases land.
/// </summary>
public sealed record RiderStatsDto(
    Guid     RiderId,
    string   RiderCode,
    string?  RiderName,
    DateOnly From,
    DateOnly To,
    int      PickupsDone,
    int      DeliveriesDone,
    int      AssignmentsTotal,
    int      AssignmentsFailed,
    decimal  TotalKm,
    decimal  CodCollected,      // 0 until Phase 3 (rider↔COD link)
    decimal  Earnings);         // 0 until Phase 4 (payout engine)
