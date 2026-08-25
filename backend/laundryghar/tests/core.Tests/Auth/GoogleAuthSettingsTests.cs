using core.Application.Identity.Auth.Common;
using Xunit;

namespace core.Tests.Auth;

/// <summary>
/// The accepted-audience set decides which Google OAuth clients can sign a user in.
/// Getting it wrong in either direction is serious: too narrow and a whole platform's
/// logins fail; too broad (or empty) and tokens minted for someone else's project would
/// be accepted.
/// </summary>
public class GoogleAuthSettingsTests
{
    [Fact]
    public void AllowedAudiences_collects_every_configured_platform_client()
    {
        var settings = new GoogleAuthSettings
        {
            WebClientId     = "web.apps.googleusercontent.com",
            AndroidClientId = "android.apps.googleusercontent.com",
            IosClientId     = "ios.apps.googleusercontent.com",
        };

        Assert.Equal(
            ["web.apps.googleusercontent.com", "android.apps.googleusercontent.com", "ios.apps.googleusercontent.com"],
            settings.AllowedAudiences());
    }

    [Fact]
    public void AllowedAudiences_includes_additional_client_ids()
    {
        var settings = new GoogleAuthSettings
        {
            WebClientId = "web.apps.googleusercontent.com",
            AdditionalClientIds = ["staging.apps.googleusercontent.com"],
        };

        Assert.Contains("staging.apps.googleusercontent.com", settings.AllowedAudiences());
    }

    [Fact]
    public void AllowedAudiences_deduplicates_repeated_ids()
    {
        // Expo reuses the web client on native when no platform client is configured,
        // so the same value legitimately appears twice in configuration.
        var settings = new GoogleAuthSettings
        {
            WebClientId     = "shared.apps.googleusercontent.com",
            AndroidClientId = "shared.apps.googleusercontent.com",
        };

        Assert.Single(settings.AllowedAudiences());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AllowedAudiences_ignores_blank_ids(string? blank)
    {
        var settings = new GoogleAuthSettings
        {
            WebClientId     = "web.apps.googleusercontent.com",
            AndroidClientId = blank,
        };

        Assert.Single(settings.AllowedAudiences());
    }

    [Fact]
    public void AllowedAudiences_trims_whitespace_around_ids()
    {
        // A trailing newline from an env file must not become part of the audience,
        // or every token from that platform fails an exact-match comparison.
        var settings = new GoogleAuthSettings { WebClientId = "  web.apps.googleusercontent.com\n" };

        Assert.Equal(["web.apps.googleusercontent.com"], settings.AllowedAudiences());
    }

    [Fact]
    public void IsConfigured_is_false_when_nothing_is_set()
    {
        // Drives the fail-closed path in GoogleIdTokenVerifier: with no audiences, ANY
        // Google token would otherwise validate.
        Assert.False(new GoogleAuthSettings().IsConfigured);
        Assert.Empty(new GoogleAuthSettings().AllowedAudiences());
    }

    [Fact]
    public void IsConfigured_is_true_with_a_single_client()
    {
        Assert.True(new GoogleAuthSettings { WebClientId = "web.apps.googleusercontent.com" }.IsConfigured);
    }
}
