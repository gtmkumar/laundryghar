using core.Application.Common.Interfaces;
using core.Infrastructure.Persistence;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Boots ONE real Postgres (postgis image — 01_bc1 has GEOGRAPHY columns) and applies the RBAC spine
/// exactly as production does: the canonical BC-1/BC-2 DDL + the four RBAC patches (#4/#6/#10/#12).
///
/// Runs as SUPERUSER: ScopeResolver is a bypass-path read and the AuditSaveChangesInterceptor writes
/// the audit row in the same save, so RLS need not be enforced (superuser bypasses it). The bootstrap
/// pre-creates only the environment the patches assume already exists in a real DB — extensions, the
/// three schemas, the app_user/app_admin grantee roles, two kernel RLS stubs referenced by a CREATE
/// POLICY, and the columns/tables that OTHER (unapplied) additive patches would have added but which
/// the EF model maps (users.perm_version/vertical_key, roles.vertical_key, identity_access.modules).
/// The product SQL files themselves are executed verbatim from disk.
/// </summary>
public sealed class RbacEfFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;

    public bool DockerAvailable { get; private set; }
    public string SuperConnString { get; private set; } = "";

    // ── Environment the four patches assume a real laundry_ghar_db already provides ──────────────
    private const string Bootstrap = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;
        CREATE EXTENSION IF NOT EXISTS citext;
        CREATE EXTENSION IF NOT EXISTS postgis;
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
        CREATE EXTENSION IF NOT EXISTS btree_gin;
        CREATE EXTENSION IF NOT EXISTS unaccent;

        CREATE SCHEMA IF NOT EXISTS kernel;
        CREATE SCHEMA IF NOT EXISTS tenancy_org;
        CREATE SCHEMA IF NOT EXISTS identity_access;

        -- Grantee roles the RLS/partition patches GRANT to (created by rls_proposal/app_user_role in prod).
        DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user')  THEN CREATE ROLE app_user;  END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_admin') THEN CREATE ROLE app_admin; END IF;
        END $$;

        -- kernel RLS helpers referenced by permission_overrides.sql's CREATE POLICY (stubbed; superuser
        -- bypasses RLS so the bodies are never evaluated by these tests).
        CREATE OR REPLACE FUNCTION kernel.rls_bypass()      RETURNS boolean LANGUAGE sql STABLE AS 'SELECT true';
        CREATE OR REPLACE FUNCTION kernel.current_user_id() RETURNS uuid    LANGUAGE sql STABLE AS 'SELECT NULL::uuid';
        """;

    // ── Columns/tables the EF model maps that come from OTHER additive patches we don't apply here ─
    private const string EfColumnParity = """
        ALTER TABLE identity_access.users ADD COLUMN IF NOT EXISTS perm_version integer NOT NULL DEFAULT 0;
        ALTER TABLE identity_access.users ADD COLUMN IF NOT EXISTS vertical_key varchar(20);
        ALTER TABLE identity_access.roles ADD COLUMN IF NOT EXISTS vertical_key varchar(20);

        -- permission_canonical_module.sql joins identity_access.modules; the real navigator-modules seed
        -- creates it. An empty stub is enough: the UPDATEs then map nothing and every permission stays an
        -- orphan (module_key NULL), which the entitlement filter always keeps.
        CREATE TABLE IF NOT EXISTS identity_access.modules (
            id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            key                varchar(64) NOT NULL UNIQUE,
            nav_order          int NOT NULL DEFAULT 100,
            permission_modules text[] NOT NULL DEFAULT '{}'
        );
        """;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgis/postgis:16-3.4").Build();
        try { await _pg.StartAsync(); }
        catch (Exception) { DockerAvailable = false; return; }

        SuperConnString = _pg.GetConnectionString();
        DockerAvailable = true;

        await using var conn = new NpgsqlConnection(SuperConnString);
        await conn.OpenAsync();

        // search_path is set as its own command so it persists on this physical connection for every
        // subsequent file (avoids wrapping the files' own BEGIN/COMMIT in an outer implicit txn).
        await Exec(conn, Bootstrap);

        await Exec(conn, "SET search_path TO tenancy_org, identity_access, kernel, public;");
        await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Script("01_bc1_tenancy_org.sql")));

        await Exec(conn, "SET search_path TO identity_access, tenancy_org, kernel, public;");
        await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Script("02_bc2_identity_access.sql")));

        await Exec(conn, EfColumnParity);

        foreach (var patch in new[]
                 {
                     "permission_overrides.sql",
                     "permission_override_scope_expiry.sql",
                     "permission_canonical_module.sql",
                     // MANDATORY: provisions the current-month audit_logs partition, else the interceptor
                     // INSERT throws "no partition of relation audit_logs found" and rolls back the save.
                     "audit_logs_partition_maintenance.sql",
                 })
        {
            await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Patch(patch)));
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    /// <summary>Fresh EF context over the superuser connection. Pass an interceptor to exercise the
    /// audit path; pass none to seed rows WITHOUT auditing them.</summary>
    public LaundryGharDbContext NewContext(IEnumerable<IInterceptor>? interceptors = null)
    {
        var ob = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql(SuperConnString, o => o.UseNetTopologySuite());
        if (interceptors is not null) ob.AddInterceptors(interceptors);
        return new LaundryGharDbContext(ob.Options);
    }

    /// <summary>Wraps the physical context in the core facade ScopeResolver consumes.</summary>
    public ICoreDbContext AsCore(LaundryGharDbContext db) => new CoreDbContext(db);

    /// <summary>Raw tenant chain (platform → brand → franchise → store) via SQL, so the store's brand_id
    /// resolves in ScopeResolver's §6 ancestor union WITHOUT dragging patch-only tenancy columns (e.g.
    /// brands.vertical_key) that the EF Brand mapping would otherwise try to INSERT.</summary>
    public async Task SeedStoreChainAsync(Guid brandId, Guid franchiseId, Guid storeId)
    {
        var platformId = Guid.NewGuid();
        var sfx = Guid.NewGuid().ToString("N")[..10];
        await using var c = new NpgsqlConnection(SuperConnString);
        await c.OpenAsync();
        await Exec(c, $$"""
            INSERT INTO tenancy_org.platforms (id, code, name)
                VALUES ('{{platformId}}', 'p_{{sfx}}', 'P');
            INSERT INTO tenancy_org.brands (id, platform_id, code, name)
                VALUES ('{{brandId}}', '{{platformId}}', 'b_{{sfx}}', 'B');
            INSERT INTO tenancy_org.franchises (id, brand_id, code, legal_name, contact_phone, billing_address)
                VALUES ('{{franchiseId}}', '{{brandId}}', 'f_{{sfx}}', 'F', '000', '{}');
            INSERT INTO tenancy_org.stores (id, brand_id, franchise_id, code, name, address_line1, city, state, pincode)
                VALUES ('{{storeId}}', '{{brandId}}', '{{franchiseId}}', 's_{{sfx}}', 'S', 'A', 'C', 'ST', '000000');
            """);
    }

    /// <summary>Open a raw superuser connection for assertion queries.</summary>
    public async Task<NpgsqlConnection> OpenAsync()
    {
        var c = new NpgsqlConnection(SuperConnString);
        await c.OpenAsync();
        return c;
    }

    public async Task<long> ScalarLongAsync(string sql, params (string name, object val)[] ps)
    {
        await using var c = new NpgsqlConnection(SuperConnString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var p in ps) cmd.Parameters.AddWithValue(p.name, p.val);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("rbac-ef")]
public sealed class RbacEfCollection : ICollectionFixture<RbacEfFixture> { }
