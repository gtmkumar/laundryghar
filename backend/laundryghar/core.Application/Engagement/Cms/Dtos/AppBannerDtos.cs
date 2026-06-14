using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.Engagement.Cms.Dtos;

/// <summary>The single source of truth for a banner's client-supplied fields. Create/update
/// requests and the read DTO all derive from this, so the field set, the entity mapping, and the
/// validation rules are each written once.</summary>
public abstract record AppBannerFields
{
    public required string AppType { get; init; }
    public required string Placement { get; init; }
    public string? Title { get; init; }
    public required string TitleLocalized { get; init; }
    public string? Subtitle { get; init; }
    public required string SubtitleLocalized { get; init; }
    public required string ImageUrl { get; init; }
    public string? ImageDarkUrl { get; init; }
    public string? CtaText { get; init; }
    public string? CtaDeeplink { get; init; }
    public string? ExternalUrl { get; init; }
    public Guid? PromotionId { get; init; }
    public Guid? CouponId { get; init; }
    public string? BackgroundColor { get; init; }
    public short DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? ShowFrom { get; init; }
    public DateTimeOffset? ShowUntil { get; init; }
    public string? TargetAudience { get; init; }
    public string[]? TargetSegments { get; init; }
    public string[]? TargetCities { get; init; }
    public string? MinAppVersion { get; init; }
}

/// <summary>Create payload — exactly the shared fields.</summary>
public sealed record CreateAppBannerRequest : AppBannerFields;

/// <summary>Update payload — the shared fields plus the settable status.</summary>
public sealed record UpdateAppBannerRequest : AppBannerFields
{
    public required string Status { get; init; }
}

/// <summary>Read model — the shared fields plus server-owned identity, counters, audit, and status.</summary>
public sealed record AppBannerDto : AppBannerFields
{
    public required Guid Id { get; init; }
    public required Guid BrandId { get; init; }
    public required int ImpressionsCount { get; init; }
    public required int ClicksCount { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Manual entity → DTO mapping (no AutoMapper).</summary>
    public static AppBannerDto FromEntity(AppBanner e) => new()
    {
        Id = e.Id,
        BrandId = e.BrandId,
        AppType = e.AppType,
        Placement = e.Placement,
        Title = e.Title,
        TitleLocalized = e.TitleLocalized,
        Subtitle = e.Subtitle,
        SubtitleLocalized = e.SubtitleLocalized,
        ImageUrl = e.ImageUrl,
        ImageDarkUrl = e.ImageDarkUrl,
        CtaText = e.CtaText,
        CtaDeeplink = e.CtaDeeplink,
        ExternalUrl = e.ExternalUrl,
        PromotionId = e.PromotionId,
        CouponId = e.CouponId,
        BackgroundColor = e.BackgroundColor,
        DisplayOrder = e.DisplayOrder,
        IsActive = e.IsActive,
        ShowFrom = e.ShowFrom,
        ShowUntil = e.ShowUntil,
        TargetAudience = e.TargetAudience,
        TargetSegments = e.TargetSegments,
        TargetCities = e.TargetCities,
        ImpressionsCount = e.ImpressionsCount,
        ClicksCount = e.ClicksCount,
        MinAppVersion = e.MinAppVersion,
        Status = e.Status,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
