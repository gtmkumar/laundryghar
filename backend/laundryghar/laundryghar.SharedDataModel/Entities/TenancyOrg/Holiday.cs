namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Holiday / closure day (tenancy_org.holidays).
/// Has created_at, updated_at, created_by, updated_by, status — but NO version or deleted_at.</summary>
public class Holiday
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string ScopeType { get; set; } = null!;
    public Guid? ScopeId { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsFullDay { get; set; }
    public TimeOnly? PartialOpenFrom { get; set; }
    public TimeOnly? PartialOpenTo { get; set; }
    public bool AcceptsOrders { get; set; }
    public bool IsRecurring { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;
}
