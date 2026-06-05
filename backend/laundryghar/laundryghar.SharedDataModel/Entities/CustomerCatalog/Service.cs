using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>A specific laundry service offered under a category (customer_catalog.services).
/// Has created_at, updated_at, created_by, updated_by, version, deleted_at.</summary>
public class Service : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string? Description { get; set; }
    public string PricingModel { get; set; } = null!;
    public int BaseTatHours { get; set; }
    public int ExpressTatHours { get; set; }
    public decimal ExpressMultiplier { get; set; }
    public bool IsExpressAvailable { get; set; }
    public bool RequiresInspection { get; set; }
    public bool RequiresQc { get; set; }
    public string? IconUrl { get; set; }
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
    public ServiceCategory Category { get; set; } = null!;
    public ICollection<PriceListItem> PriceListItems { get; set; } = [];
}
