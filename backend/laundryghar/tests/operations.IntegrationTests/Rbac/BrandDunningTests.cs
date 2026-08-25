using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// §9's `Active → PastDue → Suspended → Active` edge, for the COMPANY's subscription to us.
///
/// <para>The suspension gate has been tested since T-18. What these cover is the thing that pulls
/// the trigger — which did not exist: nothing in the codebase wrote
/// <c>brands.status = 'suspended'</c>, so a provider could stop paying and keep trading. §5 calls
/// suspend-on-nonpay "already built", and it was, for CUSTOMER subscriptions — a different
/// engine.</para>
///
/// <para>The test that matters most is <see cref="A_tos_suspension_survives_a_payment"/>. Automatic
/// reinstatement is the feature; reinstating someone suspended for fraud because they settled a bill
/// is how that feature becomes a liability.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class BrandDunningTests
{
    private readonly RbacRlsFixture _fx;
    public BrandDunningTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── suspending records WHY. Everything else here depends on that.
    [Fact]
    public async Task Suspending_for_non_payment_records_the_reason()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "nonpay");

        Assert.Equal("suspended", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.set_brand_suspension('{brand}', true, 'nonpayment')"));

        Assert.Equal("suspended/nonpayment", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status || '/' || suspension_reason FROM tenancy_org.brands WHERE id = '{brand}'"));
    }

    // 2 ── payment recovered → active, and the reason is cleared so the next cycle starts clean.
    [Fact]
    public async Task Paying_up_reinstates_the_brand()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "recover");

        await RbacRlsFixture.ExecAsync(conn, $"SELECT kernel.set_brand_suspension('{brand}', true, 'nonpayment')");
        Assert.Equal("active", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.set_brand_suspension('{brand}', false)"));

        Assert.Equal("active/", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status || '/' || coalesce(suspension_reason,'') FROM tenancy_org.brands WHERE id = '{brand}'"));
    }

    // 3 ── THE ONE THAT MATTERS. §7 lists ToS enforcement as its own reason to suspend. A fraudster
    //      settling an invoice must not reinstate themselves, and the dunning worker must only ever
    //      undo what dunning did.
    [Theory]
    [InlineData("tos")]
    [InlineData("manual")]
    public async Task A_tos_suspension_survives_a_payment(string reason)
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, $"keep{reason}");

        await RbacRlsFixture.ExecAsync(conn, $"SELECT kernel.set_brand_suspension('{brand}', true, '{reason}')");

        // The worker's reinstate call, made verbatim.
        Assert.Equal("suspended", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.set_brand_suspension('{brand}', false)"));

        Assert.Equal($"suspended/{reason}", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status || '/' || suspension_reason FROM tenancy_org.brands WHERE id = '{brand}'"));
    }

    // 4 ── a brand suspended before this column existed has no recorded reason, and must therefore
    //      never be auto-reinstated. NULL reads as "not nonpayment", which is the safe default.
    [Fact]
    public async Task A_suspension_with_no_recorded_reason_is_never_auto_cleared()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "legacy");

        await RbacRlsFixture.ExecAsync(conn,
            $"UPDATE tenancy_org.brands SET status='suspended', suspension_reason=NULL WHERE id='{brand}'");

        Assert.Equal("suspended", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.set_brand_suspension('{brand}', false)"));
    }

    // 5 ── a wind-down outranks a billing problem, in BOTH directions. Suspending a brand mid-export
    //      would break §8.2's promise that they can take their data with them.
    [Theory]
    [InlineData("cancelled")]
    [InlineData("archived")]
    public async Task A_wind_down_is_never_touched_by_dunning(string state)
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, $"wind{state}");

        await RbacRlsFixture.ExecAsync(conn,
            $"UPDATE tenancy_org.brands SET status='{state}' WHERE id='{brand}'");

        Assert.Equal(state, await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.set_brand_suspension('{brand}', true, 'nonpayment')"));
        Assert.Equal(state, await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status FROM tenancy_org.brands WHERE id = '{brand}'"));
    }

    // 6 ── suspending twice does not overwrite the original reason. A ToS suspension that dunning
    //      later touches must stay a ToS suspension, or test 3 becomes bypassable by waiting.
    [Fact]
    public async Task Re_suspending_never_overwrites_why()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "twice");

        await RbacRlsFixture.ExecAsync(conn, $"SELECT kernel.set_brand_suspension('{brand}', true, 'tos')");
        await RbacRlsFixture.ExecAsync(conn, $"SELECT kernel.set_brand_suspension('{brand}', true, 'nonpayment')");

        Assert.Equal("tos", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT suspension_reason FROM tenancy_org.brands WHERE id = '{brand}'"));
    }

    // 7 ── an unknown brand answers null rather than raising: the worker iterates a list that may
    //      have gone stale, and one deleted brand must not abandon the pass.
    [Fact]
    public async Task An_unknown_brand_answers_null()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        Assert.True(await RbacRlsFixture.ScalarAsync(conn,
            "SELECT kernel.set_brand_suspension(gen_random_uuid(), true, 'nonpayment')") is null or DBNull);
    }

    // 8 ── only the three reasons the schema allows. A typo must not create a fourth class of
    //      suspension that nothing knows how to clear.
    [Fact]
    public async Task Only_the_three_known_reasons_are_accepted()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var brand = await SeedAsync(conn, "reason");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn,
            $"SELECT kernel.set_brand_suspension('{brand}', true, 'because-i-said-so')"));

        Assert.Equal("23514", ex.SqlState);
    }

    private static async Task<Guid> SeedAsync(NpgsqlConnection conn, string tag)
    {
        var brand = Guid.NewGuid();
        await RbacRlsFixture.ExecAsync(conn,
            $"INSERT INTO tenancy_org.brands (id, name, code, status) VALUES ('{brand}', 'Dunning {tag}', '{tag}', 'active')");
        return brand;
    }
}
