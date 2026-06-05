namespace laundryghar.Analytics.Infrastructure.Auth;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer     { get; set; } = null!;
    public string Audience   { get; set; } = null!;
    public string SigningKey  { get; set; } = null!;
    public int AccessMinutes { get; set; } = 15;
}
