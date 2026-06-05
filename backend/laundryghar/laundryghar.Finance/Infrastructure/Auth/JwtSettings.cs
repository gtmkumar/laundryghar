namespace laundryghar.Finance.Infrastructure.Auth;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer     { get; init; } = string.Empty;
    public string Audience   { get; init; } = string.Empty;
    public string SigningKey  { get; init; } = string.Empty;
    public int    AccessMinutes { get; init; } = 15;
}
