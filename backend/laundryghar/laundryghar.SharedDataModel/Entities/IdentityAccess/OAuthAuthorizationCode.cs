namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// Single-use PKCE authorization code (RFC 7636).
/// The raw code is never stored — only its SHA-256 hash (hex).
/// Table: identity_access.oauth_authorization_codes.
/// </summary>
public class OAuthAuthorizationCode
{
    public Guid Id { get; set; }
    /// <summary>SHA-256(raw_code), lowercase hex. The raw code travels to the client only.</summary>
    public string CodeHash { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    /// <summary>Exact redirect_uri presented at /oauth/authorize (used for token exchange validation).</summary>
    public string RedirectUri { get; set; } = null!;
    /// <summary>BASE64URL(SHA-256(code_verifier)) — S256 only. Must equal client's verifier proof at /oauth/token.</summary>
    public string CodeChallenge { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Guid BrandId { get; set; }
    public string Scope { get; set; } = "mcp:booking";
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>NULL = unused. Non-NULL = already consumed (redeemed or replay attempt).</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public OAuthClient? Client { get; set; }
}
