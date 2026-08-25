using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// The database half of §11 P4 (migration 0016): key resolution, entitlement, and metering.
///
/// <para>The property that carries the most weight here is entitlement travelling WITH the key.
/// <c>api_access</c> is a §5 sellable feature, so a brand that stops paying for the API must have
/// keys that stop working — and answering that in the resolution query rather than at each call site
/// is what makes it impossible to forget in one.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class ApiKeyStoreTests
{
    private readonly RbacRlsFixture _fx;
    public ApiKeyStoreTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── resolution is by the PUBLIC prefix, and returns the hash for the caller to verify. The
    //      function never compares secrets, so it cannot be turned into a guessing oracle.
    [Fact]
    public async Task A_key_resolves_by_its_public_prefix()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, prefix) = await SeedKeyAsync(conn, entitled: true, scopes: "'orders.read'");

        var row = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status || '/' || array_to_string(scopes, ',') || '/' || entitled FROM kernel.resolve_api_key('{prefix}')");

        Assert.Equal("active/orders.read/true", row);
    }

    // 2 ── THE ONE THAT MATTERS. Entitlement is answered by the same query that resolves the key.
    [Fact]
    public async Task A_brand_without_api_access_resolves_as_not_entitled()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, prefix) = await SeedKeyAsync(conn, entitled: false);

        Assert.Equal(false, await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT entitled FROM kernel.resolve_api_key('{prefix}')"));

        // …and buying it turns the same key on, with no re-issue.
        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.brand_feature (brand_id, feature_key, enabled)
            VALUES ('{brand}', 'api_access', true)
            """);

        Assert.Equal(true, await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT entitled FROM kernel.resolve_api_key('{prefix}')"));
    }

    // 3 ── an entitlement that has run out is not an entitlement.
    [Fact]
    public async Task An_expired_entitlement_stops_counting()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, prefix) = await SeedKeyAsync(conn, entitled: true);

        await RbacRlsFixture.ExecAsync(conn, $"""
            UPDATE identity_access.brand_feature SET valid_until = current_date - 1
             WHERE brand_id = '{brand}' AND feature_key = 'api_access'
            """);

        Assert.Equal(false, await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT entitled FROM kernel.resolve_api_key('{prefix}')"));
    }

    // 4 ── an unknown prefix returns nothing at all — no row to inspect, nothing to distinguish a
    //      near-miss from a wild guess.
    [Fact]
    public async Task An_unknown_prefix_resolves_to_nothing()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        Assert.Equal(0L, Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
            "SELECT count(*) FROM kernel.resolve_api_key('lg_live_deadbeef')")));
    }

    // 5 ── the prefix is unique, or authentication resolves the wrong key. The migration asserts the
    //      index exists; this asserts it actually bites.
    [Fact]
    public async Task Two_keys_cannot_share_a_prefix()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, prefix) = await SeedKeyAsync(conn, entitled: true);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.api_keys (brand_id, name, key_prefix, secret_hash)
            VALUES ('{brand}', 'collision', '{prefix}', 'x')
            """));

        Assert.Equal("23505", ex.SqlState);
    }

    // 6 ── metering is a daily rollup, upserted. A row per request would make the busiest customer
    //      the one whose usage table is unqueryable.
    [Fact]
    public async Task Usage_accumulates_into_one_row_per_day()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, prefix) = await SeedKeyAsync(conn, entitled: true);

        var keyId = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT id FROM identity_access.api_keys WHERE key_prefix = '{prefix}'");

        for (var i = 0; i < 5; i++)
            await RbacRlsFixture.ExecAsync(conn,
                $"SELECT kernel.record_api_key_use('{keyId}', '{brand}', {(i == 4 ? "true" : "false")})");

        var row = await RbacRlsFixture.ScalarAsync(conn, $"""
            SELECT count(*)::text || '/' || max(request_count)::text || '/' || max(error_count)::text
            FROM identity_access.api_key_usage WHERE api_key_id = '{keyId}'
            """);

        Assert.Equal("1/5/1", row);
    }

    // 7 ── a revoked key still resolves — the handler is what refuses it. Keeping the row is
    //      deliberate: its usage history is what gets read after a leak.
    [Fact]
    public async Task A_revoked_key_keeps_its_row_and_its_history()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (_, prefix) = await SeedKeyAsync(conn, entitled: true);

        await RbacRlsFixture.ExecAsync(conn, $"""
            UPDATE identity_access.api_keys SET status = 'revoked', revoked_at = now()
             WHERE key_prefix = '{prefix}'
            """);

        Assert.Equal("revoked", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status FROM kernel.resolve_api_key('{prefix}')"));
    }

    // 8 ── a revoked key must carry the time it was revoked, or "when did this stop working" has no
    //      answer during an incident.
    [Fact]
    public async Task A_revoked_key_must_record_when()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedKeyAsync(conn, entitled: true);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.api_keys (brand_id, name, key_prefix, secret_hash, status)
            VALUES ('{brand}', 'no timestamp', 'lg_live_nowhen1', 'x', 'revoked')
            """));

        Assert.Equal("23514", ex.SqlState);
    }

    private static async Task<(Guid Brand, string Prefix)> SeedKeyAsync(
        NpgsqlConnection conn, bool entitled, string scopes = "'orders.read'")
    {
        var brand = Guid.NewGuid();
        var handle = brand.ToString("N")[..8];
        var prefix = $"lg_live_{handle}";

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brands (id, name) VALUES ('{brand}', 'API key test');
            INSERT INTO identity_access.api_keys (brand_id, name, key_prefix, secret_hash, scopes)
            VALUES ('{brand}', 'test key', '{prefix}', '$argon2id$fake', ARRAY[{scopes}]);
            """);

        if (entitled)
            await RbacRlsFixture.ExecAsync(conn, $"""
                INSERT INTO identity_access.brand_feature (brand_id, feature_key, enabled)
                VALUES ('{brand}', 'api_access', true)
                """);

        return (brand, prefix);
    }
}
