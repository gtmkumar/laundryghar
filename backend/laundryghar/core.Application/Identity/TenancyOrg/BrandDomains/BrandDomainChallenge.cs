using System.Security.Cryptography;

namespace core.Application.Identity.TenancyOrg.BrandDomains;

/// <summary>
/// The domain-ownership challenge: what value a provider must publish, and where.
/// PLATFORM_STRATEGY.md §4.2 item 2 — "provider adds a CNAME → our edge; verifies via TXT record".
/// </summary>
public static class BrandDomainChallenge
{
    /// <summary>
    /// The label the TXT record is published under, so verification lives at
    /// <c>_lg-verify.theirbrand.com</c> rather than the apex.
    ///
    /// <para>A dedicated label matters: apex TXT records are shared real estate (SPF, DMARC,
    /// Google/Microsoft site verification all live there). Publishing our challenge at the apex would
    /// mean reading — and asking the provider to edit — a record other systems depend on, and some
    /// DNS hosts merge or reorder apex TXT strings. A private label is ours alone.</para>
    /// </summary>
    public const string Label = "_lg-verify";

    /// <summary>Prefix on the TXT value so a human reading their zone can tell what it is, and so a
    /// zone carrying several verification tokens is unambiguous.</summary>
    public const string ValuePrefix = "lg-verify=";

    /// <summary>The fully-qualified name the provider creates the TXT record at.</summary>
    public static string NameFor(string domain) => $"{Label}.{Normalize(domain)}";

    /// <summary>A fresh challenge value. 256 bits of CSPRNG entropy, hex-encoded: this is the only
    /// thing standing between a domain and whoever wants to claim it, so it must not be guessable.
    /// <see cref="RandomNumberGenerator"/>, never <c>Random</c>.</summary>
    public static string NewValue()
        => ValuePrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>
    /// Canonical form of a hostname: trimmed, lowercased, without the DNS root dot, and without a
    /// scheme or path if someone pasted a URL. Applied on the way IN so the stored value is
    /// predictable, and on every comparison.
    /// </summary>
    public static string Normalize(string domain)
    {
        var d = domain.Trim().ToLowerInvariant();

        // Tolerate a pasted URL — "https://theirbrand.com/" is what people actually paste.
        if (d.StartsWith("https://", StringComparison.Ordinal)) d = d[8..];
        else if (d.StartsWith("http://", StringComparison.Ordinal)) d = d[7..];

        var slash = d.IndexOf('/');
        if (slash >= 0) d = d[..slash];

        // Strip a port if one came along with the paste.
        var colon = d.IndexOf(':');
        if (colon >= 0) d = d[..colon];

        return d.TrimEnd('.');
    }

    /// <summary>
    /// Is this a plausible public hostname we can verify? Deliberately strict — a domain row that
    /// resolves traffic must not be creatable from junk, and single-label names ("localhost") or
    /// bare IPs are never valid custom domains.
    /// </summary>
    public static bool IsPlausibleDomain(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (normalized.Length > 253) return false;
        if (System.Net.IPAddress.TryParse(normalized, out _)) return false;

        var labels = normalized.Split('.');
        if (labels.Length < 2) return false; // must have a TLD

        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63) return false;
            if (label[0] == '-' || label[^1] == '-') return false;
            foreach (var c in label)
                if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
        }

        return true;
    }
}
