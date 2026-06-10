using System.Security.Cryptography;
using laundryghar.Identity.Application.Settings;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using System.Text.Json;

namespace laundryghar.Identity.Tests.Settings;

/// <summary>
/// Unit tests for payment-gateway / WhatsApp / SMS settings parse, encrypt, mask round-trip.
/// No database, no HTTP, no MediatR — pure model/cipher assertions.
/// </summary>
public sealed class IntegrationSettingsRoundTripTests
{
    // ── Cipher setup ──────────────────────────────────────────────────────────

    /// <summary>Build a fresh AES-256-GCM cipher for each test (independent keys).</summary>
    private static IFieldCipher NewCipher()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        return new AesGcmFieldCipher(key);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PaymentGatewaySettings — parse / encrypt / decrypt round-trip
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentGatewaySettings_FromJson_DefaultsWhenNull()
    {
        var cipher   = NewCipher();
        var settings = PaymentGatewaySettings.FromJson(null, cipher);

        Assert.Equal("razorpay", settings.Provider);
        Assert.False(settings.Enabled);
        Assert.Null(settings.KeyId);
        Assert.Null(settings.KeySecret);
        Assert.Null(settings.WebhookSecret);
        Assert.False(settings.CodEnabled);
    }

    [Fact]
    public void PaymentGatewaySettings_FromJson_DefaultsWhenMalformed()
    {
        var cipher   = NewCipher();
        var settings = PaymentGatewaySettings.FromJson("not-json{{{", cipher);

        Assert.Equal("razorpay", settings.Provider);
        Assert.False(settings.Enabled);
    }

    [Fact]
    public void PaymentGatewaySettings_SecretEncryptDecryptRoundTrip()
    {
        var cipher     = NewCipher();
        const string plainKeySecret     = "rzp_live_super_secret";
        const string plainWebhookSecret = "wh_secret_12345";

        // Simulate what UpdatePaymentGatewayHandler does: encrypt before storing.
        var stored = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = true,
            KeyId         = "rzp_live_keyid",
            KeySecret     = cipher.Encrypt(plainKeySecret),
            WebhookSecret = cipher.Encrypt(plainWebhookSecret),
            CodEnabled    = true,
        };

        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // FromJson should decrypt both secrets.
        var loaded = PaymentGatewaySettings.FromJson(json, cipher);

        Assert.Equal("rzp_live_keyid",       loaded.KeyId);
        Assert.Equal(plainKeySecret,          loaded.KeySecret);
        Assert.Equal(plainWebhookSecret,      loaded.WebhookSecret);
        Assert.True(loaded.Enabled);
        Assert.True(loaded.CodEnabled);
    }

    [Fact]
    public void PaymentGatewaySettings_NullSecrets_RoundTripSafe()
    {
        var cipher = NewCipher();

        var stored = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = false,
            KeyId         = null,
            KeySecret     = null,   // Encrypt(null) = null
            WebhookSecret = null,
        };

        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = PaymentGatewaySettings.FromJson(json, cipher);

        Assert.Null(loaded.KeySecret);
        Assert.Null(loaded.WebhookSecret);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WhatsAppSettings — parse / encrypt / decrypt round-trip
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WhatsAppSettings_FromJson_DefaultsWhenNull()
    {
        var settings = WhatsAppSettings.FromJson(null, NewCipher());

        Assert.False(settings.Enabled);
        Assert.Null(settings.PhoneNumberId);
        Assert.Null(settings.AccessToken);
    }

    [Fact]
    public void WhatsAppSettings_SecretEncryptDecryptRoundTrip()
    {
        var cipher       = NewCipher();
        const string plainToken = "EAABs0tYourToken1234";

        var stored = new WhatsAppSettings
        {
            Enabled       = true,
            PhoneNumberId = "112233445566",
            AccessToken   = cipher.Encrypt(plainToken),
        };

        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = WhatsAppSettings.FromJson(json, cipher);

        Assert.True(loaded.Enabled);
        Assert.Equal("112233445566", loaded.PhoneNumberId);
        Assert.Equal(plainToken, loaded.AccessToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SmsSettings — parse / encrypt / decrypt round-trip
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SmsSettings_FromJson_DefaultsWhenNull()
    {
        var settings = SmsSettings.FromJson(null, NewCipher());

        Assert.Equal("msg91",   settings.Provider);
        Assert.False(settings.Enabled);
        Assert.Null(settings.AuthKey);
        Assert.Null(settings.SenderId);
        Assert.Null(settings.DltTemplateId);
    }

    [Fact]
    public void SmsSettings_SecretEncryptDecryptRoundTrip()
    {
        var cipher        = NewCipher();
        const string plainAuthKey = "ABCDE123456789";

        var stored = new SmsSettings
        {
            Provider      = "msg91",
            Enabled       = true,
            AuthKey       = cipher.Encrypt(plainAuthKey),
            SenderId      = "LAUNDR",
            DltTemplateId = "1234567890123456789",
        };

        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = SmsSettings.FromJson(json, cipher);

        Assert.Equal("msg91",                loaded.Provider);
        Assert.True(loaded.Enabled);
        Assert.Equal(plainAuthKey,           loaded.AuthKey);
        Assert.Equal("LAUNDR",               loaded.SenderId);
        Assert.Equal("1234567890123456789",  loaded.DltTemplateId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MaskSecret helper — masking logic
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null,              null)]
    [InlineData("",                null)]
    [InlineData("1234",            "••••1234")]  // exactly 4 chars
    [InlineData("abc",             "••••")]      // shorter than 4
    [InlineData("rzp_live_ABCDEF", "••••CDEF")]  // normal long secret
    public void MaskSecret_ProducesExpectedMask(string? input, string? expected)
    {
        var result = SettingsStore.MaskSecret(input);
        Assert.Equal(expected, result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Resolution order: settings-row enabled + creds → use DB; disabled → skip
    // (Model-level, no DB required — tests the logic of FromJson + Enabled check)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentGatewaySettings_DisabledRow_ShouldNotBeUsed()
    {
        var cipher = NewCipher();

        // A row that has creds but Enabled = false.
        var stored = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = false,
            KeyId         = "rzp_live_key",
            KeySecret     = cipher.Encrypt("rzp_live_secret"),
        };

        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = PaymentGatewaySettings.FromJson(json, cipher);

        // The consumer (SettingsFirstPaymentGateway) checks Enabled before using.
        // Here we confirm the Enabled flag survives the JSON round-trip correctly.
        Assert.False(loaded.Enabled);
        // And the secret is still decrypted (the caller decides what to do with it).
        Assert.Equal("rzp_live_secret", loaded.KeySecret);
    }

    [Fact]
    public void WhatsAppSettings_DisabledRow_EnabledFlagSurvivesRoundTrip()
    {
        var cipher = NewCipher();
        var stored = new WhatsAppSettings { Enabled = false, PhoneNumberId = "111", AccessToken = cipher.Encrypt("tok") };
        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = WhatsAppSettings.FromJson(json, cipher);

        Assert.False(loaded.Enabled);
    }

    [Fact]
    public void SmsSettings_DisabledRow_EnabledFlagSurvivesRoundTrip()
    {
        var cipher = NewCipher();
        var stored = new SmsSettings { Enabled = false, AuthKey = cipher.Encrypt("key") };
        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = SmsSettings.FromJson(json, cipher);

        Assert.False(loaded.Enabled);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Key-mismatch / corrupted-ciphertext → safe defaults (DEF-39-A)
    // Simulates the cross-service key-mismatch scenario in Development where
    // Identity encrypts with key-A and Worker (or Commerce) decrypts with key-B.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WhatsAppSettings_WrongDecryptKey_ReturnsDisabledDefaults()
    {
        // Encrypt with key-A, attempt to decrypt with key-B.
        var encryptCipher = NewCipher();
        var decryptCipher = NewCipher(); // different key

        var stored = new WhatsAppSettings
        {
            Enabled       = true,
            PhoneNumberId = "112233445566",
            AccessToken   = encryptCipher.Encrypt("EAABsToken"),
        };
        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Must return safe defaults, not throw CryptographicException.
        var loaded = WhatsAppSettings.FromJson(json, decryptCipher);

        Assert.False(loaded.Enabled);
        Assert.Null(loaded.AccessToken);
        Assert.Null(loaded.PhoneNumberId);
    }

    [Fact]
    public void SmsSettings_WrongDecryptKey_ReturnsDisabledDefaults()
    {
        var encryptCipher = NewCipher();
        var decryptCipher = NewCipher();

        var stored = new SmsSettings
        {
            Enabled   = true,
            AuthKey   = encryptCipher.Encrypt("MSG91_KEY"),
            SenderId  = "LAUNDR",
        };
        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var loaded = SmsSettings.FromJson(json, decryptCipher);

        Assert.False(loaded.Enabled);
        Assert.Null(loaded.AuthKey);
        Assert.Equal("msg91", loaded.Provider); // default provider preserved
    }

    [Fact]
    public void PaymentGatewaySettings_WrongDecryptKey_ReturnsDisabledDefaults()
    {
        var encryptCipher = NewCipher();
        var decryptCipher = NewCipher();

        var stored = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = true,
            KeyId         = "rzp_live_key",
            KeySecret     = encryptCipher.Encrypt("secret"),
            WebhookSecret = encryptCipher.Encrypt("wh_secret"),
        };
        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Commerce's GatewaySettingsCache must fall back to env-config, not throw.
        var loaded = PaymentGatewaySettings.FromJson(json, decryptCipher);

        Assert.False(loaded.Enabled);
        Assert.Null(loaded.KeySecret);
        Assert.Null(loaded.WebhookSecret);
    }

    [Fact]
    public void WhatsAppSettings_TamperedCiphertext_ReturnsDisabledDefaults()
    {
        // Tamper the stored ciphertext: flip a byte inside the base64 payload.
        var cipher = NewCipher();
        var stored = new WhatsAppSettings
        {
            Enabled     = true,
            AccessToken = cipher.Encrypt("EAABsToken"),
        };

        // Tamper: replace the enc:v1: payload with garbage base64 of the same length.
        stored.AccessToken = "enc:v1:" + Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(50));

        var json   = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var loaded = WhatsAppSettings.FromJson(json, cipher);

        Assert.False(loaded.Enabled);
        Assert.Null(loaded.AccessToken);
    }
}
