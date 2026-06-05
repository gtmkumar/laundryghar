using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Time slot for pickup/delivery scheduling (order_lifecycle.delivery_slots).
/// Has created_at, updated_at, created_by, updated_by, status. No version, no deleted_at.</summary>
public class DeliverySlot
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly SlotDate { get; set; }
    public TimeOnly SlotStart { get; set; }
    public TimeOnly SlotEnd { get; set; }
    public string SlotType { get; set; } = null!;
    public int Capacity { get; set; }
    public int BookedCount { get; set; }
    public bool IsExpress { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? CutoffAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public ICollection<DeliverySlotBooking> Bookings { get; set; } = [];
}
