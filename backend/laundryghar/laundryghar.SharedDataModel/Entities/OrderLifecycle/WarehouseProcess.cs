using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Warehouse process definition (laundry_fulfillment.warehouse_processes).
/// Has created_at, created_by only — no updated_at, no version, no deleted_at.</summary>
public class WarehouseProcess
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string ProcessCategory { get; set; } = null!;
    public short SequenceOrder { get; set; }
    public int? ExpectedDurationMin { get; set; }
    public bool RequiresMachine { get; set; }
    public bool RequiresSupervisor { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
}
