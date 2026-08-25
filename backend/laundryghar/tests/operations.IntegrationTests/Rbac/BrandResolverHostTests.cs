using core.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Host-header brand resolution — white-label tier T2 (PLATFORM_STRATEGY.md §4.2 item 4): one
/// deployment serves N branded domains, because every request's Host maps through
/// <c>tenancy_org.brand_domains</c> to a brand. Exercises the real <see cref="BrandResolver"/>
/// against a real Postgres with migrations 0002 + 0003 applied verbatim.
///
/// The properties that matter, and why:
///   • a VERIFIED host resolves to its brand — the feature itself;
///   • an UNVERIFIED host does not — a row exists from the moment a provider *adds* a domain, so
///     resolving it would let anyone claim any hostname and capture its traffic;
///   • an UNKNOWN host falls through to the pre-existing X-Brand-Id / ?brandCode= / default chain —
///     this is what guarantees every existing caller is unaffected;
///   • Host outranks X-Brand-Id, but only when the host is actually a verified custom domain;
///   • resolution is case-insensitive (the citext trap: <c>citext = text</c> compares
///     case-SENSITIVELY, so a missing cast would silently break real traffic);
///   • hits and misses are both cached, bounded by a short TTL.
/// </summary>
[Collection("rbac-ef")]
public sealed class BrandResolverHostTests
{
    private readonly RbacEfFixture _fx;
    public BrandResolverHostTests(RbacEfFixture fx) => _fx = fx;

    // 1 ── the feature: a verified custom domain resolves to its own brand, whatever the casing.
    [Fact]
    public async Task verified_host_resolves_to_its_brand_case_insensitively()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        await AddDomainAsync(brandId, $"shop{t}.example", verified: true);

        Assert.Equal(brandId, await ResolveAsync(host: $"shop{t}.example"));

        // Incoming casing is normalised in C# before the lookup.
        Assert.Equal(brandId, await ResolveAsync(host: $"SHOP{t.ToUpperInvariant()}.EXAMPLE"));

        // A fully-qualified name with the DNS root dot is the same host.
        Assert.Equal(brandId, await ResolveAsync(host: $"shop{t}.example."));

        // The STORED value's casing is the case that actually needs citext. Nothing forces a domain
        // to be persisted lowercase — the column is citext, so uniqueness ignores case but the
        // original spelling is kept. A provider who registers "TheirBrand.example" must still be
        // reachable at the lowercased host the resolver looks up. Without the ::citext cast in
        // kernel.resolve_brand_domain this comparison degrades to text = text and returns nothing.
        var mixedBrand = Guid.NewGuid();
        await _fx.SeedBrandAsync(mixedBrand);
        await AddDomainAsync(mixedBrand, $"MixedCase{t}.Example", verified: true);

        Assert.Equal(mixedBrand, await ResolveAsync(host: $"mixedcase{t}.example"));
    }

    // 2 ── the security property: adding a domain is not the same as owning it.
    [Fact]
    public async Task unverified_host_does_not_resolve_and_falls_through()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        await AddDomainAsync(brandId, $"pending{t}.example", verified: false);

        // Another brand's id supplied via the header. If the unverified host resolved, it would win
        // and return brandId — so this asserts the fall-through precisely.
        var otherBrand = Guid.NewGuid();
        var resolved = await ResolveAsync(host: $"pending{t}.example", brandIdHeader: otherBrand);

        Assert.Equal(otherBrand, resolved);
        Assert.NotEqual(brandId, resolved);
    }

    // 3 ── the compatibility property: a host we do not know changes nothing for existing callers.
    [Fact]
    public async Task unknown_host_preserves_the_header_and_query_chain()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        var brandCode = await _fx.SeedBrandAsync(brandId);

        // (a) X-Brand-Id still wins on an unknown host.
        var viaHeader = Guid.NewGuid();
        Assert.Equal(viaHeader, await ResolveAsync(host: $"api{t}.laundryghar.com", brandIdHeader: viaHeader));

        // (b) ?brandCode= still resolves through the brands table on an unknown host.
        Assert.Equal(brandId, await ResolveAsync(host: $"api{t}.laundryghar.com", brandCode: brandCode));

        // (c) localhost and bare IPs skip the lookup entirely and behave exactly as before.
        Assert.Equal(brandId, await ResolveAsync(host: "localhost", brandCode: brandCode));
        Assert.Equal(brandId, await ResolveAsync(host: "127.0.0.1", brandCode: brandCode));
    }

    // 4 ── precedence: on a VERIFIED custom domain the host wins over a conflicting X-Brand-Id.
    //      The domain has been proved owned; the header is an unchecked assertion on an anonymous
    //      endpoint, so the domain is the more trustworthy signal (and §4.2 says the host decides).
    [Fact]
    public async Task verified_host_outranks_the_x_brand_id_header()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var owner = Guid.NewGuid();
        await _fx.SeedBrandAsync(owner);
        await AddDomainAsync(owner, $"owned{t}.example", verified: true);

        var claimed = Guid.NewGuid();
        var resolved = await ResolveAsync(host: $"owned{t}.example", brandIdHeader: claimed);

        Assert.Equal(owner, resolved);
        Assert.NotEqual(claimed, resolved);
    }

    // 5 ── caching: a resolved host is served from memory within the TTL, and so is a miss.
    //      Proven by mutating the DB behind the resolver and showing the answer does not change.
    [Fact]
    public async Task hits_and_misses_are_both_cached()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        var host = $"cached{t}.example";
        await AddDomainAsync(brandId, host, verified: true);

        // One resolver instance == one IMemoryCache, mirroring a process rather than a request.
        var cache = new MemoryCache(new MemoryCacheOptions());

        Assert.Equal(brandId, await ResolveAsync(host: host, cache: cache));

        // Delete the row. A cached HIT keeps answering; an uncached resolver sees the truth.
        await ExecAsync($"DELETE FROM tenancy_org.brand_domains WHERE domain = '{host}'");

        Assert.Equal(brandId, await ResolveAsync(host: host, cache: cache));   // still cached
        Assert.Null(await ResolveAsync(host: host, brandCode: "no-such-code")); // fresh cache -> gone

        // Negative caching: resolve an unknown host, then make it real; the miss stays cached.
        var missHost = $"miss{t}.example";
        Assert.Null(await ResolveAsync(host: missHost, brandCode: "no-such-code", cache: cache));
        await AddDomainAsync(brandId, missHost, verified: true);
        Assert.Null(await ResolveAsync(host: missHost, brandCode: "no-such-code", cache: cache));

        // …and a resolver with a cold cache picks it up immediately.
        Assert.Equal(brandId, await ResolveAsync(host: missHost));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Runs the REAL BrandResolver over the fixture's contexts and a synthetic request.</summary>
    private async Task<Guid?> ResolveAsync(
        string host, Guid? brandIdHeader = null, string? brandCode = null, IMemoryCache? cache = null)
    {
        await using var db = _fx.NewContext();
        var resolver = new BrandResolver(
            _fx.AsCore(db), db,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<BrandResolver>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        if (brandIdHeader is { } h) ctx.Request.Headers["X-Brand-Id"] = h.ToString();
        if (brandCode is not null) ctx.Request.QueryString = new QueryString($"?brandCode={brandCode}");

        return await resolver.ResolveAsync(ctx, CancellationToken.None);
    }

    private Task AddDomainAsync(Guid brandId, string domain, bool verified) => ExecAsync($"""
        INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt, verified_at)
        VALUES ('{brandId}', '{domain}', 'lg-verify={Guid.NewGuid():N}', {(verified ? "now()" : "NULL")})
        """);

    private async Task ExecAsync(string sql)
    {
        await using var c = new NpgsqlConnection(_fx.SuperConnString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
}
