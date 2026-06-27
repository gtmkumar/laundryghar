using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>
/// Phase-2 gate (slice 2C): module_bundle carries vertical_key, laundry-only modules are tagged,
/// and bundle expansion filters out modules not available to the brand's vertical (the semantics
/// ApplyBundleToBrand enforces). SQL tests need Docker.
/// </summary>
public sealed class Phase2BundleVerticalTests : IAsyncLifetime
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
    public void ModuleBundle_maps_vertical_key_column()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        using var ctx = new LaundryGharDbContext(options);
        var et = ctx.Model.FindEntityType(typeof(ModuleBundle))!;
        var prop = et.FindProperty(nameof(ModuleBundle.VerticalKey));
        Assert.NotNull(prop);
        Assert.Equal("vertical_key", prop!.GetColumnName());
    }

    // Post-2B module registry + a tier bundle mixing neutral and laundry-only modules.
    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS identity_access;
        CREATE TABLE identity_access.modules (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            key varchar(64) NOT NULL UNIQUE,
            label varchar(128) NOT NULL,
            vertical_key varchar(20),
            status varchar(32) NOT NULL DEFAULT 'active',
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE TABLE identity_access.module_bundle (
            code varchar PRIMARY KEY, name varchar NOT NULL, description text
        );
        CREATE TABLE identity_access.module_bundle_item (
            bundle_code varchar NOT NULL REFERENCES identity_access.module_bundle(code),
            module_key  varchar NOT NULL REFERENCES identity_access.modules(key),
            PRIMARY KEY (bundle_code, module_key)
        );
        INSERT INTO identity_access.modules (key, label, vertical_key) VALUES
            ('orders',    'Orders',    NULL),       -- neutral
            ('warehouse', 'Warehouse', NULL),       -- tagged laundry by the 2C patch
            ('fabrics',   'Fabrics',   'laundry');  -- already laundry (2B)
        INSERT INTO identity_access.module_bundle (code, name) VALUES ('pro', 'Pro');
        INSERT INTO identity_access.module_bundle_item (bundle_code, module_key) VALUES
            ('pro','orders'), ('pro','warehouse'), ('pro','fabrics');
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

    // Mirrors the ApplyBundleToBrand expansion filter at the data layer:
    // a bundle's modules available to a brand of @vertical (neutral OR matching vertical).
    private static async Task<List<string>> ExpandForVerticalAsync(NpgsqlConnection conn, string bundle, string vertical)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT m.key
            FROM identity_access.module_bundle_item bi
            JOIN identity_access.modules m ON m.key = bi.module_key
            WHERE bi.bundle_code = @b
              AND (m.vertical_key IS NULL OR m.vertical_key = @v)
            ORDER BY m.key;
            """, conn);
        cmd.Parameters.AddWithValue("b", bundle);
        cmd.Parameters.AddWithValue("v", vertical);
        var keys = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) keys.Add(r.GetString(0));
        return keys;
    }

    [Fact]
    public async Task SliceC_adds_bundle_vertical_and_tags_warehouse_laundry()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_c_bundle_vertical.sql")));

        // Tier bundle stays neutral; warehouse is now laundry-tagged.
        Assert.Null(await ScalarAsync(conn, "SELECT vertical_key FROM identity_access.module_bundle WHERE code='pro';"));
        Assert.Equal("laundry", (string?)await ScalarAsync(conn, "SELECT vertical_key FROM identity_access.modules WHERE key='warehouse';"));
    }

    [Fact]
    public async Task Bundle_expansion_excludes_laundry_modules_for_a_salon_brand()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_c_bundle_vertical.sql")));

        // Salon brand applying 'pro' gets only the neutral module — warehouse + fabrics excluded.
        Assert.Equal(new[] { "orders" }, await ExpandForVerticalAsync(conn, "pro", "salon"));

        // Laundry brand gets all three.
        Assert.Equal(new[] { "fabrics", "orders", "warehouse" }, await ExpandForVerticalAsync(conn, "pro", "laundry"));
    }
}
