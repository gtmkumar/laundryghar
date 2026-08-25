namespace core.Application.Identity.Impersonation.Dtos;

/// <summary>Support asks a provider for a session. Scope defaults to read-only if omitted.</summary>
public sealed record RequestImpersonationRequest(Guid BrandId, string Reason, string? Scope);

/// <summary>
/// The provider's answer. <paramref name="Approve"/> false records a denial — kept rather than
/// deleted, because "we asked and they said no" is exactly the kind of thing an audit needs.
/// </summary>
/// <param name="Scope">The owner may grant LESS than was asked for (read-only when write was
/// requested) but never more — enforced in the handler.</param>
/// <param name="TtlMinutes">How long the session lives. Capped at 24h by a CHECK constraint.</param>
public sealed record DecideImpersonationRequest(bool Approve, string? Scope, int? TtlMinutes, string? Reason);

public sealed record RevokeImpersonationRequest(string? Reason);

/// <summary>What support may see about a grant WITHOUT holding any tenant access: status, scope,
/// expiry. Deliberately carries no brand data — the whole point is that it is readable before
/// consent exists.</summary>
public sealed record ImpersonationStateDto(
    Guid GrantId, string Status, string Scope, DateTimeOffset? ExpiresAt);

/// <summary>A row in the provider's console: who asked, why, what happened.</summary>
public sealed record ImpersonationGrantDto(
    Guid Id,
    Guid BrandId,
    Guid SupportUserId,
    string? SupportUserDisplay,
    string Reason,
    string Scope,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokeReason);

/// <summary>The minted session. <paramref name="ExpiresIn"/> is the SESSION's remaining seconds,
/// not the token's — see StartImpersonationSessionHandler for why they differ.</summary>
public sealed record ImpersonationSessionDto(
    string AccessToken, int ExpiresIn, Guid GrantId, Guid BrandId, string Scope);
