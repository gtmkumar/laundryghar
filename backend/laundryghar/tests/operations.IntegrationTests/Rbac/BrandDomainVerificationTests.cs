using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.BrandDomains;
using core.Application.Identity.TenancyOrg.BrandDomains.Commands;
using core.Application.Identity.TenancyOrg.BrandDomains.Dtos;
using core.Application.Identity.TenancyOrg.BrandDomains.Queries;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Domain-ownership verification end to end (PLATFORM_STRATEGY.md §4.2 item 2), against a real
/// Postgres with migration 0002 applied and a FAKE DNS resolver — so every branch that depends on
/// what the provider published is deterministic and offline.
///
/// The property that matters most: <c>verified_at</c> is what makes a hostname start serving a
/// tenant's traffic (<c>kernel.resolve_brand_domain</c> returns verified rows only). So the tests
/// are written around when it does and does not get stamped.
/// </summary>
[Collection("rbac-ef")]
public sealed class BrandDomainVerificationTests
{
    private readonly RbacEfFixture _fx;
    public BrandDomainVerificationTests(RbacEfFixture fx) => _fx = fx;

    // ── adding a domain ─────────────────────────────────────────────────────────────────────────

    // 1 ── a new domain is created UNVERIFIED, normalised, and carries a fresh challenge.
    [Fact]
    public async Task adding_a_domain_issues_a_challenge_and_does_not_verify_it()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        // Deliberately messy input: a pasted URL, mixed case, a port and a trailing path.
        var dto = await AddAsync(brandId, $"https://Shop{t}.Example:8443/pricing");

        Assert.Equal($"shop{t}.example", dto.Domain);
        Assert.False(dto.Verified);
        Assert.Null(dto.VerifiedAt);
        Assert.Equal(BrandDomainSslStatus.Pending, dto.SslStatus);

        // The console needs the complete DNS instruction: both records, spelled out.
        Assert.Equal($"_lg-verify.shop{t}.example", dto.VerificationName);
        Assert.StartsWith("lg-verify=", dto.VerificationValue);
        Assert.False(string.IsNullOrWhiteSpace(dto.CnameTarget));

        // …and it must not resolve yet.
        Assert.Null(await ResolveViaSqlAsync($"shop{t}.example"));
    }

    // 2 ── a hostname can only be claimed once, whatever its casing — and the message never reveals
    //      which brand holds it (that would expose the tenant map).
    [Fact]
    public async Task a_domain_already_claimed_is_rejected_case_insensitively()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandA);
        await _fx.SeedBrandAsync(brandB);

        await AddAsync(brandA, $"taken{t}.example");

        // Same brand, mixed case.
        var ex1 = await Assert.ThrowsAsync<ValidationException>(
            () => AddAsync(brandA, $"TAKEN{t.ToUpperInvariant()}.EXAMPLE"));
        Assert.Contains("already registered", string.Join(" ", ex1.ErrorsDictionary!["domain"]));

        // A DIFFERENT brand trying to claim it — the tenant-hijack attempt.
        await Assert.ThrowsAsync<ValidationException>(() => AddAsync(brandB, $"taken{t}.example"));

        // Guard the citext trap directly: a row STORED with mixed case must still be seen as taken.
        await ExecAsync($"""
            INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt)
            VALUES ('{brandA}', 'MixedStored{t}.Example', 'lg-verify=x')
            """);
        await Assert.ThrowsAsync<ValidationException>(() => AddAsync(brandB, $"mixedstored{t}.example"));
    }

    // 3 ── junk cannot become a domain row, because a domain row is what routes real traffic.
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("no-tld")]
    [InlineData("bad_underscore.example")]
    [InlineData("")]
    public async Task implausible_domains_are_refused(string input)
    {
        if (!_fx.DockerAvailable) return;

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        await Assert.ThrowsAsync<ValidationException>(() => AddAsync(brandId, input));
    }

    // 4 ── marking a new domain primary demotes the incumbent, so the partial unique index
    //      (one primary per brand) is satisfied by design rather than by a caught error.
    [Fact]
    public async Task adding_a_primary_demotes_the_previous_primary()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        await AddAsync(brandId, $"first{t}.example", isPrimary: true);
        await AddAsync(brandId, $"second{t}.example", isPrimary: true);

        var all = await ListAsync(brandId);
        var primary = Assert.Single(all, d => d.IsPrimary);
        Assert.Equal($"second{t}.example", primary.Domain);
    }

    // ── verifying a domain ──────────────────────────────────────────────────────────────────────

    // 5 ── SUCCESS: the exact challenge is published → verified_at is stamped and the host resolves.
    [Fact]
    public async Task publishing_the_expected_txt_record_verifies_and_makes_the_host_resolve()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        var dto = await AddAsync(brandId, $"verify{t}.example");

        Assert.Null(await ResolveViaSqlAsync(dto.Domain)); // not yet

        var dns = new FakeDns { [dto.VerificationName] = [dto.VerificationValue] };
        var result = await VerifyAsync(brandId, dto.Id, dns);

        Assert.True(result!.Verified);
        Assert.Equal(BrandDomainVerifyStatus.Verified, result.Status);

        // The point of the whole flow: the hostname now resolves to this brand.
        Assert.Equal(brandId, await ResolveViaSqlAsync(dto.Domain));
    }

    // 6 ── MISSING RECORD: nothing published yet. Reported as its own status, and nothing is stamped.
    [Fact]
    public async Task a_missing_txt_record_does_not_verify()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        var dto = await AddAsync(brandId, $"missing{t}.example");

        var result = await VerifyAsync(brandId, dto.Id, new FakeDns()); // empty zone

        Assert.False(result!.Verified);
        Assert.Equal(BrandDomainVerifyStatus.RecordNotFound, result.Status);
        Assert.Empty(result.FoundRecords);
        Assert.Null(await ResolveViaSqlAsync(dto.Domain));
    }

    // 7 ── WRONG VALUE: TXT records exist but none is ours — including ANOTHER brand's live token,
    //      which is the attack this check exists to stop. What was found is echoed back so a
    //      provider can spot their own typo.
    [Fact]
    public async Task a_txt_record_with_the_wrong_value_does_not_verify()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        var otherBrand = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        await _fx.SeedBrandAsync(otherBrand);

        var mine = await AddAsync(brandId, $"wrong{t}.example");
        var theirs = await AddAsync(otherBrand, $"other{t}.example");

        var dns = new FakeDns
        {
            [mine.VerificationName] =
            [
                "v=spf1 include:_spf.google.com ~all", // unrelated apex-style record
                theirs.VerificationValue,             // a REAL token — but not this domain's
                "lg-verify=deadbeef",                 // right shape, wrong value
            ],
        };

        var result = await VerifyAsync(brandId, mine.Id, dns);

        Assert.False(result!.Verified);
        Assert.Equal(BrandDomainVerifyStatus.ValueMismatch, result.Status);
        Assert.Equal(3, result.FoundRecords.Count);
        Assert.Null(await ResolveViaSqlAsync(mine.Domain));
    }

    // 8 ── LOOKUP FAILURE is not verification failure. A resolver outage teaches us nothing about the
    //      provider's DNS, so it gets its own status and stamps nothing.
    [Fact]
    public async Task a_dns_lookup_failure_is_reported_distinctly_and_changes_nothing()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        var dto = await AddAsync(brandId, $"servfail{t}.example");

        var result = await VerifyAsync(brandId, dto.Id, new FakeDns { Throw = true });

        Assert.False(result!.Verified);
        Assert.Equal(BrandDomainVerifyStatus.LookupFailed, result.Status);
        Assert.Null(await ResolveViaSqlAsync(dto.Domain));

        // …and the provider can simply retry once DNS is reachable again.
        var retry = await VerifyAsync(brandId, dto.Id,
            new FakeDns { [dto.VerificationName] = [dto.VerificationValue] });
        Assert.True(retry!.Verified);
    }

    // 9 ── re-verifying an already-verified domain is a no-op, and a DNS answer that has since
    //      changed does NOT un-verify it — a provider tidying their zone must not lose their site.
    [Fact]
    public async Task verification_is_idempotent_and_never_reverses()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);
        var dto = await AddAsync(brandId, $"stable{t}.example");

        var first = await VerifyAsync(brandId, dto.Id,
            new FakeDns { [dto.VerificationName] = [dto.VerificationValue] });
        Assert.Equal(BrandDomainVerifyStatus.Verified, first!.Status);
        var stampedAt = (await ListAsync(brandId)).Single(d => d.Id == dto.Id).VerifiedAt;

        // The record is gone from DNS now. Re-checking must not revoke anything.
        var second = await VerifyAsync(brandId, dto.Id, new FakeDns());

        Assert.True(second!.Verified);
        Assert.Equal(BrandDomainVerifyStatus.AlreadyVerified, second.Status);
        Assert.Equal(stampedAt, (await ListAsync(brandId)).Single(d => d.Id == dto.Id).VerifiedAt);
        Assert.Equal(brandId, await ResolveViaSqlAsync(dto.Domain));
    }

    // 10 ── cross-tenant guard: knowing another brand's domain id must not let you verify or delete
    //       it. Both handlers scope by BrandId as well as Id.
    [Fact]
    public async Task another_brands_domain_cannot_be_verified_or_deleted_by_id()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        await _fx.SeedBrandAsync(owner);
        await _fx.SeedBrandAsync(attacker);

        var dto = await AddAsync(owner, $"victim{t}.example");
        var dns = new FakeDns { [dto.VerificationName] = [dto.VerificationValue] };

        // Right id, wrong brand → treated as not found, and nothing is stamped.
        Assert.Null(await VerifyAsync(attacker, dto.Id, dns));
        Assert.Null(await ResolveViaSqlAsync(dto.Domain));

        Assert.False(await DeleteAsync(attacker, dto.Id));
        Assert.Single(await ListAsync(owner));

        // The real owner can do both.
        Assert.True((await VerifyAsync(owner, dto.Id, dns))!.Verified);
        Assert.True(await DeleteAsync(owner, dto.Id));
        Assert.Empty(await ListAsync(owner));
    }

    // 11 ── deleting a verified domain stops it resolving immediately, and frees the hostname to be
    //       claimed again (a hard delete, precisely so UNIQUE(domain) does not hold it hostage).
    [Fact]
    public async Task deleting_a_domain_stops_resolution_and_frees_the_hostname()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandA);
        await _fx.SeedBrandAsync(brandB);

        var dto = await AddAsync(brandA, $"movable{t}.example");
        await VerifyAsync(brandA, dto.Id, new FakeDns { [dto.VerificationName] = [dto.VerificationValue] });
        Assert.Equal(brandA, await ResolveViaSqlAsync(dto.Domain));

        Assert.True(await DeleteAsync(brandA, dto.Id));
        Assert.Null(await ResolveViaSqlAsync(dto.Domain));

        // Now genuinely re-claimable — a soft delete would have blocked this forever.
        var moved = await AddAsync(brandB, $"movable{t}.example");
        Assert.Equal($"movable{t}.example", moved.Domain);
        Assert.False(moved.Verified); // and it must prove ownership all over again
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static readonly IOptions<BrandDomainSettings> Settings =
        Options.Create(new BrandDomainSettings { CnameTarget = "edge.test.example" });

    private async Task<BrandDomainDto> AddAsync(Guid brandId, string domain, bool isPrimary = false)
    {
        await using var db = _fx.NewContext();
        var handler = new AddBrandDomainCommandHandler(_fx.AsCore(db), Settings);
        return await handler.HandleAsync(
            new AddBrandDomainCommand(brandId, new AddBrandDomainRequest(domain, isPrimary), null),
            CancellationToken.None);
    }

    private async Task<VerifyBrandDomainResultDto?> VerifyAsync(Guid brandId, Guid domainId, IDnsTxtLookup dns)
    {
        await using var db = _fx.NewContext();
        var handler = new VerifyBrandDomainCommandHandler(_fx.AsCore(db), dns);
        return await handler.HandleAsync(
            new VerifyBrandDomainCommand(brandId, domainId, null), CancellationToken.None);
    }

    private async Task<bool> DeleteAsync(Guid brandId, Guid domainId)
    {
        await using var db = _fx.NewContext();
        var handler = new DeleteBrandDomainCommandHandler(_fx.AsCore(db));
        return await handler.HandleAsync(new DeleteBrandDomainCommand(brandId, domainId), CancellationToken.None);
    }

    private async Task<IReadOnlyList<BrandDomainDto>> ListAsync(Guid brandId)
    {
        await using var db = _fx.NewContext();
        var handler = new GetBrandDomainsQueryHandler(_fx.AsCore(db), Settings);
        return await handler.HandleAsync(new GetBrandDomainsQuery(brandId), CancellationToken.None);
    }

    /// <summary>Asks the SHIPPED resolution function, not a re-implementation of its rules — so these
    /// tests fail if verification and resolution ever disagree about what "live" means.</summary>
    private async Task<Guid?> ResolveViaSqlAsync(string host)
    {
        await using var c = new NpgsqlConnection(_fx.SuperConnString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT kernel.resolve_brand_domain(@h)", c);
        cmd.Parameters.AddWithValue("@h", host);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? null : (Guid)v;
    }

    private async Task ExecAsync(string sql)
    {
        await using var c = new NpgsqlConnection(_fx.SuperConnString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>An offline DNS zone: name → TXT strings. Set <see cref="Throw"/> to simulate a
    /// resolver outage, which the handler must treat differently from "no record".</summary>
    private sealed class FakeDns : IDnsTxtLookup
    {
        private readonly Dictionary<string, string[]> _zone = new(StringComparer.OrdinalIgnoreCase);

        public bool Throw { get; init; }

        public string[] this[string name] { set => _zone[name] = value; }

        public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default)
        {
            if (Throw) throw new DnsLookupException("resolver unreachable (simulated).");
            return Task.FromResult<IReadOnlyList<string>>(
                _zone.TryGetValue(name, out var records) ? records : []);
        }
    }
}
