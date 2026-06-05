using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Customer booking for a delivery/pickup slot (order_lifecycle.delivery_slot_bookings).
/// FK to orders uses composite key — scalar only (order_id + order_created_at).
/// Has created_at, created_by only.</summary>
public class DeliverySlotBooking
{
    public Guid Id { get; set; }
    public Guid SlotId { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at) — scalar only.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column for composite FK to orders — scalar only.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public Guid? PickupRequestId { get; set; }
    public Guid CustomerId { get; set; }
    public string BookingType { get; set; } = null!;
    public DateTimeOffset BookedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public DeliverySlot Slot { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public PickupRequest? PickupRequest { get; set; }
}
