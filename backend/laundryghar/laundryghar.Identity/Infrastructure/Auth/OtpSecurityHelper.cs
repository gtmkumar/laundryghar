using System.Security.Cryptography;
using System.Text;

namespace laundryghar.Identity.Infrastructure.Auth;

/// <summary>
/// Pure-static helpers for OTP code hashing (HMAC-SHA256 with per-row salt) and
/// rolling-window lockout evaluation. No DB access — all queries are done by the
/// calling handlers; this class only contains testable pure logic.
/// </summary>
public static class OtpSecurityHelper
{
    // ── Dev fallback key (well-known; only ever used when env == Development) ────
    // 32 ASCII chars → 32 bytes — deterministic so dev logs are reproducible.
    private static readonly byte[] DevFallbackKey =
        Encoding.ASCII.GetBytes("dev-otp-hmac-key-DO-NOT-USE-PROD");

    /// <summary>
    /// Derives the HMAC key from <paramref name="settings"/>.
    /// In Development an empty/null HmacKey silently uses <see cref="DevFallbackKey"/>.
    /// Outside Development a missing key throws <see cref="InvalidOperationException"/>
    /// so the service fails closed at first OTP operation rather than at startup (startup
    /// might not exercise this path in all environments).
    /// </summary>
    public static byte[] ResolveHmacKey(OtpSettings settings, bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(settings.HmacKey))
            return Convert.FromBase64String(settings.HmacKey);

        if (isDevelopment)
            return DevFallbackKey;

        throw new InvalidOperationException(
            "Otp:HmacKey is required outside Development. " +
            "Inject a 32-byte base64-encoded key via the Otp__HmacKey environment variable.");
    }

    /// <summary>
    /// Generates a cryptographically random 16-byte hex salt.
    /// </summary>
    public static string GenerateSalt()
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(saltBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes HMAC-SHA256(<paramref name="hmacKey"/>, <paramref name="salt"/> + <paramref name="code"/>)
    /// and returns the lowercase hex digest.
    /// </summary>
    public static string ComputeHmac(byte[] hmacKey, string salt, string code)
    {
        ArgumentNullException.ThrowIfNull(hmacKey);
        ArgumentException.ThrowIfNullOrEmpty(salt);
        ArgumentException.ThrowIfNullOrEmpty(code);

        var message = Encoding.UTF8.GetBytes(salt + code);
        var digest  = HMACSHA256.HashData(hmacKey, message);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Legacy unsalted SHA-256 hash (matches hashes stored before the salt migration).
    /// Used only as a fallback in <see cref="VerifyCode"/> when <paramref name="storedSalt"/>
    /// is null — i.e. for rows written before the patch.
    /// </summary>
    public static string ComputeLegacySha256(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies <paramref name="candidateCode"/> against the stored hash.
    /// If <paramref name="storedSalt"/> is non-null the salted HMAC path is used;
    /// otherwise falls back to legacy SHA-256 for rows that predate the migration.
    /// </summary>
    public static bool VerifyCode(
        byte[] hmacKey,
        string? storedSalt,
        string storedHash,
        string candidateCode)
    {
        var computed = storedSalt is not null
            ? ComputeHmac(hmacKey, storedSalt, candidateCode)
            : ComputeLegacySha256(candidateCode);

        // CryptographicOperations.FixedTimeEquals prevents timing-oracle attacks.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(storedHash));
    }

    /// <summary>
    /// Sums the <c>Attempts</c> values from <paramref name="windowRows"/> to get the
    /// total failed guess count within the rolling window for an identifier.
    /// </summary>
    /// <param name="windowRows">
    /// Sequence of <c>(Attempts, CreatedAt)</c> tuples for all otp_codes rows whose
    /// <c>created_at</c> falls within the lockout window for the identifier.
    /// Callers are responsible for pre-filtering by identifier and window; this method
    /// only aggregates.
    /// </param>
    public static int SumWindowAttempts(IEnumerable<short> attemptsValues)
        => attemptsValues.Sum(a => (int)a);

    /// <summary>
    /// Returns true when <paramref name="totalAttempts"/> meets or exceeds
    /// <paramref name="threshold"/>, indicating the identifier is locked out.
    /// </summary>
    public static bool ExceedsLockoutThreshold(int totalAttempts, int threshold)
        => totalAttempts >= threshold;
}
