using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Migration <c>0031_subbrand_scope_rls</c> — audit finding A-6.
///
/// <para><b>What was wrong.</b> The franchise, store and warehouse GUCs are stamped on every
/// connection and read by zero RLS policies. Measured against the running system before the fix,
/// four principals at four different scope levels asked the orders API for the same brand and all
/// four received the same six orders — so the boundary was absent from the database (the finding)
/// and absent from the list path too, because the ~95 <c>IsWithinScope</c> call sites are mutating
/// handlers that a read never reaches.</para>
///
/// <para><b>The one deliberate difference from IsWithinScope.</b> A row whose column for the
/// caller's scope level is NULL does not constrain that caller. <c>IsWithinScope</c> denies there;
/// it can, because it is only ever handed a resource that has those ids. A policy sees every row of
/// every table, and measured on live data the strict rule would have hidden 977/977 audit_logs,
/// 16/16 system_settings and 4/4 price_lists from every franchise- and store-scoped user, plus
/// every order from warehouse staff (orders.warehouse_id is NULL on all rows and
/// store_warehouse_mappings is empty). The tests below pin both halves: the arm keeps unscoped rows
/// visible, and a row naming a DIFFERENT franchise or store is still refused.</para>
///
/// <para>The migration is applied <b>verbatim from disk</b> onto tables created with the real
/// schema-qualified names it enumerates, and every query runs as the non-owner <c>app_user</c> —
/// RLS does not apply to a table's owner, so a test that forgets that proves nothing.</para>
///
/// <para>Requires Docker; self-skips when no container runtime is reachable.</para>
/// </summary>
public sealed class SubBrandScopeRlsTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    private static readonly Guid Brand      = Guid.NewGuid();
    private static readonly Guid OtherBrand = Guid.NewGuid();
    private static readonly Guid FranchiseA = Guid.NewGuid();
    private static readonly Guid FranchiseB = Guid.NewGuid();
    private static readonly Guid StoreA     = Guid.NewGuid();
    private static readonly Guid StoreB     = Guid.NewGuid();
    private static readonly Guid WarehouseA = Guid.NewGuid();

    // orders
    private static readonly Guid OrderStoreA    = Guid.NewGuid();  // franchise A / store A, no warehouse
    private static readonly Guid OrderStoreB    = Guid.NewGuid();  // franchise B / store B
    private static readonly Guid OrderWarehouseA = Guid.NewGuid(); // franchise A / store A / warehouse A
    private static readonly Guid OrderOtherBrand = Guid.NewGuid();

    // brand-level configuration rows (no franchise/store at all)
    private static readonly Guid SettingBrandLevel = Guid.NewGuid();
    private static readonly Guid SettingStoreA     = Guid.NewGuid();

    private const string Fixture = """
        CREATE ROLE app_user  NOLOGIN;
        CREATE ROLE app_admin NOLOGIN;

        CREATE SCHEMA kernel;
        CREATE SCHEMA order_lifecycle;
        CREATE SCHEMA tenancy_org;

        -- Verbatim copies of the production helpers 0031 builds on.
        CREATE FUNCTION kernel.split_setting(p_name text) RETURNS text[] LANGUAGE sql STABLE PARALLEL SAFE AS $f$
            SELECT CASE
                WHEN current_setting(p_name, true) IS NULL THEN NULL
                WHEN current_setting(p_name, true) = '?'   THEN NULL
                ELSE coalesce(
                    (SELECT array_agg(t ORDER BY ord)
                     FROM regexp_split_to_table(current_setting(p_name, true), '\s+')
                          WITH ORDINALITY AS s(t, ord)
                     WHERE t <> ''),
                    '{}'::text[])
            END
        $f$;
        CREATE FUNCTION kernel.current_scope_nodes() RETURNS text[] LANGUAGE sql STABLE PARALLEL SAFE AS
            $f$ SELECT kernel.split_setting('app.current_scope_nodes') $f$;
        CREATE FUNCTION kernel.current_brand_id() RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_brand_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.rls_bypass() RETURNS boolean LANGUAGE sql STABLE AS
            $f$ SELECT lower(coalesce(current_setting('app.bypass_rls', true), 'false'))
                       IN ('on','true','1','yes','t') $f$;

        -- Three column shapes the migration has to cope with: all four ids, no warehouse, and a
        -- node table with no store_id of its own.
        CREATE TABLE order_lifecycle.orders (
            id uuid PRIMARY KEY, brand_id uuid NOT NULL,
            franchise_id uuid, store_id uuid, warehouse_id uuid
        );
        CREATE TABLE kernel.system_settings (
            id uuid PRIMARY KEY, brand_id uuid NOT NULL, franchise_id uuid, store_id uuid
        );
        CREATE TABLE tenancy_org.stores (
            id uuid PRIMARY KEY, brand_id uuid NOT NULL, franchise_id uuid
        );

        -- The PERMISSIVE brand policies that already ship. 0031's restrictive policy is ANDed with
        -- these, so leaving them in is what makes the composition under test the real one.
        ALTER TABLE order_lifecycle.orders  ENABLE ROW LEVEL SECURITY;
        ALTER TABLE kernel.system_settings  ENABLE ROW LEVEL SECURITY;
        ALTER TABLE tenancy_org.stores      ENABLE ROW LEVEL SECURITY;

        CREATE POLICY rls_brand ON order_lifecycle.orders FOR ALL TO app_user
            USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
            WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
        CREATE POLICY rls_brand ON kernel.system_settings FOR ALL TO app_user
            USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
            WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());
        CREATE POLICY rls_brand ON tenancy_org.stores FOR ALL TO app_user
            USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
            WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

        GRANT USAGE ON SCHEMA kernel, order_lifecycle, tenancy_org TO app_user, app_admin;
        GRANT SELECT, INSERT, UPDATE, DELETE
            ON ALL TABLES IN SCHEMA kernel, order_lifecycle, tenancy_org TO app_user, app_admin;
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA kernel TO app_user, app_admin;
        """;

    private const string Seed = """
        INSERT INTO tenancy_org.stores (id, brand_id, franchise_id) VALUES
            (@storeA, @brand, @franchiseA),
            (@storeB, @brand, @franchiseB);

        INSERT INTO order_lifecycle.orders (id, brand_id, franchise_id, store_id, warehouse_id) VALUES
            (@orderStoreA,     @brand,      @franchiseA, @storeA, NULL),
            (@orderStoreB,     @brand,      @franchiseB, @storeB, NULL),
            (@orderWarehouseA, @brand,      @franchiseA, @storeA, @warehouseA),
            (@orderOtherBrand, @otherBrand, @franchiseA, @storeA, NULL);

        INSERT INTO kernel.system_settings (id, brand_id, franchise_id, store_id) VALUES
            (@settingBrandLevel, @brand, NULL,        NULL),
            (@settingStoreA,     @brand, @franchiseA, @storeA);
        """;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        try
        {
            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
        }
        catch (Exception) { _dockerAvailable = false; return; }

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await Exec(conn, Fixture);
        await using (var seed = new NpgsqlCommand(Seed, conn)) { Bind(seed); await seed.ExecuteNonQueryAsync(); }

        await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Migration("0031_subbrand_scope_rls.up.sql")));
    }

    public async Task DisposeAsync() { if (_pg is not null) await _pg.DisposeAsync(); }

    // ── No regression: every legitimate scope level keeps what it had ───────────────────────────

    [Fact]
    public async Task A_brand_scoped_caller_sees_every_order_in_the_brand()
    {
        if (!_dockerAvailable) return;

        var seen = await OrdersAsync($"brand:{Brand}");

        // The common admin case. A brand node matches brand_id, so the sub-brand rule never bites.
        Assert.Equal(3, seen.Count);
        Assert.DoesNotContain(OrderOtherBrand, seen);   // the brand policy, still doing its job
    }

    [Fact]
    public async Task A_warehouse_scoped_caller_still_sees_orders_that_name_no_warehouse()
    {
        if (!_dockerAvailable) return;

        var seen = await OrdersAsync($"warehouse:{WarehouseA}");

        // The deliberate arm. Without it this is 0 rows and the warehouse board goes dark, because
        // orders.warehouse_id is NULL on every row in the live database.
        Assert.Contains(OrderStoreA, seen);
        Assert.Contains(OrderStoreB, seen);
        Assert.Contains(OrderWarehouseA, seen);
    }

    [Fact]
    public async Task A_store_scoped_caller_still_sees_brand_level_configuration()
    {
        if (!_dockerAvailable) return;

        var seen = await SettingsAsync($"store:{StoreA}");

        // A row that names no store is not another store's row. Measured on live data, the strict
        // rule would have hidden all 16 system_settings rows from every store-scoped user.
        Assert.Contains(SettingBrandLevel, seen);
        Assert.Contains(SettingStoreA, seen);
    }

    [Fact]
    public async Task A_platform_node_sees_everything_in_the_brand()
    {
        if (!_dockerAvailable) return;
        Assert.Equal(3, (await OrdersAsync("platform")).Count);
    }

    [Fact]
    public async Task A_bypass_session_is_unaffected()
    {
        if (!_dockerAvailable) return;

        // Platform admins short-circuit before the predicate and see across brands, as before.
        Assert.Equal(4, (await OrdersAsync("", bypass: true, brand: null)).Count);
    }

    // ── The hole, closed ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_store_scoped_caller_cannot_see_another_stores_orders()
    {
        if (!_dockerAvailable) return;

        var seen = await OrdersAsync($"store:{StoreA}");

        Assert.Contains(OrderStoreA, seen);
        Assert.Contains(OrderWarehouseA, seen);
        Assert.DoesNotContain(OrderStoreB, seen);      // this is the finding
    }

    [Fact]
    public async Task A_franchise_scoped_caller_cannot_see_another_franchises_orders()
    {
        if (!_dockerAvailable) return;

        var seen = await OrdersAsync($"franchise:{FranchiseA}");

        Assert.Contains(OrderStoreA, seen);
        Assert.DoesNotContain(OrderStoreB, seen);
    }

    [Fact]
    public async Task A_franchise_scoped_caller_cannot_see_another_franchises_stores()
    {
        if (!_dockerAvailable) return;

        var seen = await StoresAsync($"franchise:{FranchiseA}");

        Assert.Equal([StoreA], seen);
    }

    [Fact]
    public async Task A_warehouse_scoped_caller_cannot_see_another_warehouses_order()
    {
        if (!_dockerAvailable) return;

        var other = Guid.NewGuid();
        var seen = await OrdersAsync($"warehouse:{other}");

        // Rows that name no warehouse stay visible; the one that names a DIFFERENT warehouse does not.
        Assert.Contains(OrderStoreA, seen);
        Assert.DoesNotContain(OrderWarehouseA, seen);
    }

    // ── Fail-closed ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unresolved_scope_nodes_claim_denies_everything()
    {
        if (!_dockerAvailable) return;

        // The GUC is never set at all. split_setting returns NULL, the predicate returns NULL, and a
        // RESTRICTIVE policy treats NULL as not satisfied. Unresolved must not read as a decision.
        Assert.Empty(await OrdersAsync(null));
    }

    [Fact]
    public async Task A_resolved_but_empty_claim_denies_everything()
    {
        if (!_dockerAvailable) return;

        // Distinct from the above: this caller genuinely holds no memberships.
        Assert.Empty(await OrdersAsync(""));
    }

    [Fact]
    public async Task An_unrecognised_scope_type_neither_grants_nor_denies_on_its_own()
    {
        if (!_dockerAvailable) return;

        // 'territory' is a real ScopeType with no column on these tables. It must not silently
        // become a wildcard, and it must not veto a second node that does match.
        Assert.Empty(await OrdersAsync($"territory:{Guid.NewGuid()}"));
        Assert.Contains(OrderStoreA, await OrdersAsync($"territory:{Guid.NewGuid()} store:{StoreA}"));
    }

    // ── Writes ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_store_scoped_caller_cannot_insert_a_row_naming_another_store()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync($"store:{StoreA}", Brand);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO order_lifecycle.orders (id, brand_id, franchise_id, store_id)
            VALUES (gen_random_uuid(), @brand, @franchiseB, @storeB)
            """, conn);
        cmd.Parameters.AddWithValue("brand", Brand);
        cmd.Parameters.AddWithValue("franchiseB", FranchiseB);
        cmd.Parameters.AddWithValue("storeB", StoreB);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task A_store_scoped_caller_can_still_insert_a_row_for_its_own_store()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync($"store:{StoreA}", Brand);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO order_lifecycle.orders (id, brand_id, franchise_id, store_id)
            VALUES (gen_random_uuid(), @brand, @franchiseA, @storeA)
            """, conn);
        cmd.Parameters.AddWithValue("brand", Brand);
        cmd.Parameters.AddWithValue("franchiseA", FranchiseA);
        cmd.Parameters.AddWithValue("storeA", StoreA);

        // The control a block-everything policy would fail.
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());

        await using var owner = new NpgsqlConnection(_connString);
        await owner.OpenAsync();
        await Exec(owner, $"DELETE FROM order_lifecycle.orders WHERE id NOT IN ('{OrderStoreA}','{OrderStoreB}','{OrderWarehouseA}','{OrderOtherBrand}')");
    }

    [Fact]
    public async Task A_store_scoped_caller_cannot_update_another_stores_order()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync($"store:{StoreA}", Brand);
        await using var cmd = new NpgsqlCommand(
            "UPDATE order_lifecycle.orders SET franchise_id = NULL WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", OrderStoreB);

        // The row is invisible to UPDATE's USING clause, so it is not matched rather than rejected.
        Assert.Equal(0, await cmd.ExecuteNonQueryAsync());
    }

    // ── plumbing ────────────────────────────────────────────────────────────────────────────────

    private Task<List<Guid>> OrdersAsync(string? nodes, bool bypass = false, Guid? brand = null)
        => IdsAsync("SELECT id FROM order_lifecycle.orders", nodes, bypass, brand ?? (bypass ? null : Brand));

    private Task<List<Guid>> SettingsAsync(string? nodes)
        => IdsAsync("SELECT id FROM kernel.system_settings", nodes, false, Brand);

    private Task<List<Guid>> StoresAsync(string? nodes)
        => IdsAsync("SELECT id FROM tenancy_org.stores", nodes, false, Brand);

    private async Task<List<Guid>> IdsAsync(string sql, string? nodes, bool bypass, Guid? brand)
    {
        await using var conn = await SessionAsync(nodes, brand, bypass);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var ids = new List<Guid>();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    /// <summary>Opens a session, applies scope context, and drops to <c>app_user</c>. A null
    /// <paramref name="nodes"/> leaves the GUC UNSET, which is the unresolved case — distinct from
    /// passing an empty string.</summary>
    private async Task<NpgsqlConnection> SessionAsync(string? nodes, Guid? brand, bool bypass = false)
    {
        var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using (var ctx = new NpgsqlCommand("""
            SELECT set_config('app.current_brand_id', @brand,  false),
                   set_config('app.bypass_rls',       @bypass, false)
            """, conn))
        {
            ctx.Parameters.AddWithValue("brand", brand?.ToString() ?? "");
            ctx.Parameters.AddWithValue("bypass", bypass ? "true" : "false");
            await ctx.ExecuteNonQueryAsync();
        }

        if (nodes is not null)
        {
            await using var set = new NpgsqlCommand(
                "SELECT set_config('app.current_scope_nodes', @nodes, false)", conn);
            set.Parameters.AddWithValue("nodes", nodes);
            await set.ExecuteNonQueryAsync();
        }

        await using (var setRole = new NpgsqlCommand("SET ROLE app_user", conn))
            await setRole.ExecuteNonQueryAsync();

        return conn;
    }

    private static void Bind(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("brand", Brand);
        cmd.Parameters.AddWithValue("otherBrand", OtherBrand);
        cmd.Parameters.AddWithValue("franchiseA", FranchiseA);
        cmd.Parameters.AddWithValue("franchiseB", FranchiseB);
        cmd.Parameters.AddWithValue("storeA", StoreA);
        cmd.Parameters.AddWithValue("storeB", StoreB);
        cmd.Parameters.AddWithValue("warehouseA", WarehouseA);
        cmd.Parameters.AddWithValue("orderStoreA", OrderStoreA);
        cmd.Parameters.AddWithValue("orderStoreB", OrderStoreB);
        cmd.Parameters.AddWithValue("orderWarehouseA", OrderWarehouseA);
        cmd.Parameters.AddWithValue("orderOtherBrand", OrderOtherBrand);
        cmd.Parameters.AddWithValue("settingBrandLevel", SettingBrandLevel);
        cmd.Parameters.AddWithValue("settingStoreA", SettingStoreA);
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
