using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Lock-in for <c>tenancy_org.brand_domains</c> (migration 0002) — the storage half of white-label
/// tier T2 (PLATFORM_STRATEGY.md §4.2). Runs against the REAL migration file applied verbatim by
/// <see cref="RbacRlsFixture"/>, against a NON-superuser <c>app_user</c> that RLS actually binds.
///
/// The invariants here are the ones a host-resolution middleware (T-09) will depend on, so each is
/// stated as the security or correctness property it protects:
///   • <c>domain</c> is GLOBALLY unique — otherwise a hostname could map to two brands and the
///     Host → brand lookup would be non-deterministic;
///   • that uniqueness is CASE-INSENSITIVE — DNS is, so "Foo.com" must not be claimable alongside
///     "foo.com", which would otherwise be a trivial tenant-hijack;
///   • at most one <c>is_primary</c> per brand, but every brand may have its own;
///   • <c>ssl_status</c> is a closed vocabulary;
///   • RLS scopes rows to the tenant, for both reads and writes;
///   • the <c>updated_at</c> trigger is attached.
/// </summary>
[Collection("rbac-rls")]
public sealed class BrandDomainTests
{
    private readonly RbacRlsFixture _fx;
    public BrandDomainTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── a hostname belongs to exactly one brand, case-insensitively.
    [Fact]
    public async Task domain_is_globally_unique_and_case_insensitive()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        await using var su = await _fx.OpenSuperuserAsync();
        var brandA = await NewBrandAsync(su, $"A_{t}");
        var brandB = await NewBrandAsync(su, $"B_{t}");

        await InsertDomainAsync(su, brandA, $"acme{t}.example");

        // Same brand, same host → rejected.
        var dup = await Assert.ThrowsAsync<PostgresException>(
            () => InsertDomainAsync(su, brandA, $"acme{t}.example"));
        Assert.Equal("23505", dup.SqlState);

        // DIFFERENT brand, SAME host in different case → still rejected. This is the tenant-hijack
        // guard: without citext, brand B could claim "ACME.example" and steal brand A's traffic.
        var hijack = await Assert.ThrowsAsync<PostgresException>(
            () => InsertDomainAsync(su, brandB, $"ACME{t.ToUpperInvariant()}.EXAMPLE"));
        Assert.Equal("23505", hijack.SqlState);

        // And the lookup a resolver would perform matches regardless of the caller's casing.
        var resolved = await _fx.OpenSuperuserAsync();
        await using (resolved)
        {
            var found = await RbacRlsFixture.ScalarAsync(resolved,
                $"SELECT brand_id FROM tenancy_org.brand_domains WHERE domain = 'AcMe{t}.ExAmPlE'");
            Assert.Equal(brandA, found);
        }
    }

    // 2 ── one canonical host per brand; other brands are unaffected.
    [Fact]
    public async Task at_most_one_primary_host_per_brand()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        await using var su = await _fx.OpenSuperuserAsync();
        var brandA = await NewBrandAsync(su, $"A_{t}");
        var brandB = await NewBrandAsync(su, $"B_{t}");

        await InsertDomainAsync(su, brandA, $"primary{t}.example", isPrimary: true);

        // A second primary for the SAME brand → rejected by idx_brand_domains_one_primary.
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => InsertDomainAsync(su, brandA, $"second{t}.example", isPrimary: true));
        Assert.Equal("23505", ex.SqlState);

        // A non-primary alias for the same brand is fine (the index is partial)…
        await InsertDomainAsync(su, brandA, $"alias{t}.example");
        // …and another brand gets its own primary.
        await InsertDomainAsync(su, brandB, $"primaryb{t}.example", isPrimary: true);

        var aliases = await RbacRlsFixture.ScalarAsync(su,
            $"SELECT count(*) FROM tenancy_org.brand_domains WHERE brand_id = '{brandA}'");
        Assert.Equal(2L, aliases);
    }

    // 3 ── ssl_status is a closed vocabulary (ADR-005: CHECK, not a PG enum).
    [Fact]
    public async Task ssl_status_rejects_a_value_outside_the_vocabulary()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        await using var su = await _fx.OpenSuperuserAsync();
        var brand = await NewBrandAsync(su, $"S_{t}");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(su, $"""
            INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt, ssl_status)
            VALUES ('{brand}', 'ssl{t}.example', 'tok', 'totally-issued')
            """));
        Assert.Equal("23514", ex.SqlState);

        // Every documented value is accepted.
        foreach (var status in new[] { "pending", "active", "failed", "expired" })
            await RbacRlsFixture.ExecAsync(su, $"""
                INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt, ssl_status)
                VALUES ('{brand}', '{status}{t}.example', 'tok', '{status}')
                """);
    }

    // 4 ── unverified rows exist but must never resolve. This is the property T-09 relies on: a
    //      row is created the moment a provider ADDS a domain, long before they prove they own it.
    [Fact]
    public async Task unverified_domains_are_excluded_from_the_resolution_predicate()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        await using var su = await _fx.OpenSuperuserAsync();
        var brand = await NewBrandAsync(su, $"V_{t}");

        await InsertDomainAsync(su, brand, $"unverified{t}.example");                       // verified_at NULL
        await InsertDomainAsync(su, brand, $"verified{t}.example", verified: true);

        var resolvable = await RbacRlsFixture.ScalarAsync(su, $"""
            SELECT count(*) FROM tenancy_org.brand_domains
            WHERE brand_id = '{brand}' AND verified_at IS NOT NULL
            """);
        Assert.Equal(1L, resolvable);

        var unverifiedResolves = await RbacRlsFixture.ScalarAsync(su, $"""
            SELECT count(*) FROM tenancy_org.brand_domains
            WHERE domain = 'unverified{t}.example' AND verified_at IS NOT NULL
            """);
        Assert.Equal(0L, unverifiedResolves);
    }

    // 5 ── RLS: a tenant sees only its own domains, and cannot plant one on another brand.
    [Fact]
    public async Task rls_isolates_domains_by_brand_for_reads_and_writes()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        Guid brandA, brandB;
        await using (var su = await _fx.OpenSuperuserAsync())
        {
            brandA = await NewBrandAsync(su, $"A_{t}");
            brandB = await NewBrandAsync(su, $"B_{t}");
            await InsertDomainAsync(su, brandA, $"a{t}.example");
            await InsertDomainAsync(su, brandB, $"b{t}.example");
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, brand: brandA);

        // READ: only brand A's row is visible.
        var visible = await RbacRlsFixture.ScalarAsync(app,
            $"SELECT count(*) FROM tenancy_org.brand_domains WHERE domain IN ('a{t}.example','b{t}.example')");
        Assert.Equal(1L, visible);

        var whose = await RbacRlsFixture.ScalarAsync(app,
            "SELECT brand_id FROM tenancy_org.brand_domains LIMIT 1");
        Assert.Equal(brandA, whose);

        // WRITE: planting a domain on brand B is refused. The policy declares only USING, so
        // PostgreSQL applies it as the INSERT check too — 42501 insufficient_privilege.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(app, $"""
            INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt)
            VALUES ('{brandB}', 'hijack{t}.example', 'tok')
            """));
        Assert.Equal("42501", ex.SqlState);

        // Writing its OWN domain is allowed.
        await RbacRlsFixture.ExecAsync(app, $"""
            INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt)
            VALUES ('{brandA}', 'mine{t}.example', 'tok')
            """);
    }

    // 6 ── the shared updated_at trigger is attached (house convention for every mutable table).
    [Fact]
    public async Task updated_at_is_maintained_by_the_trigger()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        await using var su = await _fx.OpenSuperuserAsync();
        var brand = await NewBrandAsync(su, $"U_{t}");
        await InsertDomainAsync(su, brand, $"touch{t}.example");

        await RbacRlsFixture.ExecAsync(su, $"""
            UPDATE tenancy_org.brand_domains
               SET created_at = created_at - interval '1 day', updated_at = updated_at - interval '1 day'
             WHERE domain = 'touch{t}.example'
            """);

        // A subsequent UPDATE that does not itself set updated_at must have it stamped to now().
        await RbacRlsFixture.ExecAsync(su, $"""
            UPDATE tenancy_org.brand_domains SET ssl_status = 'active'
             WHERE domain = 'touch{t}.example'
            """);

        var bumped = await RbacRlsFixture.ScalarAsync(su, $"""
            SELECT updated_at > created_at FROM tenancy_org.brand_domains
             WHERE domain = 'touch{t}.example'
            """);
        Assert.Equal(true, bumped);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<Guid> NewBrandAsync(NpgsqlConnection su, string name)
    {
        var id = Guid.NewGuid();
        await RbacRlsFixture.ExecAsync(su,
            $"INSERT INTO tenancy_org.brands (id, name) VALUES ('{id}', '{name}')");
        return id;
    }

    private static Task InsertDomainAsync(
        NpgsqlConnection conn, Guid brandId, string domain, bool isPrimary = false, bool verified = false)
        => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brand_domains (brand_id, domain, verification_txt, is_primary, verified_at)
            VALUES ('{brandId}', '{domain}', 'lg-verify={Guid.NewGuid():N}', {(isPrimary ? "true" : "false")},
                    {(verified ? "now()" : "NULL")})
            """);
}
