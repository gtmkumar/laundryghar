using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// §6's simplified surface: "max 8 roles, 10 permission groups (not 300 permissions), plain do/don't
/// language", added as a PRESET layer over the engine (§6.3: "the engine's power stays").
///
/// <para>The constraint IS the design — §12's final risk is "role sprawl: resist adding role #9" —
/// so the counts are asserted, not assumed. And because 0013 is purely additive, the other important
/// test is the negative one: it must not have touched a single grant.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class RoleSurfaceTests
{
    private readonly RbacRlsFixture _fx;
    public RoleSurfaceTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── §6's numbers are a ceiling, and they are enforced.
    [Fact]
    public async Task there_are_at_most_eight_presets_and_ten_groups()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();

        var presets = Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
            "SELECT count(*) FROM identity_access.role_presets"));
        var groups = Convert.ToInt64(await RbacRlsFixture.ScalarAsync(conn,
            "SELECT count(*) FROM identity_access.permission_groups"));

        Assert.Equal(8L, presets);
        Assert.Equal(10L, groups);
    }

    // 2 ── every preset carries BOTH halves of §6's plain language. The "does not" half is the
    //      useful one — it is what stops someone granting Manager when they meant Staff.
    [Fact]
    public async Task every_preset_says_what_it_does_and_does_not_do()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var incomplete = await RbacRlsFixture.ScalarAsync(conn, """
            SELECT string_agg(key, ', ') FROM identity_access.role_presets
            WHERE btrim(coalesce(does, '')) = '' OR btrim(coalesce(does_not, '')) = ''
            """);

        Assert.True(incomplete is null or DBNull, $"presets missing plain language: {incomplete}");
    }

    // 3 ── the eight are exactly the eight §6.1 names.
    [Fact]
    public async Task the_presets_are_the_eight_the_strategy_names()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var keys = (string?)await RbacRlsFixture.ScalarAsync(conn,
            "SELECT string_agg(key, ',' ORDER BY sort_order) FROM identity_access.role_presets");

        Assert.Equal(
            "platform_admin,platform_support,owner,manager,staff,rider,facility_staff,customer",
            keys);
    }

    // 4 ── THE ADDITIVE GUARANTEE, checked against the migration's own text.
    //
    //      §6.3 says presets sit ON the engine; a replacement would have meant a destructive grant
    //      migration across every brand. The property that matters is therefore "0013 does not write
    //      to the grant tables at all" — and that is checked by reading the shipped SQL rather than
    //      by inspecting a fixture, because a fixture only shows what these tests happened to seed.
    //      A future edit that adds an UPDATE on role_permissions fails here even if every row in
    //      every test database still looks right.
    [Fact]
    public async Task Migration_0013_never_writes_to_a_grant_table()
    {
        var sql = (await File.ReadAllTextAsync(RepoPaths.Migration("0013_role_presets_and_groups.up.sql")))
            .ToLowerInvariant();

        // Comments describe these tables at length; only executable writes matter, so match the
        // statement forms rather than the bare table names.
        foreach (var table in new[] { "roles", "role_permissions", "user_scope_memberships", "permissions" })
        foreach (var verb in new[] { $"insert into identity_access.{table}",
                                     $"update identity_access.{table}",
                                     $"delete from identity_access.{table}" })
        {
            Assert.False(sql.Contains(verb),
                $"0013 must be purely additive, but it contains `{verb}` — presets are a "
                + "presentation over the engine, never a rewrite of it (§6.3).");
        }
    }

    // 4b ── and every preset that names an engine role names one of the SHIPPED role codes. A typo
    //       here produces a preset that silently resolves to nothing in production.
    [Fact]
    public async Task Every_preset_names_a_real_engine_role()
    {
        if (!_fx.DockerAvailable) return;

        // The 17 system roles as shipped (db/patches + seed). Written out rather than read from the
        // fixture, whose roles table holds only what individual tests seed.
        var shipped = new HashSet<string>
        {
            "auditor", "brand_admin", "catalogue_manager", "finance_manager", "franchise_owner",
            "hub_operator", "hub_supervisor", "operations_manager", "partner_admin",
            "partner_operator", "platform_admin", "regional_manager", "rider", "salon_manager",
            "salon_staff", "store_admin", "store_staff", "support", "support_lead",
            "warehouse_staff", "warehouse_supervisor",
        };

        await using var conn = await _fx.OpenSuperuserAsync();
        var codes = (string?)await RbacRlsFixture.ScalarAsync(conn,
            "SELECT string_agg(role_code, ',') FROM identity_access.role_presets WHERE role_code IS NOT NULL");

        foreach (var code in (codes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            Assert.True(shipped.Contains(code), $"preset points at unknown engine role '{code}'");
    }

    // 4c ── THE INVARIANT THAT WOULD HAVE CAUGHT IT. A module claimed by two groups means one
    //       permission counted twice, in two cells that can never both be right.
    //
    //       Migration 0013 shipped four such misclassifications, found only by diffing the live
    //       matrix against §6.2 by hand. The worst put `royalty` and `subscription` — a franchisee's
    //       royalty and a CUSTOMER's recurring plan — into "Subscription & Billing (pays us)", so
    //       the Manager preset appeared to hold full control of the platform subscription: a flat
    //       violation of Law 1 that had never actually happened. 0013's own comment argued a matrix
    //       that lies is worse than none; this is the assertion that keeps it honest.
    [Fact]
    public async Task No_module_is_claimed_by_two_permission_groups()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var duplicated = await RbacRlsFixture.ScalarAsync(conn, """
            SELECT string_agg(m || ' x' || cnt, ', ')
            FROM (
                SELECT unnest(permission_modules) AS m, count(*) AS cnt
                FROM   identity_access.permission_groups
                GROUP  BY 1 HAVING count(*) > 1
            ) d
            """);

        Assert.True(duplicated is null or DBNull,
            $"module(s) in more than one §6.2 group — a permission counted twice: {duplicated}");
    }

    // 4d ── §6.2 group 10 is "Subscription & Billing (**pays us**)". Three different money flows
    //       exist and only one is ours: `royalty` is a franchisee paying their brand, `subscription`
    //       is a customer's recurring plan. Neither may sit in this group, or an ordinary provider
    //       role reads as controlling the platform subscription.
    [Fact]
    public async Task The_billing_group_covers_only_the_platform_subscription()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var modules = (string?)await RbacRlsFixture.ScalarAsync(conn,
            "SELECT array_to_string(permission_modules, ',') FROM identity_access.permission_groups WHERE key = 'billing'");

        var set = (modules ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("royalty", set);
        Assert.DoesNotContain("subscription", set);
        Assert.Contains("saas", set);
    }

    // 4e ── the `staff` group is "Staff & Riders" meaning MANAGING them. The bare `rider` module is
    //       rider.tasks.* — a rider's own assigned jobs. Confusing the two made a Rider look like a
    //       people manager AND emptied the Rider row where §6.2 says "own".
    [Fact]
    public async Task A_riders_own_tasks_are_dispatch_not_staff_management()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var staff = (string?)await RbacRlsFixture.ScalarAsync(conn,
            "SELECT array_to_string(permission_modules, ',') FROM identity_access.permission_groups WHERE key = 'staff'");
        var dispatch = (string?)await RbacRlsFixture.ScalarAsync(conn,
            "SELECT array_to_string(permission_modules, ',') FROM identity_access.permission_groups WHERE key = 'dispatch'");

        Assert.DoesNotContain("rider", (staff ?? "").Split(','));
        Assert.Contains("riders", (staff ?? "").Split(','));
        Assert.Contains("rider", (dispatch ?? "").Split(','));
    }

    // 5 ── `customer` deliberately maps to NO engine role: customers are not in the staff RBAC graph
    //      at all (docs/rbac.md §2). Listed only so the §6 picture is complete.
    [Fact]
    public async Task the_customer_preset_maps_to_no_engine_role()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var roleCode = await RbacRlsFixture.ScalarAsync(conn,
            "SELECT role_code FROM identity_access.role_presets WHERE key = 'customer'");

        Assert.True(roleCode is null or DBNull);
    }

    // 6 ── feature-gated presets match the engine gating from migration 0006, or the two layers
    //      would disagree about whether a brand can use a role.
    [Fact]
    public async Task feature_gated_presets_agree_with_the_engine()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenSuperuserAsync();
        var gated = (string?)await RbacRlsFixture.ScalarAsync(conn, """
            SELECT string_agg(key || '=' || requires_feature, ',' ORDER BY key)
            FROM   identity_access.role_presets WHERE requires_feature IS NOT NULL
            """);

        Assert.Equal("facility_staff=processing_facility,rider=fleet", gated);
    }
}
