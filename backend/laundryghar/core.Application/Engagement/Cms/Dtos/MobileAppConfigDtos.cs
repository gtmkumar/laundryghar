using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.Engagement.Cms.Dtos;

/// <summary>The single source of truth for a mobile app config's client-supplied fields.
/// Create/update requests and the read DTO all derive from this, so the field set, the entity
/// mapping, and the validation rules are each written once.</summary>
public abstract record MobileAppConfigFields
{
    public required string AppType { get; init; }
    public required string Platform { get; init; }
    public required string ConfigKey { get; init; }
    public required string ConfigValue { get; init; }
    public string? Description { get; init; }
    public bool IsForceUpdate { get; init; }
    public string? MinAppVersion { get; init; }
    public string? MaxAppVersion { get; init; }
    public string[]? TargetSegments { get; init; }
    public short? RolloutPercent { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>Create payload — exactly the shared fields.</summary>
public sealed record CreateMobileAppConfigRequest : MobileAppConfigFields;

/// <summary>Update payload — the shared fields plus the settable status.</summary>
public sealed record UpdateMobileAppConfigRequest : MobileAppConfigFields
{
    public required string Status { get; init; }
}

/// <summary>Read model — the shared fields plus server-owned identity, audit, and status.</summary>
public sealed record MobileAppConfigDto : MobileAppConfigFields
{
    public required Guid Id { get; init; }
    public required Guid BrandId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Manual entity → DTO mapping (no AutoMapper).</summary>
    public static MobileAppConfigDto FromEntity(MobileAppConfig e) => new()
    {
        Id = e.Id,
        BrandId = e.BrandId,
        AppType = e.AppType,
        Platform = e.Platform,
        ConfigKey = e.ConfigKey,
        ConfigValue = e.ConfigValue,
        Description = e.Description,
        IsForceUpdate = e.IsForceUpdate,
        MinAppVersion = e.MinAppVersion,
        MaxAppVersion = e.MaxAppVersion,
        TargetSegments = e.TargetSegments,
        RolloutPercent = e.RolloutPercent,
        IsActive = e.IsActive,
        Status = e.Status,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
