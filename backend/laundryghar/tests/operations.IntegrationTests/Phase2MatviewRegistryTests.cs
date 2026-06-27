using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-2 gate (slice 2I): the analytics matview registry is seeded with the shared set,
/// rider-perf is gated by fulfilment mode, and the refresh function iterates the registry (proven by
/// refreshing a real registered matview). Needs Docker.</summary>
public sealed class Phase2MatviewRegistryTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        try { await _pg.StartAsync(); _connString = _pg.GetConnectionString(); }
        catch (Exception) { _dockerAvailable = false; }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    [Fact]
    public async Task SliceI_seeds_registry_and_refresh_iterates_it()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_i_matview_registry.sql")));

        // All 7 shared matviews are registered.
        Assert.Equal(7L, (long)(await ScalarAsync(conn,
            "SELECT count(*) FROM analytics.matview_registry WHERE status='active';"))!);

        // warehouse-throughput is laundry-only; rider-performance is mode-gated.
        Assert.Equal("laundry", (string?)await ScalarAsync(conn,
            "SELECT vertical_key FROM analytics.matview_registry WHERE matview_name='mv_warehouse_throughput';"));
        Assert.Contains("point_to_point", (string)(await ScalarAsync(conn,
            "SELECT fulfillment_modes FROM analytics.matview_registry WHERE matview_name='mv_rider_performance';"))!);

        // Prove the function REALLY iterates the registry: register a live matview and refresh.
        await ExecAsync(conn, """
            CREATE TABLE analytics.src (id int);
            INSERT INTO analytics.src VALUES (1),(2);
            CREATE MATERIALIZED VIEW analytics.mv_demo AS SELECT count(*) AS n FROM analytics.src;
            INSERT INTO analytics.matview_registry (matview_name, refresh_order, refresh_concurrently)
            VALUES ('mv_demo', 5, false);
            INSERT INTO analytics.src VALUES (3);
            """);
        await ExecAsync(conn, "SELECT analytics.refresh_all_matviews();");

        // The registry-driven refresh picked up mv_demo and rebuilt it (now counts 3 rows).
        Assert.Equal(3, Convert.ToInt32(await ScalarAsync(conn, "SELECT n FROM analytics.mv_demo;")));
    }
}
