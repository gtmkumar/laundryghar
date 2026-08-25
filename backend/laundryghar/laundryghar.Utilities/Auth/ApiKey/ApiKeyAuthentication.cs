using System.Security.Claims;
using System.Text.Encodings.Web;
using laundryghar.SharedDataModel.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace laundryghar.Utilities.Auth.ApiKey;

/// <summary>Shape and claim names of a machine credential (§11 P4, migration 0016).</summary>
public static class ApiKeyClaims
{
    /// <summary>Authentication scheme name.</summary>
    public const string Scheme = "ApiKey";

    /// <summary><c>token_use</c> value. Distinct from user/customer/partner so an API key can never
    /// satisfy a policy written for a signed-in human, and vice versa.</summary>
    public const string TokenUse = "api_key";

    public const string KeyIdClaim = "api_key_id";
    public const string ScopeClaim = "api_scope";
    public const string EnvironmentClaim = "api_env";
    public const string RateLimitClaim = "api_rate_limit";

    /// <summary>
    /// Cache key holding a resolved key's per-minute ceiling, so the rate limiter — which runs
    /// BEFORE authentication — can find it without a database call or a claim.
    ///
    /// <para>Caching a LIMIT is safe in a way that caching an authentication decision is not: the
    /// worst case of a stale entry is a customer getting yesterday's ceiling for a few minutes.
    /// Revocation deliberately does not go through here.</para>
    /// </summary>
    public static string RateLimitCacheKey(string prefix) => $"apikeylimit:{prefix}";

    /// <summary>How long a cached ceiling lives.</summary>
    public static readonly TimeSpan RateLimitCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Header for the credential, in addition to <c>Authorization: Bearer</c>.</summary>
    public const string HeaderName = "X-API-Key";

    /// <summary>Vendor tag every key starts with, so leak scanners can recognise ours.</summary>
    public const string Vendor = "lg";

    /// <summary>Characters in the public handle. Fixed-length hex, which is what makes the
    /// underscore-delimited format unambiguous — see <see cref="TryParse"/>.</summary>
    public const int HandleLength = 16;

    /// <summary>
    /// Splits <c>lg_live_&lt;handle&gt;_&lt;secret&gt;</c> into its public handle and its secret.
    /// Returns false for anything that is not exactly that shape — a partial match must never fall
    /// through to a lookup, because a lookup on a malformed key is a lookup an attacker controls.
    ///
    /// <para><b>Why the split is bounded at four and the handle is validated as hex.</b> The secret
    /// is base64url, whose alphabet INCLUDES the underscore — so a plain <c>Split('_')</c> returns
    /// five or six parts for a perfectly valid key and rejects it. Found by issuing a real key and
    /// watching it 401. Bounding the split keeps the secret intact, and pinning the handle to exactly
    /// 16 hex characters is what stops that leniency from also accepting <c>lg_live_a1_b2_secret</c>
    /// as handle <c>a1</c>.</para>
    /// </summary>
    public static bool TryParse(string? candidate, out string prefix, out string secret)
    {
        prefix = secret = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        // At most four: everything after the third underscore is the secret, underscores and all.
        var parts = candidate.Split('_', 4);
        if (parts.Length != 4) return false;
        if (parts[0] != Vendor) return false;
        if (parts[1] is not ("live" or "test")) return false;
        if (parts[3].Length == 0) return false;

        var handle = parts[2];
        if (handle.Length != HandleLength) return false;
        foreach (var c in handle)
            if (!char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')) return false;

        // The prefix stored in the database is the whole public half — vendor, environment and
        // handle — so a `test` key can never resolve to a `live` key that shares a handle.
        prefix = $"{parts[0]}_{parts[1]}_{handle}";
        secret = parts[3];
        return true;
    }
}

public sealed class ApiKeyOptions : AuthenticationSchemeOptions;

/// <summary>Verifies a presented secret against a stored hash. An interface so the Utilities layer
/// does not take a dependency on the Argon2 implementation that lives in core.Infrastructure.</summary>
public interface IApiKeySecretVerifier
{
    bool Verify(string secret, string hash);
}

/// <summary>
/// Authenticates a machine caller presenting an API key (§11 P4).
///
/// <para>Every failure returns the SAME message. Telling a caller whether a key is unknown, revoked,
/// expired, or belongs to a brand that has stopped paying is telling an attacker which of their
/// guesses was closest — and none of those distinctions helps a legitimate integrator, who has the
/// key's status in their own console.</para>
///
/// <para>The one exception is entitlement: a brand whose <c>api_access</c> lapsed gets a distinct
/// answer, because that IS actionable — and it is only reachable by someone who has already proved
/// they hold a real key.</para>
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyOptions>
{
    /// <summary>The single failure message. See the class comment for why there is only one.</summary>
    public const string InvalidMessage = "Invalid API key.";

    /// <summary>Distinct because it is actionable and only reachable once the key itself verified.</summary>
    public const string NotEntitledMessage = "This account's plan does not include API access.";

    private readonly IApiKeyStore _store;
    private readonly IApiKeySecretVerifier _verifier;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache? _cache;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        IApiKeyStore store, IApiKeySecretVerifier verifier,
        Microsoft.Extensions.Caching.Memory.IMemoryCache? cache = null)
        : base(options, logger, encoder)
    {
        _store = store;
        _verifier = verifier;
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = Request.Headers[ApiKeyClaims.HeaderName].ToString();
        if (string.IsNullOrEmpty(raw))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                raw = auth["Bearer ".Length..].Trim();
        }

        // No credential at all is NoResult, not Fail: other schemes may still authenticate this
        // request, and a bearer JWT arrives on the very same header.
        if (string.IsNullOrEmpty(raw)) return AuthenticateResult.NoResult();
        if (!ApiKeyClaims.TryParse(raw, out var prefix, out var secret)) return AuthenticateResult.NoResult();

        ApiKeyRecord? key;
        try
        {
            key = await _store.ResolveAsync(prefix, Context.RequestAborted);
        }
        catch
        {
            // Fail closed: a key we cannot resolve is not an authenticated one.
            return AuthenticateResult.Fail(InvalidMessage);
        }

        if (key is null) return AuthenticateResult.Fail(InvalidMessage);
        if (key.Status != "active") return AuthenticateResult.Fail(InvalidMessage);
        if (key.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            return AuthenticateResult.Fail(InvalidMessage);

        // Verified BEFORE the entitlement check, so the distinct "not entitled" answer is only ever
        // reachable by someone holding a real key.
        if (!_verifier.Verify(secret, key.SecretHash)) return AuthenticateResult.Fail(InvalidMessage);

        if (!key.Entitled) return AuthenticateResult.Fail(NotEntitledMessage);

        // Publish this key's ceiling for the rate limiter. It partitions on the PREFIX read straight
        // from the header, because it runs before authentication — the ApiKey scheme is not the
        // default one, so it is only invoked during authorization, and until then HttpContext.User
        // is empty. Partitioning pre-auth is also strictly better: a guessing loop is throttled
        // before it can cost anything, rather than after.
        // Stored unwrapped so the limiter's `is int` test matches the boxed value; a key with no
        // explicit ceiling caches nothing and falls back to the host default.
        if (_cache is not null && key.RateLimitPerMinute is { } perKeyLimit)
            _cache.Set(ApiKeyClaims.RateLimitCacheKey(prefix), perKeyLimit, ApiKeyClaims.RateLimitCacheTtl);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new("token_use", ApiKeyClaims.TokenUse),
            new("brand_id", key.BrandId.ToString()),
            new(ApiKeyClaims.KeyIdClaim, key.Id.ToString()),
            new(ApiKeyClaims.EnvironmentClaim, key.Environment),
        };
        foreach (var scope in key.Scopes) claims.Add(new Claim(ApiKeyClaims.ScopeClaim, scope));
        if (key.RateLimitPerMinute is { } limit)
            claims.Add(new Claim(ApiKeyClaims.RateLimitClaim, limit.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyClaims.Scheme));

        // AWAITED, not fire-and-forget. The store resolves a scoped DbContext, and the scope is torn
        // down with the request — a detached task raced disposal and lost most of its writes, so a
        // key that served nine requests reported two. The write is one indexed upsert; correctness
        // is worth more than the microseconds.
        //
        // Metered on successful AUTHENTICATION, which counts requests the customer's key actually
        // served or was refused on scope — but NOT ones the rate limiter rejected, because that
        // middleware runs before this handler and short-circuits. That is the honest reading of the
        // number: a 429 is traffic we declined to serve, not usage.
        await _store.RecordUseAsync(key.Id, key.BrandId, isError: false, Context.RequestAborted);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyClaims.Scheme));
    }
}
