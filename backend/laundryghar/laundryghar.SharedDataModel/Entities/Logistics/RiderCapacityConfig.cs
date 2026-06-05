using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Per-rider, per-slot capacity configuration (logistics.rider_capacity_config).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.
/// day_of_week CHECK: 0–6 (0=Sunday). status CHECK: active | inactive | archived.</summary>
public class RiderCapacityConfig
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>FK to tenancy_org.stores — optional; NULL means applies to all stores.</summary>
    public Guid? StoreId { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday. NULL means applies to all days.</summary>
    public short? DayOfWeek { get; set; }

    public TimeOnly? SlotStart { get; set; }
    public TimeOnly? SlotEnd { get; set; }
    public int MaxPickupsPerSlot { get; set; }
    public int MaxDeliveriesPerSlot { get; set; }
    public int MaxConcurrentOrders { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Rider Rider { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Store? Store { get; set; }
}
