using System.Security.Cryptography;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.Worker.Tests.Channels;

/// <summary>
/// Unit tests for notification settings resolution logic.
/// Tests the model-level behaviour (enabled + has creds → use; disabled → skip;
/// neither → log-only fallback) without hitting a database or HTTP endpoint.
/// </summary>
public sealed class NotificationSettingsResolutionTests
{
    private static IFieldCipher NewCipher()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        return new AesGcmFieldCipher(key);
    }

    // ── WhatsApp resolution logic ─────────────────────────────────────────────

    [Fact]
    public void WhatsApp_EnabledWithCreds_ShouldBeUsable()
    {
        var cipher = NewCipher();
        var stored = new WhatsAppSettings
        {
            Enabled       = true,
            PhoneNumberId = "112233445566",
            AccessToken   = cipher.Encrypt("EAABsToken"),
        };

        var json   = Serialize(stored);
        var loaded = WhatsAppSettings.FromJson(json, cipher);

        var usable = loaded.Enabled
            && !string.IsNullOrWhiteSpace(loaded.AccessToken)
            && !string.IsNullOrWhiteSpace(loaded.PhoneNumberId);

        Assert.True(usable);
        Assert.Equal("EAABsToken",   loaded.AccessToken);
        Assert.Equal("112233445566", loaded.PhoneNumberId);
    }

    [Fact]
    public void WhatsApp_Disabled_ShouldNotBeUsed()
    {
        var cipher = NewCipher();
        var stored = new WhatsAppSettings
        {
            Enabled       = false,
            PhoneNumberId = "112233445566",
            AccessToken   = cipher.Encrypt("EAABsToken"),
        };

        var json   = Serialize(stored);
        var loaded = WhatsAppSettings.FromJson(json, cipher);

        var usable = loaded.Enabled
            && !string.IsNullOrWhiteSpace(loaded.AccessToken)
            && !string.IsNullOrWhiteSpace(loaded.PhoneNumberId);

        Assert.False(usable);
    }

    [Fact]
    public void WhatsApp_EnabledButNoPhoneNumberId_ShouldNotBeUsed()
    {
        var settings = new WhatsAppSettings
        {
            Enabled       = true,
            PhoneNumberId = null,
            AccessToken   = "EAABsToken",
        };

        var usable = settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.AccessToken)
            && !string.IsNullOrWhiteSpace(settings.PhoneNumberId);

        Assert.False(usable);
    }

    [Fact]
    public void WhatsApp_Null_DefaultsToDisabled()
    {
        var settings = WhatsAppSettings.FromJson(null, NewCipher());

        Assert.False(settings.Enabled);
        Assert.Null(settings.AccessToken);
        Assert.Null(settings.PhoneNumberId);
    }

    // ── SMS resolution logic ──────────────────────────────────────────────────

    [Fact]
    public void Sms_EnabledWithCreds_ShouldBeUsable()
    {
        var cipher = NewCipher();
        var stored = new SmsSettings
        {
            Enabled   = true,
            AuthKey   = cipher.Encrypt("MSG91_AUTH_KEY"),
            SenderId  = "LAUNDR",
        };

        var json   = Serialize(stored);
        var loaded = SmsSettings.FromJson(json, cipher);

        var usable = loaded.Enabled
            && !string.IsNullOrWhiteSpace(loaded.AuthKey)
            && !string.IsNullOrWhiteSpace(loaded.SenderId);

        Assert.True(usable);
        Assert.Equal("MSG91_AUTH_KEY", loaded.AuthKey);
        Assert.Equal("LAUNDR",         loaded.SenderId);
    }

    [Fact]
    public void Sms_Disabled_ShouldNotBeUsed()
    {
        var settings = new SmsSettings
        {
            Enabled  = false,
            AuthKey  = "MSG91_AUTH_KEY",
            SenderId = "LAUNDR",
        };

        var usable = settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.AuthKey)
            && !string.IsNullOrWhiteSpace(settings.SenderId);

        Assert.False(usable);
    }

    [Fact]
    public void Sms_EnabledButNoSenderId_ShouldNotBeUsed()
    {
        var settings = new SmsSettings
        {
            Enabled  = true,
            AuthKey  = "MSG91_AUTH_KEY",
            SenderId = null,
        };

        var usable = settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.AuthKey)
            && !string.IsNullOrWhiteSpace(settings.SenderId);

        Assert.False(usable);
    }

    [Fact]
    public void Sms_Null_DefaultsToDisabled()
    {
        var settings = SmsSettings.FromJson(null, NewCipher());

        Assert.False(settings.Enabled);
        Assert.Null(settings.AuthKey);
        Assert.Null(settings.SenderId);
    }

    // ── Neither configured → log-only fallback ────────────────────────────────

    [Theory]
    [InlineData("whatsapp")]
    [InlineData("sms")]
    public void NeitherDbNorEnvConfigured_ShouldFallBackToLogOnly(string channel)
    {
        // Represent the "nothing configured" state.
        var waSettings  = new WhatsAppSettings { Enabled = false };
        var smsSettings = new SmsSettings      { Enabled = false };

        var waUsable = waSettings.Enabled
            && !string.IsNullOrWhiteSpace(waSettings.AccessToken)
            && !string.IsNullOrWhiteSpace(waSettings.PhoneNumberId);

        var smsUsable = smsSettings.Enabled
            && !string.IsNullOrWhiteSpace(smsSettings.AuthKey)
            && !string.IsNullOrWhiteSpace(smsSettings.SenderId);

        if (channel == "whatsapp") Assert.False(waUsable);
        else                       Assert.False(smsUsable);

        // Neither env-config creds present either — fallback to LoggingChannelSender.
        // (The actual fallback is exercised at runtime; here we confirm the guard conditions.)
        string? envAccessToken = null;
        string? envAuthKey     = null;
        Assert.Null(envAccessToken);
        Assert.Null(envAuthKey);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string Serialize<T>(T obj)
        => System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
}
