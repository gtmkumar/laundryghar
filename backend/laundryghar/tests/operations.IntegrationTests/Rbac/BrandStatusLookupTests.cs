using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Regression guard for a bug that shipped silently and was found only by driving a real suspended
/// brand: the §9 login-only gate could not see brand status at all.
///
/// <para><c>tenancy_org.brands</c> carries <c>rls_admin_only USING (kernel.rls_bypass())</c>, so it
/// reads as EMPTY for ordinary tenant traffic — which is precisely the traffic the suspension gate
/// exists to stop. Reading <c>brands.status</c> through EF therefore returned null on every tenant
/// request, and because the status store fails OPEN by design, the gate did not error: it simply
/// never fired. A suspended brand kept trading, and every unit test stayed green, because those tests
/// inject a fake store.</para>
///
/// <para>That combination — a fail-open default over an RLS-invisible table — hides itself, so it
/// gets a test that runs as the real non-superuser <c>app_user</c> against the real policy.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class BrandStatusLookupTests
{
    private readonly RbacRlsFixture _fx;
    public BrandStatusLookupTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── the exact shape of the bug: app_user cannot SEE the table, but CAN read a status.
    [Fact]
    public async Task app_user_reads_a_status_it_cannot_read_from_the_table()
    {
        if (!_fx.DockerAvailable) return;

        var brandId = Guid.NewGuid();
        await using (var su = await _fx.OpenSuperuserAsync())
            await RbacRlsFixture.ExecAsync(su,
                $"INSERT INTO tenancy_org.brands (id, name, status) VALUES ('{brandId}', 'Susp Co', 'suspended')");

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app);   // ordinary tenant session: no bypass

        // The direct read is blocked — this is what made the EF version silently return null.
        var visible = await RbacRlsFixture.ScalarAsync(app, "SELECT count(*) FROM tenancy_org.brands");
        Assert.Equal(0L, visible);

        // The SECURITY DEFINER lookup still answers, and answers correctly.
        var status = await RbacRlsFixture.ScalarAsync(app, $"SELECT kernel.brand_status('{brandId}')");
        Assert.Equal("suspended", status);
    }

    // 2 ── an active brand reads back as active, so the gate stays shut for healthy tenants.
    [Fact]
    public async Task an_active_brand_reads_back_as_active()
    {
        if (!_fx.DockerAvailable) return;

        var brandId = Guid.NewGuid();
        await using (var su = await _fx.OpenSuperuserAsync())
            await RbacRlsFixture.ExecAsync(su,
                $"INSERT INTO tenancy_org.brands (id, name) VALUES ('{brandId}', 'Healthy Co')");

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app);

        Assert.Equal("active", await RbacRlsFixture.ScalarAsync(app, $"SELECT kernel.brand_status('{brandId}')"));
    }

    // 3 ── an unknown brand returns NULL, which the store treats as "do not block" (fail open).
    //      Worth pinning: the worst case of guessing wrong here is a suspended brand trading briefly,
    //      which beats an outage for every healthy tenant.
    [Fact]
    public async Task an_unknown_brand_returns_null()
    {
        if (!_fx.DockerAvailable) return;

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app);

        Assert.Null(await RbacRlsFixture.ScalarAsync(app, $"SELECT kernel.brand_status('{Guid.NewGuid()}')"));
    }
}
