using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Individual pricing row within a price list (customer_catalog.price_list_items).
/// Has created_at, updated_at, created_by, updated_by, status. No version, no deleted_at.</summary>
public class PriceListItem
{
    public Guid Id { get; set; }
    public Guid PriceListId { get; set; }
    public Guid BrandId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? ItemVariantId { get; set; }
    public Guid? FabricTypeId { get; set; }
    public Guid? ItemGroupId { get; set; }
    public decimal BasePrice { get; set; }
    public decimal? ExpressPrice { get; set; }
    public int MinimumQuantity { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool IsTaxable { get; set; }
    public string? DisplayLabel { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public PriceList PriceList { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public ItemVariant? ItemVariant { get; set; }
    public FabricType? FabricType { get; set; }
    public ItemGroup? ItemGroup { get; set; }
}
