namespace laundryghar.Identity.Infrastructure.Auth;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;

    /// <summary>HS256 signing key (dev only; swap for RS256 cert in production).</summary>
    public string SigningKey { get; set; } = null!;

    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 30;
}
