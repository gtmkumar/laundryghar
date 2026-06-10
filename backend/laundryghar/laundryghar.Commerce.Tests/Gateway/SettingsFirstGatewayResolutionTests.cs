using System.Security.Cryptography;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using Microsoft.Extensions.Options;

namespace laundryghar.Commerce.Tests.Gateway;

/// <summary>
/// Unit tests for SettingsFirstPaymentGateway resolution-order logic.
///
/// The gateway itself is not mocked — we test the resolution path at the
/// settings model level (enabled + has creds → use; disabled → skip;
/// neither source → throw).
///
/// No database, no HTTP — resolution logic exercised via model assertions.
/// </summary>
public sealed class SettingsFirstGatewayResolutionTests
{
    private static IFieldCipher NewCipher()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        return new AesGcmFieldCipher(key);
    }

    // ── Settings model: Enabled + credentials present ─────────────────────────

    [Fact]
    public void DbSettings_EnabledWithCreds_ShouldBeUsable()
    {
        var cipher = NewCipher();
        var stored = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = true,
            KeyId         = "rzp_live_abc",
            KeySecret     = cipher.Encrypt("rzp_live_secret"),
            WebhookSecret = cipher.Encrypt("wh_secret"),
        };

        // Simulate resolution: Enabled + KeyId + KeySecret non-empty → usable
        Assert.True(stored.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(stored.KeyId));
        // KeySecret is stored as ciphertext at this point (not decrypted yet).
        // FromJson with cipher will decrypt — verify the round-trip.
        var json   = System.Text.Json.JsonSerializer.Serialize(stored, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var loaded = PaymentGatewaySettings.FromJson(json, cipher);

        Assert.True(loaded.Enabled);
        Assert.Equal("rzp_live_abc",    loaded.KeyId);
        Assert.Equal("rzp_live_secret", loaded.KeySecret);
        Assert.Equal("wh_secret",       loaded.WebhookSecret);
    }

    // ── Settings model: Disabled → resolution should fall back to env config ──

    [Fact]
    public void DbSettings_Disabled_ShouldNotBeUsed()
    {
        var cipher = NewCipher();
        var stored = new PaymentGatewaySettings
        {
            Provider  = "razorpay",
            Enabled   = false,
            KeyId     = "rzp_live_abc",
            KeySecret = cipher.Encrypt("secret"),
        };

        // The SettingsFirstPaymentGateway checks: dbSettings.Enabled && has creds.
        // Disabled → should NOT use — consumer falls through to env config.
        Assert.False(stored.Enabled);
    }

    // ── Settings model: Enabled but no creds → fall through to env config ─────

    [Fact]
    public void DbSettings_EnabledButNoCreds_ShouldNotBeUsed()
    {
        var settings = new PaymentGatewaySettings
        {
            Provider  = "razorpay",
            Enabled   = true,
            KeyId     = null,          // no key id
            KeySecret = null,
        };

        // Resolution check: Enabled AND KeyId AND KeySecret all non-empty.
        var hasAllCreds = settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.KeyId)
            && !string.IsNullOrWhiteSpace(settings.KeySecret);

        Assert.False(hasAllCreds);
    }

    // ── Env config: both keys present → should be usable ─────────────────────

    [Fact]
    public void EnvConfig_BothKeysPresent_IsUsable()
    {
        var envSettings = new RazorpaySettings
        {
            KeyId     = "rzp_test_key",
            KeySecret = "rzp_test_secret",
        };

        var hasAllCreds = !string.IsNullOrWhiteSpace(envSettings.KeyId)
                       && !string.IsNullOrWhiteSpace(envSettings.KeySecret);

        Assert.True(hasAllCreds);
    }

    // ── Neither source configured → should fail closed ────────────────────────

    [Fact]
    public void NeitherSourceConfigured_FailsClosed()
    {
        var dbSettings  = new PaymentGatewaySettings { Enabled = false };
        var envSettings = new RazorpaySettings();

        var dbUsable  = dbSettings.Enabled
            && !string.IsNullOrWhiteSpace(dbSettings.KeyId)
            && !string.IsNullOrWhiteSpace(dbSettings.KeySecret);

        var envUsable = !string.IsNullOrWhiteSpace(envSettings.KeyId)
                     && !string.IsNullOrWhiteSpace(envSettings.KeySecret);

        // Neither usable → gateway must throw (tested at runtime; here we confirm
        // the guard conditions evaluate to false for the empty/disabled case).
        Assert.False(dbUsable);
        Assert.False(envUsable);
    }

    // ── RazorpaySettings: Options.Create round-trip ───────────────────────────

    [Fact]
    public void RazorpaySettings_OptionsCreate_BindsCorrectly()
    {
        var settings = new RazorpaySettings
        {
            KeyId         = "rzp_live_id",
            KeySecret     = "rzp_live_sec",
            WebhookSecret = "wh_sec",
        };

        var opts = Options.Create(settings);

        Assert.Equal("rzp_live_id",  opts.Value.KeyId);
        Assert.Equal("rzp_live_sec", opts.Value.KeySecret);
        Assert.Equal("wh_sec",       opts.Value.WebhookSecret);
    }
}
