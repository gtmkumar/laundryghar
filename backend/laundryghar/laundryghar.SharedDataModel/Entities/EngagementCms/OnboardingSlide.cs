using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>App onboarding carousel slide content (engagement_cms.onboarding_slides).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.</summary>
public class OnboardingSlide
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string AppType { get; set; } = null!;
    public string Title { get; set; } = null!;

    /// <summary>jsonb — localised title map.</summary>
    public string TitleLocalized { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>jsonb — localised description map.</summary>
    public string DescriptionLocalized { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;
    public string? ImageDarkUrl { get; set; }
    public string? AnimationUrl { get; set; }
    public string? CtaText { get; set; }
    public string? CtaDeeplink { get; set; }

    /// <summary>character(7) — hex colour code e.g. #FFFFFF.</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>character(7) — hex colour code e.g. #000000.</summary>
    public string? TextColor { get; set; }

    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ShowFrom { get; set; }
    public DateTimeOffset? ShowUntil { get; set; }
    public string? MinAppVersion { get; set; }
    public string? MaxAppVersion { get; set; }

    /// <summary>text[] — target audience segments.</summary>
    public string[]? TargetSegments { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
}
