using core.Application.Identity.TenancyOrg.BrandDomains;
using Xunit;

namespace core.Tests.TenancyOrg;

/// <summary>
/// The pure half of domain verification: how a hostname is canonicalised, what counts as a domain we
/// are willing to serve, and the shape of the ownership challenge (PLATFORM_STRATEGY.md §4.2 item 2).
/// </summary>
public class BrandDomainChallengeTests
{
    [Theory]
    // Casing and surrounding whitespace.
    [InlineData("  TheirBrand.Example  ", "theirbrand.example")]
    // The DNS root dot: "example.com." and "example.com" are the same host.
    [InlineData("theirbrand.example.", "theirbrand.example")]
    // People paste URLs, not hostnames.
    [InlineData("https://theirbrand.example/", "theirbrand.example")]
    [InlineData("http://theirbrand.example/shop?a=1", "theirbrand.example")]
    [InlineData("https://theirbrand.example:8443/", "theirbrand.example")]
    [InlineData("theirbrand.example:443", "theirbrand.example")]
    public void normalize_canonicalises_what_people_actually_type(string input, string expected)
        => Assert.Equal(expected, BrandDomainChallenge.Normalize(input));

    [Theory]
    [InlineData("theirbrand.example")]
    [InlineData("shop.theirbrand.co.in")]
    [InlineData("a-b.example")]
    [InlineData("xn--80ak6aa92e.com")] // punycode IDN
    public void plausible_domains_are_accepted(string domain)
        => Assert.True(BrandDomainChallenge.IsPlausibleDomain(domain));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]        // single label — never a public custom domain
    [InlineData("example")]          // no TLD
    [InlineData("127.0.0.1")]        // bare IPv4
    [InlineData("::1")]              // bare IPv6
    [InlineData("-lead.example")]    // label may not start with a hyphen
    [InlineData("trail-.example")]   // …nor end with one
    [InlineData("a..example")]       // empty label
    [InlineData("under_score.example")]
    [InlineData("spaces here.example")]
    public void implausible_domains_are_rejected(string domain)
        => Assert.False(BrandDomainChallenge.IsPlausibleDomain(BrandDomainChallenge.Normalize(domain)));

    [Fact]
    public void oversized_names_are_rejected()
    {
        var tooLongLabel = new string('a', 64) + ".example";                 // label > 63
        var tooLongName = string.Join('.', Enumerable.Repeat("abcdefghij", 26)) + ".example"; // > 253

        Assert.False(BrandDomainChallenge.IsPlausibleDomain(tooLongLabel));
        Assert.False(BrandDomainChallenge.IsPlausibleDomain(tooLongName));
    }

    [Fact]
    public void challenge_is_published_under_a_private_label_not_the_apex()
    {
        // Apex TXT is shared with SPF/DMARC/other vendors' verification; ours gets its own label.
        Assert.Equal("_lg-verify.theirbrand.example",
            BrandDomainChallenge.NameFor("TheirBrand.Example."));
    }

    [Fact]
    public void challenge_values_are_prefixed_and_unguessable()
    {
        var a = BrandDomainChallenge.NewValue();
        var b = BrandDomainChallenge.NewValue();

        Assert.StartsWith("lg-verify=", a);
        Assert.NotEqual(a, b);

        // 32 CSPRNG bytes → 64 hex chars. This token is the only thing standing between a domain
        // and whoever wants to claim it, so its entropy is a security property, not a detail.
        var token = a["lg-verify=".Length..];
        Assert.Equal(64, token.Length);
        Assert.True(token.All(Uri.IsHexDigit));
        Assert.Equal(token.ToLowerInvariant(), token);
    }

    [Fact]
    public void a_thousand_challenges_are_all_distinct()
    {
        var values = Enumerable.Range(0, 1000).Select(_ => BrandDomainChallenge.NewValue()).ToHashSet();
        Assert.Equal(1000, values.Count);
    }
}
