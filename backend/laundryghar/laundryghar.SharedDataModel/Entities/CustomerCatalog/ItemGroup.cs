using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Logical group of garment items (customer_catalog.item_groups).
/// Has created_at, updated_at, created_by, updated_by, deleted_at, status.
/// No version — does NOT implement IAuditableEntity.</summary>
public class ItemGroup
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string? IconUrl { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsVisibleMobile { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = [];
    public ICollection<PriceListItem> PriceListItems { get; set; } = [];
}
