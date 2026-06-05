namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Maps which warehouses serve which stores (tenancy_org.store_warehouse_mappings).
/// No version or deleted_at — not IAuditableEntity or ISoftDeletable.
/// Has created_at, updated_at, created_by but NOT updated_by or version.</summary>
public class StoreWarehouseMapping
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }
    public Guid WarehouseId { get; set; }
    public bool IsPrimary { get; set; }
    public string[] ServiceTypes { get; set; } = [];
    public short Priority { get; set; }
    public TimeOnly? CutoffTime { get; set; }
    public int? TravelTimeMinutes { get; set; }
    public decimal? DistanceKm { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Store Store { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
