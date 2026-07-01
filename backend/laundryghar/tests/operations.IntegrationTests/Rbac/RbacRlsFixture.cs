using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Shared container fixture that stands up a real, trimmed-but-real-shaped RBAC spine
/// (kernel helpers + tenancy_org + identity_access) with PostgreSQL Row-Level Security
/// enabled, plus a NON-superuser <c>app_user</c> LOGIN role that is subject to those
/// policies. The verbatim RBAC patches (permission_overrides.sql +
/// permission_override_scope_expiry.sql) are applied so the tests exercise the same DDL
/// production ships. Requires Docker; if the container cannot start the fixture flips
/// <see cref="DockerAvailable"/> to false and every test self-skips.
/// </summary>
public sealed class RbacRlsFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _superConnString = "";
    private string _appConnString = "";

    /// <summary>True once the postgres:16-alpine container is up. Tests skip when false.</summary>
    public bool DockerAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        try
        {
            await _pg.StartAsync();
        }
        catch (Exception)
        {
            DockerAvailable = false;
            return;
        }

        DockerAvailable = true;
        _superConnString = _pg.GetConnectionString();
        // app_user is derived from the superuser string: same host/port/db, different creds,
        // pooling OFF so every OpenAppUserAsync() yields a fresh session with no leaked GUCs.
        _appConnString = new NpgsqlConnectionStringBuilder(_superConnString)
        {
            Username = "app_user",
            Password = "app_user",
            Pooling = false,
        }.ConnectionString;

        await BootstrapAsync();
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    public async Task<NpgsqlConnection> OpenSuperuserAsync()
    {
        var conn = new NpgsqlConnection(_superConnString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<NpgsqlConnection> OpenAppUserAsync()
    {
        var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        return conn;
    }

    // ---- shared exec helpers ------------------------------------------------

    public static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    /// <summary>
    /// Byte-for-byte mirror of <c>RlsConnectionInterceptor.BuildSetConfigCommand</c>:
    /// the same six <c>set_config</c> calls, same setting names, empty-string for null
    /// uuids, bypass rendered as literal "true"/"false", and is_local = false (session).
    /// This is the ONLY way the tests should push tenant context onto an app_user session.
    /// The <paramref name="partner"/> arg mirrors the interceptor's
    /// <c>app.current_partner_id</c> var (added for the RaaS partner RLS layer); it is
    /// optional and defaults to null so every existing brand/user test still compiles.
    /// </summary>
    public static async Task SetRlsAsync(
        NpgsqlConnection conn,
        Guid? brand = null,
        Guid? franchise = null,
        Guid? store = null,
        Guid? user = null,
        bool bypass = false,
        Guid? partner = null)
    {
        var brandId = brand?.ToString() ?? string.Empty;
        var franchiseId = franchise?.ToString() ?? string.Empty;
        var storeId = store?.ToString() ?? string.Empty;
        var userId = user?.ToString() ?? string.Empty;
        var partnerId = partner?.ToString() ?? string.Empty;
        var bypassRls = bypass ? "true" : "false";

        await using var cmd = new NpgsqlCommand("""
            SELECT
                set_config('app.current_brand_id',     @brand_id,     false),
                set_config('app.current_franchise_id', @franchise_id, false),
                set_config('app.current_store_id',     @store_id,     false),
                set_config('app.current_user_id',      @user_id,      false),
                set_config('app.current_partner_id',   @partner_id,   false),
                set_config('app.bypass_rls',           @bypass_rls,   false)
            """, conn);
        cmd.Parameters.AddWithValue("@brand_id", brandId);
        cmd.Parameters.AddWithValue("@franchise_id", franchiseId);
        cmd.Parameters.AddWithValue("@store_id", storeId);
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@partner_id", partnerId);
        cmd.Parameters.AddWithValue("@bypass_rls", bypassRls);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- one-time bootstrap on the superuser connection ---------------------

    private async Task BootstrapAsync()
    {
        await using var conn = await OpenSuperuserAsync();

        // 0-2. extensions, schemas, kernel current_* helpers + the HARDENED bypass.
        await ExecAsync(conn, """
            CREATE EXTENSION IF NOT EXISTS pgcrypto;
            CREATE EXTENSION IF NOT EXISTS citext;

            CREATE SCHEMA IF NOT EXISTS kernel;
            CREATE SCHEMA IF NOT EXISTS tenancy_org;
            CREATE SCHEMA IF NOT EXISTS identity_access;

            CREATE OR REPLACE FUNCTION kernel.current_brand_id() RETURNS uuid LANGUAGE sql STABLE AS
              $$ SELECT NULLIF(current_setting('app.current_brand_id', true), '')::uuid $$;
            CREATE OR REPLACE FUNCTION kernel.current_franchise_id() RETURNS uuid LANGUAGE sql STABLE AS
              $$ SELECT NULLIF(current_setting('app.current_franchise_id', true), '')::uuid $$;
            CREATE OR REPLACE FUNCTION kernel.current_store_id() RETURNS uuid LANGUAGE sql STABLE AS
              $$ SELECT NULLIF(current_setting('app.current_store_id', true), '')::uuid $$;
            CREATE OR REPLACE FUNCTION kernel.current_user_id() RETURNS uuid LANGUAGE sql STABLE AS
              $$ SELECT NULLIF(current_setting('app.current_user_id', true), '')::uuid $$;
            CREATE OR REPLACE FUNCTION kernel.current_customer_id() RETURNS uuid LANGUAGE sql STABLE AS
              $$ SELECT NULLIF(current_setting('app.current_customer_id', true), '')::uuid $$;

            CREATE OR REPLACE FUNCTION kernel.rls_bypass() RETURNS boolean LANGUAGE sql STABLE AS
              $$ SELECT lower(coalesce(current_setting('app.bypass_rls', true), 'false'))
                        IN ('on','true','1','yes','t') $$;
            """);

        // 3. trimmed real-shaped tables (owned by postgres).
        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS tenancy_org.brands (
                id   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                name text NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tenancy_org.franchises (
                id       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                brand_id uuid NOT NULL,
                code     text NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tenancy_org.stores (
                id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                brand_id     uuid NOT NULL,
                franchise_id uuid,
                code         text NOT NULL
            );

            CREATE TABLE IF NOT EXISTS identity_access.users (
                id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                phone_e164   varchar(20) UNIQUE,
                email        citext UNIQUE,
                user_type    varchar(30) NOT NULL DEFAULT 'staff',
                status       varchar(20) NOT NULL DEFAULT 'active',
                perm_version int NOT NULL DEFAULT 0,
                deleted_at   timestamptz,
                CHECK (phone_e164 IS NOT NULL OR email IS NOT NULL)
            );
            CREATE TABLE IF NOT EXISTS identity_access.roles (
                id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                brand_id   uuid,
                code       varchar(50) NOT NULL,
                name       varchar(100) NOT NULL,
                scope_type varchar(20) NOT NULL,
                deleted_at timestamptz,
                status     varchar(20) NOT NULL DEFAULT 'active',
                UNIQUE (brand_id, code)
            );
            CREATE TABLE IF NOT EXISTS identity_access.permissions (
                id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                code       varchar(100) NOT NULL UNIQUE,
                module     varchar(50) NOT NULL DEFAULT 'x',
                action     varchar(50) NOT NULL DEFAULT 'x',
                name       varchar(200) NOT NULL DEFAULT 'x',
                risk_level varchar(20) NOT NULL DEFAULT 'normal'
            );
            CREATE TABLE IF NOT EXISTS identity_access.role_permissions (
                id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                role_id       uuid NOT NULL REFERENCES identity_access.roles(id) ON DELETE CASCADE,
                permission_id uuid NOT NULL REFERENCES identity_access.permissions(id) ON DELETE CASCADE,
                UNIQUE (role_id, permission_id)
            );
            CREATE TABLE IF NOT EXISTS identity_access.user_scope_memberships (
                id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id    uuid NOT NULL REFERENCES identity_access.users(id),
                scope_type varchar(20) NOT NULL,
                scope_id   uuid,
                role_id    uuid NOT NULL,
                is_primary bool DEFAULT false,
                revoked_at timestamptz,
                expires_at timestamptz,
                metadata   jsonb NOT NULL DEFAULT '{}',
                UNIQUE (user_id, scope_type, scope_id, role_id)
            );
            CREATE TABLE IF NOT EXISTS identity_access.audit_logs (
                id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                occurred_at   timestamptz NOT NULL DEFAULT now(),
                brand_id      uuid,
                actor_user_id uuid,
                action        varchar(100) NOT NULL,
                resource_type varchar(50) NOT NULL,
                new_values    jsonb,
                success       bool NOT NULL DEFAULT true
            );
            """);

        // 4. roles: app_admin (NOLOGIN) + app_user (LOGIN, locked-down NON-superuser).
        //    Must exist before step 5 — the patches GRANT to both.
        await ExecAsync(conn, """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_admin') THEN
                    CREATE ROLE app_admin NOLOGIN;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
                    CREATE ROLE app_user LOGIN PASSWORD 'app_user'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
            END $$;
            ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            """);

        // 5. apply the verbatim RBAC patches (order matters: base table then scope/expiry).
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("permission_overrides.sql")));
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("permission_override_scope_expiry.sql")));

        // 6. grants for both app roles across all three schemas.
        await ExecAsync(conn, """
            DO $$
            DECLARE s text;
            BEGIN
                FOREACH s IN ARRAY ARRAY['kernel','tenancy_org','identity_access'] LOOP
                    EXECUTE format('GRANT USAGE ON SCHEMA %I TO app_user, app_admin', s);
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO app_user, app_admin', s);
                    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO app_user, app_admin', s);
                END LOOP;
            END $$;
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA kernel TO app_user, app_admin;
            """);

        // 7. policies (FOR ALL TO app_user).
        await ExecAsync(conn, """
            -- brand-scoped tables
            DROP POLICY IF EXISTS rls_brand ON tenancy_org.franchises;
            CREATE POLICY rls_brand ON tenancy_org.franchises FOR ALL TO app_user
                USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
                WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

            DROP POLICY IF EXISTS rls_brand ON tenancy_org.stores;
            CREATE POLICY rls_brand ON tenancy_org.stores FOR ALL TO app_user
                USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
                WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

            DROP POLICY IF EXISTS rls_brand ON identity_access.roles;
            CREATE POLICY rls_brand ON identity_access.roles FOR ALL TO app_user
                USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
                WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

            DROP POLICY IF EXISTS rls_brand ON identity_access.audit_logs;
            CREATE POLICY rls_brand ON identity_access.audit_logs FOR ALL TO app_user
                -- Mirrors db/patches/audit_logs_rls_null_brand.sql: strict READ, but WRITE also
                -- permits brand_id IS NULL so a system/partner/worker audit row can't abort its
                -- business write.
                USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
                WITH CHECK (kernel.rls_bypass()
                            OR brand_id = kernel.current_brand_id()
                            OR brand_id IS NULL);

            -- user-self scoped table
            DROP POLICY IF EXISTS rls_user_self ON identity_access.user_scope_memberships;
            CREATE POLICY rls_user_self ON identity_access.user_scope_memberships FOR ALL TO app_user
                USING (kernel.rls_bypass() OR user_id = kernel.current_user_id())
                WITH CHECK (kernel.rls_bypass() OR user_id = kernel.current_user_id());

            -- admin-only tables (only visible/writable under a bypass)
            DROP POLICY IF EXISTS rls_admin_only ON tenancy_org.brands;
            CREATE POLICY rls_admin_only ON tenancy_org.brands FOR ALL TO app_user
                USING (kernel.rls_bypass()) WITH CHECK (kernel.rls_bypass());

            DROP POLICY IF EXISTS rls_admin_only ON identity_access.users;
            CREATE POLICY rls_admin_only ON identity_access.users FOR ALL TO app_user
                USING (kernel.rls_bypass()) WITH CHECK (kernel.rls_bypass());

            DROP POLICY IF EXISTS rls_admin_only ON identity_access.permissions;
            CREATE POLICY rls_admin_only ON identity_access.permissions FOR ALL TO app_user
                USING (kernel.rls_bypass()) WITH CHECK (kernel.rls_bypass());

            DROP POLICY IF EXISTS rls_admin_only ON identity_access.role_permissions;
            CREATE POLICY rls_admin_only ON identity_access.role_permissions FOR ALL TO app_user
                USING (kernel.rls_bypass()) WITH CHECK (kernel.rls_bypass());
            """);

        // 8. enable RLS (user_permission_override was already enabled by the patch).
        await ExecAsync(conn, """
            ALTER TABLE tenancy_org.brands                    ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenancy_org.franchises                ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenancy_org.stores                    ENABLE ROW LEVEL SECURITY;
            ALTER TABLE identity_access.users                 ENABLE ROW LEVEL SECURITY;
            ALTER TABLE identity_access.roles                 ENABLE ROW LEVEL SECURITY;
            ALTER TABLE identity_access.permissions           ENABLE ROW LEVEL SECURITY;
            ALTER TABLE identity_access.role_permissions      ENABLE ROW LEVEL SECURITY;
            ALTER TABLE identity_access.user_scope_memberships ENABLE ROW LEVEL SECURITY;
            ALTER TABLE identity_access.audit_logs            ENABLE ROW LEVEL SECURITY;
            """);

        // 9. RaaS partner spine (issue #14). Trimmed, real-shaped mirror of
        //    db/patches/raas_partner_schema.sql + db/patches/rls_partner.sql — hand-rolled
        //    (alpine has no postgis, so we do NOT apply the real 05_bc5 DDL). The
        //    partner_id column is the tenant isolation key, exactly like brand_id above.
        await ExecAsync(conn, """
            CREATE SCHEMA IF NOT EXISTS logistics;

            -- app.current_partner_id → uuid (clone of kernel.current_brand_id()).
            CREATE OR REPLACE FUNCTION kernel.current_partner_id() RETURNS uuid LANGUAGE sql STABLE AS
              $$ SELECT NULLIF(current_setting('app.current_partner_id', true), '')::uuid $$;

            -- partners — the isolation ROOT (partner_id = partners.id).
            CREATE TABLE IF NOT EXISTS logistics.partners (
                id     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                code   text,
                status text
            );
            -- partner_users — partner login principals (id = JWT sub).
            CREATE TABLE IF NOT EXISTS logistics.partner_users (
                id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                partner_id   uuid,
                phone_e164   text,
                partner_role text,
                status       text
            );
            -- partner_bookings — booking raised by a partner (partner_id = rls key).
            CREATE TABLE IF NOT EXISTS logistics.partner_bookings (
                id                         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                partner_id                 uuid NOT NULL,
                brand_id                   uuid,
                created_by_partner_user_id uuid,
                status                     text NOT NULL DEFAULT 'requested'
            );
            """);

        // 9a. grants for both app roles on the logistics schema + partner tables + fn.
        await ExecAsync(conn, """
            GRANT USAGE ON SCHEMA logistics TO app_user, app_admin;
            GRANT SELECT, INSERT, UPDATE, DELETE ON
                logistics.partners, logistics.partner_users, logistics.partner_bookings
                TO app_user, app_admin;
            GRANT EXECUTE ON FUNCTION kernel.current_partner_id() TO app_user, app_admin;
            """);

        // 9b. rls_partner policies (FOR ALL TO app_user), mirroring rls_partner.sql.
        await ExecAsync(conn, """
            -- partner_bookings — isolate by partner_id.
            DROP POLICY IF EXISTS rls_partner ON logistics.partner_bookings;
            CREATE POLICY rls_partner ON logistics.partner_bookings FOR ALL TO app_user
                USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
                WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

            -- partner_users — isolate by partner_id.
            DROP POLICY IF EXISTS rls_partner ON logistics.partner_users;
            CREATE POLICY rls_partner ON logistics.partner_users FOR ALL TO app_user
                USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
                WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

            -- partners — isolation ROOT: match on id (a partner sees only its own org row).
            DROP POLICY IF EXISTS rls_partner ON logistics.partners;
            CREATE POLICY rls_partner ON logistics.partners FOR ALL TO app_user
                USING      (kernel.rls_bypass() OR id = kernel.current_partner_id())
                WITH CHECK (kernel.rls_bypass() OR id = kernel.current_partner_id());
            """);

        // 9c. activate RLS on the partner spine.
        await ExecAsync(conn, """
            ALTER TABLE logistics.partners         ENABLE ROW LEVEL SECURITY;
            ALTER TABLE logistics.partner_users    ENABLE ROW LEVEL SECURITY;
            ALTER TABLE logistics.partner_bookings ENABLE ROW LEVEL SECURITY;
            """);

        // 9d. RaaS partner DISPATCH spine (FULL-11b / issue #14). Trimmed, real-shaped mirror of
        //     db/patches/raas_partner_dispatch_schema.sql + rls_partner_dispatch.sql. This is the
        //     DUAL-VISIBILITY table: it carries partner_id AND brand_id, and the COMBINED
        //     rls_partner_or_brand policy lets BOTH the owning partner (partner arm) and the serving
        //     brand's fleet staff (brand arm) see the row. The FK → partner_bookings(id) matches
        //     production; brand_id / rider_id are scalar (no FK).
        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS logistics.partner_dispatches (
                id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                partner_id         uuid NOT NULL,
                partner_booking_id uuid NOT NULL REFERENCES logistics.partner_bookings(id) ON DELETE CASCADE,
                brand_id           uuid,
                rider_id           uuid,
                status             text NOT NULL DEFAULT 'pending',
                last_known_lat     numeric(10,7),
                last_known_lng     numeric(10,7),
                created_at         timestamptz NOT NULL DEFAULT now()
            );
            """);

        // 9e. grants + the COMBINED partner-or-brand policy, mirroring rls_partner_dispatch.sql.
        await ExecAsync(conn, """
            GRANT SELECT, INSERT, UPDATE, DELETE ON logistics.partner_dispatches TO app_user, app_admin;

            DROP POLICY IF EXISTS rls_partner_or_brand ON logistics.partner_dispatches;
            CREATE POLICY rls_partner_or_brand ON logistics.partner_dispatches FOR ALL TO app_user
                USING      (kernel.rls_bypass()
                            OR partner_id = kernel.current_partner_id()
                            OR (brand_id IS NOT NULL AND brand_id = kernel.current_brand_id()))
                WITH CHECK (kernel.rls_bypass()
                            OR partner_id = kernel.current_partner_id()
                            OR (brand_id IS NOT NULL AND brand_id = kernel.current_brand_id()));

            ALTER TABLE logistics.partner_dispatches ENABLE ROW LEVEL SECURITY;
            """);

        // 10. RaaS partner PREPAID WALLET spine (FULL-9 / issue #14). Trimmed, real-shaped
        //     mirror of db/patches/raas_partner_wallet_schema.sql + rls_partner_wallet.sql. Both
        //     commerce tables carry partner_id — the same isolation key as the logistics spine,
        //     so the rls_partner policy is byte-for-byte the same shape. available_balance is a
        //     GENERATED column, exactly as production.
        await ExecAsync(conn, """
            CREATE SCHEMA IF NOT EXISTS commerce;

            -- partner_wallet_accounts — one prepaid balance per partner (partner_id UNIQUE).
            CREATE TABLE IF NOT EXISTS commerce.partner_wallet_accounts (
                id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                partner_id        uuid NOT NULL,
                currency_code     char(3) NOT NULL DEFAULT 'INR',
                balance           numeric(14,2) NOT NULL DEFAULT 0,
                locked_balance    numeric(14,2) NOT NULL DEFAULT 0,
                available_balance numeric(14,2) GENERATED ALWAYS AS (balance - locked_balance) STORED,
                status            text NOT NULL DEFAULT 'active',
                CONSTRAINT partner_wallet_accounts_partner_id_key UNIQUE (partner_id)
            );
            -- partner_wallet_transactions — append-only credit/debit ledger. idempotency_key is unique
            -- PER PARTNER (not globally) — mirrors production's composite constraint (see
            -- raas_partner_wallet_idem_scope.sql) so two partners can reuse the same free-form key.
            CREATE TABLE IF NOT EXISTS commerce.partner_wallet_transactions (
                id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                partner_wallet_account_id uuid,
                partner_id                uuid NOT NULL,
                direction                 smallint NOT NULL CHECK (direction IN (1, -1)),
                amount                    numeric(14,2) NOT NULL,
                balance_before            numeric(14,2),
                balance_after             numeric(14,2),
                reference_type            varchar(30),
                reference_id              uuid,
                idempotency_key           varchar(100),
                CONSTRAINT partner_wallet_transactions_partner_idempotency_key
                    UNIQUE (partner_id, idempotency_key)
            );
            """);

        // 10a. grants for both app roles on the commerce partner-wallet tables.
        await ExecAsync(conn, """
            GRANT USAGE ON SCHEMA commerce TO app_user, app_admin;
            GRANT SELECT, INSERT, UPDATE, DELETE ON
                commerce.partner_wallet_accounts, commerce.partner_wallet_transactions
                TO app_user, app_admin;
            """);

        // 10b. rls_partner policies (FOR ALL TO app_user), mirroring rls_partner_wallet.sql.
        await ExecAsync(conn, """
            DROP POLICY IF EXISTS rls_partner ON commerce.partner_wallet_accounts;
            CREATE POLICY rls_partner ON commerce.partner_wallet_accounts FOR ALL TO app_user
                USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
                WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());

            DROP POLICY IF EXISTS rls_partner ON commerce.partner_wallet_transactions;
            CREATE POLICY rls_partner ON commerce.partner_wallet_transactions FOR ALL TO app_user
                USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
                WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());
            """);

        // 10c. activate RLS on the partner-wallet spine.
        await ExecAsync(conn, """
            ALTER TABLE commerce.partner_wallet_accounts     ENABLE ROW LEVEL SECURITY;
            ALTER TABLE commerce.partner_wallet_transactions ENABLE ROW LEVEL SECURITY;
            """);

        // 11. RaaS partner INVOICE spine (FULL-10 / issue #14). Trimmed, real-shaped mirror of
        //     db/patches/raas_partner_invoice_schema.sql + rls_partner_invoice.sql. Keyed by
        //     partner_id (same isolation key), with the GENERATED amount_due column exactly as
        //     production so the acceptance gate exercises the real DDL shape.
        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS commerce.partner_invoices (
                id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                partner_id     uuid NOT NULL,
                invoice_number varchar(40) NOT NULL UNIQUE,
                grand_total    numeric(14,2) NOT NULL DEFAULT 0,
                amount_paid    numeric(14,2) NOT NULL DEFAULT 0,
                amount_due     numeric(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
                status         varchar(20) NOT NULL DEFAULT 'issued'
            );
            """);

        // 11a. grants for both app roles on the commerce partner-invoice table.
        await ExecAsync(conn, """
            GRANT SELECT, INSERT, UPDATE, DELETE ON commerce.partner_invoices TO app_user, app_admin;
            """);

        // 11b. rls_partner policy (FOR ALL TO app_user), mirroring rls_partner_invoice.sql.
        await ExecAsync(conn, """
            DROP POLICY IF EXISTS rls_partner ON commerce.partner_invoices;
            CREATE POLICY rls_partner ON commerce.partner_invoices FOR ALL TO app_user
                USING      (kernel.rls_bypass() OR partner_id = kernel.current_partner_id())
                WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());
            """);

        // 11c. activate RLS on the partner-invoice spine.
        await ExecAsync(conn, """
            ALTER TABLE commerce.partner_invoices ENABLE ROW LEVEL SECURITY;
            """);
    }
}

/// <summary>Binds the RbacRlsFixture as a single shared instance for the whole test collection.</summary>
[CollectionDefinition("rbac-rls")]
public sealed class RbacRlsCollection : ICollectionFixture<RbacRlsFixture>
{
}
