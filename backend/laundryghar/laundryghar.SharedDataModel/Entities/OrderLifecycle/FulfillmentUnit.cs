using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Individual garment tracked through the laundry lifecycle (laundry_fulfillment.garments).
/// FK to orders uses composite key (order_id, order_created_at).
/// Has created_at, updated_at, created_by, updated_by, version, status. No deleted_at.</summary>
public class FulfillmentUnit
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid StoreId { get; set; }
    public Guid? WarehouseId { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public Guid OrderItemId { get; set; }
    public Guid CustomerId { get; set; }
    public string TagCode { get; set; } = null!;
    public string? SecondaryTagCode { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? ItemVariantId { get; set; }
    public Guid? ItemGroupId { get; set; }
    public Guid? FabricTypeId { get; set; }
    public string? Color { get; set; }
    public string? BrandName { get; set; }
    public string? Size { get; set; }
    public decimal? DeclaredValue { get; set; }
    public string CurrentStage { get; set; } = null!;
    public string? CurrentLocationType { get; set; }
    public Guid? CurrentLocationId { get; set; }
    public Guid? CurrentBatchId { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }
    public Guid? LastScannedBy { get; set; }
    public DateTimeOffset? ExpectedCompletionAt { get; set; }
    public DateTimeOffset? ActualCompletionAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Strategy-private laundry attributes, persisted as the <c>attributes</c> jsonb column
    /// (multi-vertical Phase 1 / slice F-2). These are laundry <c>process_deliver</c>-specific
    /// and deliberately kept OFF the generic fulfilment-unit spine so other verticals' units
    /// don't carry empty wash/QC columns. <c>fabric_type_id</c> stays a real FK column (it is a
    /// referential link used by warehouse joins, not a free-form attribute).
    /// </summary>
    public LaundryUnitAttributes Attributes { get; set; } = new();

    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public Warehouse? Warehouse { get; set; }
    public Order Order { get; set; } = null!;
    public OrderItem OrderItem { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Item? Item { get; set; }
    public ItemVariant? ItemVariant { get; set; }
    public ItemGroup? ItemGroup { get; set; }
    public FabricType? FabricType { get; set; }
    public WarehouseBatch? CurrentBatch { get; set; }
    public ICollection<FulfillmentUnitInspection> Inspections { get; set; } = [];
    public ICollection<FulfillmentUnitInspectionPhoto> InspectionPhotos { get; set; } = [];
    public ICollection<QualityCheck> QualityChecks { get; set; } = [];
}

/// <summary>
/// Laundry <c>process_deliver</c>-private attributes of a fulfilment unit, stored as the
/// <c>laundry_fulfillment.fulfillment_unit.attributes</c> jsonb column (owned type, mapped via
/// <c>ToJson</c>). Extracted off the generic spine in multi-vertical Phase 1 so the shared
/// fulfilment-unit shape carries no wash/QC-specific columns.
/// </summary>
public class LaundryUnitAttributes
{
    public int? WeightGrams { get; set; }
    public bool HasOrnaments { get; set; }
    public bool HasLining { get; set; }
    public bool IsDesignerWear { get; set; }
    public short RewashCount { get; set; }
    public string? CareInstructions { get; set; }
}
