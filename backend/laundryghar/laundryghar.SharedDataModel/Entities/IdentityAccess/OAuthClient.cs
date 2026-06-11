namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// Dynamically-registered OAuth 2.1 public client (RFC 7591).
/// Public clients only — no client_secret stored or issued.
/// Table: identity_access.oauth_clients.
/// </summary>
public class OAuthClient
{
    public Guid Id { get; set; }
    /// <summary>Opaque public client identifier (URL-safe random string).</summary>
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    /// <summary>
    /// Allowed redirect URIs. Stored as a PostgreSQL text[].
    /// Validation rules (enforced on registration and at /authorize):
    ///   https:// — exact-match required.
    ///   http://localhost or http://127.0.0.1 — scheme + host match only (port-agnostic loopback).
    ///   All other http:// URIs — rejected.
    /// </summary>
    public string[] RedirectUris { get; set; } = [];
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Set to now() on each successful /oauth/token code exchange.
    /// NULL means the client has never completed a token exchange — used by
    /// OAuthCleanupService to purge abandoned registrations older than 7 days.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
