using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// §9's wizard, at the layer that decides what it says (migration 0017).
///
/// <para>The design under test is that progress is <b>derived</b>, never remembered. The obvious
/// alternative — a <c>completed_steps</c> list the API appends to — is the one that lies: finish
/// "add your first location", delete the location, and the console shows a finished setup over a
/// business that cannot take an order. <see cref="Deleting_the_data_undoes_the_step"/> is the test
/// that pins that down, and it is the only one here that could not pass under the stored design.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class OnboardingFactsTests
{
    private readonly RbacRlsFixture _fx;
    public OnboardingFactsTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── an untouched account reports nothing done, and does not invent a brand.
    [Fact]
    public async Task A_fresh_brand_reports_nothing_done()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "fresh");

        var row = await RbacRlsFixture.ScalarAsync(conn, $"""
            SELECT locations::text || '/' || catalog_items::text || '/' ||
                   coalesce(primary_domain, 'none')
            FROM kernel.brand_onboarding_facts('{brand}')
            """);

        Assert.Equal("0/0/none", row);
    }

    // 2 ── an unknown brand reports ZERO progress — never someone else's. It is indistinguishable
    //      from a brand-new account, which is the right answer: the caller can only ever pass their
    //      own tenant's id, so there is nothing here to enumerate.
    [Fact]
    public async Task An_unknown_brand_reports_no_progress()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        var row = await RbacRlsFixture.ScalarAsync(conn, """
            SELECT locations::text || '/' || catalog_items::text || '/' ||
                   coalesce(primary_domain, 'none') || '/' ||
                   coalesce(own_franchise_id::text, 'none')
            FROM kernel.brand_onboarding_facts(gen_random_uuid())
            """);

        Assert.Equal("0/0/none/none", row);
    }

    // 3 ── the counts follow the real rows.
    [Fact]
    public async Task The_counts_follow_the_real_rows()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "counts");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.stores (brand_id, code) VALUES ('{brand}', 'S1'), ('{brand}', 'S2');
            INSERT INTO customer_catalog.items (brand_id) VALUES ('{brand}'), ('{brand}'), ('{brand}');
            """);

        Assert.Equal("2/3", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT locations::text || '/' || catalog_items::text FROM kernel.brand_onboarding_facts('{brand}')"));
    }

    // 4 ── THE POINT OF THE WHOLE DESIGN. Take the data away and the step un-completes.
    [Fact]
    public async Task Deleting_the_data_undoes_the_step()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "undo");

        await RbacRlsFixture.ExecAsync(conn,
            $"INSERT INTO tenancy_org.stores (brand_id, code) VALUES ('{brand}', 'ONLY')");
        Assert.Equal(1, Convert.ToInt32(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT locations FROM kernel.brand_onboarding_facts('{brand}')")));

        await RbacRlsFixture.ExecAsync(conn,
            $"DELETE FROM tenancy_org.stores WHERE brand_id = '{brand}'");

        // A stored `completed_steps` list would still say this step was done.
        Assert.Equal(0, Convert.ToInt32(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT locations FROM kernel.brand_onboarding_facts('{brand}')")));
    }

    // 5 ── a soft-deleted or inactive location is not a location.
    [Fact]
    public async Task Inactive_and_deleted_rows_do_not_count()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "inactive");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.stores (brand_id, code, status) VALUES ('{brand}', 'X', 'inactive');
            INSERT INTO tenancy_org.stores (brand_id, code, deleted_at) VALUES ('{brand}', 'Y', now());
            INSERT INTO customer_catalog.items (brand_id, status) VALUES ('{brand}', 'archived');
            """);

        Assert.Equal("0/0", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT locations::text || '/' || catalog_items::text FROM kernel.brand_onboarding_facts('{brand}')"));
    }

    // 6 ── the provider's own operating entity is handed over, because `franchises` is an ENTERPRISE
    //      feature and a store needs a franchise id — without this a Starter provider is correctly
    //      refused the franchise list and cannot complete step one.
    [Fact]
    public async Task The_providers_own_operating_entity_is_surfaced()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "own");

        Assert.True(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT own_franchise_id FROM kernel.brand_onboarding_facts('{brand}')") is null or DBNull);

        await RbacRlsFixture.ExecAsync(conn,
            $"INSERT INTO tenancy_org.franchises (brand_id, code) VALUES ('{brand}', 'OWN')");

        Assert.False(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT own_franchise_id FROM kernel.brand_onboarding_facts('{brand}')") is null or DBNull);
    }

    // 7 ── going live is idempotent, and creates a verified primary host in OUR zone with no DNS
    //      challenge — proving we control our own zone would be ceremony.
    [Fact]
    public async Task Going_live_is_idempotent()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "golive");

        var first = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.ensure_brand_subdomain('{brand}', 'example.test')");
        var second = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.ensure_brand_subdomain('{brand}', 'example.test')");

        Assert.Equal(first, second);
        Assert.Equal(1L, Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT count(*) FROM tenancy_org.brand_domains WHERE brand_id = '{brand}'")));
        Assert.Equal(first, await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT primary_domain FROM kernel.brand_onboarding_facts('{brand}')"));
    }

    // 8 ── and it never displaces a custom domain the provider already went live on. Pushing a brand
    //      back onto our sub-domain after they moved to their own would be a visible regression to
    //      every one of their customers.
    [Fact]
    public async Task Going_live_never_displaces_a_custom_domain()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "custom");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brand_domains
                (brand_id, domain, verification_txt, verified_at, is_primary)
            VALUES ('{brand}', 'wash.example.com', 'txt', now(), true)
            """);

        Assert.Equal("wash.example.com", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.ensure_brand_subdomain('{brand}', 'example.test')"));
    }

    private static async Task<Guid> SeedAsync(NpgsqlConnection conn, string tag)
    {
        var brand = Guid.NewGuid();
        await RbacRlsFixture.ExecAsync(conn,
            $"INSERT INTO tenancy_org.brands (id, name, code) VALUES ('{brand}', 'Onboarding {tag}', '{tag}')");
        return brand;
    }
}
