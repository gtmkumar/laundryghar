namespace laundryghar.SharedDataModel.Contracts;

/// <summary>What one observation of a custom domain found.</summary>
/// <param name="Health">unknown | healthy | degraded | unreachable.</param>
/// <param name="SslExpiresAt">Null when no certificate could be read at all.</param>
public sealed record DomainHealthResult(
    string Health, string? Detail, DateTimeOffset? SslExpiresAt, string? Issuer);

public static class DomainHealth
{
    public const string Unknown = "unknown";
    public const string Healthy = "healthy";

    /// <summary>Resolves and serves, but the certificate is expiring, expired, or for the wrong name.</summary>
    public const string Degraded = "degraded";

    /// <summary>Does not resolve, or refuses the connection.</summary>
    public const string Unreachable = "unreachable";
}

/// <summary>
/// Observes whether one custom domain is actually working (§12: automated SSL and domain health
/// checks "from day one").
///
/// <para>Deliberately an OBSERVER, not an issuer. Certificate issuance is blocked on OQ-6 — Let's
/// Encrypt versus Cloudflare-for-SaaS is an infrastructure and cost decision, and choosing a vendor
/// on someone's behalf is choosing their bill. Checking works identically whichever wins: a cert
/// from any issuer, or one uploaded by hand, leaves the same observable expiry.</para>
/// </summary>
public interface IDomainHealthChecker
{
    Task<DomainHealthResult> CheckAsync(string domain, CancellationToken ct = default);
}
