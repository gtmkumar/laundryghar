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

    /// <summary>
    /// Estimated cart lines submitted by the customer at booking time.
    /// Serialised as JSON array: [{ serviceId?, itemId?, displayLabel, quantity, estimatedUnitPrice? }].
    /// These are ESTIMATES — the authoritative order is created after weighing at the store.
    /// </summary>
    public string RequestedItems { get; set; } = "[]";

    /// <summary>
    /// Customer payment intent at booking: wallet | cod | upi-deferred.
    /// UPI/card selections are recorded as "upi-deferred"; actual collection
    /// is always handled when the order is confirmed after weighing.
    /// </summary>
    public string PaymentPreference { get; set; } = "cod";

    /// <summary>
    /// Optional caller-supplied idempotency key.
    /// When provided, duplicate requests from the same customer return the
    /// existing pickup request instead of creating a new one.
    /// Uniqueness is enforced per (customer_id, idempotency_key) at DB level
    /// via a partial unique index (NULL keys excluded).
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Channel that originated this booking: app | web | mcp | whatsapp | pos | call.
    /// Defaults to "app" to keep backward compatibility with existing mobile callers.
    /// </summary>
    public string Source { get; set; } = "app";

    /// <summary>
    /// Optional coupon code submitted by the customer at booking time.
    /// Stored so it can be threaded into the order when admin converts the pickup to an order.
    /// Validated server-side at submit (coupon must be active + in-window + within per-customer limits).
    /// </summary>
    public string? CouponCode { get; set; }

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
