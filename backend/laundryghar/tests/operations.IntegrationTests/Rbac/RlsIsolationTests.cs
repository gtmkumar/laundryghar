using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Proves PostgreSQL RLS enforces cross-brand / cross-user isolation for the NON-superuser,
/// NON-bypassrls <c>app_user</c> role. Ground truth is always seeded on the SUPERUSER
/// connection (which owns the tables and so bypasses RLS) BEFORE any app_user assertion —
/// once RLS is on, app_user's WITH CHECK would itself block the cross-brand seed rows.
/// Every test uses fresh Guids so the collection-shared container stays interference-free.
/// </summary>
[Collection("rbac-rls")]
public sealed class RlsIsolationTests
{
    private readonly RbacRlsFixture _fx;

    public RlsIsolationTests(RbacRlsFixture fx) => _fx = fx;

    // ---- seed helpers (run on the superuser connection) ---------------------

    private static async Task SeedStoreAsync(NpgsqlConnection su, Guid brandId, Guid storeId, string code)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenancy_org.stores (id, brand_id, code) VALUES (@id, @b, @c)", su);
        cmd.Parameters.AddWithValue("@id", storeId);
        cmd.Parameters.AddWithValue("@b", brandId);
        cmd.Parameters.AddWithValue("@c", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedUserAsync(NpgsqlConnection su)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identity_access.users (id, email) VALUES (@id, @e)", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@e", $"{id}@rls.test");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid> SeedRoleAsync(NpgsqlConnection su, Guid brandId)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identity_access.roles (id, brand_id, code, name, scope_type) " +
            "VALUES (@id, @b, @c, @n, 'brand')", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@b", brandId);
        cmd.Parameters.AddWithValue("@c", $"role_{id:N}");
        cmd.Parameters.AddWithValue("@n", "RLS test role");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task SeedMembershipAsync(
        NpgsqlConnection su, Guid userId, Guid scopeId, Guid roleId)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identity_access.user_scope_memberships " +
            "(user_id, scope_type, scope_id, role_id) VALUES (@u, 'brand', @s, @r)", su);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@s", scopeId);
        cmd.Parameters.AddWithValue("@r", roleId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql, params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ---- tests --------------------------------------------------------------

    [Fact]
    public async Task CrossBrand_stores_read_isolated()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid brandA = Guid.NewGuid(), brandB = Guid.NewGuid();
        Guid storeA = Guid.NewGuid(), storeB = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedStoreAsync(su, brandA, storeA, "A-1");
            await SeedStoreAsync(su, brandB, storeB, "B-1");

            // superuser (table owner) sees both.
            Assert.Equal(2L, await CountAsync(su,
                "SELECT count(*) FROM tenancy_org.stores WHERE brand_id = ANY(@ids)",
                ("@ids", new[] { brandA, brandB })));
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, brand: brandA);

        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM tenancy_org.stores WHERE brand_id = ANY(@ids)",
            ("@ids", new[] { brandA, brandB })));
        // and specifically only brand A's store is visible.
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM tenancy_org.stores WHERE id = @id", ("@id", storeA)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM tenancy_org.stores WHERE id = @id", ("@id", storeB)));
    }

    [Fact]
    public async Task CrossBrand_store_insert_withcheck_rejected()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid brandA = Guid.NewGuid(), brandB = Guid.NewGuid();

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, brand: brandA);

        // cross-brand insert must be blocked by the WITH CHECK clause -> 42501.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO tenancy_org.stores (brand_id, code) VALUES (@b, 'x')", app);
            cmd.Parameters.AddWithValue("@b", brandB);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", ex.SqlState);

        // same-brand insert succeeds.
        await using (var ok = new NpgsqlCommand(
            "INSERT INTO tenancy_org.stores (brand_id, code) VALUES (@b, 'x')", app))
        {
            ok.Parameters.AddWithValue("@b", brandA);
            Assert.Equal(1, await ok.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task HardenedBypass_true_sees_all_brands()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid brandA = Guid.NewGuid(), brandB = Guid.NewGuid();
        Guid storeA = Guid.NewGuid(), storeB = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedStoreAsync(su, brandA, storeA, "A-1");
            await SeedStoreAsync(su, brandB, storeB, "B-1");
        }

        await using var app = await _fx.OpenAppUserAsync();
        // regression lock: hardened kernel.rls_bypass() must accept the literal 'true'.
        await RbacRlsFixture.SetRlsAsync(app, bypass: true);

        Assert.Equal(2L, await CountAsync(app,
            "SELECT count(*) FROM tenancy_org.stores WHERE brand_id = ANY(@ids)",
            ("@ids", new[] { brandA, brandB })));
    }

    [Fact]
    public async Task UserSelf_membership_isolated()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid brandA = Guid.NewGuid();
        Guid u1, u2, role;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            u1 = await SeedUserAsync(su);
            u2 = await SeedUserAsync(su);
            role = await SeedRoleAsync(su, brandA);
            await SeedMembershipAsync(su, u1, brandA, role);
            await SeedMembershipAsync(su, u2, brandA, role);
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, user: u1);

        // only U1's membership is visible.
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM identity_access.user_scope_memberships WHERE user_id = ANY(@u)",
            ("@u", new[] { u1, u2 })));
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM identity_access.user_scope_memberships WHERE user_id = @u", ("@u", u1)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM identity_access.user_scope_memberships WHERE user_id = @u", ("@u", u2)));

        // inserting a membership owned by U2 (fresh scope so no unique collision) is blocked.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO identity_access.user_scope_memberships " +
                "(user_id, scope_type, scope_id, role_id) VALUES (@u, 'brand', @s, @r)", app);
            cmd.Parameters.AddWithValue("@u", u2);
            cmd.Parameters.AddWithValue("@s", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@r", role);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task AdminOnly_users_hidden_without_bypass()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid u1, u2;
        await using (var su = await _fx.OpenSuperuserAsync())
        {
            u1 = await SeedUserAsync(su);
            u2 = await SeedUserAsync(su);
        }

        await using var app = await _fx.OpenAppUserAsync();

        // no bypass -> admin-only table is fully opaque.
        await RbacRlsFixture.SetRlsAsync(app, bypass: false);
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM identity_access.users WHERE id = ANY(@u)", ("@u", new[] { u1, u2 })));

        // bypass -> the seeded users become visible.
        await RbacRlsFixture.SetRlsAsync(app, bypass: true);
        Assert.Equal(2L, await CountAsync(app,
            "SELECT count(*) FROM identity_access.users WHERE id = ANY(@u)", ("@u", new[] { u1, u2 })));
    }

    [Fact]
    public async Task Audit_row_withcheck_enforced()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid brandA = Guid.NewGuid(), brandB = Guid.NewGuid();

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, brand: brandA);

        // in-brand audit row is accepted.
        await using (var ok = new NpgsqlCommand(
            "INSERT INTO identity_access.audit_logs (brand_id, action, resource_type) " +
            "VALUES (@b, 'test.action', 'store')", app))
        {
            ok.Parameters.AddWithValue("@b", brandA);
            Assert.Equal(1, await ok.ExecuteNonQueryAsync());
        }

        // cross-brand audit row is rejected by WITH CHECK.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO identity_access.audit_logs (brand_id, action, resource_type) " +
                "VALUES (@b, 'test.action', 'store')", app);
            cmd.Parameters.AddWithValue("@b", brandB);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task AppUser_is_not_superuser_and_not_bypassrls()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        await using var su = await _fx.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'app_user'", su);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.False(r.GetBoolean(0)); // rolsuper
        Assert.False(r.GetBoolean(1)); // rolbypassrls
    }

    [Fact]
    public async Task SetRlsAsync_mirrors_interceptor_contract()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        var brand = Guid.NewGuid();

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, brand: brand, user: null, bypass: false);

        Assert.Equal(brand.ToString(),
            (string)(await RbacRlsFixture.ScalarAsync(app, "SELECT current_setting('app.current_brand_id')"))!);
        // null uuid is rendered as empty string, exactly like the interceptor.
        Assert.Equal("",
            (string)(await RbacRlsFixture.ScalarAsync(app, "SELECT current_setting('app.current_user_id')"))!);
        // bypass rendered as the literal 'false'.
        Assert.Equal("false",
            (string)(await RbacRlsFixture.ScalarAsync(app, "SELECT current_setting('app.bypass_rls')"))!);
    }
}
