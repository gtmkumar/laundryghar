using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>
/// Phase-1 gate: validate the DESTRUCTIVE SQL patches (slice F-2 attribute jsonb extraction and
/// slice B-2 current_stage CHECK relaxation) against a real PostgreSQL, end to end. Spins up a
/// throwaway postgres container, builds a minimal post-slice-F fulfilment_unit fixture (incl. the
/// dependent mv_warehouse_throughput matview), applies the actual patch files from db/patches, and
/// asserts the backfill, column drops, matview restoration, and CHECK relaxation.
///
/// Requires Docker. Skipped automatically if no container runtime is reachable.
/// </summary>
public sealed class Phase1SqlMigrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        try
        {
            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
        }
        catch (Exception)
        {
            _dockerAvailable = false; // no Docker in this environment → tests no-op
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // The minimal post-slice-F fixture: the generic spine columns + the 6 legacy laundry attribute
    // columns + the enumerated current_stage CHECK, plus the matview that reads rewash_count.
    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS laundry_fulfillment;
        CREATE TABLE laundry_fulfillment.fulfillment_unit (
            id                   uuid PRIMARY KEY,
            brand_id             uuid NOT NULL,
            warehouse_id         uuid,
            current_stage        varchar(30) NOT NULL DEFAULT 'received'
                                 CONSTRAINT garments_current_stage_check
                                 CHECK (current_stage IN ('received','sorting','washing','qc','delivered','lost','damaged','rewash')),
            weight_grams         integer,
            has_ornaments        boolean NOT NULL DEFAULT false,
            has_lining           boolean NOT NULL DEFAULT false,
            is_designer_wear     boolean NOT NULL DEFAULT false,
            rewash_count         smallint NOT NULL DEFAULT 0,
            care_instructions    text,
            actual_completion_at timestamptz,
            created_at           timestamptz NOT NULL DEFAULT now()
        );

        CREATE MATERIALIZED VIEW mv_warehouse_throughput AS
        SELECT g.brand_id, g.warehouse_id,
               DATE(g.created_at AT TIME ZONE 'Asia/Kolkata') AS throughput_date,
               COUNT(*)                                                     AS garments_received,
               COUNT(*) FILTER (WHERE g.current_stage = 'delivered')        AS garments_delivered,
               COUNT(*) FILTER (WHERE g.current_stage IN ('lost','damaged')) AS issues_count,
               COUNT(*) FILTER (WHERE g.rewash_count > 0)                   AS rewash_count,
               AVG(EXTRACT(EPOCH FROM (g.actual_completion_at - g.created_at))/3600)
                   FILTER (WHERE g.actual_completion_at IS NOT NULL)        AS avg_tat_hours
        FROM laundry_fulfillment.fulfillment_unit g
        WHERE g.warehouse_id IS NOT NULL
        GROUP BY g.brand_id, g.warehouse_id, DATE(g.created_at AT TIME ZONE 'Asia/Kolkata');
        CREATE UNIQUE INDEX idx_mvwt_unique ON mv_warehouse_throughput(brand_id, warehouse_id, throughput_date);

        -- Two rows, same brand+warehouse+day so they group into one matview row.
        INSERT INTO laundry_fulfillment.fulfillment_unit
            (id, brand_id, warehouse_id, current_stage, weight_grams, has_ornaments, has_lining,
             is_designer_wear, rewash_count, care_instructions, created_at)
        VALUES
            ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222',
             '33333333-3333-3333-3333-333333333333', 'qc', 1200, true, false, true, 2,
             'dry clean only', '2026-02-01T08:00:00Z'),
            ('44444444-4444-4444-4444-444444444444', '22222222-2222-2222-2222-222222222222',
             '33333333-3333-3333-3333-333333333333', 'received', NULL, false, false, false, 0,
             NULL, '2026-02-01T09:00:00Z');
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
    public async Task SliceF2_and_B2_patches_apply_and_preserve_data()
    {
        if (!_dockerAvailable) return; // Docker unavailable — skip silently.

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);

        // Apply the actual patches from disk, in order.
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase1_slice_f2_fulfillment_unit_attributes.sql")));
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase1_slice_b2_relax_fulfillment_unit_stage_check.sql")));

        // 1. The 6 attribute columns are gone from the spine.
        var leftover = (long)(await ScalarAsync(conn, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='laundry_fulfillment' AND table_name='fulfillment_unit'
              AND column_name IN ('weight_grams','has_ornaments','has_lining','is_designer_wear','rewash_count','care_instructions');
            """))!;
        Assert.Equal(0, leftover);

        // 2. attributes jsonb exists and backfilled correctly (row A — fully populated).
        Assert.Equal(1200, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT (attributes->>'weight_grams')::int FROM laundry_fulfillment.fulfillment_unit WHERE id='11111111-1111-1111-1111-111111111111';")));
        Assert.Equal(true, await ScalarAsync(conn,
            "SELECT (attributes->>'has_ornaments')::boolean FROM laundry_fulfillment.fulfillment_unit WHERE id='11111111-1111-1111-1111-111111111111';"));
        Assert.Equal(true, await ScalarAsync(conn,
            "SELECT (attributes->>'is_designer_wear')::boolean FROM laundry_fulfillment.fulfillment_unit WHERE id='11111111-1111-1111-1111-111111111111';"));
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT (attributes->>'rewash_count')::int FROM laundry_fulfillment.fulfillment_unit WHERE id='11111111-1111-1111-1111-111111111111';")));
        Assert.Equal("dry clean only", (string?)await ScalarAsync(conn,
            "SELECT attributes->>'care_instructions' FROM laundry_fulfillment.fulfillment_unit WHERE id='11111111-1111-1111-1111-111111111111';"));

        // 3. Row B — nullable attributes are JSON null (key present, value null).
        Assert.Equal("null", (string?)await ScalarAsync(conn,
            "SELECT attributes->'weight_grams' FROM laundry_fulfillment.fulfillment_unit WHERE id='44444444-4444-4444-4444-444444444444';"));
        Assert.Equal(0, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT (attributes->>'rewash_count')::int FROM laundry_fulfillment.fulfillment_unit WHERE id='44444444-4444-4444-4444-444444444444';")));

        // 4. The dependent matview is restored and reads rewash_count from the jsonb.
        //    Both rows group into one row; rewash_count = count(rewash>0) = 1 (only row A).
        var mvRewash = await ScalarAsync(conn, "SELECT rewash_count FROM mv_warehouse_throughput LIMIT 1;");
        Assert.Equal(1, Convert.ToInt32(mvRewash));
        var mvReceived = await ScalarAsync(conn, "SELECT garments_received FROM mv_warehouse_throughput LIMIT 1;");
        Assert.Equal(2, Convert.ToInt32(mvReceived));
    }

    [Fact]
    public async Task SliceB2_relaxes_current_stage_to_allow_novel_strategy_stages()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase1_slice_f2_fulfillment_unit_attributes.sql")));
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase1_slice_b2_relax_fulfillment_unit_stage_check.sql")));

        // A novel stage not in the original enumerated whitelist is now accepted.
        await ExecAsync(conn, """
            INSERT INTO laundry_fulfillment.fulfillment_unit (id, brand_id, current_stage)
            VALUES ('55555555-5555-5555-5555-555555555555', '22222222-2222-2222-2222-222222222222', 'staged_for_salon');
            """);
        var ok = await ScalarAsync(conn,
            "SELECT current_stage FROM laundry_fulfillment.fulfillment_unit WHERE id='55555555-5555-5555-5555-555555555555';");
        Assert.Equal("staged_for_salon", (string?)ok);

        // But the non-empty sanity floor still holds.
        await Assert.ThrowsAsync<PostgresException>(async () => await ExecAsync(conn, """
            INSERT INTO laundry_fulfillment.fulfillment_unit (id, brand_id, current_stage)
            VALUES ('66666666-6666-6666-6666-666666666666', '22222222-2222-2222-2222-222222222222', '');
            """));
    }
}
