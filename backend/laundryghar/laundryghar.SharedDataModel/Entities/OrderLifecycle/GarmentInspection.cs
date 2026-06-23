using laundryghar.SharedDataModel.Entities.TenancyOrg;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Inspection record for a garment at a stage (laundry_fulfillment.garment_inspections).
/// FK to orders uses composite key — scalar columns only (no composite nav).
/// Has created_at, created_by only.</summary>
public class GarmentInspection
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid GarmentId { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at) — scalar only (no nav to avoid double-navigation via garment).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    /// <summary>FK to identity_access.users — cross-BC, scalar only.</summary>
    public Guid? InspectedByUserId { get; set; }

    public string InspectedByType { get; set; } = null!;
    public string InspectionType { get; set; } = null!;
    public DateTimeOffset InspectedAt { get; set; }
    public string? LocationType { get; set; }
    public Guid? LocationId { get; set; }

    /// <summary>GEOGRAPHY(Point,4326) — real geography type confirmed via \d+.</summary>
    public Point? GeoLocation { get; set; }

    public string Conditions { get; set; } = null!;
    public string? OverallCondition { get; set; }
    public short IssuesCount { get; set; }
    public string? Notes { get; set; }
    public bool CustomerAcknowledged { get; set; }
    public DateTimeOffset? CustomerAcknowledgedAt { get; set; }
    public string? CustomerSignatureS3Key { get; set; }
    public bool CustomerOtpVerified { get; set; }
    public string? QcResult { get; set; }
    public string? QcFailureReason { get; set; }
    public short RewashCount { get; set; }
    public bool RequiresSpecialCare { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Garment Garment { get; set; } = null!;
    public ICollection<GarmentInspectionPhoto> Photos { get; set; } = [];
}
