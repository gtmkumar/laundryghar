using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>
/// Phase-2 gate (slice 2B): the module registry carries vertical_key and the patch carves a
/// laundry-keyed `fabrics` module out of the generic catalog module. SQL test needs Docker.
/// </summary>
public sealed class Phase2FabricModuleTests : IAsyncLifetime
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
    public void AppModule_maps_vertical_key_column()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        using var ctx = new LaundryGharDbContext(options);
        var et = ctx.Model.FindEntityType(typeof(AppModule))!;
        var prop = et.FindProperty(nameof(AppModule.VerticalKey));
        Assert.NotNull(prop);
        Assert.Equal("vertical_key", prop!.GetColumnName());
    }

    // Minimal post-seed module registry fixture (matches identity_access.modules shape).
    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS identity_access;
        CREATE TABLE identity_access.modules (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            key varchar(64) NOT NULL UNIQUE,
            label varchar(128) NOT NULL,
            icon varchar(64),
            route varchar(160),
            section varchar(64),
            nav_order int NOT NULL DEFAULT 100,
            matrix_order int NOT NULL DEFAULT 100,
            show_in_nav boolean NOT NULL DEFAULT false,
            show_in_matrix boolean NOT NULL DEFAULT true,
            required_permission varchar(128),
            permission_modules text[] NOT NULL DEFAULT '{}',
            status varchar(32) NOT NULL DEFAULT 'active',
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        INSERT INTO identity_access.modules (key, label, required_permission, permission_modules)
        VALUES ('pricing', 'Pricing', 'pricing.read', '{pricing,catalog}');
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
    public async Task SliceB_adds_vertical_key_and_laundry_fabrics_module()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_b_fabric_module.sql")));

        // The fabrics module exists, tagged laundry, gating the fabric permission.
        Assert.Equal("laundry", (string?)await ScalarAsync(conn,
            "SELECT vertical_key FROM identity_access.modules WHERE key='fabrics';"));
        Assert.Equal("catalog.fabric.manage", (string?)await ScalarAsync(conn,
            "SELECT required_permission FROM identity_access.modules WHERE key='fabrics';"));
        Assert.True((bool)(await ScalarAsync(conn,
            "SELECT show_in_nav FROM identity_access.modules WHERE key='fabrics';"))!);

        // The pre-existing generic module stays vertical-neutral (visible to every brand).
        Assert.Null(await ScalarAsync(conn,
            "SELECT vertical_key FROM identity_access.modules WHERE key='pricing';"));

        // The vocabulary CHECK rejects an unknown vertical.
        await Assert.ThrowsAsync<PostgresException>(async () => await ExecAsync(conn,
            "UPDATE identity_access.modules SET vertical_key='bogus' WHERE key='fabrics';"));
    }

    [Fact]
    public async Task SliceB_is_idempotent()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        var patch = await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_b_fabric_module.sql"));
        await ExecAsync(conn, patch);
        await ExecAsync(conn, patch); // second apply must be a no-op, not an error

        Assert.Equal(1, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT count(*) FROM identity_access.modules WHERE key='fabrics';")));
    }
}
