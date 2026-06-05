using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Optional service add-on (e.g. stain treatment, fold & pack) (customer_catalog.add_ons).
/// Has created_at, updated_at, created_by, updated_by, deleted_at, status. No version.</summary>
public class AddOn
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string? Description { get; set; }
    public string PricingType { get; set; } = null!;
    public decimal PriceValue { get; set; }
    public decimal? MinCharge { get; set; }
    public decimal? MaxCharge { get; set; }

    /// <summary>uuid[] — array of applicable service IDs (cross-entity reference, no nav property).</summary>
    public Guid[] ApplicableServices { get; set; } = [];

    /// <summary>uuid[] — array of applicable service category IDs (cross-entity reference, no nav property).</summary>
    public Guid[] ApplicableCategories { get; set; } = [];

    public bool IsTaxable { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool RequiresApproval { get; set; }
    public string? IconUrl { get; set; }
    public short DisplayOrder { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
}
