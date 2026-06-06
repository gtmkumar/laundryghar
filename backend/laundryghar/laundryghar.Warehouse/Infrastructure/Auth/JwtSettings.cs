namespace laundryghar.Warehouse.Infrastructure.Auth;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;

    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 30;

    // ── RS256 / JWKS ──────────────────────────────────────────────────────────
    /// <summary>Verifying services: base URL of the Identity issuer whose JWKS endpoint
    /// publishes the RS256 public key(s).</summary>
    public string? Authority { get; set; }

    public string? PrivateKey { get; set; }
    public string? PrivateKeyPath { get; set; }
}
