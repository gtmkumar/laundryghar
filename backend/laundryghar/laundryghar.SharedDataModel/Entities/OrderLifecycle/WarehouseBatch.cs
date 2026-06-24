using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Machine/process batch in the warehouse (laundry_fulfillment.warehouse_batches).
/// Has created_at, updated_at, created_by, updated_by. No version, no deleted_at.</summary>
public class WarehouseBatch
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid WarehouseId { get; set; }
    public string BatchNumber { get; set; } = null!;
    public string BatchType { get; set; } = null!;
    public Guid? ServiceId { get; set; }
    public string? MachineId { get; set; }
    public string? CycleProgram { get; set; }
    public int ExpectedGarmentCount { get; set; }
    public int ActualGarmentCount { get; set; }
    public int? TotalWeightGrams { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public Guid? StartedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public int? DurationMinutes { get; set; }
    public string ChemicalsUsed { get; set; } = null!;
    public decimal? TemperatureCelsius { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = null!;
    public string? FailureReason { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public Service? Service { get; set; }
    public ICollection<FulfillmentUnit> FulfillmentUnits { get; set; } = [];
    public ICollection<QualityCheck> QualityChecks { get; set; } = [];
}
