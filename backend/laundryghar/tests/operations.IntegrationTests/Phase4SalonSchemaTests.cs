using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-4 gate: the salon vertical ships a NEW private salon_fulfillment schema (4 tables
/// + brand RLS) additively, with no change to the shared spine. Needs Docker.</summary>
public sealed class Phase4SalonSchemaTests : IAsyncLifetime
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
    public async Task Salon_schema_creates_four_tables_with_rls_and_accepts_an_appointment()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase4_salon_fulfillment_schema.sql")));

        // 4 tables + 4 RLS policies in the new private schema.
        Assert.Equal(4L, (long)(await ScalarAsync(conn, """
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='salon_fulfillment' AND c.relkind='r'
              AND c.relname IN ('staff_members','resources','appointments','resource_bookings');
            """))!);
        Assert.Equal(4L, (long)(await ScalarAsync(conn,
            "SELECT count(*) FROM pg_policies WHERE schemaname='salon_fulfillment';"))!);

        // An appointment links to the shared order spine via the same composite key shape, and
        // accepts the strategy-owned 'booked' status + a salon-private attributes jsonb.
        await ExecAsync(conn, """
            INSERT INTO salon_fulfillment.appointments
                (brand_id, franchise_id, store_id, order_id, order_created_at, customer_id,
                 appointment_status, scheduled_start, scheduled_end, attributes)
            VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
                    now(), gen_random_uuid(), 'booked', now(), now() + interval '45 min',
                    '{"duration_minutes": 45, "staff_tier": "senior"}'::jsonb);
            """);
        Assert.Equal(45, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT (attributes->>'duration_minutes')::int FROM salon_fulfillment.appointments LIMIT 1;")));
    }
}
