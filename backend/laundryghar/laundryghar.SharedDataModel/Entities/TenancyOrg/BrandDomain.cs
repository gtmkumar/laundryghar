namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>
/// A custom hostname a brand serves under — white-label tier T2 (PLATFORM_STRATEGY.md §4.2).
/// The provider points a CNAME at our edge and proves ownership with a DNS TXT record; from then on
/// the request's <c>Host</c> header resolves through this table to <see cref="BrandId"/>, which is
/// set as <c>app.current_brand_id</c> so RLS scopes everything below it. One deployment serves N
/// branded domains — there is no per-provider server.
/// </summary>
public class BrandDomain
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>The hostname as it arrives in the Host header. Stored CITEXT: DNS is
    /// case-insensitive, and the column is globally unique so a host maps to exactly one brand.</summary>
    public string Domain { get; set; } = null!;

    /// <summary>The value the provider publishes as a DNS TXT record to prove ownership. Retained
    /// after verification so a re-check can run without re-issuing the challenge.</summary>
    public string VerificationTxt { get; set; } = null!;

    /// <summary><c>null</c> until ownership is proven. Resolution MUST treat null as "does not
    /// resolve" — otherwise claiming someone else's hostname would hijack their traffic.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>Certificate lifecycle, written by the SSL automation:
    /// <c>pending</c> · <c>active</c> · <c>failed</c> · <c>expired</c>.</summary>
    public string SslStatus { get; set; } = BrandDomainSslStatus.Pending;

    /// <summary>The canonical host for the brand: the redirect target, and the base used whenever the
    /// platform generates an absolute URL. At most one per brand (partial unique index).</summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>True when this row may be used to resolve a request to its brand.</summary>
    public bool IsResolvable => VerifiedAt is not null;
}

/// <summary>The <c>ssl_status</c> vocabulary, mirroring the DB CHECK constraint
/// (<c>brand_domains_ssl_status_check</c>). Lookup-by-CHECK rather than a PG enum, per ADR-005.</summary>
public static class BrandDomainSslStatus
{
    public const string Pending = "pending";
    public const string Active  = "active";
    public const string Failed  = "failed";
    public const string Expired = "expired";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pending, Active, Failed, Expired };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
