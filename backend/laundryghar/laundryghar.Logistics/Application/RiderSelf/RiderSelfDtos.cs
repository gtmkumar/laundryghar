using NetTopologySuite.Geometries;

namespace laundryghar.Logistics.Application.RiderSelf;

/// <summary>GPS ping submitted by a rider device.</summary>
public sealed record LocationPingInput(
    /// <summary>Longitude (WGS-84).</summary>
    double Longitude,
    /// <summary>Latitude (WGS-84).</summary>
    double Latitude,
    /// <summary>UTC timestamp of this ping — used as the partition key (pinged_at).</summary>
    DateTimeOffset PingedAt,
    decimal? AccuracyMeters,
    decimal? SpeedKmph,
    decimal? HeadingDegrees,
    short?   BatteryPercent,
    bool?    IsMoving,
    string?  ActivityType,
    Guid?    CurrentAssignmentId);

/// <summary>Batch ping response.</summary>
public sealed record PingBatchResponse(int Accepted);

/// <summary>Rider-self status update for own assignment.</summary>
public sealed record RiderAssignmentStatusUpdateRequest(string Status);

/// <summary>Rider-self status update for one per-order task (started/arrived/completed/failed).</summary>
public sealed record RiderTaskStatusUpdateRequest(string Status);

/// <summary>Code the customer reads out, submitted for server-side verification.</summary>
public sealed record RiderTaskOtpVerifyRequest(string Code);
