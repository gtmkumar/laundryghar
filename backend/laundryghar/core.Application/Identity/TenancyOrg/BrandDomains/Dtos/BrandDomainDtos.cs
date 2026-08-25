namespace core.Application.Identity.TenancyOrg.BrandDomains.Dtos;

/// <summary>One custom domain of a brand, as the admin console shows it.</summary>
public sealed record BrandDomainDto(
    Guid Id,
    Guid BrandId,
    string Domain,
    bool Verified,
    DateTimeOffset? VerifiedAt,
    string SslStatus,
    bool IsPrimary,
    /// <summary>The DNS name the provider must create the TXT record at, e.g.
    /// <c>_lg-verify.theirbrand.com</c>. Echoed on every read so the console can show the exact
    /// instruction again at any time, not just once at creation.</summary>
    string VerificationName,
    /// <summary>The exact TXT value to publish.</summary>
    string VerificationValue,
    /// <summary>The CNAME target the provider points the domain itself at, so the console can render
    /// the complete two-record instruction. Configured per environment.</summary>
    string CnameTarget,
    DateTimeOffset CreatedAt);

/// <summary>Register a custom domain for a brand. The challenge is generated server-side.</summary>
public sealed record AddBrandDomainRequest(string Domain, bool IsPrimary = false);

/// <summary>Outcome of a verification attempt.</summary>
/// <param name="Verified">True only when the expected TXT value was found.</param>
/// <param name="Status">
/// <c>verified</c> · <c>already_verified</c> · <c>record_not_found</c> (no TXT at the name) ·
/// <c>value_mismatch</c> (TXT records exist but none carries our challenge) ·
/// <c>lookup_failed</c> (DNS could not be reached — says nothing about the provider's setup).
/// </param>
/// <param name="Message">Human-readable detail for the console.</param>
/// <param name="FoundRecords">What was actually seen at the name, so a provider can spot a typo.</param>
public sealed record VerifyBrandDomainResultDto(
    bool Verified,
    string Status,
    string Message,
    IReadOnlyList<string> FoundRecords);

/// <summary>The verification status vocabulary — see <see cref="VerifyBrandDomainResultDto"/>.</summary>
public static class BrandDomainVerifyStatus
{
    public const string Verified        = "verified";
    public const string AlreadyVerified = "already_verified";
    public const string RecordNotFound  = "record_not_found";
    public const string ValueMismatch   = "value_mismatch";
    public const string LookupFailed    = "lookup_failed";
}
