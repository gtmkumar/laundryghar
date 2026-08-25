using core.Application.Identity.AccessControl.Queries.GetNavigator;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// The sidebar's ORDER, against a real Postgres.
///
/// <para>Ordering looks like the kind of thing that cannot break, which is why it broke. The
/// handler sorted by <c>nav_order</c> alone and three pairs of seeded rows shared a value
/// (cms/promotions, fabrics/subscriptions, appointments/warehouse). An unordered tie is resolved
/// by whatever the storage engine hands back — the live database returned CMS above Promotions
/// while the browser rendered Promotions above CMS, and neither was wrong. A menu that can
/// silently rearrange itself between two page loads has given up the muscle memory that is most
/// of what a menu is for.</para>
///
/// <para>Migration 0022 removed the ties and added a unique index so new ones cannot be seeded;
/// this pins the handler side, which is what decides the outcome if a tie ever exists again.</para>
///
/// <para>Test 1 uses TWELVE tied rows, not two or three. That is the whole difference between a
/// test and a decoration here: with three rows an unordered scan lands on the expected order by
/// chance often enough that deleting the tiebreaker still passed — measured, not assumed. Twelve
/// rows seeded out of order collide with sorted order about once in 479,001,600 runs.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class NavigatorOrderingTests
{
    private readonly RbacEfFixture _fx;
    public NavigatorOrderingTests(RbacEfFixture fx) => _fx = fx;

    // A fixed, deliberately unsorted permutation of a..l. Fixed rather than shuffled at runtime so
    // a failure is reproducible from the test name alone.
    private static readonly string[] Scrambled =
        ["h", "c", "k", "a", "f", "l", "b", "i", "e", "j", "d", "g"];

    // 1 ── THE BUG: rows sharing a nav_order must still come back in a fixed order.
    [Fact]
    public async Task Rows_sharing_a_nav_order_are_ordered_by_key_not_by_chance()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();
        var section = $"sect_{t}";

        await SeedAsync(Scrambled.Select(x => Mod($"navord_{t}_{x}", section, navOrder: 700)).ToArray());

        var keys = await OrderedKeysAsync($"navord_{t}_");

        Assert.Equal(Scrambled.OrderBy(x => x, StringComparer.Ordinal).Select(x => $"navord_{t}_{x}"), keys);
    }

    // 2 ── and the tiebreaker stays a TIEbreaker: an explicit nav_order still wins over the key.
    //      Without this, "sort by key" would satisfy test 1 while destroying the curated order.
    [Fact]
    public async Task Nav_order_still_outranks_the_key()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();
        var section = $"sect_{t}";

        await SeedAsync(
            Mod($"navrank_{t}_a", section, navOrder: 800),   // alphabetically first…
            Mod($"navrank_{t}_z", section, navOrder: 700));  // …but ordered second

        var keys = await OrderedKeysAsync($"navrank_{t}_");

        Assert.Equal([$"navrank_{t}_z", $"navrank_{t}_a"], keys);
    }

    // 3 ── the same query run twice returns the same order. This is the property the user actually
    //      experiences; tests 1-2 pin the mechanism, this pins the promise.
    [Fact]
    public async Task The_order_is_stable_across_repeated_calls()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();
        var section = $"sect_{t}";

        await SeedAsync(Scrambled.Select(x => Mod($"navstab_{t}_{x}", section, navOrder: 900)).ToArray());

        var first = await OrderedKeysAsync($"navstab_{t}_");
        var again = await OrderedKeysAsync($"navstab_{t}_");

        Assert.Equal(first, again);
        Assert.Equal(Scrambled.Length, first.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task SeedAsync(params AppModule[] modules)
    {
        await using var db = _fx.NewContext();
        db.AddRange(modules);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The navigator's items for one test's rows, IN ORDER. Filtered by the caller's unique prefix
    /// because the fixture's database is shared across the whole collection.
    /// </summary>
    private async Task<List<string>> OrderedKeysAsync(string prefix)
    {
        await using var db = _fx.NewContext();
        var config = new ConfigurationBuilder().Build();   // entitlement not enforced
        var user = new FakeCurrentUser { UserId = Guid.NewGuid(), IsPlatformAdmin = true };

        var nav = await new GetNavigatorQueryHandler(_fx.AsCore(db), user, config)
            .HandleAsync(new GetNavigatorQuery(), CancellationToken.None);

        return nav.Sections
            .SelectMany(s => s.Items)
            .Select(i => i.Key)
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
    }

    private static AppModule Mod(string key, string section, int navOrder) => new()
    {
        Id = Guid.NewGuid(), Key = key, Label = key, Section = section,
        NavOrder = navOrder, MatrixOrder = 100,
        ShowInNav = true, ShowInMatrix = false, RequiredPermission = null,
        PermissionModules = [], IsCore = true, FeatureKey = null, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
}
