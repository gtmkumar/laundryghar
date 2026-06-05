using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Immutable garment process scan log (order_lifecycle.process_logs).
/// PARTITIONED table — composite PK (Id, OccurredAt) required by PG range partitioning.
/// Has created_at, created_by only.</summary>
public class ProcessLog
{
    public Guid Id { get; set; }

    /// <summary>Partition key — part of composite PK.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public Guid BrandId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid GarmentId { get; set; }
    public string TagCode { get; set; } = null!;
    public Guid? ProcessId { get; set; }
    public string ProcessCode { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? FromStage { get; set; }
    public string? ToStage { get; set; }
    public Guid? PerformedByUserId { get; set; }
    public string? PerformedByName { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Notes { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseBatch? Batch { get; set; }
    public Garment Garment { get; set; } = null!;
    public WarehouseProcess? Process { get; set; }
}
