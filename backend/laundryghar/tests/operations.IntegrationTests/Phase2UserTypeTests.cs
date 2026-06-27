using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-2 gate (slice 2D): the user_type CHECK is widened to allow the neutral
/// ops_staff while still accepting warehouse_staff. Needs Docker.</summary>
public sealed class Phase2UserTypeTests : IAsyncLifetime
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

    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS identity_access;
        CREATE TABLE identity_access.users (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            user_type varchar(30) NOT NULL DEFAULT 'staff'
                CHECK (user_type IN ('platform_admin','brand_admin','franchise_owner',
                                     'store_admin','staff','warehouse_staff','rider','auditor','support'))
        );
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

    [Fact]
    public async Task SliceD_widens_user_type_to_allow_ops_staff()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);

        // Before the patch, ops_staff is rejected by the original CHECK.
        await Assert.ThrowsAsync<PostgresException>(async () => await ExecAsync(conn,
            "INSERT INTO identity_access.users (user_type) VALUES ('ops_staff');"));

        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_d_user_type_neutral.sql")));

        // After the patch, both the neutral and the legacy laundry type are accepted.
        await ExecAsync(conn, "INSERT INTO identity_access.users (user_type) VALUES ('ops_staff');");
        await ExecAsync(conn, "INSERT INTO identity_access.users (user_type) VALUES ('warehouse_staff');");

        // An unknown type is still rejected.
        await Assert.ThrowsAsync<PostgresException>(async () => await ExecAsync(conn,
            "INSERT INTO identity_access.users (user_type) VALUES ('stylist');"));
    }
}
