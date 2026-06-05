namespace laundryghar.Engagement.Infrastructure.Auth;

/// <summary>JWT validation settings sourced from appsettings Jwt section.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer     { get; set; } = string.Empty;
    public string Audience   { get; set; } = string.Empty;
    public string SigningKey  { get; set; } = string.Empty;
}
