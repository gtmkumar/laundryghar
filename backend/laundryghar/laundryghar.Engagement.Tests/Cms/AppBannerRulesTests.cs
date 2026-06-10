using laundryghar.Engagement.Application.Cms.Commands;

namespace laundryghar.Engagement.Tests.Cms;

/// <summary>
/// Unit tests for <see cref="AppBannerRules"/> URL/deeplink allow-list logic.
///
/// <para>
/// The two entry points tested are <c>IsAllowedHttpUrl</c> and <c>IsAllowedDeeplink</c>,
/// each with a production flag (<c>isDevelopment=false</c>) and a development flag
/// (<c>isDevelopment=true</c>).
/// </para>
/// </summary>
public sealed class AppBannerRulesTests
{
    // ── IsAllowedHttpUrl — Production (isDevelopment = false) ────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowedHttpUrl_Production_AllowsNullOrWhitespace(string? value)
        => Assert.True(AppBannerRules.IsAllowedHttpUrl(value, isDevelopment: false));

    [Theory]
    [InlineData("https://cdn.laundryghar.com/banner.jpg")]
    [InlineData("https://example.com/img.png")]
    public void IsAllowedHttpUrl_Production_AllowsHttps(string value)
        => Assert.True(AppBannerRules.IsAllowedHttpUrl(value, isDevelopment: false));

    [Theory]
    [InlineData("http://cdn.laundryghar.com/banner.jpg")]
    [InlineData("http://example.com/img.png")]
    public void IsAllowedHttpUrl_Production_RejectsPlainHttp(string value)
        => Assert.False(AppBannerRules.IsAllowedHttpUrl(value, isDevelopment: false));

    [Theory]
    [InlineData("ftp://files.example.com/banner.jpg")]
    [InlineData("intent://host/#Intent")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public void IsAllowedHttpUrl_Production_RejectsDangerousSchemes(string value)
        => Assert.False(AppBannerRules.IsAllowedHttpUrl(value, isDevelopment: false));

    // ── IsAllowedHttpUrl — Development (isDevelopment = true) ────────────────

    [Theory]
    [InlineData("http://localhost:3000/banner.jpg")]
    [InlineData("http://192.168.1.1/mock.jpg")]
    [InlineData("https://cdn.laundryghar.com/banner.jpg")]
    public void IsAllowedHttpUrl_Development_AllowsHttpAndHttps(string value)
        => Assert.True(AppBannerRules.IsAllowedHttpUrl(value, isDevelopment: true));

    [Theory]
    [InlineData("ftp://files.example.com/banner.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("content://media/external")]
    public void IsAllowedHttpUrl_Development_StillRejectsDangerousSchemes(string value)
        => Assert.False(AppBannerRules.IsAllowedHttpUrl(value, isDevelopment: true));

    // ── IsAllowedDeeplink — Production ───────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowedDeeplink_Production_AllowsNullOrWhitespace(string? value)
        => Assert.True(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: false));

    [Theory]
    [InlineData("/orders")]
    [InlineData("/services/ironing")]
    public void IsAllowedDeeplink_Production_AllowsRelativePaths(string value)
        => Assert.True(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: false));

    [Theory]
    [InlineData("laundryghar://order/123")]
    [InlineData("LAUNDRYGHAR://home")]   // case-insensitive
    public void IsAllowedDeeplink_Production_AllowsAppScheme(string value)
        => Assert.True(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: false));

    [Theory]
    [InlineData("https://laundryghar.com/promo")]
    [InlineData("https://example.com/landing")]
    public void IsAllowedDeeplink_Production_AllowsHttps(string value)
        => Assert.True(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: false));

    [Theory]
    [InlineData("http://laundryghar.com/promo")]
    [InlineData("http://example.com/landing")]
    public void IsAllowedDeeplink_Production_RejectsPlainHttp(string value)
        => Assert.False(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: false));

    [Theory]
    [InlineData("intent://host/#Intent;scheme=https;end")]
    [InlineData("javascript:void(0)")]
    [InlineData("file:///data/app")]
    [InlineData("content://media/external/images/1")]
    public void IsAllowedDeeplink_Production_RejectsDangerousSchemes(string value)
        => Assert.False(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: false));

    // ── IsAllowedDeeplink — Development ──────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:8081/promo")]
    [InlineData("http://10.0.2.2:3000/landing")]
    [InlineData("https://cdn.example.com/promo")]
    [InlineData("laundryghar://home")]
    [InlineData("/orders")]
    public void IsAllowedDeeplink_Development_AllowsHttpAndOtherSafeValues(string value)
        => Assert.True(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: true));

    [Theory]
    [InlineData("intent://host/#Intent")]
    [InlineData("javascript:alert(1)")]
    public void IsAllowedDeeplink_Development_StillRejectsDangerousSchemes(string value)
        => Assert.False(AppBannerRules.IsAllowedDeeplink(value, isDevelopment: true));
}
