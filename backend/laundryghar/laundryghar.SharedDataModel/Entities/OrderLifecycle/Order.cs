using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Laundry order (order_lifecycle.orders).
/// PARTITIONED table — composite PK (Id, CreatedAt) required by PG range partitioning.
/// amount_due is a GENERATED ALWAYS column — mapped as ValueGeneratedOnAddOrUpdate.
/// Has all IAuditableEntity columns + deleted_at.</summary>
public class Order : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Partition key — part of composite PK.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public string OrderNumber { get; set; } = null!;
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid StoreId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? PickupAddressId { get; set; }
    public Guid? DeliveryAddressId { get; set; }
    public Guid? PickupSlotId { get; set; }
    public Guid? DeliverySlotId { get; set; }

    /// <summary>FK to logistics.riders — cross-BC, scalar only.</summary>
    public Guid? PickupRiderId { get; set; }

    /// <summary>FK to logistics.riders — cross-BC, scalar only.</summary>
    public Guid? DeliveryRiderId { get; set; }

    public string Channel { get; set; } = null!;

    /// <summary>Marketplace job kind — see <see cref="Enums.JobType"/>. Defaults to laundry;
    /// 'parcel' is a point-to-point delivery riding the same order spine.</summary>
    public string JobType { get; set; } = "laundry";

    public string OrderType { get; set; } = null!;
    public bool IsExpress { get; set; }

    /// <summary>Vehicle tier this job requires — see <see cref="Enums.VehicleTier"/>.
    /// NULL = no constraint (any eligible rider). Drives tier-aware dispatch matching.</summary>
    public string? RequestedVehicleTier { get; set; }

    public bool RequiresPickup { get; set; }
    public bool RequiresDelivery { get; set; }
    public string? PickupOtp { get; set; }
    public string? DeliveryOtp { get; set; }
    public decimal Subtotal { get; set; }
    public decimal AddonTotal { get; set; }
    public decimal ExpressSurcharge { get; set; }
    public decimal PickupCharge { get; set; }
    public decimal DeliveryCharge { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal CouponDiscount { get; set; }
    public decimal LoyaltyDiscount { get; set; }
    public decimal PackageDiscount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only.</summary>
    public decimal? AmountDue { get; set; }

    public decimal RefundedAmount { get; set; }
    public string CurrencyCode { get; set; } = null!;

    /// <summary>FK to commerce.coupons — cross-BC, scalar only.</summary>
    public Guid? CouponId { get; set; }

    public string? CouponCode { get; set; }

    /// <summary>FK to commerce.packages — cross-BC, scalar only.</summary>
    public Guid? PackageId { get; set; }

    /// <summary>FK to commerce.customer_packages — cross-BC, scalar only.</summary>
    public Guid? CustomerPackageId { get; set; }

    public int LoyaltyPointsUsed { get; set; }
    public int LoyaltyPointsEarned { get; set; }
    public int TotalItems { get; set; }
    public int TotalGarments { get; set; }
    public int? TotalWeightGrams { get; set; }
    public string Status { get; set; } = null!;
    public string? SubStatus { get; set; }
    public string PaymentStatus { get; set; } = null!;
    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? PickupScheduledAt { get; set; }
    public DateTimeOffset? PickedUpAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? QcCompletedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? OutForDeliveryAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public string? CancelledByType { get; set; }
    public Guid? CancelledById { get; set; }
    public DateTimeOffset? PromisedDeliveryAt { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTimeOffset? InvoiceGeneratedAt { get; set; }
    public string? InvoiceS3Key { get; set; }
    public string? NotesCustomer { get; set; }
    public string? NotesInternal { get; set; }
    public short? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTimeOffset? RatedAt { get; set; }
    public string Metadata { get; set; } = null!;
    public IPAddress? SourceIp { get; set; }
    public string? SourceUserAgent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public Warehouse? Warehouse { get; set; }
    public Customer Customer { get; set; } = null!;
    public CustomerAddress? PickupAddress { get; set; }
    public CustomerAddress? DeliveryAddress { get; set; }
    public DeliverySlot? PickupSlot { get; set; }
    public DeliverySlot? DeliverySlot { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = [];
    public ICollection<OrderAddon> OrderAddons { get; set; } = [];
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = [];
    public ICollection<OrderNote> Notes { get; set; } = [];
    public ICollection<Garment> Garments { get; set; } = [];
}
