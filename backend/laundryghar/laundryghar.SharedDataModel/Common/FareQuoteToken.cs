using System.Text.Json;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// The signed contents of a fare quote. Issued by the /fare/quote endpoint and replayed
/// at order creation so the quoted price is a binding contract that cannot be tampered
/// with or used past its TTL.
/// </summary>
public sealed record FareQuotePayload(
    Guid PickupAddressId,
    Guid DeliveryAddressId,
    string? Tier,
    decimal DistanceKm,
    decimal PickupCharge,
    decimal DeliveryCharge,
    decimal SurgeMultiplier,
    long ExpiresAtUnix);

/// <summary>
/// Issues and verifies fare-quote tokens. The token is the AES-256-GCM ciphertext of the
/// JSON payload (via the shared <see cref="IFieldCipher"/>): the GCM auth tag makes it
/// unforgeable without the server key, so the ciphertext doubles as the signature. The
/// embedded expiry makes replay safe — no quote table or HMAC key management required.
/// </summary>
public static class FareQuoteToken
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Serialises and encrypts the payload into an opaque token string.</summary>
    public static string Issue(IFieldCipher cipher, FareQuotePayload payload)
        => cipher.Encrypt(JsonSerializer.Serialize(payload, Json))
           ?? throw new InvalidOperationException("Fare quote token encryption failed.");

    /// <summary>
    /// Decrypts and parses a token. Returns null when the token is missing, tampered
    /// (decryption/parse fails), or expired. The caller must additionally check that the
    /// payload's addresses + tier match the order request before trusting the charges.
    /// </summary>
    public static FareQuotePayload? Verify(IFieldCipher cipher, string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        string? json;
        try { json = cipher.Decrypt(token); }
        catch { return null; }
        if (string.IsNullOrEmpty(json)) return null;

        FareQuotePayload? payload;
        try { payload = JsonSerializer.Deserialize<FareQuotePayload>(json, Json); }
        catch (JsonException) { return null; }
        if (payload is null) return null;

        if (DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix) < now) return null;
        return payload;
    }
}
