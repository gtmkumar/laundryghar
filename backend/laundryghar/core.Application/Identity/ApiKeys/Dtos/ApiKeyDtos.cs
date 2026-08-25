namespace core.Application.Identity.ApiKeys.Dtos;

/// <param name="Scopes">Least privilege: omitting this issues a key that can authenticate and do
/// nothing, which is the safe default when someone is clicking through a form.</param>
/// <param name="Environment">live | test. A test key that reaches production fails at the prefix.</param>
public sealed record CreateApiKeyRequest(
    string Name,
    IReadOnlyList<string>? Scopes,
    string? Environment,
    int? RateLimitPerMinute,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// The one and only time the secret is returned. It is not stored in a recoverable form, so it
/// cannot be shown again — losing it means issuing a new key, which is the correct trade: the
/// alternative is a database where every provider's credentials are readable.
/// </summary>
public sealed record CreatedApiKeyDto(Guid Id, string Name, string ApiKey, string KeyPrefix,
    IReadOnlyList<string> Scopes, string Environment, DateTimeOffset? ExpiresAt);

/// <summary>What the console lists. No secret, ever.</summary>
public sealed record ApiKeyDto(
    Guid Id, string Name, string KeyPrefix, string Environment, IReadOnlyList<string> Scopes,
    string Status, int? RateLimitPerMinute, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt, DateTimeOffset CreatedAt,
    long RequestsLast30Days, long ErrorsLast30Days);
