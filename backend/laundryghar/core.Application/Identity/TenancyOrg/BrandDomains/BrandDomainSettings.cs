namespace core.Application.Identity.TenancyOrg.BrandDomains;

/// <summary>
/// Per-environment configuration for custom domains (bound from the <c>BrandDomains</c> section).
/// </summary>
public sealed class BrandDomainSettings
{
    public const string SectionName = "BrandDomains";

    /// <summary>
    /// The hostname a provider points their domain's CNAME at — our edge. Environment-specific
    /// (staging and production terminate on different edges), so it is configuration, never a
    /// constant. Surfaced in the DTO purely so the console can render the exact DNS instruction.
    /// </summary>
    public string CnameTarget { get; set; } = "edge.laundryghar.com";
}
