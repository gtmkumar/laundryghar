using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Migration <c>0030_roles_global_visibility</c>.
///
/// <para><b>What was broken.</b> <c>identity_access.roles</c> was given the generic <c>rls_brand</c>
/// policy from <c>db/patches/rls_proposal.sql</c>, whose USING clause is
/// <c>brand_id = kernel.current_brand_id()</c>. That template is written for tables whose
/// <c>brand_id</c> is NOT NULL. On this table it is nullable and a NULL means "global, shipped with
/// the platform" — so for all 17 system roles the predicate evaluated <c>NULL = uuid</c> → NULL →
/// not satisfied, and no brand session could see any system role at all. Measured on the live
/// database before the fix: a session scoped to one brand saw 2 of the 19 roles it should have.</para>
///
/// <para><b>Why it mattered.</b> The application layer says the opposite in
/// <c>GetAccessRoles</c> — "System roles (BrandId == null) are global" — so handler and policy
/// disagreed and the policy silently won. Two consequences: a brand admin's role picker could not
/// offer a single system role, and the A-1 rank guard could not resolve a brand admin's own rank
/// (their <c>brand_admin</c> row was invisible), so it refused every brand-scoped caller.</para>
///
/// <para>These apply the migration <b>verbatim from disk</b> to a minimal fixture and drive it as a
/// non-owner role, the same approach <see cref="UsersBrandRlsTests"/> takes with 0029. The write
/// assertions carry the weight: relaxing a read must not relax a write, or the fix would trade one
/// hole for a larger one.</para>
///
/// <para>Requires Docker; self-skips when no container runtime is reachable.</para>
/// </summary>
public sealed class RolesGlobalVisibilityRlsTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    private static readonly Guid BrandA = Guid.NewGuid();
    private static readonly Guid BrandB = Guid.NewGuid();

    private static readonly Guid GlobalRole = Guid.NewGuid();   // brand_id NULL, is_system
    private static readonly Guid RoleA      = Guid.NewGuid();   // brand A custom
    private static readonly Guid RoleB      = Guid.NewGuid();   // brand B custom

    /// <summary>The minimum 0030 touches, carrying the policy it replaces exactly as production
    /// had it — including the NULL blind spot that is the reason for the migration.</summary>
    private const string Fixture = """
        CREATE ROLE app_user  NOLOGIN;
        CREATE ROLE app_admin NOLOGIN;

        CREATE SCHEMA kernel;
        CREATE SCHEMA identity_access;

        CREATE FUNCTION kernel.current_brand_id() RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_brand_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.rls_bypass()       RETURNS boolean LANGUAGE sql STABLE AS
            $f$ SELECT lower(coalesce(current_setting('app.bypass_rls', true), 'false'))
                       IN ('on','true','1','yes','t') $f$;

        CREATE TABLE identity_access.roles (
            id          uuid PRIMARY KEY,
            code        varchar(64) NOT NULL,
            name        varchar(128) NOT NULL,
            brand_id    uuid,
            is_system   boolean NOT NULL DEFAULT false,
            priority    smallint NOT NULL DEFAULT 100,
            status      varchar(32) NOT NULL DEFAULT 'active',
            deleted_at  timestamptz
        );

        ALTER TABLE identity_access.roles ENABLE ROW LEVEL SECURITY;

        -- The policy as rls_proposal.sql wrote it: correct for a NOT NULL brand_id, wrong here.
        CREATE POLICY rls_brand ON identity_access.roles FOR ALL TO app_user
            USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
            WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

        GRANT USAGE ON SCHEMA kernel, identity_access TO app_user, app_admin;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity_access TO app_user, app_admin;
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA kernel TO app_user, app_admin;
        """;

    private const string Seed = """
        INSERT INTO identity_access.roles (id, code, name, brand_id, is_system, priority) VALUES
            (@globalRole, 'brand_admin',    'Brand Administrator', NULL,    true,  20),
            (@roleA,      'ops_manager_a',  'Ops Manager A',       @brandA, false, 25),
            (@roleB,      'ops_manager_b',  'Ops Manager B',       @brandB, false, 25);
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
        await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Migration("0030_roles_global_visibility.up.sql")));
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // ── Read: the fix ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_brand_session_can_read_the_global_system_role()
    {
        if (!_dockerAvailable) return;

        // The whole point: before 0030 this was empty, which is why the rank guard refused everyone.
        Assert.Contains(GlobalRole, await VisibleAsync(BrandA));
        Assert.Contains(GlobalRole, await VisibleAsync(BrandB));
    }

    [Fact]
    public async Task A_brand_session_still_cannot_read_another_brands_custom_role()
    {
        if (!_dockerAvailable) return;

        var a = await VisibleAsync(BrandA);
        Assert.Contains(RoleA, a);
        Assert.DoesNotContain(RoleB, a);       // the isolation that must survive the relaxation

        var b = await VisibleAsync(BrandB);
        Assert.Contains(RoleB, b);
        Assert.DoesNotContain(RoleA, b);
    }

    [Fact]
    public async Task A_session_with_no_brand_context_sees_only_global_roles()
    {
        if (!_dockerAvailable) return;

        // No brand set: NULL = NULL is still not satisfied for the custom rows, so a context-less
        // session gets the shared catalogue and nobody's tenant data.
        var seen = await VisibleAsync(null);
        Assert.Equal([GlobalRole], seen);
    }

    [Fact]
    public async Task A_bypass_session_sees_everything()
    {
        if (!_dockerAvailable) return;

        var seen = await VisibleAsync(null, bypass: true);
        Assert.Contains(GlobalRole, seen);
        Assert.Contains(RoleA, seen);
        Assert.Contains(RoleB, seen);
    }

    // ── Write: what must NOT have been relaxed ──────────────────────────────────────────────────

    [Fact]
    public async Task A_brand_session_cannot_update_a_global_role()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync(BrandA);
        await using var cmd = new NpgsqlCommand(
            "UPDATE identity_access.roles SET name = 'TAMPERED' WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", GlobalRole);

        // Readable is not writable. The per-command split makes UPDATE's USING clause the strict
        // one, so a global row is not merely rejected — it is not matched at all, and zero rows
        // change. (A FOR ALL policy would instead match it via the relaxed USING and reject it at
        // WITH CHECK with 42501; both refuse the write, but not being targetable is the stronger
        // of the two, and it is what makes DELETE safe as well.)
        Assert.Equal(0, await cmd.ExecuteNonQueryAsync());

        await using var check = await SessionAsync(BrandA);
        await using var name = new NpgsqlCommand(
            "SELECT name FROM identity_access.roles WHERE id = @id", check);
        name.Parameters.AddWithValue("id", GlobalRole);
        Assert.Equal("Brand Administrator", (string)(await name.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_brand_session_cannot_insert_a_global_role()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync(BrandA);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO identity_access.roles (id, code, name, brand_id)
            VALUES (gen_random_uuid(), 'smuggled', 'Smuggled', NULL)
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task A_brand_session_cannot_delete_a_global_role()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync(BrandA);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM identity_access.roles WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", GlobalRole);

        // This is why the policy is split per command rather than left as one FOR ALL. DELETE is
        // filtered by USING, and WITH CHECK cannot cover it — there is no new row to check. A
        // FOR ALL policy carrying the relaxed USING therefore let a brand session DELETE the global
        // brand_admin row; measured, before the split. Zero rows affected is the assertion, because
        // a row the policy hides from DELETE is simply not matched, not an error.
        Assert.Equal(0, await cmd.ExecuteNonQueryAsync());

        await using var check = await SessionAsync(BrandA);
        await using var still = new NpgsqlCommand(
            "SELECT count(*) FROM identity_access.roles WHERE id = @id", check);
        still.Parameters.AddWithValue("id", GlobalRole);
        Assert.Equal(1L, (long)(await still.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_brand_session_can_still_insert_its_own_role()
    {
        if (!_dockerAvailable) return;

        await using var conn = await SessionAsync(BrandA);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO identity_access.roles (id, code, name, brand_id)
            VALUES (gen_random_uuid(), 'mine', 'Mine', @brand)
            """, conn);
        cmd.Parameters.AddWithValue("brand", BrandA);

        // The control that a block-everything policy would fail.
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ── plumbing ────────────────────────────────────────────────────────────────────────────────

    private async Task<List<Guid>> VisibleAsync(Guid? brand, bool bypass = false)
    {
        await using var conn = await SessionAsync(brand, bypass);
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM identity_access.roles WHERE code <> 'mine' ORDER BY priority", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var ids = new List<Guid>();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    /// <summary>Opens a session, applies tenant context, and drops to <c>app_user</c> — RLS does not
    /// apply to the owner, so a test that forgets this proves nothing.</summary>
    private async Task<NpgsqlConnection> SessionAsync(Guid? brand, bool bypass = false)
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

        await using (var setRole = new NpgsqlCommand("SET ROLE app_user", conn))
            await setRole.ExecuteNonQueryAsync();

        return conn;
    }

    private static void Bind(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("globalRole", GlobalRole);
        cmd.Parameters.AddWithValue("roleA", RoleA);
        cmd.Parameters.AddWithValue("roleB", RoleB);
        cmd.Parameters.AddWithValue("brandA", BrandA);
        cmd.Parameters.AddWithValue("brandB", BrandB);
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
