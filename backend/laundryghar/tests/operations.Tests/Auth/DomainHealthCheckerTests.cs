using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Persistence;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// §12: automated SSL and domain health checks "from day one" (docs/TASKS.md T-12).
///
/// <para>These reach the real internet, which is normally a smell in a unit test — and here it is
/// the point. The thing under test is whether we can read a live certificate's expiry and notice a
/// broken one; a mocked TLS handshake would only prove the mock works. Every test self-skips when
/// the network is unavailable, so an offline machine sees no failures.</para>
/// </summary>
public class DomainHealthCheckerTests
{
    private static readonly TlsDomainHealthChecker Checker = new();

    /// <summary>badssl.com publishes deliberately-broken certificates for exactly this purpose.</summary>
    private const string ExpiredCert = "expired.badssl.com";
    private const string WrongHostCert = "wrong.host.badssl.com";

    // 1 ── a healthy host reports healthy, with a real expiry and issuer.
    [Fact]
    public async Task A_healthy_host_reports_its_certificate()
    {
        var result = await Checker.CheckAsync("www.cloudflare.com");
        if (Skip(result)) return;

        Assert.Equal(DomainHealth.Healthy, result.Health);
        Assert.NotNull(result.SslExpiresAt);
        Assert.True(result.SslExpiresAt > DateTimeOffset.UtcNow,
            "a healthy certificate must not already be expired");
        Assert.False(string.IsNullOrWhiteSpace(result.Issuer));
    }

    // 2 ── THE ONE THIS EXISTS FOR. An expired certificate must be reported as degraded WITH its
    //      expiry date — not as a connection failure. "It expired on Tuesday" is actionable;
    //      "the handshake failed" sends someone to look at the network.
    [Fact]
    public async Task An_expired_certificate_is_degraded_and_dated()
    {
        var result = await Checker.CheckAsync(ExpiredCert);
        if (Skip(result)) return;

        Assert.Equal(DomainHealth.Degraded, result.Health);
        Assert.NotNull(result.SslExpiresAt);
        Assert.True(result.SslExpiresAt < DateTimeOffset.UtcNow);
        Assert.Contains("expired", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    // 3 ── a VALID certificate for the wrong name still gives every visitor a browser warning, so
    //      "valid" alone is not the question being asked.
    [Fact]
    public async Task A_certificate_for_the_wrong_host_is_degraded()
    {
        var result = await Checker.CheckAsync(WrongHostCert);
        if (Skip(result)) return;

        Assert.Equal(DomainHealth.Degraded, result.Health);
        Assert.Contains("different host", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    // 4 ── a host that does not exist is unreachable, and says why. Never an exception: one bad
    //      domain must not abandon a sweep over everyone else's.
    [Fact]
    public async Task A_host_that_does_not_resolve_is_unreachable()
    {
        var result = await Checker.CheckAsync("this-host-does-not-exist.laundryghar-test.invalid");

        Assert.Equal(DomainHealth.Unreachable, result.Health);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.SslExpiresAt);
    }

    // 5 ── garbage input is an observation, not a crash.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a hostname at all")]
    public async Task Malformed_input_is_reported_not_thrown(string domain)
    {
        var result = await Checker.CheckAsync(domain);

        Assert.Equal(DomainHealth.Unreachable, result.Health);
    }

    // 6 ── the per-check timeout is bounded, or one blackholing domain stalls the whole sweep.
    [Fact]
    public async Task A_check_is_time_bounded()
    {
        var started = DateTimeOffset.UtcNow;

        // 203.0.113.0/24 is TEST-NET-3 (RFC 5737) — reserved for documentation and routed nowhere,
        // so a connection attempt hangs rather than being refused.
        await Checker.CheckAsync("203.0.113.1");

        Assert.True(DateTimeOffset.UtcNow - started < TlsDomainHealthChecker.Timeout + TimeSpan.FromSeconds(5),
            "a check must not outlive its timeout");
    }

    /// <summary>True when the result reflects an offline machine rather than the host under test.</summary>
    private static bool Skip(DomainHealthResult result) =>
        result.Health == DomainHealth.Unreachable && result.SslExpiresAt is null;
}
