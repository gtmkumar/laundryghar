using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-2 gate (slice 2J): the notification event→template mapping is a seeded, vertical-
/// tagged catalog (data-driven), and the Phase-1-deferred GARMENT_LOST mapping for fulfillment.lost
/// is cataloged as laundry. Needs Docker.</summary>
public sealed class Phase2NotificationCatalogTests : IAsyncLifetime
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
    public async Task SliceJ_seeds_event_catalog_with_laundry_garment_lost()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_j_notification_event_catalog.sql")));

        // Neutral order event maps to its template (vertical-agnostic).
        Assert.Equal("ORDER_READY", (string?)await ScalarAsync(conn, """
            SELECT template_code FROM engagement_cms.notification_event_catalog
            WHERE event_type='order.status_changed' AND status_match='ready';
            """));

        // The Phase-1-deferred GARMENT_LOST mapping is cataloged + tagged laundry.
        Assert.Equal("GARMENT_LOST", (string?)await ScalarAsync(conn,
            "SELECT template_code FROM engagement_cms.notification_event_catalog WHERE event_type='fulfillment.lost';"));
        Assert.Equal("laundry", (string?)await ScalarAsync(conn,
            "SELECT vertical_key FROM engagement_cms.notification_event_catalog WHERE event_type='fulfillment.lost';"));

        // A data-driven resolve (what the service does) returns one row per event/status.
        Assert.Equal("ORDER_CANCELLED", (string?)await ScalarAsync(conn, """
            SELECT template_code FROM engagement_cms.notification_event_catalog
            WHERE event_type='order.cancelled' AND status_match='*';
            """));
    }
}
