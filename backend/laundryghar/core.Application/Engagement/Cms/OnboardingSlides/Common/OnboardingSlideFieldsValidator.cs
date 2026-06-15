using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.OnboardingSlides.Common;

/// <summary>Shared onboarding-slide field rules. Link targets are surfaced to mobile clients and
/// handed to router.push / Linking.openURL, so only safe forms are allowed (no intent://, file://,
/// javascript: …). Plain http:// is accepted in Development only.</summary>
internal static class OnboardingSlideRules
{
    internal static readonly string[] ValidAppTypes = ["customer", "rider", "staff", "pos"];

    internal static bool IsAllowedDeeplink(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.StartsWith('/')) return true;
        if (value.StartsWith("laundryghar://", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return isDevelopment && uri.Scheme == Uri.UriSchemeHttp;
    }

    internal static bool IsAllowedHttpUrl(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return isDevelopment && uri.Scheme == Uri.UriSchemeHttp;
    }
}

/// <summary>Rules common to create and update, written once against <see cref="OnboardingSlideFields"/>.
/// The thin per-command validators delegate to this via <c>Include</c>.</summary>
public sealed class OnboardingSlideFieldsValidator<T> : AbstractValidator<T>
    where T : OnboardingSlideFields
{
    public OnboardingSlideFieldsValidator(IHostEnvironment env)
    {
        var isDev = env.IsDevelopment();

        RuleFor(x => x.AppType).NotEmpty()
            .Must(t => OnboardingSlideRules.ValidAppTypes.Contains(t))
            .WithMessage("app_type must be one of: customer, rider, staff, pos");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleLocalized).NotEmpty();
        RuleFor(x => x.DescriptionLocalized).NotEmpty();
        RuleFor(x => x.ImageUrl).NotEmpty()
            .Must(u => OnboardingSlideRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageUrl must be an http(s) URL." : "imageUrl must be an https URL.");
        RuleFor(x => x.ImageDarkUrl)
            .Must(u => OnboardingSlideRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageDarkUrl must be an http(s) URL." : "imageDarkUrl must be an https URL.");
        RuleFor(x => x.AnimationUrl)
            .Must(u => OnboardingSlideRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "animationUrl must be an http(s) URL." : "animationUrl must be an https URL.");
        RuleFor(x => x.CtaDeeplink)
            .Must(u => OnboardingSlideRules.IsAllowedDeeplink(u, isDev))
            .WithMessage("ctaDeeplink must be a relative path, an https URL, or a laundryghar:// link.");
    }
}
