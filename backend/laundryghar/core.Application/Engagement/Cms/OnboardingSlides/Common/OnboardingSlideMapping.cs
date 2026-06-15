using core.Application.Engagement.Cms.Dtos;
using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.Engagement.Cms.OnboardingSlides.Common;

internal static class OnboardingSlideMapping
{
    /// <summary>Copies every client-supplied field onto <paramref name="entity"/>. Server-owned
    /// fields (id, brand, audit, status) are set by the caller.</summary>
    public static OnboardingSlide ApplyFields(this OnboardingSlide entity, OnboardingSlideFields f)
    {
        entity.AppType = f.AppType;
        entity.Title = f.Title;
        entity.TitleLocalized = f.TitleLocalized;
        entity.Description = f.Description;
        entity.DescriptionLocalized = f.DescriptionLocalized;
        entity.ImageUrl = f.ImageUrl;
        entity.ImageDarkUrl = f.ImageDarkUrl;
        entity.AnimationUrl = f.AnimationUrl;
        entity.CtaText = f.CtaText;
        entity.CtaDeeplink = f.CtaDeeplink;
        entity.BackgroundColor = f.BackgroundColor;
        entity.TextColor = f.TextColor;
        entity.DisplayOrder = f.DisplayOrder;
        entity.IsActive = f.IsActive;
        entity.ShowFrom = f.ShowFrom;
        entity.ShowUntil = f.ShowUntil;
        entity.MinAppVersion = f.MinAppVersion;
        entity.MaxAppVersion = f.MaxAppVersion;
        entity.TargetSegments = f.TargetSegments;
        return entity;
    }
}
