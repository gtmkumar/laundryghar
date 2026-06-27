using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-4 gate: salon becomes a fully entitleable vertical — a salon-tagged module +
/// bundle and a service_minutes quota unit — exercising the Phase-2 vertical seams. Needs Docker.</summary>
public sealed class Phase4SalonPackTests : IAsyncLifetime
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

    // Post-2B/2C/2E shape: modules + bundle tables with vertical_key, subscription_plans with quota CHECK.
    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS identity_access;
        CREATE SCHEMA IF NOT EXISTS commerce;
        CREATE TABLE identity_access.modules (
            key varchar(64) PRIMARY KEY, label varchar(128) NOT NULL, icon varchar(64), route varchar(160),
            section varchar(64), nav_order int NOT NULL DEFAULT 100, matrix_order int NOT NULL DEFAULT 100,
            show_in_nav boolean NOT NULL DEFAULT false, show_in_matrix boolean NOT NULL DEFAULT true,
            required_permission varchar(128), permission_modules text[] NOT NULL DEFAULT '{}',
            vertical_key varchar(20), status varchar(20) NOT NULL DEFAULT 'active',
            created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now());
        CREATE TABLE identity_access.module_bundle (
            code varchar PRIMARY KEY, name varchar NOT NULL, description text, vertical_key varchar(20));
        CREATE TABLE identity_access.module_bundle_item (
            bundle_code varchar NOT NULL REFERENCES identity_access.module_bundle(code),
            module_key varchar NOT NULL REFERENCES identity_access.modules(key),
            PRIMARY KEY (bundle_code, module_key));
        INSERT INTO identity_access.modules (key, label) VALUES
            ('orders','Orders'),('customers','Customers'),('pricing','Pricing'),('pos','POS');
        CREATE TABLE commerce.subscription_plans (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), code varchar(50) NOT NULL,
            quota_type varchar(20) NOT NULL DEFAULT 'credit'
                CHECK (quota_type IN ('credit','order_count','job_count','weight_kg','unlimited')));
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
    public async Task Salon_pack_registers_module_bundle_and_service_minutes_quota()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase4_salon_pack.sql")));

        Assert.Equal("salon", (string?)await ScalarAsync(conn,
            "SELECT vertical_key FROM identity_access.modules WHERE key='appointments';"));
        Assert.Equal("salon", (string?)await ScalarAsync(conn,
            "SELECT vertical_key FROM identity_access.module_bundle WHERE code='salon-starter';"));

        // The salon bundle contains the salon-only module plus the neutral shared ones.
        Assert.Equal(5L, (long)(await ScalarAsync(conn,
            "SELECT count(*) FROM identity_access.module_bundle_item WHERE bundle_code='salon-starter';"))!);
        Assert.Equal(1L, (long)(await ScalarAsync(conn,
            "SELECT count(*) FROM identity_access.module_bundle_item WHERE bundle_code='salon-starter' AND module_key='appointments';"))!);

        // service_minutes quota is now accepted on a plan.
        await ExecAsync(conn,
            "INSERT INTO commerce.subscription_plans (code, quota_type) VALUES ('SALON_GOLD','service_minutes');");
    }
}
