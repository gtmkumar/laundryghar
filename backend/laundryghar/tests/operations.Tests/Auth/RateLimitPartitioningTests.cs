using System.Text;
using System.Text.Json;
using laundryghar.Gateway;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// PLATFORM_STRATEGY.md §7 "per-provider rate limits". IP is the wrong unit for a multi-tenant
/// platform in BOTH directions, and these tests are written around that:
///   • it punishes the innocent — an office, a carrier NAT or a corporate proxy collapses many
///     tenants onto one IP, so a busy neighbour throttles everyone behind it;
///   • it fails to contain the guilty — one tenant across a few machines simply gets a multiple of
///     the budget, and a runaway integration degrades the platform for everyone else.
/// Keying on the brand makes the budget follow the tenant being metered.
/// </summary>
public class RateLimitPartitioningTests
{
    private const int IpLimit = 300;
    private const int BrandLimit = 3000;

    private static readonly Guid BrandA = Guid.NewGuid();
    private static readonly Guid BrandB = Guid.NewGuid();

    // 1 ── the punished-innocent case: two tenants behind ONE IP get separate budgets.
    [Fact]
    public async Task two_brands_on_the_same_ip_do_not_share_a_budget()
    {
        var a = Resolve(Ctx(ip: "203.0.113.9", brandToken: BrandA));
        var b = Resolve(Ctx(ip: "203.0.113.9", brandToken: BrandB));

        Assert.NotEqual(a.Key, b.Key);
        Assert.Equal(BrandLimit, a.Limit);
        await Task.CompletedTask;
    }

    // 2 ── the uncontained-guilty case: one tenant across MANY IPs shares a single budget.
    [Fact]
    public void one_brand_across_many_ips_shares_one_budget()
    {
        var first  = Resolve(Ctx(ip: "198.51.100.1", brandToken: BrandA));
        var second = Resolve(Ctx(ip: "198.51.100.2", brandToken: BrandA));
        var third  = Resolve(Ctx(ip: "10.0.0.7",     brandToken: BrandA));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(first.Key, third.Key);
    }

    // 3 ── unauthenticated traffic has no tenant, so IP is the only identity there is.
    [Fact]
    public void anonymous_traffic_falls_back_to_the_ip_partition()
    {
        var one = Resolve(Ctx(ip: "192.0.2.5"));
        var two = Resolve(Ctx(ip: "192.0.2.6"));

        Assert.StartsWith("ip:", one.Key);
        Assert.Equal(IpLimit, one.Limit);
        Assert.NotEqual(one.Key, two.Key);
    }

    // 4 ── an explicit X-Brand-Id wins (that is how a platform admin acts on a tenant).
    [Fact]
    public void the_x_brand_id_header_takes_precedence()
    {
        var resolved = Resolve(Ctx(ip: "203.0.113.9", brandHeader: BrandB, brandToken: BrandA));
        Assert.Equal($"brand:{BrandB}", resolved.Key);
    }

    // 5 ── X-Forwarded-For is honoured, else a whole proxy estate collapses into one bucket.
    [Fact]
    public void the_forwarded_client_ip_is_used_when_present()
    {
        var resolved = Resolve(Ctx(ip: "10.0.0.1", forwardedFor: "198.51.100.44, 10.0.0.1"));
        Assert.Equal("ip:198.51.100.44", resolved.Key);
    }

    // 6 ── keys are namespaced, so a brand id can never collide with an IP string.
    [Fact]
    public void brand_and_ip_keys_live_in_separate_namespaces()
    {
        Assert.StartsWith("brand:", Resolve(Ctx(ip: "1.1.1.1", brandToken: BrandA)).Key);
        Assert.StartsWith("ip:",    Resolve(Ctx(ip: "1.1.1.1")).Key);
    }

    // 7 ── a garbage or unsigned-nonsense Authorization header must not throw; it degrades to the IP
    //      partition. The gateway does not validate tokens — the service behind it does — so the only
    //      requirement here is that a malformed one cannot take the limiter down.
    [Theory]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Bearer a.b")]
    [InlineData("Bearer ...")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    public void a_malformed_authorization_header_degrades_to_ip(string header)
    {
        var ctx = Ctx(ip: "203.0.113.77");
        ctx.Request.Headers.Authorization = header;

        var resolved = Resolve(ctx);

        Assert.Equal("ip:203.0.113.77", resolved.Key);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static (string Key, int Limit) Resolve(HttpContext ctx)
        => RateLimitPartitioning.Resolve(ctx, IpLimit, BrandLimit);

    private static DefaultHttpContext Ctx(
        string ip, Guid? brandToken = null, Guid? brandHeader = null, string? forwardedFor = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);

        if (forwardedFor is not null) ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (brandHeader is { } h) ctx.Request.Headers["X-Brand-Id"] = h.ToString();
        if (brandToken is { } t) ctx.Request.Headers.Authorization = $"Bearer {FakeJwt(t)}";

        return ctx;
    }

    /// <summary>A structurally valid, deliberately UNSIGNED JWT. The gateway reads the payload
    /// without verifying anything — see RateLimitPartitioning's class comment for why that is safe
    /// for a bucket choice and would not be for anything else.</summary>
    private static string FakeJwt(Guid brandId)
    {
        static string Seg(object o) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)))
                   .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Seg(new { alg = "RS256", typ = "JWT" })}." +
               $"{Seg(new { sub = Guid.NewGuid(), brand_id = brandId.ToString() })}." +
               "not-a-real-signature";
    }
}
