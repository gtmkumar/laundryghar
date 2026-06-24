using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Line item within an order (order_lifecycle.order_items).
/// FK to orders uses composite key (order_id, order_created_at).
/// Has created_at, updated_at, created_by, updated_by, status. No version, no deleted_at.</summary>
public class OrderItem
{
    public Guid Id { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried on child for composite FK.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }
    public short LineNumber { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? ItemVariantId { get; set; }
    public Guid? FabricTypeId { get; set; }
    public Guid? PriceListItemId { get; set; }
    public string ItemNameSnapshot { get; set; } = null!;
    public string ServiceNameSnapshot { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = null!;
    public decimal LineSubtotal { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineAddonsTotal { get; set; }
    public decimal LineTax { get; set; }
    public decimal LineTotal { get; set; }
    public bool IsExpress { get; set; }
    public string? Notes { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Order Order { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public ItemVariant? ItemVariant { get; set; }
    public FabricType? FabricType { get; set; }
    public PriceListItem? PriceListItem { get; set; }
    public ICollection<OrderAddon> OrderAddons { get; set; } = [];
    public ICollection<FulfillmentUnit> FulfillmentUnits { get; set; } = [];
}
