using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Customer pickup request (order_lifecycle.pickup_requests).
/// Has created_at, updated_at, created_by, updated_by. No version, no deleted_at.</summary>
public class PickupRequest
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = null!;
    public Guid BrandId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? StoreId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid AddressId { get; set; }
    public Guid? PickupSlotId { get; set; }
    public DateOnly PickupDate { get; set; }
    public TimeOnly PickupWindowStart { get; set; }
    public TimeOnly PickupWindowEnd { get; set; }
    public bool IsExpress { get; set; }
    public int? EstimatedItems { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public Guid[] ServicesRequested { get; set; } = [];
    public string? CustomerNotes { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at) — scalar only (no composite nav supported here).</summary>
    public Guid? ConvertedOrderId { get; set; }

    /// <summary>Partition-key column for composite FK to orders.</summary>
    public DateTimeOffset? ConvertedOrderCreatedAt { get; set; }

    public string Status { get; set; } = null!;
    public string? CancellationReason { get; set; }
    public string? CancelledByType { get; set; }
    public Guid? CancelledById { get; set; }
    public Guid? RescheduledFromId { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise? Franchise { get; set; }
    public Store? Store { get; set; }
    public Customer Customer { get; set; } = null!;
    public CustomerAddress Address { get; set; } = null!;
    public DeliverySlot? PickupSlot { get; set; }
    public PickupRequest? RescheduledFrom { get; set; }
    public ICollection<DeliveryAssignment> DeliveryAssignments { get; set; } = [];
    public ICollection<DeliverySlotBooking> SlotBookings { get; set; } = [];
}
