namespace laundryghar.Mcp.Infrastructure.Auth;

/// <summary>
/// JWT configuration for the MCP service.
/// Mirrors the same shape used by all other LaundryGhar services.
/// Authority points at the Identity issuer (RS256 JWKS endpoint).
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;

    /// <summary>Base URL of the Identity issuer — the JWKS is fetched from {Authority}/.well-known/openid-configuration.</summary>
    public string? Authority { get; set; }
}
