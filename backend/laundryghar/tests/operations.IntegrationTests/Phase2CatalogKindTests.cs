using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>
/// Phase-2 gate (slice 2A): validate the catalog_kind/attributes seam — the EF model maps the new
/// columns, and the SQL patch adds them, backfills catalog_kind from the brand's vertical, and
/// enforces the kind vocabulary. SQL test requires Docker; skipped silently if unavailable.
/// </summary>
public sealed class Phase2CatalogKindTests : IAsyncLifetime
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

    // ── EF model assertions (no DB) ──────────────────────────────────────────────────────────

    private static LaundryGharDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        return new LaundryGharDbContext(options);
    }

    [Fact]
    public void Item_maps_catalog_kind_and_attributes_columns()
    {
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(typeof(Item))!;

        var kind = et.FindProperty(nameof(Item.CatalogKind));
        Assert.NotNull(kind);
        Assert.Equal("catalog_kind", kind!.GetColumnName());

        var attrs = et.FindProperty(nameof(Item.Attributes));
        Assert.NotNull(attrs);
        Assert.Equal("attributes", attrs!.GetColumnName());
        Assert.Equal("jsonb", attrs.GetColumnType());
    }

    // ── SQL patch validation (Docker) ────────────────────────────────────────────────────────

    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS tenancy_org;
        CREATE SCHEMA IF NOT EXISTS customer_catalog;
        CREATE TABLE tenancy_org.brands (
            id uuid PRIMARY KEY,
            vertical_key varchar(20) NOT NULL DEFAULT 'laundry'
        );
        CREATE TABLE customer_catalog.items (
            id uuid PRIMARY KEY,
            brand_id uuid NOT NULL,
            code varchar(50) NOT NULL,
            deleted_at timestamptz
        );
        INSERT INTO tenancy_org.brands (id, vertical_key) VALUES
            ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'laundry'),
            ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'salon');
        INSERT INTO customer_catalog.items (id, brand_id, code) VALUES
            ('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'SHIRT'),
            ('22222222-2222-2222-2222-222222222222', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'TROUSER'),
            ('33333333-3333-3333-3333-333333333333', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'HAIRCUT');
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
        return await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task SliceA_patch_adds_columns_and_backfills_from_brand_vertical()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_a_catalog_kind.sql")));

        // Laundry-brand items → laundry_garment.
        Assert.Equal("laundry_garment", (string?)await ScalarAsync(conn,
            "SELECT catalog_kind FROM customer_catalog.items WHERE id='11111111-1111-1111-1111-111111111111';"));
        // Salon-brand item → service (backfilled from brand.vertical_key).
        Assert.Equal("service", (string?)await ScalarAsync(conn,
            "SELECT catalog_kind FROM customer_catalog.items WHERE id='33333333-3333-3333-3333-333333333333';"));
        // attributes defaults to an empty jsonb object.
        Assert.Equal("{}", (string?)await ScalarAsync(conn,
            "SELECT attributes::text FROM customer_catalog.items WHERE id='22222222-2222-2222-2222-222222222222';"));
    }

    [Fact]
    public async Task SliceA_check_rejects_unknown_catalog_kind()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_a_catalog_kind.sql")));

        await Assert.ThrowsAsync<PostgresException>(async () => await ExecAsync(conn,
            "UPDATE customer_catalog.items SET catalog_kind='bogus' WHERE id='11111111-1111-1111-1111-111111111111';"));
    }
}
