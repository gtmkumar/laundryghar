using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.AppBanners.Common;

/// <summary>Shared banner-field rules. Link targets are surfaced to mobile clients and handed to
/// router.push / Linking.openURL, so only safe forms are allowed (no intent://, file://, javascript: …).
/// Plain http:// is accepted in Development only.</summary>
internal static class AppBannerRules
{
    internal static readonly string[] ValidPlacements =
        ["home_top", "home_middle", "home_bottom", "services_top", "cart_top", "order_success", "profile"];

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

/// <summary>Rules common to create and update, written once against <see cref="AppBannerFields"/>.
/// The thin per-command validators delegate to this via <c>SetValidator</c>.</summary>
public sealed class AppBannerFieldsValidator<T> : AbstractValidator<T>
    where T : AppBannerFields
{
    public AppBannerFieldsValidator(IHostEnvironment env)
    {
        var isDev = env.IsDevelopment();

        RuleFor(x => x.Placement).NotEmpty()
            .Must(p => AppBannerRules.ValidPlacements.Contains(p))
            .WithMessage("placement must be one of: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile");
        RuleFor(x => x.TitleLocalized).NotEmpty();
        RuleFor(x => x.SubtitleLocalized).NotEmpty();
        RuleFor(x => x.ImageUrl).NotEmpty()
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageUrl must be an http(s) URL." : "imageUrl must be an https URL.");
        RuleFor(x => x.ImageDarkUrl)
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageDarkUrl must be an http(s) URL." : "imageDarkUrl must be an https URL.");
        RuleFor(x => x.CtaDeeplink)
            .Must(u => AppBannerRules.IsAllowedDeeplink(u, isDev))
            .WithMessage("ctaDeeplink must be a relative path, an https URL, or a laundryghar:// link.");
        RuleFor(x => x.ExternalUrl)
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "externalUrl must be an http(s) URL." : "externalUrl must be an https URL.");
    }
}
