using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Audit finding F-3: <c>identity_access.users</c> is the one sensitive table in this database with
/// no second line of defence — no <c>brand_id</c> column, row security switched off, and therefore
/// a tenant boundary that exists only in application code. F-1 and F-2 (cross-tenant disclosure of
/// PAN, Aadhaar, bank account, IFSC and UPI) were the symptoms; this is the condition.
///
/// <para>Migration <c>0029_users_brand_rls</c> supplies the missing layer by RESOLVING the brand
/// through live memberships rather than storing it. These tests apply that migration
/// <b>verbatim from disk</b> onto a minimal fixture and drive it as a non-owner role, so what is
/// under test is the file that ships rather than a paraphrase of it — the same approach
/// <see cref="AbacSqlParityTests"/> takes with 0025.</para>
///
/// <para>Three things here are worth more than the isolation assertions, because each is a way this
/// migration could pass review and still be wrong in production:</para>
/// <list type="bullet">
/// <item><b>INSERT must still work.</b> A user's row is written before their first membership, so a
/// naive <c>FOR ALL</c> policy would make it impossible to create an account at all.</item>
/// <item><b>A user must always see themselves</b>, or every profile and password-change path breaks
/// for anyone whose memberships do not resolve to the brand their session carries.</item>
/// <item><b>Revoked and expired memberships must not grant visibility</b> — the liveness test is the
/// whole difference between "was once a colleague" and "is one".</item>
/// </list>
///
/// <para>Requires Docker; self-skips when no container runtime is reachable.</para>
/// </summary>
public sealed class UsersBrandRlsTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    // Brands are referenced by id only — the boundary never reads a brands table, it reads the
    // scope tables the memberships point at.
    private static readonly Guid BrandA = Guid.NewGuid();
    private static readonly Guid BrandB = Guid.NewGuid();

    private static readonly Guid FranchiseA = Guid.NewGuid();
    private static readonly Guid WarehouseA = Guid.NewGuid();
    private static readonly Guid StoreB     = Guid.NewGuid();

    private static readonly Guid UBrandA     = Guid.NewGuid();  // membership at the brand itself
    private static readonly Guid UFranchiseA = Guid.NewGuid();  // one level down
    private static readonly Guid UWarehouseA = Guid.NewGuid();  // a different limb
    private static readonly Guid UBrandB     = Guid.NewGuid();
    private static readonly Guid UStoreB     = Guid.NewGuid();
    private static readonly Guid UNone       = Guid.NewGuid();  // invited, not yet placed
    private static readonly Guid URevoked    = Guid.NewGuid();  // membership at BrandA, revoked
    private static readonly Guid UExpired    = Guid.NewGuid();  // membership at BrandA, lapsed
    private static readonly Guid UPlatform   = Guid.NewGuid();  // platform scope, no brand

    /// <summary>
    /// The minimum that migration 0029 touches, plus the policy it replaces — reproduced exactly as
    /// production carries it today, including the fact that row security is OFF so it never runs.
    /// </summary>
    private const string Fixture = """
        CREATE ROLE app_user  NOLOGIN;
        CREATE ROLE app_admin NOLOGIN;

        CREATE SCHEMA kernel;
        CREATE SCHEMA identity_access;
        CREATE SCHEMA tenancy_org;

        CREATE FUNCTION kernel.current_brand_id() RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_brand_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.current_user_id()  RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_user_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.rls_bypass()       RETURNS boolean LANGUAGE sql STABLE AS
            $f$ SELECT lower(coalesce(current_setting('app.bypass_rls', true), 'false'))
                       IN ('on','true','1','yes','t') $f$;

        CREATE TABLE identity_access.users (
            id         uuid PRIMARY KEY,
            email      text,
            user_type  varchar(30) NOT NULL DEFAULT 'staff',
            status     varchar(20) NOT NULL DEFAULT 'active',
            deleted_at timestamptz);

        CREATE TABLE identity_access.user_scope_memberships (
            id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            user_id    uuid NOT NULL REFERENCES identity_access.users(id),
            scope_type varchar(20) NOT NULL,
            scope_id   uuid,
            revoked_at timestamptz,
            expires_at timestamptz);

        CREATE TABLE tenancy_org.franchises  (id uuid PRIMARY KEY, brand_id uuid NOT NULL);
        CREATE TABLE tenancy_org.stores      (id uuid PRIMARY KEY, brand_id uuid NOT NULL);
        CREATE TABLE tenancy_org.warehouses  (id uuid PRIMARY KEY, brand_id uuid NOT NULL);

        -- What production has today: a policy that has never run.
        CREATE POLICY rls_admin_only ON identity_access.users FOR ALL TO app_user
            USING (kernel.rls_bypass()) WITH CHECK (kernel.rls_bypass());

        GRANT USAGE ON SCHEMA kernel, identity_access, tenancy_org TO app_user, app_admin;
        GRANT SELECT, INSERT, UPDATE, DELETE
            ON ALL TABLES IN SCHEMA identity_access, tenancy_org TO app_user, app_admin;
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA kernel TO app_user, app_admin;
        """;

    private const string Seed = """
        INSERT INTO tenancy_org.franchises (id, brand_id) VALUES (@franchiseA, @brandA);
        INSERT INTO tenancy_org.warehouses (id, brand_id) VALUES (@warehouseA, @brandA);
        INSERT INTO tenancy_org.stores     (id, brand_id) VALUES (@storeB,     @brandB);

        INSERT INTO identity_access.users (id, email) VALUES
            (@uBrandA,     'a.brand@test.local'),
            (@uFranchiseA, 'a.franchise@test.local'),
            (@uWarehouseA, 'a.warehouse@test.local'),
            (@uBrandB,     'b.brand@test.local'),
            (@uStoreB,     'b.store@test.local'),
            (@uNone,       'nobody@test.local'),
            (@uRevoked,    'revoked@test.local'),
            (@uExpired,    'expired@test.local'),
            (@uPlatform,   'platform@test.local');

        INSERT INTO identity_access.user_scope_memberships
            (user_id, scope_type, scope_id, revoked_at, expires_at) VALUES
            (@uBrandA,     'brand',     @brandA,     NULL,  NULL),
            (@uFranchiseA, 'franchise', @franchiseA, NULL,  NULL),
            (@uWarehouseA, 'warehouse', @warehouseA, NULL,  NULL),
            (@uBrandB,     'brand',     @brandB,     NULL,  NULL),
            (@uStoreB,     'store',     @storeB,     NULL,  NULL),
            (@uRevoked,    'brand',     @brandA,     now(), NULL),
            (@uExpired,    'brand',     @brandA,     NULL,  now() - interval '1 day'),
            (@uPlatform,   'platform',  NULL,        NULL,  NULL);
        """;

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
            _dockerAvailable = false;
            return;
        }

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await Exec(conn, Fixture);
        await using (var seed = new NpgsqlCommand(Seed, conn))
        {
            Bind(seed);
            await seed.ExecuteNonQueryAsync();
        }

        // The migration under test, exactly as it ships.
        await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Migration("0029_users_brand_rls.up.sql")));
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // ── Isolation ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_brand_session_sees_its_own_users_through_every_scope_level()
    {
        if (!_dockerAvailable) return;

        var seen = await VisibleAsync(brand: BrandA);

        // Direct, and both kinds of descendant node.
        Assert.Contains(UBrandA, seen);
        Assert.Contains(UFranchiseA, seen);
        Assert.Contains(UWarehouseA, seen);
    }

    [Fact]
    public async Task A_brand_session_cannot_see_another_brands_users()
    {
        if (!_dockerAvailable) return;

        var a = await VisibleAsync(brand: BrandA);
        var b = await VisibleAsync(brand: BrandB);

        // The disclosure F-1 and F-2 exploited, now refused by the database itself.
        Assert.DoesNotContain(UBrandB, a);
        Assert.DoesNotContain(UStoreB, a);

        Assert.DoesNotContain(UBrandA, b);
        Assert.DoesNotContain(UFranchiseA, b);
        Assert.DoesNotContain(UWarehouseA, b);

        Assert.Contains(UBrandB, b);
        Assert.Contains(UStoreB, b);
    }

    [Fact]
    public async Task The_boundary_is_exact_not_merely_narrower()
    {
        if (!_dockerAvailable) return;

        // Asserting the whole set, not just a couple of members: a policy that returned too FEW
        // rows would pass every "does not contain" test above while breaking the product.
        var seen = await VisibleAsync(brand: BrandA);

        Assert.Equal(
            new HashSet<Guid> { UBrandA, UFranchiseA, UWarehouseA },
            seen.ToHashSet());
    }

    [Fact]
    public async Task A_revoked_or_expired_membership_grants_nothing()
    {
        if (!_dockerAvailable) return;

        var seen = await VisibleAsync(brand: BrandA);

        // Both users hold a membership row pointing at BrandA. Neither is live.
        Assert.DoesNotContain(URevoked, seen);
        Assert.DoesNotContain(UExpired, seen);
    }

    [Fact]
    public async Task A_user_with_no_membership_is_visible_to_no_tenant()
    {
        if (!_dockerAvailable) return;

        Assert.DoesNotContain(UNone, await VisibleAsync(brand: BrandA));
        Assert.DoesNotContain(UNone, await VisibleAsync(brand: BrandB));
    }

    [Fact]
    public async Task A_platform_membership_does_not_leak_into_a_brand_session()
    {
        if (!_dockerAvailable) return;

        // 'platform' resolves to no single brand, so it must not widen a tenant's view. Platform
        // operators reach this table through the bypass instead.
        Assert.DoesNotContain(UPlatform, await VisibleAsync(brand: BrandA));
        Assert.DoesNotContain(UPlatform, await VisibleAsync(brand: BrandB));
    }

    [Fact]
    public async Task A_session_with_no_brand_context_sees_nobody()
    {
        if (!_dockerAvailable) return;

        // NULL brand must resolve to "no right established", not to "unfiltered". Getting this
        // backwards is precisely the fail-open A0.6 found on the ten customer policies.
        Assert.Empty(await VisibleAsync(brand: null));
    }

    [Fact]
    public async Task Bypass_still_sees_everyone()
    {
        if (!_dockerAvailable) return;

        // Platform admins and the background workers depend on this.
        var seen = await VisibleAsync(brand: null, bypass: true);
        Assert.Equal(9, seen.Count);
    }

    // ── The self arm ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_user_can_always_see_their_own_row()
    {
        if (!_dockerAvailable) return;

        // UBrandB carrying a BrandA context is the awkward case: nothing about their memberships
        // resolves to the session's brand. Without the self arm they would vanish from their own
        // profile, password change and step-up — an account that appears deleted.
        var seen = await VisibleAsync(brand: BrandA, self: UBrandB);

        Assert.Contains(UBrandB, seen);
        Assert.Contains(UBrandA, seen);          // and the tenant view is unchanged
        Assert.DoesNotContain(UStoreB, seen);    // the self arm widens by exactly one row
    }

    // ── Writes ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_cross_tenant_update_touches_no_rows()
    {
        if (!_dockerAvailable) return;

        var affected = await NonQueryAsync(
            "UPDATE identity_access.users SET status = 'suspended' WHERE id = @target",
            brand: BrandA, target: UBrandB);

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task A_cross_tenant_delete_touches_no_rows()
    {
        if (!_dockerAvailable) return;

        var affected = await NonQueryAsync(
            "DELETE FROM identity_access.users WHERE id = @target",
            brand: BrandA, target: UBrandB);

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task An_in_tenant_update_still_works()
    {
        if (!_dockerAvailable) return;

        // The negative control. A policy that blocked everything would pass both tests above.
        var affected = await NonQueryAsync(
            "UPDATE identity_access.users SET status = 'active' WHERE id = @target",
            brand: BrandA, target: UFranchiseA);

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task Creating_a_user_still_works_before_they_have_any_membership()
    {
        if (!_dockerAvailable) return;

        // The chicken-and-egg the INSERT policy exists for: the row must be writable before the
        // membership that would make it visible can reference it. A WITH CHECK requiring a
        // resolvable brand here would make account creation impossible.
        var fresh = Guid.NewGuid();

        var affected = await NonQueryAsync(
            "INSERT INTO identity_access.users (id, email) VALUES (@target, 'fresh@test.local')",
            brand: BrandA, target: fresh);

        Assert.Equal(1, affected);

        // ...and it is correctly invisible until a membership places it.
        Assert.DoesNotContain(fresh, await VisibleAsync(brand: BrandA));
        Assert.Contains(fresh, await VisibleAsync(brand: null, bypass: true));
    }

    // ── The policy set itself ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_migration_leaves_row_security_on_and_the_inert_policy_gone()
    {
        if (!_dockerAvailable) return;

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var enabled = (bool)(await Scalar(conn,
            "SELECT relrowsecurity FROM pg_class WHERE oid = 'identity_access.users'::regclass"))!;
        Assert.True(enabled);

        var policies = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT policyname FROM pg_policies WHERE schemaname='identity_access' AND tablename='users' ORDER BY 1",
            conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) policies.Add(r.GetString(0));

        // rls_admin_only would have hidden every user from every ordinary session had it ever run.
        Assert.Equal(
            new[] { "rls_users_delete", "rls_users_insert", "rls_users_select", "rls_users_update" },
            policies);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens a session, applies tenant context, and drops to <c>app_user</c>.</summary>
    /// <remarks>
    /// The role switch is what makes this a real test. Row security does not apply to a table's
    /// owner, so running as the container's superuser would see everything and prove nothing.
    /// </remarks>
    private async Task<NpgsqlConnection> SessionAsync(Guid? brand, Guid? self = null, bool bypass = false)
    {
        var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand("""
            SELECT set_config('app.current_brand_id', @brand,  false),
                   set_config('app.current_user_id',  @self,   false),
                   set_config('app.bypass_rls',       @bypass, false)
            """, conn))
        {
            cmd.Parameters.AddWithValue("@brand", brand?.ToString() ?? "");
            cmd.Parameters.AddWithValue("@self", self?.ToString() ?? "");
            cmd.Parameters.AddWithValue("@bypass", bypass ? "true" : "false");
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var setRole = new NpgsqlCommand("SET ROLE app_user", conn))
            await setRole.ExecuteNonQueryAsync();

        return conn;
    }

    private async Task<List<Guid>> VisibleAsync(Guid? brand, Guid? self = null, bool bypass = false)
    {
        await using var conn = await SessionAsync(brand, self, bypass);
        await using var cmd = new NpgsqlCommand("SELECT id FROM identity_access.users", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var ids = new List<Guid>();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task<int> NonQueryAsync(string sql, Guid? brand, Guid target, Guid? self = null)
    {
        await using var conn = await SessionAsync(brand, self);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@target", target);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static void Bind(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("@brandA", BrandA);
        cmd.Parameters.AddWithValue("@brandB", BrandB);
        cmd.Parameters.AddWithValue("@franchiseA", FranchiseA);
        cmd.Parameters.AddWithValue("@warehouseA", WarehouseA);
        cmd.Parameters.AddWithValue("@storeB", StoreB);
        cmd.Parameters.AddWithValue("@uBrandA", UBrandA);
        cmd.Parameters.AddWithValue("@uFranchiseA", UFranchiseA);
        cmd.Parameters.AddWithValue("@uWarehouseA", UWarehouseA);
        cmd.Parameters.AddWithValue("@uBrandB", UBrandB);
        cmd.Parameters.AddWithValue("@uStoreB", UStoreB);
        cmd.Parameters.AddWithValue("@uNone", UNone);
        cmd.Parameters.AddWithValue("@uRevoked", URevoked);
        cmd.Parameters.AddWithValue("@uExpired", UExpired);
        cmd.Parameters.AddWithValue("@uPlatform", UPlatform);
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }
}
