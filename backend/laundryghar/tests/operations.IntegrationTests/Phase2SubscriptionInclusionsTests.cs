using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>Phase-2 gate (slice 2E): the EF model maps the fulfilment-leg inclusions into a
/// fulfillment_inclusions jsonb (owned type), and the patch widens quota_type + moves the 3 flag
/// columns into the jsonb. SQL test needs Docker.</summary>
public sealed class Phase2SubscriptionInclusionsTests : IAsyncLifetime
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
    public void Plan_maps_inclusions_to_fulfillment_inclusions_jsonb()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        using var ctx = new LaundryGharDbContext(options);
        var et = ctx.Model.FindEntityType(typeof(SubscriptionPlan))!;

        // The 3 flags are no longer scalar columns on the spine.
        foreach (var p in new[] { "PickupIncluded", "DeliveryIncluded", "ExpressIncluded" })
            Assert.Null(et.FindProperty(p));

        var nav = et.FindNavigation(nameof(SubscriptionPlan.Inclusions));
        Assert.NotNull(nav);
        Assert.True(nav!.TargetEntityType.IsMappedToJson());
        Assert.Equal("fulfillment_inclusions", nav.TargetEntityType.GetContainerColumnName());
    }

    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS commerce;
        CREATE TABLE commerce.subscription_plans (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            code varchar(50) NOT NULL,
            quota_type varchar(20) NOT NULL DEFAULT 'credit'
                CHECK (quota_type IN ('credit','order_count','weight_kg','unlimited')),
            pickup_included boolean NOT NULL DEFAULT true,
            delivery_included boolean NOT NULL DEFAULT true,
            express_included boolean NOT NULL DEFAULT false
        );
        INSERT INTO commerce.subscription_plans (id, code, quota_type, pickup_included, delivery_included, express_included)
        VALUES ('11111111-1111-1111-1111-111111111111', 'GOLD', 'order_count', true, false, true);
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
    public async Task SliceE_widens_quota_and_moves_flags_to_jsonb()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_e_subscription_inclusions.sql")));

        // Flags backfilled into the jsonb with correct values.
        Assert.Equal(true, await ScalarAsync(conn,
            "SELECT (fulfillment_inclusions->>'pickup_included')::boolean FROM commerce.subscription_plans WHERE code='GOLD';"));
        Assert.Equal(false, await ScalarAsync(conn,
            "SELECT (fulfillment_inclusions->>'delivery_included')::boolean FROM commerce.subscription_plans WHERE code='GOLD';"));
        Assert.Equal(true, await ScalarAsync(conn,
            "SELECT (fulfillment_inclusions->>'express_included')::boolean FROM commerce.subscription_plans WHERE code='GOLD';"));

        // The 3 flag columns are gone.
        Assert.Equal(0L, (long)(await ScalarAsync(conn, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='commerce' AND table_name='subscription_plans'
              AND column_name IN ('pickup_included','delivery_included','express_included');
            """))!);

        // The neutral job_count quota unit is now accepted.
        await ExecAsync(conn,
            "UPDATE commerce.subscription_plans SET quota_type='job_count' WHERE code='GOLD';");
    }
}
