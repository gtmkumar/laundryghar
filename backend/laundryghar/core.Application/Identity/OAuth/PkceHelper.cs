using System.Security.Cryptography;
using System.Text;

namespace core.Application.Identity.OAuth;

/// <summary>
/// Pure-static helpers for OAuth 2.1 PKCE (RFC 7636) and redirect-URI validation.
/// No dependencies — fully unit-testable without DI.
/// </summary>
public static class PkceHelper
{
    // ── Code generation ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random opaque authorization code (≥ 256 bits / 32 bytes).
    /// Returns a URL-safe Base64 string (no padding) — safe to embed in query strings.
    /// </summary>
    public static string GenerateRawCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 bits
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Computes SHA-256 of <paramref name="rawCode"/> and returns the lowercase hex digest.
    /// Stored in DB; the raw code is sent to the client only once and never persisted.
    /// </summary>
    public static string HashCode(string rawCode)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawCode));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── PKCE S256 verification ───────────────────────────────────────────────

    /// <summary>
    /// Verifies that <paramref name="codeVerifier"/> produces the stored
    /// <paramref name="storedChallenge"/> (BASE64URL(SHA-256(verifier))).
    /// Returns true only on an exact, timing-safe match.
    /// Rejects if either argument is null/empty.
    /// </summary>
    public static bool VerifyCodeVerifier(string codeVerifier, string storedChallenge)
    {
        if (string.IsNullOrWhiteSpace(codeVerifier) || string.IsNullOrWhiteSpace(storedChallenge))
            return false;

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computed = Base64UrlEncode(hash);

        // Timing-safe comparison (both strings are Base64URL — same character set).
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(storedChallenge));
    }

    // ── Redirect-URI validation ──────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="uri"/> is an acceptable OAuth redirect URI.
    /// Rules (OAuth 2.1 §9.7 + localhost BCP RFC 8252):
    ///   1. https:// any host — exact-match only (stored URI = requested URI).
    ///   2. http://localhost — port-agnostic (scheme+host match; port ignored).
    ///   3. http://127.0.0.1 — port-agnostic loopback.
    ///   4. All other http:// — rejected.
    /// </summary>
    public static bool IsValidRedirectUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme == Uri.UriSchemeHttps)
            return true;

        if (parsed.Scheme == Uri.UriSchemeHttp)
        {
            var host = parsed.Host.ToLowerInvariant();
            return host == "localhost" || host == "127.0.0.1";
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="requestedUri"/> matches the
    /// <paramref name="storedUri"/> according to OAuth 2.1 redirect-URI comparison:
    ///   - https:// URIs: full string equality (RFC 6749 §3.1.2.3 exact match).
    ///   - http://localhost and http://127.0.0.1: scheme + host match, port-agnostic
    ///     (RFC 8252 §8.3 loopback interface redirection).
    /// </summary>
    public static bool RedirectUriMatches(string storedUri, string requestedUri)
    {
        if (!Uri.TryCreate(storedUri, UriKind.Absolute, out var stored)
         || !Uri.TryCreate(requestedUri, UriKind.Absolute, out var requested))
            return false;

        if (stored.Scheme == Uri.UriSchemeHttps)
        {
            // Exact string match (normalised by Uri.ToString() strips trailing slash differences
            // but keeps path/query exactly). Use the original strings for strictness.
            return string.Equals(storedUri, requestedUri, StringComparison.Ordinal);
        }

        if (stored.Scheme == Uri.UriSchemeHttp)
        {
            var storedHost = stored.Host.ToLowerInvariant();
            var requestedHost = requested.Host.ToLowerInvariant();

            bool isLoopback = storedHost is "localhost" or "127.0.0.1";
            if (!isLoopback) return false;

            // Loopback: scheme + host match, port-agnostic, path must also match
            return requested.Scheme == Uri.UriSchemeHttp
                && requestedHost == storedHost
                && stored.AbsolutePath == requested.AbsolutePath;
        }

        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random URL-safe client_id (16 bytes → 22 chars).
    /// </summary>
    public static string GenerateClientId()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
