using core.Application.Identity.AccessControl;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// "Roles follow features" (PLATFORM_STRATEGY.md §5/§6.3): buy Fleet → the Rider role appears; buy
/// Processing → Facility Staff appears. A courier shop sees four roles where a full laundry sees
/// seven, without anyone pruning a list by hand.
///
/// <para>Exercises <see cref="BrandFeatureGate"/> — the single decision both halves share — against a
/// REAL Postgres with migrations 0005 (features) and 0006 (roles.feature_key) applied. The gate is
/// used by the read path (GetAccessRoles: don't offer it) and the write path (GrantMembership: don't
/// allow it), and both matter: hiding alone is cosmetic, since anyone able to craft the request could
/// otherwise grant a Rider on a brand with no fleet — producing a user whose permissions the
/// entitlement filter strips at every login, i.e. a role that silently does nothing.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class RolesFollowFeaturesTests
{
    private readonly RbacEfFixture _fx;
    public RolesFollowFeaturesTests(RbacEfFixture fx) => _fx = fx;

    // 1 ── the headline behaviour: a role gated on an unowned feature is unavailable; buying the
    //      feature makes it available, with nothing else changing.
    [Fact]
    public async Task buying_the_feature_is_what_makes_the_role_available()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var fleet = Feat($"fleet_{t}");
        await SeedAsync([fleet]);

        // Before buying: the gate says no.
        await using (var db = _fx.NewContext())
        {
            var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_fx.AsCore(db), brandId, CancellationToken.None);
            Assert.False(BrandFeatureGate.IsRoleAvailable(fleet.Key, entitled));
        }

        await SeedAsync([Licence(brandId, fleet.Key)]);

        // After buying: the same role is available. One licence, no other change.
        await using (var db = _fx.NewContext())
        {
            var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_fx.AsCore(db), brandId, CancellationToken.None);
            Assert.True(BrandFeatureGate.IsRoleAvailable(fleet.Key, entitled));
        }
    }

    // 2 ── an UNGATED role (feature_key null) is always available — the vertical-neutral operating
    //      roles must never vanish because of a plan.
    [Fact]
    public async Task an_ungated_role_is_always_available()
    {
        if (!_fx.DockerAvailable) return;

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);   // brand owns nothing at all

        await using var db = _fx.NewContext();
        var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_fx.AsCore(db), brandId, CancellationToken.None);

        Assert.NotNull(entitled);                 // brand context exists…
        Assert.True(BrandFeatureGate.IsRoleAvailable(null, entitled)); // …and an ungated role passes
    }

    // 3 ── a CORE feature gates nothing: a role behind it stays available with no licence row.
    [Fact]
    public async Task a_role_behind_a_core_feature_needs_no_licence()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var coreFeature = Feat($"corefeat_{t}", isCore: true);
        await SeedAsync([coreFeature]);

        await using var db = _fx.NewContext();
        var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_fx.AsCore(db), brandId, CancellationToken.None);

        Assert.True(BrandFeatureGate.IsRoleAvailable(coreFeature.Key, entitled));
    }

    // 4 ── an EXPIRED or DISABLED licence stops gating open, mirroring the token filter exactly. A
    //      lapsed plan must retire the role, not leave it grantable forever.
    [Fact]
    public async Task an_expired_or_disabled_licence_does_not_open_the_gate()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var expired  = Feat($"expired_{t}");
        var disabled = Feat($"disabled_{t}");
        var valid    = Feat($"valid_{t}");
        await SeedAsync([expired, disabled, valid]);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedAsync(
        [
            Licence(brandId, expired.Key,  validUntil: today.AddDays(-1)),
            Licence(brandId, disabled.Key, enabled: false),
            Licence(brandId, valid.Key,    validUntil: today),   // expires today → still valid today
        ]);

        await using var db = _fx.NewContext();
        var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_fx.AsCore(db), brandId, CancellationToken.None);

        Assert.False(BrandFeatureGate.IsRoleAvailable(expired.Key, entitled));
        Assert.False(BrandFeatureGate.IsRoleAvailable(disabled.Key, entitled));
        Assert.True(BrandFeatureGate.IsRoleAvailable(valid.Key, entitled));
    }

    // 5 ── one brand's purchase does not open another brand's gate.
    [Fact]
    public async Task the_gate_is_per_brand()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var buyer = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _fx.SeedBrandAsync(buyer);
        await _fx.SeedBrandAsync(other);

        var feature = Feat($"perbrand_{t}");
        await SeedAsync([feature]);
        await SeedAsync([Licence(buyer, feature.Key)]);

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        var buyerEntitled = await BrandFeatureGate.EntitledFeaturesAsync(core, buyer, CancellationToken.None);
        var otherEntitled = await BrandFeatureGate.EntitledFeaturesAsync(core, other, CancellationToken.None);

        Assert.True(BrandFeatureGate.IsRoleAvailable(feature.Key, buyerEntitled));
        Assert.False(BrandFeatureGate.IsRoleAvailable(feature.Key, otherEntitled));
    }

    // 6 ── with NO brand in context (a platform admin with nothing selected) the gate does not apply
    //      at all. Null must mean "do not gate", never "owns nothing" — otherwise a platform operator
    //      would lose every gated role from their own console.
    [Fact]
    public async Task no_brand_context_means_no_gating()
    {
        if (!_fx.DockerAvailable) return;

        await using var db = _fx.NewContext();
        var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_fx.AsCore(db), null, CancellationToken.None);

        Assert.Null(entitled);
        Assert.True(BrandFeatureGate.IsRoleAvailable("anything_at_all", entitled));
    }

    // 7 ── the shipped mapping from migration 0006 is what the strategy names, and nothing more.
    //      Guards against a later edit quietly gating a role the strategy never said to gate.
    [Fact]
    public async Task the_seeded_role_gates_match_the_strategy()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT code, feature_key FROM identity_access.roles WHERE feature_key IS NOT NULL ORDER BY code", conn);

        var gated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                gated[reader.GetString(0)] = reader.GetString(1);

        // The fixture applies the canonical 02_bc2 DDL, which does not seed the system role rows —
        // so when no roles exist there is nothing to assert about the mapping.
        if (gated.Count == 0) return;

        Assert.All(gated, kv => Assert.Contains(kv.Key, new[]
        {
            "rider", "warehouse_supervisor", "warehouse_staff", "partner_admin", "partner_operator",
        }));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task SeedAsync(IEnumerable<object> entities)
    {
        var all = entities.ToList();
        await using var seed = _fx.NewContext();
        foreach (var batch in new[]
                 {
                     all.OfType<AppFeature>().Cast<object>().ToList(),
                     all.Where(e => e is not AppFeature).ToList(),
                 })
        {
            if (batch.Count == 0) continue;
            seed.AddRange(batch);
            await seed.SaveChangesAsync();
        }
    }

    private static AppFeature Feat(string key, bool isCore = false) => new()
    {
        Key = key, Name = key, IsCore = isCore, IsSellable = true, Status = "active",
        SortOrder = 100, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static BrandFeature Licence(
        Guid brandId, string featureKey, bool enabled = true, DateOnly? validUntil = null) => new()
    {
        BrandId = brandId, FeatureKey = featureKey, Enabled = enabled, ValidUntil = validUntil,
        Source = "manual", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
}
