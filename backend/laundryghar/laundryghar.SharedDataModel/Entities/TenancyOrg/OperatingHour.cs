namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Weekly operating schedule for a store or warehouse (tenancy_org.operating_hours).
/// Has created_at, updated_at, created_by, updated_by and status, but NO version or deleted_at.</summary>
public class OperatingHour
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string ScopeType { get; set; } = null!;
    public Guid ScopeId { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday (DB CHECK 0–6).</summary>
    public short DayOfWeek { get; set; }

    public bool IsClosed { get; set; }
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
    public TimeOnly? BreakStart { get; set; }
    public TimeOnly? BreakEnd { get; set; }
    public string? Notes { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;
}
