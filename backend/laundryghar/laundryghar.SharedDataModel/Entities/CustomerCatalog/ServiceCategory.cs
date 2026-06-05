using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Top-level laundry service category (customer_catalog.service_categories).
/// Has created_at, updated_at, created_by, updated_by, version, deleted_at — all IAuditableEntity + ISoftDeletable.
/// No status in interface — status is a plain column here (no updated_by in interface affects nothing).</summary>
public class ServiceCategory : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLocalized { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? ColorHex { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsVisibleMobile { get; set; }
    public bool IsVisiblePos { get; set; }
    public string[] RequiresWarehouseCap { get; set; } = [];
    public string Status { get; set; } = null!;
    public DateOnly? SeasonalFrom { get; set; }
    public DateOnly? SeasonalTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<Service> Services { get; set; } = [];
}
