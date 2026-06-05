using laundryghar.SharedDataModel.Entities.TenancyOrg;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>GPS location ping from a rider's device (logistics.rider_location_pings).
/// PARTITIONED table — composite PK (Id, PingedAt) required by PG range partitioning on pinged_at.
/// Has created_at, created_by only — no updated_at, no version, no deleted_at.
/// location column is GEOGRAPHY(Point,4326) — not null.</summary>
public class RiderLocationPing
{
    public Guid Id { get; set; }

    /// <summary>Partition key — part of composite PK.</summary>
    public DateTimeOffset PingedAt { get; set; }

    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>GEOGRAPHY(Point,4326) — not null.</summary>
    public Point Location { get; set; } = null!;

    public decimal? AccuracyMeters { get; set; }
    public decimal? SpeedKmph { get; set; }
    public decimal? HeadingDegrees { get; set; }
    public short? BatteryPercent { get; set; }
    public bool? IsMoving { get; set; }
    public string? ActivityType { get; set; }

    /// <summary>FK to logistics.rider_assignments — nullable; ON DELETE SET NULL.</summary>
    public Guid? CurrentAssignmentId { get; set; }

    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Rider Rider { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public RiderAssignment? CurrentAssignment { get; set; }
}
