using laundryghar.SharedDataModel.Entities.CustomerCatalog;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Add-on service applied to an order/item (order_lifecycle.order_addons).
/// FK to orders uses composite key (order_id, order_created_at).
/// Has created_at, created_by only — immutable log row.</summary>
public class OrderAddon
{
    public Guid Id { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried on child for composite FK.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public Guid? OrderItemId { get; set; }
    public Guid AddonId { get; set; }
    public string AddonNameSnapshot { get; set; } = null!;
    public string PricingType { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalCharge { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Order Order { get; set; } = null!;
    public OrderItem? OrderItem { get; set; }
    public AddOn AddOn { get; set; } = null!;
}
