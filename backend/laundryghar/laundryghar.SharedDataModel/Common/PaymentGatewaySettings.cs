using System.Security.Cryptography;
using System.Text.Json;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// Payment-gateway configuration, persisted as JSON in
/// kernel.system_settings (category 'payment', key 'gateway').
/// Shared between Identity (reads/writes via the Settings panel)
/// and Commerce (resolves gateway credentials at runtime).
///
/// SECRET fields (KeySecret, WebhookSecret) are stored as AES-256-GCM
/// ciphertext via IFieldCipher; GET responses return a masked view
/// (•••• + last 4 chars) plus a hasValue flag.
/// </summary>
public sealed class PaymentGatewaySettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string  Provider       { get; set; } = "razorpay";
    public bool    Enabled        { get; set; }
    public string? KeyId          { get; set; }
    /// <summary>AES-GCM ciphertext (enc:v1: prefix) or null.</summary>
    public string? KeySecret      { get; set; }
    /// <summary>AES-GCM ciphertext (enc:v1: prefix) or null.</summary>
    public string? WebhookSecret  { get; set; }
    public bool    CodEnabled     { get; set; }

    /// <summary>
    /// Deserializes the stored JSON and decrypts secret fields.
    /// Returns a safe default (Enabled=false, null secrets) when the JSON is
    /// null/malformed OR when the cipher cannot decrypt a stored secret (e.g.
    /// a cross-service key mismatch in Development). <see cref="Commerce.Infrastructure.Gateway.GatewaySettingsCache"/>
    /// callers fall back to env-config credentials rather than throwing.
    /// </summary>
    public static PaymentGatewaySettings FromJson(string? json, IFieldCipher cipher)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PaymentGatewaySettings();
        try
        {
            var s = JsonSerializer.Deserialize<PaymentGatewaySettings>(json, Json)
                    ?? new PaymentGatewaySettings();
            s.KeySecret     = cipher.Decrypt(s.KeySecret);
            s.WebhookSecret = cipher.Decrypt(s.WebhookSecret);
            return s;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
        {
            // CryptographicException: auth-tag mismatch (key mismatch between services, or
            //   corrupted ciphertext). FormatException: malformed base64 inside the enc:v1 payload.
            // Both return disabled defaults — SettingsFirstPaymentGateway then falls back to
            // env-config credentials (step 2 of its resolution order) rather than throwing.
            return new PaymentGatewaySettings();
        }
    }
}
