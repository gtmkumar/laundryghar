namespace laundryghar.SharedDataModel.Contracts;

/// <summary>A resolved API key, straight from the database. <paramref name="Entitled"/> is whether
/// the owning brand currently licenses `api_access` (§5).</summary>
public sealed record ApiKeyRecord(
    Guid Id, Guid BrandId, string SecretHash, string[] Scopes, string Status,
    string Environment, int? RateLimitPerMinute, DateTimeOffset? ExpiresAt, bool Entitled);

/// <summary>
/// Resolves and meters API keys (migration 0016).
///
/// <para>Uncached, like <see cref="IImpersonationStateStore"/> and for the same reason: revoking a
/// leaked credential must take effect on the next request, not at the end of a TTL. "We revoked it,
/// it stops working within a minute" is not an answer anyone wants to give after a key ends up in a
/// public repository.</para>
/// </summary>
public interface IApiKeyStore
{
    /// <summary>Looks a key up by its PUBLIC prefix. Returns the stored hash for the caller to
    /// verify — it never compares secrets itself, so it cannot be used as a guessing oracle.</summary>
    Task<ApiKeyRecord?> ResolveAsync(string prefix, CancellationToken ct = default);

    /// <summary>Increments today's usage rollup. Fire-and-forget: metering must never fail a
    /// request the caller was entitled to make.</summary>
    Task RecordUseAsync(Guid keyId, Guid brandId, bool isError, CancellationToken ct = default);
}
