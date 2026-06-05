using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Runtime feature / config key-value store per brand, app type and platform
/// (engagement_cms.mobile_app_config).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.</summary>
public class MobileAppConfig
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string AppType { get; set; } = null!;
    public string Platform { get; set; } = null!;
    public string ConfigKey { get; set; } = null!;

    /// <summary>jsonb — config value payload.</summary>
    public string ConfigValue { get; set; } = null!;

    public string? Description { get; set; }
    public bool IsForceUpdate { get; set; }
    public string? MinAppVersion { get; set; }
    public string? MaxAppVersion { get; set; }

    /// <summary>text[] — target audience segments.</summary>
    public string[]? TargetSegments { get; set; }

    /// <summary>0–100 rollout percentage. CHECK: rollout_percent BETWEEN 0 AND 100.</summary>
    public short? RolloutPercent { get; set; }

    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid? CreatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
}
