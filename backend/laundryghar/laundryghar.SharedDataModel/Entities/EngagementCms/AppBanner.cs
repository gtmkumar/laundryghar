using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Promotional banner displayed within the mobile app (engagement_cms.app_banners).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.</summary>
public class AppBanner
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string AppType { get; set; } = null!;
    public string Placement { get; set; } = null!;
    public string? Title { get; set; }

    /// <summary>jsonb — localised title map.</summary>
    public string TitleLocalized { get; set; } = null!;

    public string? Subtitle { get; set; }

    /// <summary>jsonb — localised subtitle map.</summary>
    public string SubtitleLocalized { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;
    public string? ImageDarkUrl { get; set; }
    public string? CtaText { get; set; }
    public string? CtaDeeplink { get; set; }
    public string? ExternalUrl { get; set; }
    public Guid? PromotionId { get; set; }
    public Guid? CouponId { get; set; }

    /// <summary>character(7) — hex colour code e.g. #FFFFFF.</summary>
    public string? BackgroundColor { get; set; }

    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ShowFrom { get; set; }
    public DateTimeOffset? ShowUntil { get; set; }
    public string? TargetAudience { get; set; }

    /// <summary>text[] — audience segment identifiers.</summary>
    public string[]? TargetSegments { get; set; }

    /// <summary>text[] — city identifiers.</summary>
    public string[]? TargetCities { get; set; }

    public int ImpressionsCount { get; set; }
    public int ClicksCount { get; set; }
    public string? MinAppVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Promotion? Promotion { get; set; }
    public Coupon? Coupon { get; set; }
}
