using System.Security.Cryptography;
using System.Text.Json;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// SMS gateway configuration (MSG91), persisted as JSON in
/// kernel.system_settings (category 'sms', key 'provider').
/// Shared between Identity (reads/writes) and Worker (resolves at runtime).
///
/// AuthKey is SECRET — stored as AES-256-GCM ciphertext.
/// </summary>
public sealed class SmsSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string  Provider       { get; set; } = "msg91";
    public bool    Enabled        { get; set; }
    /// <summary>AES-GCM ciphertext (enc:v1: prefix) or null.</summary>
    public string? AuthKey        { get; set; }
    public string? SenderId       { get; set; }
    public string? DltTemplateId  { get; set; }

    /// <summary>
    /// Deserializes the stored JSON and decrypts secret fields.
    /// Returns a safe default (Enabled=false, null secrets) when the JSON is
    /// null/malformed OR when the cipher cannot decrypt a stored secret (e.g.
    /// a cross-service key mismatch in Development). Callers degrade gracefully
    /// to the logging fallback rather than throwing.
    /// </summary>
    public static SmsSettings FromJson(string? json, IFieldCipher cipher)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SmsSettings();
        try
        {
            var s = JsonSerializer.Deserialize<SmsSettings>(json, Json)
                    ?? new SmsSettings();
            s.AuthKey = cipher.Decrypt(s.AuthKey);
            return s;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
        {
            // CryptographicException: auth-tag mismatch (key mismatch between services, or
            //   corrupted ciphertext). FormatException: malformed base64 inside the enc:v1 payload.
            // Both are treated identically to a malformed JSON row — return disabled defaults
            // so every consumer degrades gracefully rather than throwing into the outbox loop.
            return new SmsSettings();
        }
    }
}
