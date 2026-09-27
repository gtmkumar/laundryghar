using System.Text.Json;
using laundryghar.Utilities.Authorization.Abac;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// The load-bearing test of A5: <c>authz.permits()</c> in PostgreSQL and
/// <c>ConditionEvaluator</c>/<c>PolicyDecisionPoint</c> in C# must reach the SAME answer from the
/// SAME policy rows.
///
/// <para>The whole premise of docs/ABAC_IMPLEMENTATION_PLAN.md §4 is that one rule store drives both
/// the API and the database. Two evaluators over one store is only an improvement on the status quo
/// — 136 hand-written RLS predicates that no application code reads — if they cannot disagree. A
/// divergence here would be worse than the drift it replaced, because the shared row store makes
/// them LOOK synchronised.</para>
///
/// <para>Applies db/migrations/0025 verbatim onto a minimal fixture, so what is under test is the
/// file that ships, not a paraphrase of it. Requires Docker; self-skips when no container runtime
/// is reachable.</para>
/// </summary>
public sealed class AbacSqlParityTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    // The parts of migration 0024 this test needs, without partman or the tenancy_org FK: the
    // policy tables, the kernel session-variable helpers the 0025 functions call, and the two roles
    // its GRANTs name.
    private const string Fixture = """
        CREATE ROLE app_user  NOLOGIN;
        CREATE ROLE app_admin NOLOGIN;

        CREATE SCHEMA kernel;
        CREATE SCHEMA authz;

        CREATE FUNCTION kernel.current_brand_id()     RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_brand_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.current_franchise_id() RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_franchise_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.current_store_id()     RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_store_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.current_user_id()      RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_user_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.current_customer_id()  RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_customer_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.current_partner_id()   RETURNS uuid LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.current_partner_id', true), '')::uuid $f$;
        CREATE FUNCTION kernel.rls_bypass()           RETURNS boolean LANGUAGE sql STABLE AS
            $f$ SELECT lower(coalesce(current_setting('app.bypass_rls', true), 'false'))
                       IN ('on','true','1','yes','t') $f$;

        CREATE TABLE authz.attribute (
            key varchar(120) PRIMARY KEY, category varchar(20) NOT NULL,
            data_type varchar(20) NOT NULL, resolver_key varchar(80), description text,
            is_active boolean NOT NULL DEFAULT true);

        CREATE TABLE authz.resource_type (
            key varchar(120) PRIMARY KEY, schema_name varchar(63), table_name varchar(63),
            attribute_map jsonb NOT NULL DEFAULT '{}'::jsonb, description text,
            is_active boolean NOT NULL DEFAULT true,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now());

        CREATE TABLE authz.action (key varchar(60) PRIMARY KEY, description text);

        CREATE TABLE authz.policy (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            key varchar(160) NOT NULL, version int NOT NULL DEFAULT 1, brand_id uuid,
            description text, effect varchar(10) NOT NULL CHECK (effect IN ('permit','deny')),
            resource_type varchar(120) NOT NULL, action varchar(60) NOT NULL,
            permission_code varchar(120), priority int NOT NULL DEFAULT 100,
            effective_from timestamptz NOT NULL DEFAULT now(), effective_to timestamptz,
            is_active boolean NOT NULL DEFAULT true,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT policy_key_version_unique UNIQUE (key, version));

        CREATE TABLE authz.policy_condition (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            policy_id uuid NOT NULL REFERENCES authz.policy(id) ON DELETE CASCADE,
            parent_id uuid REFERENCES authz.policy_condition(id) ON DELETE CASCADE,
            node_type varchar(12) NOT NULL, left_attribute varchar(120), operator varchar(20),
            right_kind varchar(12), right_value jsonb, sort_order int NOT NULL DEFAULT 0);
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
        await Exec(conn, await File.ReadAllTextAsync(
            RepoPaths.Migration("0025_authz_permits_and_rls_generator.up.sql")));
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // ── The comparison matrix ─────────────────────────────────────────────────────────────────
    // Each row is one (left, operator, right) triple stated as jsonb. Both evaluators are handed
    // exactly the same three values and must return the same three-valued answer. Absent operands
    // are expressed by passing SQL NULL / omitting the bag key.

    public static TheoryData<string, string?, string?, string> Cases() => new()
    {
        // op,            left jsonb,        right jsonb,     expected
        { ConditionOperator.Eq,         "\"abc\"",   "\"abc\"",  "True" },
        { ConditionOperator.Eq,         "\"abc\"",   "\"ABC\"",  "True" },   // case-insensitive
        { ConditionOperator.Eq,         "\"abc\"",   "\"xyz\"",  "False" },
        { ConditionOperator.Eq,         "5",         "5.0",      "True" },   // numeric equality
        { ConditionOperator.Eq,         null,        "\"abc\"",  "Indeterminate" },
        { ConditionOperator.Eq,         "null",      "null",     "True" },
        { ConditionOperator.Eq,         "null",      "\"abc\"",  "False" },
        { ConditionOperator.Neq,        "\"abc\"",   "\"xyz\"",  "True" },

        { ConditionOperator.IsNull,     "null",      null,       "True" },
        { ConditionOperator.IsNull,     "\"abc\"",   null,       "False" },
        { ConditionOperator.IsNull,     null,        null,       "Indeterminate" },
        { ConditionOperator.IsNotNull,  "\"abc\"",   null,       "True" },

        { ConditionOperator.Lt,         "4",         "5",        "True" },
        { ConditionOperator.Lte,        "5",         "5",        "True" },
        { ConditionOperator.Gt,         "6",         "5",        "True" },
        { ConditionOperator.Gte,        "4",         "5",        "False" },
        // Ordering non-numeric, non-temporal values is meaningless and must fail closed.
        { ConditionOperator.Lt,         "\"abc\"",   "\"xyz\"",  "Indeterminate" },

        { ConditionOperator.In,         "\"b\"",     "[\"a\",\"b\"]", "True" },
        { ConditionOperator.In,         "\"c\"",     "[\"a\",\"b\"]", "False" },
        { ConditionOperator.In,         "\"b\"",     "\"b\"",    "Indeterminate" }, // right not a list
        { ConditionOperator.NotIn,      "\"c\"",     "[\"a\",\"b\"]", "True" },

        // contains: element membership when the left is a list, substring when it is text.
        { ConditionOperator.Contains,   "[\"auditor\"]", "\"auditor\"", "True" },
        { ConditionOperator.Contains,   "[\"auditor\"]", "\"admin\"",   "False" },
        { ConditionOperator.Contains,   "\"foobar\"",    "\"OOB\"",     "True" },
        { ConditionOperator.Contains,   "null",          "\"a\"",       "False" },

        { ConditionOperator.StartsWith, "\"foobar\"", "\"FOO\"",  "True" },
        { ConditionOperator.StartsWith, "\"foobar\"", "\"bar\"",  "False" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Sql_and_dotnet_agree_on_every_operator(
        string op, string? leftJson, string? rightJson, string expected)
    {
        if (!_dockerAvailable) return;

        // ── SQL side ──────────────────────────────────────────────────────────────────────────
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT authz.compare(@op, @l::jsonb, @r::jsonb)", conn);
        cmd.Parameters.AddWithValue("op", op);
        cmd.Parameters.AddWithValue("l", (object?)leftJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("r", (object?)rightJson ?? DBNull.Value);

        var raw = await cmd.ExecuteScalarAsync();
        var sql = raw is null or DBNull ? Ternary.Indeterminate
                : (bool)raw ? Ternary.True : Ternary.False;

        // ── C# side ───────────────────────────────────────────────────────────────────────────
        var bag = AbacAttributeBag.Empty();
        if (leftJson is not null) bag.Set("resource.status", Unwrap(leftJson));

        var condition = new AbacCondition
        {
            NodeType = ConditionNodeType.Compare,
            LeftAttribute = "resource.status",
            Operator = op,
            RightKind = "literal",
            RightValue = rightJson is null ? null : Unwrap(rightJson),
        };

        var dotnet = ConditionEvaluator.Evaluate(condition, bag);

        Assert.Equal(Enum.Parse<Ternary>(expected), sql);
        Assert.Equal(sql, dotnet);
    }

    // ── The combining algorithm ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unevaluable_deny_denies_in_sql_exactly_as_it_does_in_the_pdp()
    {
        if (!_dockerAvailable) return;

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await Exec(conn, """
            INSERT INTO authz.resource_type (key) VALUES ('orders') ON CONFLICT DO NOTHING;
            INSERT INTO authz.action (key) VALUES ('read') ON CONFLICT DO NOTHING;

            INSERT INTO authz.policy (key, effect, resource_type, action, priority)
            VALUES ('t.permit', 'permit', 'orders', 'read', 500)
            ON CONFLICT DO NOTHING;

            INSERT INTO authz.policy (key, effect, resource_type, action, priority)
            VALUES ('t.deny-unresolvable', 'deny', 'orders', 'read', 100)
            ON CONFLICT DO NOTHING;

            -- The deny compares an attribute nothing supplies → indeterminate → must still deny.
            INSERT INTO authz.policy_condition
                (policy_id, node_type, left_attribute, operator, right_kind, right_value)
            SELECT id, 'compare', 'subject.roles', 'contains', 'literal', '"auditor"'
            FROM authz.policy WHERE key = 't.deny-unresolvable';
            """);

        // app.current_roles deliberately NOT set → kernel.current_roles() is NULL → indeterminate.
        await using var cmd = new NpgsqlCommand(
            "SELECT authz.permits('orders', 'read', jsonb_build_object('brand_id', gen_random_uuid()))",
            conn);
        var permitted = (bool)(await cmd.ExecuteScalarAsync())!;

        Assert.False(permitted);

        // And the same shape through the C# PDP.
        var pdp = new PolicyDecisionPoint();
        var decision = pdp.Decide(
            new AbacRequest("orders", "read", AbacAttributeBag.Empty(), DateTimeOffset.UtcNow),
            [
                new AbacPolicy { Key = "t.permit", Effect = PolicyEffect.Permit,
                                 ResourceType = "orders", Action = "read", Priority = 500 },
                new AbacPolicy { Key = "t.deny-unresolvable", Effect = PolicyEffect.Deny,
                                 ResourceType = "orders", Action = "read", Priority = 100,
                                 Condition = new AbacCondition
                                 {
                                     NodeType = ConditionNodeType.Compare,
                                     LeftAttribute = AbacAttributeKeys.SubjectRoles,
                                     Operator = ConditionOperator.Contains,
                                     RightKind = "literal", RightValue = "auditor",
                                 } },
            ]);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public async Task Set_roles_resolves_the_deny_and_the_permit_then_applies()
    {
        if (!_dockerAvailable) return;

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await Exec(conn, """
            INSERT INTO authz.resource_type (key) VALUES ('orders') ON CONFLICT DO NOTHING;
            INSERT INTO authz.action (key) VALUES ('read') ON CONFLICT DO NOTHING;
            INSERT INTO authz.policy (key, effect, resource_type, action, priority)
            VALUES ('p2.permit', 'permit', 'orders', 'read', 500) ON CONFLICT DO NOTHING;
            INSERT INTO authz.policy (key, effect, resource_type, action, priority)
            VALUES ('p2.deny', 'deny', 'orders', 'read', 100) ON CONFLICT DO NOTHING;
            INSERT INTO authz.policy_condition
                (policy_id, node_type, left_attribute, operator, right_kind, right_value)
            SELECT id, 'compare', 'subject.roles', 'contains', 'literal', '"auditor"'
            FROM authz.policy WHERE key = 'p2.deny';
            """);

        // A non-auditor: the deny does not match, the unconditional permit applies.
        await Exec(conn, "SELECT set_config('app.current_roles', 'brand_admin', false)");
        Assert.True((bool)(await Scalar(conn,
            "SELECT authz.permits('orders','read', jsonb_build_object('brand_id', gen_random_uuid()))"))!);

        // An auditor: the deny matches and overrides the permit.
        await Exec(conn, "SELECT set_config('app.current_roles', 'auditor', false)");
        Assert.False((bool)(await Scalar(conn,
            "SELECT authz.permits('orders','read', jsonb_build_object('brand_id', gen_random_uuid()))"))!);

        // Resolved-but-empty is a real answer: no roles → the deny cannot match → permitted.
        await Exec(conn, "SELECT set_config('app.current_roles', '', false)");
        Assert.True((bool)(await Scalar(conn,
            "SELECT authz.permits('orders','read', jsonb_build_object('brand_id', gen_random_uuid()))"))!);

        // The '?' sentinel means UNRESOLVED, and an unevaluable deny fails closed.
        await Exec(conn, "SELECT set_config('app.current_roles', '?', false)");
        Assert.False((bool)(await Scalar(conn,
            "SELECT authz.permits('orders','read', jsonb_build_object('brand_id', gen_random_uuid()))"))!);
    }

    [Fact]
    public async Task Within_scope_matches_the_membership_level_and_platform_matches_everything()
    {
        if (!_dockerAvailable) return;

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var franchise = Guid.NewGuid();
        var other = Guid.NewGuid();

        await Exec(conn, $"SELECT set_config('app.current_scope_nodes', 'franchise:{franchise}', false)");

        Assert.True((bool)(await Scalar(conn,
            $"SELECT authz.within_scope(jsonb_build_object('franchise_id', '{franchise}'::text))"))!);
        Assert.False((bool)(await Scalar(conn,
            $"SELECT authz.within_scope(jsonb_build_object('franchise_id', '{other}'::text))"))!);

        await Exec(conn, "SELECT set_config('app.current_scope_nodes', 'platform', false)");
        Assert.True((bool)(await Scalar(conn,
            $"SELECT authz.within_scope(jsonb_build_object('franchise_id', '{other}'::text))"))!);

        // Unresolved memberships → NULL → indeterminate, never an implicit allow.
        await Exec(conn, "SELECT set_config('app.current_scope_nodes', '?', false)");
        Assert.Null(await Scalar(conn,
            $"SELECT authz.within_scope(jsonb_build_object('franchise_id', '{other}'::text))"));

        // Present but empty → false, a real "you hold no memberships".
        await Exec(conn, "SELECT set_config('app.current_scope_nodes', '', false)");
        Assert.False((bool)(await Scalar(conn,
            $"SELECT authz.within_scope(jsonb_build_object('franchise_id', '{other}'::text))"))!);
    }

    [Fact]
    public async Task Default_deny_holds_when_no_policy_targets_the_pair()
    {
        if (!_dockerAvailable) return;

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        Assert.False((bool)(await Scalar(conn,
            "SELECT authz.permits('nothing_targets_this', 'read', '{}'::jsonb)"))!);
    }

    [Fact]
    public async Task The_generator_emits_a_policy_body_bound_to_the_registered_columns()
    {
        if (!_dockerAvailable) return;

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await Exec(conn, """
            INSERT INTO authz.resource_type (key, schema_name, table_name, attribute_map)
            VALUES ('gen_test', 'public', 'widgets',
                    '{"resource.brand_id":"brand_id","resource.id":"id"}'::jsonb)
            ON CONFLICT (key) DO UPDATE SET schema_name = EXCLUDED.schema_name,
                                            table_name = EXCLUDED.table_name,
                                            attribute_map = EXCLUDED.attribute_map;
            """);

        var body = (string)(await Scalar(conn,
            "SELECT authz.generated_policy_body('gen_test', 'read')"))!;

        Assert.Contains("authz.permits('gen_test', 'read'", body);
        Assert.Contains("jsonb_build_object('brand_id', brand_id", body);
        Assert.Contains("kernel.rls_bypass()", body);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    /// <summary>jsonb text → the CLR shape NpgsqlPolicySource would have produced, so the C# side
    /// is fed exactly what it would see in production rather than a hand-tuned equivalent.</summary>
    private static object? Unwrap(string json)
    {
        var el = JsonDocument.Parse(json).RootElement;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray()
                .Select(e => (object?)(e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()))
                .ToList(),
            _ => null,
        };
    }
}
