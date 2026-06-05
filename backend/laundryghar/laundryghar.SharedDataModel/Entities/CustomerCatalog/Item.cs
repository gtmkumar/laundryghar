using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Garment/article that can be laundered (customer_catalog.items).
/// Has created_at, updated_at, created_by, updated_by, version, deleted_at.</summary>
public class Item : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? ItemGroupId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? ImageUrl { get; set; }
    public int? TypicalWeightGrams { get; set; }
    public bool RequiresPerSidePrice { get; set; }

    /// <summary>tsvector column — mapped as string, read-only (DB-managed full-text index).</summary>
    public string? SearchTokens { get; set; }

    public string[] Aliases { get; set; } = [];
    public short DisplayOrder { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ItemGroup? ItemGroup { get; set; }
    public ICollection<ItemVariant> ItemVariants { get; set; } = [];
    public ICollection<PriceListItem> PriceListItems { get; set; } = [];
}
