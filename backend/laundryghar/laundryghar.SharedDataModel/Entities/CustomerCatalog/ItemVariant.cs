using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Variant of an item (fabric/side/size/colour) (customer_catalog.item_variants).
/// Has created_at, updated_at, created_by, updated_by, deleted_at, status.
/// No version — does NOT implement IAuditableEntity.</summary>
public class ItemVariant
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? FabricTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string VariantName { get; set; } = null!;
    public string? Side { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public short DisplayOrder { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public FabricType? FabricType { get; set; }
    public ICollection<PriceListItem> PriceListItems { get; set; } = [];
}
