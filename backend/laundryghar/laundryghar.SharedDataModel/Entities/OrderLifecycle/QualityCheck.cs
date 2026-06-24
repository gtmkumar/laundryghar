using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>QC record for a garment (laundry_fulfillment.quality_checks).
/// FK to orders uses composite key — scalar only.
/// Has created_at, updated_at, created_by, updated_by, status. No version, no deleted_at.</summary>
public class QualityCheck
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid FulfillmentUnitId { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at) — scalar only.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column for composite FK — scalar only.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public Guid? BatchId { get; set; }
    public short QcRound { get; set; }

    /// <summary>FK to identity_access.users — cross-BC, scalar only.</summary>
    public Guid InspectorUserId { get; set; }

    public DateTimeOffset InspectedAt { get; set; }
    public string Result { get; set; } = null!;
    public string Issues { get; set; } = null!;
    public Guid? PreWashInspectionId { get; set; }
    public Guid? PostWashInspectionId { get; set; }
    public string? ComparisonNotes { get; set; }
    public bool RequiresRewash { get; set; }
    public string? RewashPriority { get; set; }
    public bool SupervisorApproval { get; set; }
    public Guid? SupervisorUserId { get; set; }
    public DateTimeOffset? SupervisorApprovedAt { get; set; }
    public bool CustomerCommunicated { get; set; }
    public DateTimeOffset? CustomerCommunicatedAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public FulfillmentUnit FulfillmentUnit { get; set; } = null!;
    public WarehouseBatch? Batch { get; set; }
    public FulfillmentUnitInspection? PreWashInspection { get; set; }
    public FulfillmentUnitInspection? PostWashInspection { get; set; }
}
