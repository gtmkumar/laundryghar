using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// §9: "Cancellation = export offered, wind-down retention, then deletion per DPDP."
/// §8.2: "export their data at any time; take it with them if they leave."
///
/// <para>Two of these tests matter far more than the rest, and they are the two where being wrong is
/// unrecoverable: <see cref="An_export_never_leaks_another_tenants_data"/> (we hand a company a file
/// containing someone else's business) and <see cref="A_purge_that_cannot_finish_deletes_nothing"/>
/// (we tell a company their data is gone when it is not). Everything else here is bookkeeping by
/// comparison.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class BrandCancellationTests
{
    private readonly RbacRlsFixture _fx;
    public BrandCancellationTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── the table list is derived, never hand-maintained: a list of ~120 tables would be wrong
    //      within a month, and wrong here means an incomplete export or an incomplete deletion.
    [Fact]
    public async Task The_table_list_is_derived_from_the_catalogue()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        // Create a brand-scoped table the migration has never heard of…
        await RbacRlsFixture.ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS identity_access.zz_invented_later (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(), brand_id uuid NOT NULL);
            """);

        // …and it is covered anyway, with no migration change.
        var covered = await RbacRlsFixture.ScalarAsync(conn, """
            SELECT count(*) FROM kernel.brand_scoped_tables()
            WHERE schema_name = 'identity_access' AND table_name = 'zz_invented_later'
            """);

        await RbacRlsFixture.ExecAsync(conn, "DROP TABLE identity_access.zz_invented_later");
        Assert.Equal(1L, Convert.ToInt64(covered));
    }

    // 2 ── THE ONE THAT MATTERS MOST. An export is a bulk read of a whole company. Handing one
    //      tenant another tenant's rows is the single worst thing this feature could do.
    [Fact]
    public async Task An_export_never_leaks_another_tenants_data()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        var (mine, _) = await SeedAsync(conn, "mine");
        var (theirs, _) = await SeedAsync(conn, "theirs");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
            SELECT '{mine}', u.id, 'my row' FROM identity_access.users u
             WHERE u.email = 'wind-mine@laundryghar.test';
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
            SELECT '{theirs}', u.id, 'their row' FROM identity_access.users u
             WHERE u.email = 'wind-theirs@laundryghar.test';
            """);

        var mineJson = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT string_agg(row_data::text, ' ') FROM kernel.export_brand('{mine}')");

        Assert.Contains("my row", (string?)mineJson);
        Assert.DoesNotContain("their row", (string?)mineJson);
        Assert.DoesNotContain(theirs.ToString(), (string?)mineJson);
    }

    // 3 ── the export opens with the company's own registration record — a row ordinary RLS hides
    //      (tenancy_org.brands is admin-only), and exactly the sort of thing "take it with them"
    //      means. If this ever stops working, the export silently loses the identity of the tenant.
    [Fact]
    public async Task An_export_includes_the_brands_own_record()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "ownrec");

        var sources = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT string_agg(DISTINCT source, ',') FROM kernel.export_brand('{brand}')");

        Assert.Contains("tenancy_org.brands", (string?)sources);
    }

    // 4 ── deletion actually deletes.
    [Fact]
    public async Task A_purge_removes_the_tenants_rows()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn, "purge");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
            VALUES ('{brand}', '{user}', 'to be deleted')
            """);

        await RbacRlsFixture.ExecAsync(conn, $"SELECT * FROM kernel.purge_brand('{brand}')");

        Assert.Equal(0L, Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT count(*) FROM identity_access.impersonation_grants WHERE brand_id = '{brand}'")));
    }

    // 5 ── …and leaves a tombstone rather than deleting the brand row, because audit_logs.brand_id
    //      is an FK with ON DELETE RESTRICT into a 7-year ledger. The record survives; everything
    //      identifying about it does not.
    [Fact]
    public async Task A_purge_tombstones_the_brand_instead_of_deleting_it()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "tomb");

        await RbacRlsFixture.ExecAsync(conn, $"SELECT * FROM kernel.purge_brand('{brand}')");

        var row = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status || '/' || name FROM tenancy_org.brands WHERE id = '{brand}'");

        Assert.Equal("archived/Deleted brand", row);
    }

    // 6 ── the audit ledger survives the purge. Deleting the record of what happened would defeat
    //      both the impersonation audit and any later question about the deletion itself.
    [Fact]
    public async Task A_purge_keeps_the_audit_ledger()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "ledger");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.audit_logs (brand_id, action, resource_type, success)
            VALUES ('{brand}', 'test.action', 'test', true)
            """);

        await RbacRlsFixture.ExecAsync(conn, $"SELECT * FROM kernel.purge_brand('{brand}')");

        Assert.Equal(1L, Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT count(*) FROM identity_access.audit_logs WHERE brand_id = '{brand}'")));
    }

    // 7 ── THE OTHER ONE THAT MATTERS. A purge blocked by an unexpected foreign key must RAISE, not
    //      finish quietly having deleted most things. Reporting a partial deletion as complete means
    //      telling a customer their data is gone when it is not.
    [Fact]
    public async Task A_purge_that_cannot_finish_deletes_nothing()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn, "stall");

        // A brand-scoped table whose rows are pinned by a NO ACTION reference from a table that
        // carries no brand_id — so the purge can never reach the dependent and can never clear it.
        await RbacRlsFixture.ExecAsync(conn, $"""
            CREATE TABLE identity_access.zz_pinned (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(), brand_id uuid NOT NULL);
            CREATE TABLE identity_access.zz_pinner (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                pinned_id uuid NOT NULL REFERENCES identity_access.zz_pinned(id));
            INSERT INTO identity_access.zz_pinned (id, brand_id)
                VALUES ('22222222-2222-2222-2222-222222222222', '{brand}');
            INSERT INTO identity_access.zz_pinner (pinned_id)
                VALUES ('22222222-2222-2222-2222-222222222222');
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
                VALUES ('{brand}', '{user}', 'should survive a failed purge');
            """);

        try
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                RbacRlsFixture.ExecAsync(conn, $"SELECT * FROM kernel.purge_brand('{brand}')"));
            Assert.Contains("purge stalled", ex.MessageText);

            // The transaction rolled back, so the rows the purge DID manage to delete are still here
            // and the brand is untouched. Nothing was half-deleted and reported as done.
            Assert.Equal(1L, Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
                $"SELECT count(*) FROM identity_access.impersonation_grants WHERE brand_id = '{brand}'")));
            Assert.NotEqual("archived", await RbacRlsFixture.ScalarAsync(conn,
                $"SELECT status FROM tenancy_org.brands WHERE id = '{brand}'"));
        }
        finally
        {
            await RbacRlsFixture.ExecAsync(conn,
                "DROP TABLE IF EXISTS identity_access.zz_pinner; DROP TABLE IF EXISTS identity_access.zz_pinned;");
        }
    }

    // 8 ── a wind-down must end. A retention window in the past or with no end is not retention.
    [Fact]
    public async Task A_retention_window_must_be_in_the_future()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "window");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brand_cancellations (brand_id, retention_until)
            VALUES ('{brand}', now() - interval '1 day')
            """));

        Assert.Equal("23514", ex.SqlState);
    }

    // 9 ── one live wind-down per brand: a second would let re-requesting reset the clock, which is
    //      how "we deleted it" becomes "we kept it indefinitely".
    [Fact]
    public async Task A_brand_cannot_have_two_live_wind_downs()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "double");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brand_cancellations (brand_id, retention_until)
            VALUES ('{brand}', now() + interval '30 days')
            """);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brand_cancellations (brand_id, retention_until)
            VALUES ('{brand}', now() + interval '30 days')
            """));

        Assert.Equal("23505", ex.SqlState);
    }

    // 10 ── `cancelled` is a real brand state, so the suspension gate can freeze operations on it.
    [Fact]
    public async Task Cancelled_is_an_accepted_brand_status()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "state");

        await RbacRlsFixture.ExecAsync(conn,
            $"UPDATE tenancy_org.brands SET status = 'cancelled' WHERE id = '{brand}'");

        Assert.Equal("cancelled", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.brand_status('{brand}')"));
    }

    // 11 ── the record OF the deletion survives the deletion. The purge deletes every brand-scoped
    //       table, and brand_cancellations is one — so without an explicit exemption it erases its
    //       own wind-down row, the worker's "mark it purged" update matches nothing, and the question
    //       asked six months later ("did we actually delete this tenant, and when?") has no answer.
    //       Found by purging a real brand and watching the row vanish.
    [Fact]
    public async Task A_purge_keeps_the_record_that_it_happened()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "proof");

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brand_cancellations (brand_id, reason, retention_until)
            VALUES ('{brand}', 'they left', now() + interval '1 day')
            """);

        await RbacRlsFixture.ExecAsync(conn, $"SELECT * FROM kernel.purge_brand('{brand}')");

        Assert.Equal("they left", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT reason FROM tenancy_org.brand_cancellations WHERE brand_id = '{brand}'"));
    }

    // 12 ── the status transition is deliberately narrow: an owner cancelling can never become an
    //       owner deleting, and can never un-suspend themselves past a billing problem.
    [Theory]
    [InlineData("archived")]
    [InlineData("suspended")]
    public async Task The_cancellation_transition_refuses_every_other_status(string target)
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, $"narrow{target}");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn,
            $"SELECT kernel.set_brand_cancellation_state('{brand}', '{target}')"));

        Assert.Contains("only moves between active and cancelled", ex.MessageText);
    }

    // 13 ── …and it refuses to touch a brand whose data is already gone.
    [Fact]
    public async Task An_archived_brand_cannot_be_cancelled_again()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, _) = await SeedAsync(conn, "already");

        await RbacRlsFixture.ExecAsync(conn, $"SELECT * FROM kernel.purge_brand('{brand}')");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn,
            $"SELECT kernel.set_brand_cancellation_state('{brand}', 'cancelled')"));

        Assert.Contains("already been deleted", ex.MessageText);
    }

    private static async Task<(Guid Brand, Guid User)> SeedAsync(NpgsqlConnection conn, string tag)
    {
        var brand = Guid.NewGuid();
        var user = Guid.NewGuid();

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brands (id, name) VALUES ('{brand}', 'Wind-down {tag}');
            INSERT INTO identity_access.users (id, email, user_type)
            VALUES ('{user}', 'wind-{tag}@laundryghar.test', 'staff');
            """);

        return (brand, user);
    }
}
