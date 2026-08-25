using core.Application.Identity.Entitlements.Commands;
using core.Application.Identity.Entitlements.Dtos;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// §5's connective tissue: <c>entitlement = plan ∪ purchased add-ons</c> (docs/TASKS.md T-04).
///
/// <para>The rule that makes this safe to run against paying customers is the second half of that
/// union. A plan change re-expands the <c>bundle</c>-sourced rows and must never touch a
/// <c>manual</c> one — because a manual row is something the customer BOUGHT separately, and
/// silently removing it during a routine tier change is taking away something that was paid
/// for.</para>
///
/// <para>The bundles here are deliberately <b>unpriced</b>. A priced bundle also creates a
/// subscription and issues an invoice, and mixing billing into these assertions would mean a failure
/// no longer tells you which half broke.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class PlanChangeTests
{
    /// <summary>One platform for the whole class — brands need a parent, and creating a fresh one
    /// per test would collide on the platform code.</summary>
    private const string Platform = "44444444-4444-4444-4444-444444444444";

    private readonly RbacEfFixture _fx;
    public PlanChangeTests(RbacEfFixture fx) => _fx = fx;

    // 1 ── UPGRADE. Everything the new tier includes is licensed.
    [Fact]
    public async Task An_upgrade_grants_the_new_tiers_features()
    {
        if (!_fx.DockerAvailable) return;
        var brand = await SeedAsync();

        await ApplyAsync(brand, "t4-starter");
        Assert.Equal(["t4-bookings", "t4-scheduling"], await EnabledAsync(brand));

        await ApplyAsync(brand, "t4-pro");
        Assert.Equal(["t4-bookings", "t4-fleet", "t4-loyalty", "t4-scheduling"], await EnabledAsync(brand));
    }

    // 2 ── DOWNGRADE. Only what the new tier lacks goes, and it goes for real — a downgrade that
    //      leaves the old features licensed means nobody ever has a reason to stay on the high tier.
    [Fact]
    public async Task A_downgrade_removes_only_what_the_new_tier_lacks()
    {
        if (!_fx.DockerAvailable) return;
        var brand = await SeedAsync();

        await ApplyAsync(brand, "t4-pro");
        await ApplyAsync(brand, "t4-starter");

        Assert.Equal(["t4-bookings", "t4-scheduling"], await EnabledAsync(brand));
    }

    // 3 ── THE ONE THAT MATTERS. An à-la-carte add-on is something the customer paid for on top of
    //      their plan. A tier change must not take it away — that is the whole reason `source`
    //      exists as a column.
    [Fact]
    public async Task An_add_on_survives_a_plan_change_in_both_directions()
    {
        if (!_fx.DockerAvailable) return;
        var brand = await SeedAsync();

        await ApplyAsync(brand, "t4-starter");

        // Bought separately, not part of any tier here.
        await using (var db = _fx.NewContext())
            await new SetBrandFeatureCommandHandler(_fx.AsCore(db)).HandleAsync(
                new SetBrandFeatureCommand(brand, new SetBrandFeatureRequest("t4-whatsapp", true, null), null),
                CancellationToken.None);

        Assert.Contains("t4-whatsapp", await EnabledAsync(brand));

        await ApplyAsync(brand, "t4-pro");        // upgrade
        Assert.Contains("t4-whatsapp", await EnabledAsync(brand));

        await ApplyAsync(brand, "t4-starter");    // and back down
        Assert.Contains("t4-whatsapp", await EnabledAsync(brand));

        // …and it is still recorded as an add-on rather than being absorbed into the plan, so an
        // operator can still see it was bought separately and revoke it deliberately.
        await using var check = _fx.NewContext();
        var row = await check.Set<laundryghar.SharedDataModel.Entities.IdentityAccess.BrandFeature>()
            .AsNoTracking().FirstAsync(f => f.BrandId == brand && f.FeatureKey == "t4-whatsapp");
        Assert.Equal("manual", row.Source);
    }

    // 4 ── a manual DISABLE also survives. The mirror image of the test above, and the one people
    //      forget: if re-applying a plan silently re-enabled a feature an operator had deliberately
    //      withheld, "switch this off for them" would be undone by the next billing change.
    [Fact]
    public async Task A_manual_disable_survives_a_plan_change()
    {
        if (!_fx.DockerAvailable) return;
        var brand = await SeedAsync();

        await ApplyAsync(brand, "t4-pro");
        await using (var db = _fx.NewContext())
            await new SetBrandFeatureCommandHandler(_fx.AsCore(db)).HandleAsync(
                new SetBrandFeatureCommand(brand, new SetBrandFeatureRequest("t4-loyalty", false, null), null),
                CancellationToken.None);

        Assert.DoesNotContain("t4-loyalty", await EnabledAsync(brand));

        await ApplyAsync(brand, "t4-pro");   // re-apply the same tier
        Assert.DoesNotContain("t4-loyalty", await EnabledAsync(brand));
    }

    // 5 ── IDEMPOTENT. Applying the same tier twice changes nothing and, in particular, does not
    //      accumulate duplicate rows — the plan is re-expanded on every subscription event, so a
    //      non-idempotent apply would grow the table forever.
    [Fact]
    public async Task Re_applying_the_same_tier_is_a_no_op()
    {
        if (!_fx.DockerAvailable) return;
        var brand = await SeedAsync();

        await ApplyAsync(brand, "t4-pro");
        var first = await EnabledAsync(brand);
        var firstCount = await RowCountAsync(brand);

        await ApplyAsync(brand, "t4-pro");
        await ApplyAsync(brand, "t4-pro");

        Assert.Equal(first, await EnabledAsync(brand));
        Assert.Equal(firstCount, await RowCountAsync(brand));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task ApplyAsync(Guid brand, string bundle)
    {
        await using var db = _fx.NewContext();
        await new ApplyBundleToBrandCommandHandler(_fx.AsCore(db)).HandleAsync(
            new ApplyBundleToBrandCommand(brand, new ApplyBundleRequest(bundle), null),
            CancellationToken.None);
    }

    private async Task<string[]> EnabledAsync(Guid brand)
    {
        await using var db = _fx.NewContext();
        return await db.Set<laundryghar.SharedDataModel.Entities.IdentityAccess.BrandFeature>()
            .AsNoTracking()
            .Where(f => f.BrandId == brand && f.Enabled && f.FeatureKey.StartsWith("t4-"))
            .Select(f => f.FeatureKey)
            .OrderBy(k => k)
            .ToArrayAsync();
    }

    private async Task<int> RowCountAsync(Guid brand)
    {
        await using var db = _fx.NewContext();
        return await db.Set<laundryghar.SharedDataModel.Entities.IdentityAccess.BrandFeature>()
            .AsNoTracking().CountAsync(f => f.BrandId == brand);
    }

    /// <summary>A brand plus two unpriced tiers: starter ⊂ pro, and one sellable feature
    /// (`t4-whatsapp`) that belongs to NEITHER, so it can only ever be an add-on.</summary>
    private async Task<Guid> SeedAsync()
    {
        var brand = Guid.NewGuid();
        await using var conn = await _fx.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO tenancy_org.platforms (id, code, name)
            VALUES ('{Platform}', 'T4PLAT', 'Plan change platform')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO tenancy_org.brands (id, platform_id, code, name, vertical_key)
            VALUES ('{brand}', '{Platform}', 'T4{brand.ToString("N")[..8]}', 'Plan change test', 'laundry');

            INSERT INTO identity_access.features (key, name, is_core, is_sellable, status)
            VALUES ('t4-bookings','Bookings',false,true,'active'),
                   ('t4-scheduling','Scheduling',false,true,'active'),
                   ('t4-fleet','Fleet',false,true,'active'),
                   ('t4-loyalty','Loyalty',false,true,'active'),
                   ('t4-whatsapp','WhatsApp bot',false,true,'active')
            ON CONFLICT (key) DO NOTHING;

            INSERT INTO identity_access.module_bundle (code, name, description)
            VALUES ('t4-starter','T4 Starter',null), ('t4-pro','T4 Pro',null)
            ON CONFLICT (code) DO NOTHING;

            INSERT INTO identity_access.bundle_feature (bundle_code, feature_key) VALUES
                ('t4-starter','t4-bookings'), ('t4-starter','t4-scheduling'),
                ('t4-pro','t4-bookings'), ('t4-pro','t4-scheduling'),
                ('t4-pro','t4-fleet'), ('t4-pro','t4-loyalty')
            ON CONFLICT DO NOTHING;
            """;
        await cmd.ExecuteNonQueryAsync();

        return brand;
    }
}
