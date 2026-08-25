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

        -- kernel RLS helpers referenced by permission_overrides.sql's and
        -- brand_module_entitlement.sql's CREATE POLICY (stubbed; superuser bypasses RLS so the
        -- bodies are never evaluated by these tests). They must nonetheless EXIST, because
        -- CREATE POLICY resolves the functions in its USING clause at creation time.
        CREATE OR REPLACE FUNCTION kernel.rls_bypass()       RETURNS boolean LANGUAGE sql STABLE AS 'SELECT true';
        CREATE OR REPLACE FUNCTION kernel.current_user_id()  RETURNS uuid    LANGUAGE sql STABLE AS 'SELECT NULL::uuid';
        CREATE OR REPLACE FUNCTION kernel.current_brand_id() RETURNS uuid    LANGUAGE sql STABLE AS 'SELECT NULL::uuid';

        -- Verbatim copy of the production function (db/patches/triggers_set_updated_at.sql);
        -- migration 0002 attaches it to tenancy_org.brand_domains.
        CREATE OR REPLACE FUNCTION kernel.set_updated_at() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF NEW.updated_at IS NOT DISTINCT FROM OLD.updated_at THEN
                NEW.updated_at := now();
            END IF;
            RETURN NEW;
        END
        $fn$;
        """;

    // ── Columns/tables the EF model maps that come from OTHER additive patches we don't apply here ─
    private const string EfColumnParity = """
        ALTER TABLE identity_access.users ADD COLUMN IF NOT EXISTS perm_version integer NOT NULL DEFAULT 0;
        ALTER TABLE identity_access.users ADD COLUMN IF NOT EXISTS vertical_key varchar(20);
        ALTER TABLE identity_access.roles ADD COLUMN IF NOT EXISTS vertical_key varchar(20);

        -- phase0_multi_vertical.sql's brand discriminator. GetNavigator SELECTs brands.vertical_key
        -- to apply its vertical gate, so the column must exist; the DEFAULT matches the real patch,
        -- which keeps the raw-SQL tenant seeding below working unchanged.
        ALTER TABLE tenancy_org.brands
            ADD COLUMN IF NOT EXISTS vertical_key varchar(20) NOT NULL DEFAULT 'laundry';

        -- permission_canonical_module.sql joins identity_access.modules, and
        -- brand_module_entitlement.sql adds is_core to it + FKs brand_module.module_key at it.
        -- The real navigator-modules seed creates the table in production; here we create the same
        -- SHAPE (mirroring AppModuleConfiguration) and leave it EMPTY. Empty is the safe default for
        -- the pre-existing tests: permission_canonical_module's UPDATEs then map nothing, every
        -- permission stays an orphan (module_key NULL), and the entitlement filter always keeps
        -- orphans. Tests that care about entitlement insert their own module rows.
        CREATE TABLE IF NOT EXISTS identity_access.modules (
            id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            key                 varchar(64) NOT NULL UNIQUE,
            label               varchar(128) NOT NULL DEFAULT '',
            icon                varchar(64),
            route               varchar(160),
            section             varchar(64),
            nav_order           int NOT NULL DEFAULT 100,
            matrix_order        int NOT NULL DEFAULT 100,
            show_in_nav         boolean NOT NULL DEFAULT false,
            show_in_matrix      boolean NOT NULL DEFAULT true,
            required_permission varchar(128),
            permission_modules  text[] NOT NULL DEFAULT '{}',
            vertical_key        varchar(20),
            status              varchar(32) NOT NULL DEFAULT 'active',
            created_at          timestamptz NOT NULL DEFAULT now(),
            updated_at          timestamptz NOT NULL DEFAULT now()
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
                     // The PaaS entitlement axis: modules.is_core + brand_module + the bundle catalog.
                     // Applied verbatim so the entitlement tests exercise the REAL schema (FKs, the
                     // brand-scoped RLS policy shape, the source CHECK) rather than a hand-rolled stub.
                     // Its backfill is a no-op here: modules is empty and brands are seeded per-test.
                     "brand_module_entitlement.sql",
                     // MANDATORY: provisions the current-month audit_logs partition, else the interceptor
                     // INSERT throws "no partition of relation audit_logs found" and rolls back the save.
                     "audit_logs_partition_maintenance.sql",
                 })
        {
            await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Patch(patch)));
        }

        // Versioned migrations (db/migrations/), applied verbatim in order: brand_domains and the
        // SECURITY DEFINER host lookup BrandResolver calls. All NEW schema lives here, not in patches.
        foreach (var migration in new[]
                 {
                     "0002_brand_domains.up.sql",
                     "0003_resolve_brand_domain.up.sql",
                 })
        {
            await Exec(conn, await File.ReadAllTextAsync(RepoPaths.Migration(migration)));
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

    /// <summary>Raw platform → brand pair via SQL (no franchise/store), for brand-scoped tests such as
    /// entitlement. Same rationale as <see cref="SeedStoreChainAsync"/>: raw SQL avoids EF trying to
    /// write patch-only tenancy columns this fixture does not provision.</summary>
    public async Task<string> SeedBrandAsync(Guid brandId, string verticalKey = "laundry")
    {
        var platformId = Guid.NewGuid();
        var sfx = Guid.NewGuid().ToString("N")[..10];
        await using var c = new NpgsqlConnection(SuperConnString);
        await c.OpenAsync();
        await Exec(c, $$"""
            INSERT INTO tenancy_org.platforms (id, code, name)
                VALUES ('{{platformId}}', 'p_{{sfx}}', 'P');
            INSERT INTO tenancy_org.brands (id, platform_id, code, name, vertical_key)
                VALUES ('{{brandId}}', '{{platformId}}', 'b_{{sfx}}', 'B', '{{verticalKey}}');
            """);
        return $"b_{sfx}";
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
