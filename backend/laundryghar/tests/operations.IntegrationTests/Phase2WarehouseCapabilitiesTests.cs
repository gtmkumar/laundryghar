using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-2 gate (slice 2H): warehouse laundry-capability flags move into a
/// processing_capabilities jsonb (owned type), backfill-then-drop. SQL test needs Docker.</summary>
public sealed class Phase2WarehouseCapabilitiesTests : IAsyncLifetime
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
    public void Warehouse_maps_processing_capabilities_to_jsonb()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        using var ctx = new LaundryGharDbContext(options);
        var et = ctx.Model.FindEntityType(typeof(Warehouse))!;

        foreach (var p in new[] { "HasDryClean", "HasSteamIron", "HasShoeCleaning", "HasCarpetCleaning" })
            Assert.Null(et.FindProperty(p));

        var nav = et.FindNavigation(nameof(Warehouse.ProcessingCapabilities));
        Assert.NotNull(nav);
        Assert.True(nav!.TargetEntityType.IsMappedToJson());
        Assert.Equal("processing_capabilities", nav.TargetEntityType.GetContainerColumnName());
    }

    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS tenancy_org;
        CREATE TABLE tenancy_org.warehouses (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            has_dry_clean boolean NOT NULL DEFAULT false,
            has_steam_iron boolean NOT NULL DEFAULT false,
            has_shoe_cleaning boolean NOT NULL DEFAULT false,
            has_carpet_cleaning boolean NOT NULL DEFAULT false);
        INSERT INTO tenancy_org.warehouses (has_dry_clean, has_steam_iron, has_shoe_cleaning, has_carpet_cleaning)
        VALUES (true, false, true, false);
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
    public async Task SliceH_backfills_then_drops_capability_flags()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_h_warehouse_capabilities.sql")));

        Assert.Equal(true, await ScalarAsync(conn,
            "SELECT (processing_capabilities->>'has_dry_clean')::boolean FROM tenancy_org.warehouses LIMIT 1;"));
        Assert.Equal(false, await ScalarAsync(conn,
            "SELECT (processing_capabilities->>'has_steam_iron')::boolean FROM tenancy_org.warehouses LIMIT 1;"));
        Assert.Equal(true, await ScalarAsync(conn,
            "SELECT (processing_capabilities->>'has_shoe_cleaning')::boolean FROM tenancy_org.warehouses LIMIT 1;"));

        Assert.Equal(0L, (long)(await ScalarAsync(conn, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='tenancy_org' AND table_name='warehouses'
              AND column_name IN ('has_dry_clean','has_steam_iron','has_shoe_cleaning','has_carpet_cleaning');
            """))!);
    }
}
