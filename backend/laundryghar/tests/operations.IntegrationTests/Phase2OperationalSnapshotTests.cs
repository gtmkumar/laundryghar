using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-2 gate (slice 2G): the fulfilment-leg shift counters move into an
/// operational_snapshot jsonb (owned type), backfill-then-drop (Risk #5). SQL test needs Docker.</summary>
public sealed class Phase2OperationalSnapshotTests : IAsyncLifetime
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

    [Fact]
    public void ShiftHandover_maps_operational_to_jsonb()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        using var ctx = new LaundryGharDbContext(options);
        var et = ctx.Model.FindEntityType(typeof(ShiftHandover))!;

        Assert.Null(et.FindProperty("PickupsRemaining"));
        Assert.Null(et.FindProperty("DeliveriesRemaining"));

        var nav = et.FindNavigation(nameof(ShiftHandover.Operational));
        Assert.NotNull(nav);
        Assert.True(nav!.TargetEntityType.IsMappedToJson());
        Assert.Equal("operational_snapshot", nav.TargetEntityType.GetContainerColumnName());
    }

    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS finance_royalty;
        CREATE TABLE finance_royalty.shift_handovers (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            pickups_remaining int NOT NULL DEFAULT 0,
            deliveries_remaining int NOT NULL DEFAULT 0);
        INSERT INTO finance_royalty.shift_handovers (pickups_remaining, deliveries_remaining)
        VALUES (4, 7);
        """;

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
    public async Task SliceG_backfills_then_drops_leg_counters()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_g_operational_snapshot.sql")));

        Assert.Equal(4, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT (operational_snapshot->>'pickups_remaining')::int FROM finance_royalty.shift_handovers LIMIT 1;")));
        Assert.Equal(7, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT (operational_snapshot->>'deliveries_remaining')::int FROM finance_royalty.shift_handovers LIMIT 1;")));

        Assert.Equal(0L, (long)(await ScalarAsync(conn, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='finance_royalty' AND table_name='shift_handovers'
              AND column_name IN ('pickups_remaining','deliveries_remaining');
            """))!);
    }
}
