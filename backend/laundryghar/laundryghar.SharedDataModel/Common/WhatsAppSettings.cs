using System.Security.Cryptography;
using System.Text.Json;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// WhatsApp Cloud API configuration, persisted as JSON in
/// kernel.system_settings (category 'whatsapp', key 'cloud').
/// Shared between Identity (reads/writes) and Worker (resolves at runtime).
///
/// AccessToken is SECRET — stored as AES-256-GCM ciphertext.
/// </summary>
public sealed class WhatsAppSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool    Enabled       { get; set; }
    public string? PhoneNumberId { get; set; }
    /// <summary>AES-GCM ciphertext (enc:v1: prefix) or null.</summary>
    public string? AccessToken   { get; set; }

    /// <summary>
    /// When true, Identity delivers login OTPs via a WhatsApp authentication
    /// template (falling back to SMS when delivery fails). Independent of
    /// <see cref="Enabled"/>, which gates the Worker's notification fan-out —
    /// but OTP sending still requires Enabled + credentials.
    /// </summary>
    public bool    OtpEnabled       { get; set; }
    /// <summary>
    /// Name of the approved authentication-category template in Meta Business
    /// Manager (e.g. "otp_login"). Required for OTP delivery.
    /// </summary>
    public string? OtpTemplateName  { get; set; }

    /// <summary>
    /// Deserializes the stored JSON and decrypts secret fields.
    /// Returns a safe default (Enabled=false, null secrets) when the JSON is
    /// null/malformed OR when the cipher cannot decrypt a stored secret (e.g.
    /// a cross-service key mismatch in Development). Callers degrade gracefully
    /// to the logging fallback rather than throwing.
    /// </summary>
    public static WhatsAppSettings FromJson(string? json, IFieldCipher cipher)
    {
        if (string.IsNullOrWhiteSpace(json)) return new WhatsAppSettings();
        try
        {
            var s = JsonSerializer.Deserialize<WhatsAppSettings>(json, Json)
                    ?? new WhatsAppSettings();
            s.AccessToken = cipher.Decrypt(s.AccessToken);
            return s;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
        {
            // CryptographicException: auth-tag mismatch (key mismatch between services, or
            //   corrupted ciphertext). FormatException: malformed base64 inside the enc:v1 payload.
            // Both are treated identically to a malformed JSON row — return disabled defaults
            // so every consumer degrades gracefully rather than throwing into the outbox loop.
            return new WhatsAppSettings();
        }
    }
}
