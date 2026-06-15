using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.Engagement.Cms.Dtos;

/// <summary>The single source of truth for an onboarding slide's client-supplied fields.
/// Create/update requests and the read DTO all derive from this, so the field set, the entity
/// mapping, and the validation rules are each written once.</summary>
public abstract record OnboardingSlideFields
{
    public required string AppType { get; init; }
    public required string Title { get; init; }
    public required string TitleLocalized { get; init; }
    public string? Description { get; init; }
    public required string DescriptionLocalized { get; init; }
    public required string ImageUrl { get; init; }
    public string? ImageDarkUrl { get; init; }
    public string? AnimationUrl { get; init; }
    public string? CtaText { get; init; }
    public string? CtaDeeplink { get; init; }
    public string? BackgroundColor { get; init; }
    public string? TextColor { get; init; }
    public short DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? ShowFrom { get; init; }
    public DateTimeOffset? ShowUntil { get; init; }
    public string? MinAppVersion { get; init; }
    public string? MaxAppVersion { get; init; }
    public string[]? TargetSegments { get; init; }
}

/// <summary>Create payload — exactly the shared fields.</summary>
public sealed record CreateOnboardingSlideRequest : OnboardingSlideFields;

/// <summary>Update payload — the shared fields plus the settable status.</summary>
public sealed record UpdateOnboardingSlideRequest : OnboardingSlideFields
{
    public required string Status { get; init; }
}

/// <summary>Read model — the shared fields plus server-owned identity, audit, and status.</summary>
public sealed record OnboardingSlideDto : OnboardingSlideFields
{
    public required Guid Id { get; init; }
    public required Guid BrandId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Manual entity → DTO mapping (no AutoMapper).</summary>
    public static OnboardingSlideDto FromEntity(OnboardingSlide e) => new()
    {
        Id = e.Id,
        BrandId = e.BrandId,
        AppType = e.AppType,
        Title = e.Title,
        TitleLocalized = e.TitleLocalized,
        Description = e.Description,
        DescriptionLocalized = e.DescriptionLocalized,
        ImageUrl = e.ImageUrl,
        ImageDarkUrl = e.ImageDarkUrl,
        AnimationUrl = e.AnimationUrl,
        CtaText = e.CtaText,
        CtaDeeplink = e.CtaDeeplink,
        BackgroundColor = e.BackgroundColor,
        TextColor = e.TextColor,
        DisplayOrder = e.DisplayOrder,
        IsActive = e.IsActive,
        ShowFrom = e.ShowFrom,
        ShowUntil = e.ShowUntil,
        MinAppVersion = e.MinAppVersion,
        MaxAppVersion = e.MaxAppVersion,
        TargetSegments = e.TargetSegments,
        Status = e.Status,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
