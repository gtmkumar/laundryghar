using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Reference lookup for garment condition types (order_lifecycle.garment_conditions).
/// Has created_at, updated_at, created_by, updated_by, status. No version, no deleted_at.</summary>
public class GarmentCondition
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string[] SeverityLevels { get; set; } = [];
    public bool RequiresDisclaimer { get; set; }
    public string? DisclaimerText { get; set; }
    public string? IconUrl { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
}
