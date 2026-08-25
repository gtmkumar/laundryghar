using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Gateway;

/// <summary>
/// Chooses the rate-limit partition for a request — PLATFORM_STRATEGY.md §7 "per-provider rate
/// limits".
///
/// <para>The gateway sits in front of the services and does NOT validate tokens (each service does
/// that against the Identity JWKS). So the brand is READ from the token without verifying the
/// signature — which is safe here and would not be anywhere else: the only thing this value decides
/// is which bucket a request is counted in. Forging it cannot grant access, read data, or raise a
/// limit; the worst a forged brand_id achieves is being counted against someone else's budget, and
/// the service behind the gateway still rejects the token outright. Anything that mattered would be
/// validated.</para>
/// </summary>
public static class RateLimitPartitioning
{
    /// <summary>
    /// The partition key and its permit limit. Brand-keyed when the caller carries a brand, IP-keyed
    /// otherwise. Keys are prefixed so a brand id can never collide with an IP string.
    /// </summary>
    public static (string Key, int Limit) Resolve(HttpContext ctx, int ipPermitLimit, int brandPermitLimit)
    {
        var brandId = BrandIdOf(ctx);
        if (!string.IsNullOrEmpty(brandId))
            return ($"brand:{brandId}", brandPermitLimit);

        return ($"ip:{ClientIp(ctx)}", ipPermitLimit);
    }

    /// <summary>X-Brand-Id if present, else the unvalidated brand_id claim of the bearer token.</summary>
    private static string? BrandIdOf(HttpContext ctx)
    {
        var header = ctx.Request.Headers["X-Brand-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && Guid.TryParse(header, out var fromHeader))
            return fromHeader.ToString();

        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(auth) ||
            !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var raw = auth["Bearer ".Length..].Trim();
        if (raw.Length == 0) return null;

        // Decoded by hand rather than with a JWT library, for two reasons: the gateway has no JWT
        // dependency and does not need one to pick a bucket, and doing it manually keeps it obvious
        // at the call site that NOTHING here is validated — no signature check, no expiry check.
        try
        {
            var parts = raw.Split('.');
            if (parts.Length < 2) return null;

            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                Base64UrlDecode(parts[1]));

            if (payload is null || !payload.TryGetValue("brand_id", out var value)) return null;

            var claim = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return Guid.TryParse(claim, out var id) ? id.ToString() : null;
        }
        catch
        {
            // A malformed token is not this component's problem — the service behind the gateway
            // will reject it. Fall through to the IP partition so the request is still counted.
            return null;
        }
    }

    /// <summary>base64url (RFC 4648 §5) — JWT segments drop the padding and swap two characters.</summary>
    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// <summary>The real client IP, honouring X-Forwarded-For through the proxy layer.</summary>
    private static string ClientIp(HttpContext ctx)
    {
        var forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()      // leftmost = the real client
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
